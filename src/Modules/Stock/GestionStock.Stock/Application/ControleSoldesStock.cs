using System.Data;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Application;

/// <summary>
/// Contrôle de cohérence des soldes (ENF04) : le journal de chaque article est rejoué dans l'ordre d'écriture avec les
/// règles du domaine (<see cref="StockSite.Recalculer"/>, <see cref="StockEmplacement.Recalculer"/>), CUMP compris,
/// puis comparé aux soldes enregistrés. Seul le module Stock écrit ses soldes (RG01) : la reconstruction est ici.
/// </summary>
internal sealed class ControleSoldesStock(StockDbContext contexte, IUniteDeTravail uniteDeTravail) : IControleSoldesStock
{
    public async Task<ControleStock> ControlerAsync(CancellationToken annulation = default)
    {
        try
        {
            // Instantané cohérent : soldes et journal lus dans une même transaction en lecture répétable, pendant que
            // les ventes continuent (chaque mouvement et ses soldes sont écrits ensemble, RG01).
            await using var transaction = await contexte.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, annulation);
            var sites = await contexte.StocksSites.AsNoTracking().ToDictionaryAsync(s => (s.ArticleId, s.SiteId), annulation);
            var emplacements = await contexte.StocksEmplacements.AsNoTracking().ToDictionaryAsync(e => (e.ArticleId, e.EmplacementId, e.LotId), annulation);

            var ecarts = new List<EcartStock>();
            var sitesVus = new HashSet<(Guid, Guid)>();
            var emplacementsVus = new HashSet<(Guid, Guid, Guid?)>();
            var journal = new List<MouvementStock>();

            void ComparerArticle()
            {
                var attendu = Rejouer(journal);
                foreach (var (cle, recalcule) in attendu.Sites)
                {
                    sitesVus.Add(cle);
                    ComparerSite(recalcule, sites.GetValueOrDefault(cle), ecarts);
                }

                foreach (var (cle, (recalcule, siteId)) in attendu.Emplacements)
                {
                    emplacementsVus.Add(cle);
                    ComparerEmplacement(recalcule, siteId, emplacements.GetValueOrDefault(cle)?.QuantitePhysique ?? 0m, ecarts);
                }

                journal.Clear();
            }

            // Lecture en flux, article par article (index article × site × id) : la mémoire ne tient qu'un article.
            await foreach (var mouvement in contexte.Mouvements.AsNoTracking()
                .OrderBy(m => m.ArticleId).ThenBy(m => m.SiteId).ThenBy(m => m.Id)
                .AsAsyncEnumerable().WithCancellation(annulation))
            {
                if (journal.Count > 0 && journal[0].ArticleId != mouvement.ArticleId)
                {
                    ComparerArticle();
                }

                journal.Add(mouvement);
            }

            if (journal.Count > 0)
            {
                ComparerArticle();
            }

            // Soldes qu'aucun mouvement n'explique : le journal dit zéro.
            foreach (var (cle, solde) in sites.Where(s => !sitesVus.Contains(s.Key)))
            {
                ComparerSite(StockSite.Ouvrir(cle.ArticleId, cle.SiteId), solde, ecarts);
            }

            foreach (var (cle, solde) in emplacements.Where(e => !emplacementsVus.Contains(e.Key)))
            {
                ComparerEmplacement(StockEmplacement.Ouvrir(cle.ArticleId, cle.EmplacementId, cle.LotId), null, solde.QuantitePhysique, ecarts);
            }

            var controles = sitesVus.Union(sites.Keys).Count() + emplacementsVus.Union(emplacements.Keys).Count();
            return new ControleStock(controles, ecarts);
        }
        catch (ExceptionDomaineStock erreur)
        {
            throw new ExceptionStock(erreur.Code, erreur.Message, erreur);
        }
    }

    public Task<IReadOnlyList<EcartStock>> ReconstruireAsync(Guid articleId, CancellationToken annulation = default) =>
        uniteDeTravail.ExecuterAsync(jeton => Reconstruire(articleId, jeton), annulation);

    private async Task<IReadOnlyList<EcartStock>> Reconstruire(Guid articleId, CancellationToken annulation)
    {
        try
        {
            // Même ordre de verrouillage qu'un mouvement (site, puis emplacements) : pas d'interblocage avec une vente.
            var sites = await contexte.StocksSites
                .FromSql($"SELECT *, xmin FROM stock.stock_site WHERE article_id = {articleId} ORDER BY site_id FOR UPDATE")
                .ToDictionaryAsync(s => (s.ArticleId, s.SiteId), annulation);
            var emplacements = await contexte.StocksEmplacements
                .FromSql($"SELECT * FROM stock.stock_emplacement WHERE article_id = {articleId} ORDER BY emplacement_id FOR UPDATE")
                .ToDictionaryAsync(e => (e.ArticleId, e.EmplacementId, e.LotId), annulation);
            var journal = await contexte.Mouvements.AsNoTracking()
                .Where(m => m.ArticleId == articleId)
                .OrderBy(m => m.SiteId).ThenBy(m => m.Id)
                .ToListAsync(annulation);

            var attendu = Rejouer(journal);
            foreach (var cle in sites.Keys.Except(attendu.Sites.Keys))
            {
                attendu.Sites[cle] = StockSite.Ouvrir(cle.ArticleId, cle.SiteId);
            }

            foreach (var cle in emplacements.Keys.Except(attendu.Emplacements.Keys))
            {
                attendu.Emplacements[cle] = (StockEmplacement.Ouvrir(cle.ArticleId, cle.EmplacementId, cle.LotId), null);
            }

            var ecarts = new List<EcartStock>();
            foreach (var (cle, recalcule) in attendu.Sites)
            {
                var solde = sites.GetValueOrDefault(cle);
                var avant = ecarts.Count;
                ComparerSite(recalcule, solde, ecarts);
                if (ecarts.Count > avant)
                {
                    if (solde is null)
                    {
                        solde = StockSite.Ouvrir(cle.ArticleId, cle.SiteId);
                        contexte.StocksSites.Add(solde);
                    }

                    solde.Reconstruire(recalcule);
                }
            }

            foreach (var (cle, (recalcule, siteId)) in attendu.Emplacements)
            {
                var solde = emplacements.GetValueOrDefault(cle);
                var avant = ecarts.Count;
                ComparerEmplacement(recalcule, siteId, solde?.QuantitePhysique ?? 0m, ecarts);
                if (ecarts.Count > avant)
                {
                    if (solde is null)
                    {
                        solde = StockEmplacement.Ouvrir(cle.ArticleId, cle.EmplacementId, cle.LotId);
                        contexte.StocksEmplacements.Add(solde);
                    }

                    solde.Reconstruire(recalcule);
                }
            }

            await contexte.SaveChangesAsync(annulation);
            return ecarts;
        }
        catch (ExceptionDomaineStock erreur)
        {
            throw new ExceptionStock(erreur.Code, erreur.Message, erreur);
        }
    }

    /// <summary>
    /// Soldes attendus d'un article d'après son journal, trié par site puis par ordre d'écriture : chaque site est rejoué
    /// dans l'ordre (CUMP), chaque emplacement touché reçoit ses entrées et sorties, avec le site de ses mouvements.
    /// </summary>
    private static SoldesAttendus Rejouer(IReadOnlyList<MouvementStock> journal)
    {
        var attendu = new SoldesAttendus();
        foreach (var parSite in journal.GroupBy(m => (m.ArticleId, m.SiteId)))
        {
            attendu.Sites[parSite.Key] = StockSite.Recalculer(parSite.Key.ArticleId, parSite.Key.SiteId, parSite);
        }

        var parEmplacement = journal
            .SelectMany(m => new[] { m.EmplacementSourceId, m.EmplacementDestinationId }
                .Where(e => e is not null)
                .Distinct()
                .Select(e => (Cle: (m.ArticleId, EmplacementId: e!.Value, m.LotId), Mouvement: m)))
            .GroupBy(x => x.Cle, x => x.Mouvement);
        foreach (var groupe in parEmplacement)
        {
            var (articleId, emplacementId, lotId) = groupe.Key;
            attendu.Emplacements[groupe.Key] = (StockEmplacement.Recalculer(articleId, emplacementId, lotId, groupe), groupe.First().SiteId);
        }

        return attendu;
    }

    private static void ComparerSite(StockSite attendu, StockSite? enregistre, List<EcartStock> ecarts)
    {
        Comparer(SoldeStockControle.Site, GrandeurStock.QuantitePhysique, attendu.ArticleId, attendu.SiteId, null, null, attendu.QuantitePhysique, enregistre?.QuantitePhysique ?? 0m, ecarts);
        Comparer(SoldeStockControle.Site, GrandeurStock.QuantiteReservee, attendu.ArticleId, attendu.SiteId, null, null, attendu.QuantiteReservee, enregistre?.QuantiteReservee ?? 0m, ecarts);
        Comparer(SoldeStockControle.Site, GrandeurStock.QuantiteBloquee, attendu.ArticleId, attendu.SiteId, null, null, attendu.QuantiteBloquee, enregistre?.QuantiteBloquee ?? 0m, ecarts);
        Comparer(SoldeStockControle.Site, GrandeurStock.Cump, attendu.ArticleId, attendu.SiteId, null, null, attendu.Cump, enregistre?.Cump ?? 0m, ecarts);
    }

    private static void ComparerEmplacement(StockEmplacement attendu, Guid? siteId, decimal enregistre, List<EcartStock> ecarts) =>
        Comparer(SoldeStockControle.Emplacement, GrandeurStock.QuantitePhysique, attendu.ArticleId, siteId, attendu.EmplacementId, attendu.LotId, attendu.QuantitePhysique, enregistre, ecarts);

    private static void Comparer(
        SoldeStockControle solde,
        GrandeurStock grandeur,
        Guid articleId,
        Guid? siteId,
        Guid? emplacementId,
        Guid? lotId,
        decimal journal,
        decimal enregistre,
        List<EcartStock> ecarts)
    {
        if (journal != enregistre)
        {
            ecarts.Add(new EcartStock(solde, grandeur, articleId, siteId, emplacementId, lotId, journal, enregistre));
        }
    }

    private sealed class SoldesAttendus
    {
        public Dictionary<(Guid ArticleId, Guid SiteId), StockSite> Sites { get; } = [];

        public Dictionary<(Guid ArticleId, Guid EmplacementId, Guid? LotId), (StockEmplacement Solde, Guid? SiteId)> Emplacements { get; } = [];
    }
}
