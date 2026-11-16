using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire.Contracts;
using GestionStock.Inventaire.Domain;
using GestionStock.Inventaire.Infrastructure;
using GestionStock.Referentiel.Contracts;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Inventaire.Application;

internal sealed record ResumeInventaire(
    Guid Id,
    string Numero,
    TypeInventaire Type,
    StatutInventaire Statut,
    DateTime CreeLeUtc,
    string CreePar,
    DateTime? TermineLeUtc,
    string? TerminePar,
    int NombreLignes,
    int NombreComptees);

internal sealed record LigneInventaireDto(
    Guid Id,
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string UniteStock,
    int PrecisionQuantite,
    decimal? QuantiteTheorique,
    decimal? QuantiteComptee,
    decimal? Ecart,
    MotifEcart MotifEcart,
    decimal? CoutOuverture,
    decimal CoutUnitaire,
    decimal? ValeurEcart,
    DateTime? ComptageUtc,
    string? ComptePar);

internal sealed record DetailInventaire(ResumeInventaire Inventaire, decimal ValeurEcarts, IReadOnlyList<LigneInventaireDto> Lignes);

internal sealed record NouvelInventaire(TypeInventaire Type, IReadOnlyList<Guid>? Articles);

internal sealed record Comptage(Guid ArticleId, decimal Quantite, MotifEcart MotifEcart = MotifEcart.Aucun);

