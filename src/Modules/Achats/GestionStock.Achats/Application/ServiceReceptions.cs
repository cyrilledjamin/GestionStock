using GestionStock.Achats.Contracts;
using GestionStock.Achats.Domain;
using GestionStock.Achats.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Referentiel.Contracts;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Achats.Application;

/// <summary>Réception dans la liste.</summary>
internal sealed record ResumeReception(
    Guid Id,
    string Numero,
    Guid FournisseurId,
    string Fournisseur,
    string? NumeroBonLivraison,
    StatutReception Statut,
    DateTime DateCreationUtc,
    DateTime? DateValidationUtc,
    int NombreLignes,
    decimal Valeur);

/// <summary>Réception complète, avec ses lignes et les articles concernés.</summary>
internal sealed record DetailReception(
    Guid Id,
    string Numero,
    Guid FournisseurId,
    string Fournisseur,
    string? NumeroBonLivraison,
    StatutReception Statut,
    DateTime DateCreationUtc,
    string CreePar,
    DateTime? DateValidationUtc,
    string? ValideePar,
    decimal Valeur,
    IReadOnlyList<DetailLigneReception> Lignes);

internal sealed record DetailLigneReception(
    Guid Id,
    int Ordre,
    Guid ArticleId,
    string CodeArticle,
    string LibelleArticle,
    string UniteStock,
    decimal Quantite,
    decimal CoutUnitaire,
    decimal Valeur);

/// <summary>Saisie d'une réception : en-tête et lignes.</summary>
internal sealed record SaisieReception(Guid FournisseurId, string? NumeroBonLivraison, IReadOnlyList<SaisieLigneReception> Lignes);

