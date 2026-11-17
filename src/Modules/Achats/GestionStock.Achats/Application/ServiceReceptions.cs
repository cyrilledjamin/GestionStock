using GestionStock.Achats.Contracts;
using GestionStock.Achats.Domain;
using GestionStock.Achats.Infrastructure;
using GestionStock.Identite.Contracts;
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
    decimal Valeur,
    decimal TotalFrais);

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
    decimal TotalFrais,
    decimal MontantTotal,
    IReadOnlyList<DetailLigneReception> Lignes,
    IReadOnlyList<DetailFraisApproche> Frais,
    IReadOnlyDictionary<string, string> Personnel);

internal sealed record DetailLigneReception(
    Guid Id,
    int Ordre,
    Guid ArticleId,
    string CodeArticle,
    string LibelleArticle,
    string UniteStock,
    decimal Quantite,
    decimal CoutUnitaire,
    decimal Valeur,
    decimal FraisApprocheAlloues,
    decimal CoutEntree);

/// <summary>Frais d'approche, avec la part non incorporée au stock pour un frais tardif.</summary>
internal sealed record DetailFraisApproche(
    Guid Id,
    TypeFrais Type,
    string? Libelle,
    decimal Montant,
    CleRepartition Cle,
    bool ApresValidation,
    string SaisiPar,
    DateTime SaisiLeUtc,
    decimal MontantNonIncorpore);

/// <summary>Saisie d'un frais d'approche ; clé Valeur par défaut (décision 18).</summary>
internal sealed record SaisieFrais(TypeFrais Type, decimal Montant, CleRepartition? Cle, string? Libelle);

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
    IConsultationStock consultationStock,
    IUniteDeTravail uniteDeTravail,
    IUtilisateurCourant utilisateur,
    IHorloge horloge,
    IAnnuairePersonnel annuaire)
{
    public async Task<IReadOnlyList<ResumeReception>> ListerAsync(CancellationToken annulation)
    {
        // Résumé calculé par la base (nombre de lignes, valeur, frais) : les lignes, frais et répartitions ne sont pas
        // chargés pour une liste.
        return await contexte.Receptions.IgnoreAutoIncludes().AsNoTracking()
            .OrderByDescending(r => r.DateCreationUtc)
            .Select(r => new ResumeReception(
                r.Id,
                r.Numero,
                r.FournisseurId,
                contexte.Fournisseurs.Where(f => f.Id == r.FournisseurId).Select(f => f.Nom).Single(),
                r.NumeroBonLivraison,
                r.Statut,
                r.DateCreationUtc,
                r.DateValidationUtc,
                r.Lignes.Count,
                r.Lignes.Sum(l => l.Quantite * l.CoutUnitaire),
                r.Frais.Sum(f => f.Montant)))
            .ToListAsync(annulation);
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
                            ligne.CoutEntree,
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

    /// <summary>
    /// Ajoute un frais d'approche. Sur un brouillon, il entrera dans le coût d'entrée à la validation. Sur une réception
    /// validée (frais tardif, RG30), chaque ligne revalorise la quantité de l'article encore en stock, dans la même
    /// transaction que le frais ; la part de la marchandise déjà sortie reste non incorporée.
    /// </summary>
    public async Task<DetailReception> AjouterFraisAsync(Guid id, SaisieFrais saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var reception = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var aCompleter = await Verrouiller(id, jeton);
                var frais = aCompleter.AjouterFrais(
                    saisie.Type,
                    saisie.Montant,
                    saisie.Cle ?? CleRepartition.Valeur,
                    saisie.Libelle,
                    utilisateur.Identifiant,
                    horloge.MaintenantUtc);
                if (frais.ApresValidation)
                {
                    await Revaloriser(aCompleter, frais, jeton);
                }

                await contexte.SaveChangesAsync(jeton);
                return aCompleter;
            },
            annulation);

        return await VersDetail(reception, annulation);
    }

    public async Task<DetailReception> RetirerFraisAsync(Guid id, Guid fraisId, CancellationToken annulation)
    {
        var reception = await contexte.Receptions.SingleOrDefaultAsync(r => r.Id == id, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        reception.RetirerFrais(fraisId);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(reception, annulation);
    }

    private async Task<Reception> Verrouiller(Guid id, CancellationToken annulation)
    {
        var trouvee = await contexte.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM achats.reception WHERE id = {id} FOR UPDATE")
            .ToListAsync(annulation);
        if (trouvee.Count == 0)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        }

        return await contexte.Receptions.SingleAsync(r => r.Id == id, annulation);
    }

    /// <summary>
    /// Une ligne revalorise au plus sa quantité reçue, et jamais plus que le stock présent de l'article ; deux lignes
    /// du même article se partagent ce stock.
    /// </summary>
    private async Task Revaloriser(Reception reception, FraisApproche frais, CancellationToken annulation)
    {
        var site = await referentiel.ObtenirSiteBoutiqueAsync(annulation);
        var restant = new Dictionary<Guid, decimal>();
        foreach (var ligne in reception.Lignes.OrderBy(l => l.Ordre))
        {
            if (!restant.TryGetValue(ligne.ArticleId, out var enStock))
            {
                var solde = await consultationStock.ObtenirSoldeSiteAsync(ligne.ArticleId, site.SiteId, annulation);
                enStock = Math.Max(0m, solde?.QuantitePhysique ?? 0m);
            }

            var allocation = frais.Allocations.Single(a => a.LigneReceptionId == ligne.Id);
            allocation.Incorporer(enStock, ligne.Quantite);
            restant[ligne.ArticleId] = enStock - allocation.QuantiteRevalorisee;
            if (allocation.QuantiteRevalorisee <= 0m)
            {
                continue;
            }

            await mouvements.EnregistrerAsync(
                new DemandeMouvement(
                    TypeMouvementStock.Revalorisation,
                    ligne.ArticleId,
                    site.SiteId,
                    Source: null,
                    Destination: null,
                    allocation.QuantiteRevalorisee,
                    allocation.SupplementUnitaire,
                    TypesDocumentsAchats.Reception,
                    reception.Id,
                    utilisateur.Identifiant,
                    IdIdempotence: allocation.Id,
                    DateUtc: frais.SaisiLeUtc),
                annulation);
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
                ligne.Quantite * ligne.CoutUnitaire,
                ligne.FraisApprocheAlloues,
                ligne.CoutEntree));
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
            reception.ValeurMarchandise,
            reception.TotalFrais,
            reception.ValeurMarchandise + reception.TotalFrais,
            lignes,
            [.. reception.Frais.OrderBy(f => f.SaisiLeUtc).Select(f => new DetailFraisApproche(
                f.Id, f.Type, f.Libelle, f.Montant, f.Cle, f.ApresValidation, f.SaisiPar, f.SaisiLeUtc, f.MontantNonIncorpore))],
            await annuaire.DesignationsAsync([reception.CreePar, reception.ValideePar, .. reception.Frais.Select(f => f.SaisiPar)], annulation));
    }
}