/// <summary>
/// Inventaires (UC11, RG15, RG16) : création, comptage, validation des écarts (un mouvement par écart, en une seule
/// transaction), annulation ; inventaire d'ouverture créé par la reprise des données.
/// </summary>
internal sealed class ServiceInventaires(
    InventaireDbContext contexte,
    IConsultationReferentiel referentiel,
    IConsultationStock consultationStock,
    IServiceMouvementsStock mouvements,
    IUniteDeTravail uniteDeTravail,
    IUtilisateurCourant utilisateur,
    IHorloge horloge) : IServiceInventaire
{
    public async Task<IReadOnlyList<ResumeInventaire>> ListerAsync(StatutInventaire? statut, CancellationToken annulation)
    {
        var inventaires = await contexte.Inventaires.AsNoTracking()
            .Where(i => statut == null || i.Statut == statut)
            .OrderByDescending(i => i.CreeLeUtc)
            .Take(200)
            .ToListAsync(annulation);
        return [.. inventaires.Select(Resume)];
    }

    public async Task<DetailInventaire> ObtenirAsync(Guid id, CancellationToken annulation)
    {
        var inventaire = await contexte.Inventaires.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.InventaireIntrouvable);
        return await VersDetail(inventaire, annulation);
    }

    /// <summary>Inventaire complet (tous les articles actifs) ou partiel (articles choisis, ou ajoutés au fil du comptage).</summary>
    public async Task<DetailInventaire> CreerAsync(NouvelInventaire saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (saisie.Type is not (TypeInventaire.Complet or TypeInventaire.Partiel))
        {
            throw new ExceptionInventaire(CodesErreurInventaire.TypeInvalide);
        }

        IReadOnlyList<ArticleReferentiel> articles = saisie.Type == TypeInventaire.Complet
            ? await referentiel.ListerArticlesActifsAsync(annulation)
            : await ArticlesActifs(saisie.Articles ?? [], annulation);

        var inventaire = Domain.Inventaire.Creer(await Numero(InventaireDbContext.SequenceInventaire, "INV", annulation), saisie.Type, [.. articles.Select(a => VersAInventorier(a))], utilisateur.Identifiant, horloge.MaintenantUtc);
        contexte.Inventaires.Add(inventaire);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(inventaire, annulation);
    }

    public async Task<InventaireCree> CreerOuvertureAsync(IReadOnlyList<LigneOuverture> lignes, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(lignes);
        var articles = new List<ArticleAInventorier>();
        foreach (var ligne in lignes)
        {
            var article = await referentiel.ObtenirArticleAsync(ligne.ArticleId, annulation)
                ?? throw new ExceptionInventaire(CodesErreurInventaire.ArticleIntrouvable);
            articles.Add(VersAInventorier(article, ligne.CoutUnitaire));
        }

        var inventaire = Domain.Inventaire.Creer(await Numero(InventaireDbContext.SequenceInventaire, "INV", annulation), TypeInventaire.Ouverture, articles, utilisateur.Identifiant, horloge.MaintenantUtc);
        contexte.Inventaires.Add(inventaire);
        await contexte.SaveChangesAsync(annulation);
        return new InventaireCree(inventaire.Id, inventaire.Numero);
    }

    /// <summary>Comptage d'un article : la quantité théorique est relevée maintenant (RG16).</summary>
    public async Task<DetailInventaire> CompterAsync(Guid id, Comptage comptage, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(comptage);
        var inventaire = await contexte.Inventaires.SingleOrDefaultAsync(i => i.Id == id, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.InventaireIntrouvable);
        var article = await referentiel.ObtenirArticleAsync(comptage.ArticleId, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.ArticleIntrouvable);
        var site = await referentiel.ObtenirSiteBoutiqueAsync(annulation);
        var theorique = (await consultationStock.ObtenirSoldeSiteAsync(article.Id, site.SiteId, annulation))?.QuantitePhysique ?? 0m;

        inventaire.Compter(VersAInventorier(article), comptage.Quantite, theorique, comptage.MotifEcart, utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(inventaire, annulation);
    }

    /// <summary>
    /// Validation (RG15) : un mouvement par écart vers ou depuis l'emplacement de vente, tout ou rien. Entrée au coût
    /// d'ouverture s'il est connu, sinon au CUMP ; sortie au CUMP ; écart négatif classé freinte : mouvement Demarque (RG47).
    /// </summary>
    public async Task<DetailInventaire> ValiderAsync(Guid id, CancellationToken annulation)
    {
        var inventaire = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                await Verrouiller(id, jeton);
                var aValider = await contexte.Inventaires.SingleOrDefaultAsync(i => i.Id == id, jeton)
                    ?? throw new ExceptionInventaire(CodesErreurInventaire.InventaireIntrouvable);
                aValider.Valider(utilisateur.Identifiant, horloge.MaintenantUtc);

                var site = await referentiel.ObtenirSiteBoutiqueAsync(jeton);
                var vente = new EmplacementMouvement(site.EmplacementVenteId, TypeEmplacementStock.Vente);
                foreach (var ligne in aValider.Lignes.Where(l => l.Ecart is not null and not 0m).OrderBy(l => l.CodeArticle))
                {
                    var ecart = ligne.Ecart!.Value;
                    var cump = (await consultationStock.ObtenirSoldeSiteAsync(ligne.ArticleId, site.SiteId, jeton))?.Cump ?? 0m;
                    var entree = ecart > 0m;
                    var type = !entree && ligne.MotifEcart == MotifEcart.Freinte ? TypeMouvementStock.Demarque : TypeMouvementStock.AjustementInventaire;
                    await EnregistrerMouvement(
                        new DemandeMouvement(
                            type,
                            ligne.ArticleId,
                            site.SiteId,
                            Source: entree ? null : vente,
                            Destination: entree ? vente : null,
                            Math.Abs(ecart),
                            decimal.Round(entree ? ligne.CoutOuverture ?? cump : cump, 4),
                            TypesDocumentsInventaire.Inventaire,
                            aValider.Id,
                            utilisateur.Identifiant,
                            IdIdempotence: ligne.Id),
                        jeton);
                }

                await contexte.SaveChangesAsync(jeton);
                return aValider;
            },
            annulation);
        return await VersDetail(inventaire, annulation);
    }

    public async Task<DetailInventaire> AnnulerAsync(Guid id, CancellationToken annulation)
    {
        var inventaire = await contexte.Inventaires.SingleOrDefaultAsync(i => i.Id == id, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.InventaireIntrouvable);
        inventaire.Annuler(utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDetail(inventaire, annulation);
    }

    // --- Outils partagés avec la démarque -------------------------------------------------------------------------

    internal async Task<string> Numero(string sequence, string prefixe, CancellationToken annulation)
    {
        var sql = sequence == InventaireDbContext.SequenceDemarque
            ? "SELECT nextval('inventaire.numero_demarque') AS \"Value\""
            : "SELECT nextval('inventaire.numero_inventaire') AS \"Value\"";
        var numero = await contexte.Database.SqlQueryRaw<long>(sql).SingleAsync(annulation);
        return $"{prefixe}-{numero:D6}";
    }

    internal async Task EnregistrerMouvement(DemandeMouvement demande, CancellationToken annulation)
    {
        try
        {
            await mouvements.EnregistrerAsync(demande, annulation);
        }
        catch (ExceptionStock erreur)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.MouvementRefuse, erreur.Code);
        }
    }

    internal static ArticleAInventorier VersAInventorier(ArticleReferentiel article, decimal? coutOuverture = null) =>
        new(article.Id, article.Code, article.Libelle, article.UniteStock, article.PrecisionQuantite, coutOuverture);

    private async Task<List<ArticleReferentiel>> ArticlesActifs(IReadOnlyList<Guid> ids, CancellationToken annulation)
    {
        var articles = new List<ArticleReferentiel>();
        foreach (var id in ids.Distinct())
        {
            var article = await referentiel.ObtenirArticleAsync(id, annulation)
                ?? throw new ExceptionInventaire(CodesErreurInventaire.ArticleIntrouvable);
            if (!article.Actif)
            {
                throw new ExceptionInventaire(CodesErreurInventaire.ArticleInactif, article.Code);
            }

            articles.Add(article);
        }

        return articles;
    }

    /// <summary>Verrou de l'inventaire : une seconde validation simultanée attend, puis le trouve validé.</summary>
    private async Task Verrouiller(Guid id, CancellationToken annulation)
    {
        var trouve = await contexte.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventaire.inventaire WHERE id = {id} FOR UPDATE").ToListAsync(annulation);
        if (trouve.Count == 0)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.InventaireIntrouvable);
        }
    }

    private async Task<DetailInventaire> VersDetail(Domain.Inventaire inventaire, CancellationToken annulation)
    {
        var site = await referentiel.ObtenirSiteBoutiqueAsync(annulation);
        var lignes = new List<LigneInventaireDto>();
        foreach (var l in inventaire.Lignes.OrderBy(l => l.CodeArticle))
        {
            var cump = (await consultationStock.ObtenirSoldeSiteAsync(l.ArticleId, site.SiteId, annulation))?.Cump ?? 0m;
            var cout = l.Ecart > 0m && l.CoutOuverture is { } ouverture ? ouverture : cump;
            lignes.Add(new LigneInventaireDto(
                l.Id, l.ArticleId, l.CodeArticle, l.Libelle, l.UniteStock, l.PrecisionQuantite,
                l.QuantiteTheorique, l.QuantiteComptee, l.Ecart, l.MotifEcart, l.CoutOuverture, cout,
                l.Ecart * cout, l.ComptageUtc, l.ComptePar));
        }

        return new DetailInventaire(Resume(inventaire), lignes.Sum(l => l.ValeurEcart ?? 0m), lignes);
    }

    private static ResumeInventaire Resume(Domain.Inventaire i) =>
        new(i.Id, i.Numero, i.Type, i.Statut, i.CreeLeUtc, i.CreePar, i.TermineLeUtc, i.TerminePar, i.Lignes.Count, i.Lignes.Count(l => l.QuantiteComptee is not null));
}