/// <summary>
/// Réception simple (UC04, V1) : saisie en brouillon, puis validation. La validation écrit, dans une seule
/// transaction, le statut de la réception et un mouvement EntreeReception par ligne vers l'emplacement de vente
/// (RG13 : sans gestion par emplacements, disponible dès la validation) ; le module Stock recalcule le CUMP (RG17).
/// </summary>
internal sealed class ServiceReceptions(
    AchatsDbContext contexte,
    IConsultationReferentiel referentiel,
    IServiceMouvementsStock mouvements,
    IUniteDeTravail uniteDeTravail,
    IUtilisateurCourant utilisateur,
    IHorloge horloge)
{
    public async Task<IReadOnlyList<ResumeReception>> ListerAsync(CancellationToken annulation)
    {
        var receptions = await contexte.Receptions.AsNoTracking()
            .OrderByDescending(r => r.DateCreationUtc)
            .ToListAsync(annulation);
        var fournisseurs = await contexte.Fournisseurs.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => f.Nom, annulation);

        return [.. receptions.Select(r => new ResumeReception(
            r.Id,
            r.Numero,
            r.FournisseurId,
            fournisseurs[r.FournisseurId],
            r.NumeroBonLivraison,
            r.Statut,
            r.DateCreationUtc,
            r.DateValidationUtc,
            r.Lignes.Count,
            r.Lignes.Sum(l => l.Quantite * l.CoutUnitaire)))];
    }

    public async Task<DetailReception> ObtenirAsync(Guid id, CancellationToken annulation)
    {
        var reception = await contexte.Receptions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        return await VersDetail(reception, annulation);
    }

    public async Task<DetailReception> CreerAsync(SaisieReception saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        await ControlerSaisie(saisie, annulation);

        var sequence = await contexte.Database
            .SqlQuery<long>($"SELECT nextval('achats.numero_reception') AS \"Value\"")
            .SingleAsync(annulation);
        var reception = Reception.Creer(
            $"R-{sequence:D6}",
            saisie.FournisseurId,
            saisie.NumeroBonLivraison,
            saisie.Lignes,
            utilisateur.Identifiant,
            horloge.MaintenantUtc);

        contexte.Receptions.Add(reception);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(reception, annulation);
    }

    public async Task<DetailReception> ModifierAsync(Guid id, SaisieReception saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var reception = await contexte.Receptions.SingleOrDefaultAsync(r => r.Id == id, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        if (reception.Statut == StatutReception.Validee)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionDejaValidee);
        }

        await ControlerSaisie(saisie, annulation);
        reception.Remplacer(saisie.FournisseurId, saisie.NumeroBonLivraison, saisie.Lignes);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(reception, annulation);
    }

    /// <summary>Valide la réception et fait entrer la marchandise en stock, tout ou rien.</summary>
    public async Task<DetailReception> ValiderAsync(Guid id, CancellationToken annulation)
    {
        var reception = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                // Verrou de la réception : une seconde validation simultanée attend, puis voit le statut Validee.
                var trouvee = await contexte.Database.SqlQuery<Guid>(
                        $"SELECT id AS \"Value\" FROM achats.reception WHERE id = {id} FOR UPDATE")
                    .ToListAsync(jeton);
                if (trouvee.Count == 0)
                {
                    throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
                }

                var aValider = await contexte.Receptions.SingleAsync(r => r.Id == id, jeton);
                if (aValider.Statut == StatutReception.Validee)
                {
                    throw new ExceptionAchats(CodesErreurAchats.ReceptionDejaValidee);
                }

                // Tout est contrôlé avant la première écriture ; une erreur du module Stock annule aussi l'ensemble.
                var lignes = aValider.Lignes.OrderBy(l => l.Ordre).ToList();
                await ControlerSaisie(
                    new SaisieReception(aValider.FournisseurId, aValider.NumeroBonLivraison, [.. lignes.Select(l => new SaisieLigneReception(l.ArticleId, l.Quantite, l.CoutUnitaire))]),
                    jeton);

                var site = await referentiel.ObtenirSiteBoutiqueAsync(jeton);
                var auteur = utilisateur.Identifiant;
                var maintenant = horloge.MaintenantUtc;
                foreach (var ligne in lignes)
                {
                    await mouvements.EnregistrerAsync(
                        new DemandeMouvement(
                            TypeMouvementStock.EntreeReception,
                            ligne.ArticleId,
                            site.SiteId,
                            Source: null,
                            Destination: new EmplacementMouvement(site.EmplacementVenteId, TypeEmplacementStock.Vente),
                            ligne.Quantite,
                            ligne.CoutUnitaire,
                            TypesDocumentsAchats.Reception,
                            aValider.Id,
                            auteur,
                            IdIdempotence: ligne.Id,
                            DateUtc: maintenant),
                        jeton);
                }

                aValider.Valider(auteur, maintenant);
                await contexte.SaveChangesAsync(jeton);
                return aValider;
            },
            annulation);

        return await VersDetail(reception, annulation);
    }

    /// <summary>Fournisseur actif, articles existants et actifs, quantités conformes à la précision de l'article (RG46).</summary>
    private async Task ControlerSaisie(SaisieReception saisie, CancellationToken annulation)
    {
        var fournisseur = await contexte.Fournisseurs.AsNoTracking().SingleOrDefaultAsync(f => f.Id == saisie.FournisseurId, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.FournisseurIntrouvable);
        if (!fournisseur.Actif)
        {
            throw new ExceptionAchats(CodesErreurAchats.FournisseurInactif, fournisseur.Nom);
        }

        if (saisie.Lignes is null || saisie.Lignes.Count == 0)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionSansLigne);
        }

        foreach (var ligne in saisie.Lignes)
        {
            var article = await referentiel.ObtenirArticleAsync(ligne.ArticleId, annulation)
                ?? throw new ExceptionAchats(CodesErreurAchats.ArticleIntrouvable);
            if (!article.Actif)
            {
                throw new ExceptionAchats(CodesErreurAchats.ArticleInactif, article.Code);
            }

            if (decimal.Round(ligne.Quantite, article.PrecisionQuantite) != ligne.Quantite)
            {
                throw new ExceptionAchats(CodesErreurAchats.PrecisionQuantite, $"{article.Code} : {article.PrecisionQuantite}");
            }
        }
    }

    private async Task<DetailReception> VersDetail(Reception reception, CancellationToken annulation)
    {
        var fournisseur = await contexte.Fournisseurs.AsNoTracking()
            .Where(f => f.Id == reception.FournisseurId)
            .Select(f => f.Nom)
            .SingleAsync(annulation);

        var lignes = new List<DetailLigneReception>();
        foreach (var ligne in reception.Lignes.OrderBy(l => l.Ordre))
        {
            var article = await referentiel.ObtenirArticleAsync(ligne.ArticleId, annulation);
            lignes.Add(new DetailLigneReception(
                ligne.Id,
                ligne.Ordre,
                ligne.ArticleId,
                article?.Code ?? "?",
                article?.Libelle ?? "?",
                article?.UniteStock ?? "",
                ligne.Quantite,
                ligne.CoutUnitaire,
                ligne.Quantite * ligne.CoutUnitaire));
        }

        return new DetailReception(
            reception.Id,
            reception.Numero,
            reception.FournisseurId,
            fournisseur,
            reception.NumeroBonLivraison,
            reception.Statut,
            reception.DateCreationUtc,
            reception.CreePar,
            reception.DateValidationUtc,
            reception.ValideePar,
            lignes.Sum(l => l.Valeur),
            lignes);
    }
}
