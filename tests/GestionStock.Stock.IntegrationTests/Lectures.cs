using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>Écritures par le contrat public et relectures directes en base, chacune dans sa portée.</summary>
internal static class Lectures
{
    public static Task<ResultatMouvement> EnregistrerAsync(this BasePostgres basePostgres, DemandeMouvement demande) =>
        basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<IServiceMouvementsStock>().EnregistrerAsync(demande, TestContext.Current.CancellationToken));

    public static Task<SoldeStockSite?> SoldeSiteAsync(this BasePostgres basePostgres, Scenario scenario) =>
        basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<IConsultationStock>()
                .ObtenirSoldeSiteAsync(scenario.Article, scenario.Site, TestContext.Current.CancellationToken));

    public static Task<decimal?> QuantiteEmplacementAsync(this BasePostgres basePostgres, Scenario scenario, EmplacementMouvement emplacement) =>
        basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<StockDbContext>().StocksEmplacements
                .Where(e => e.ArticleId == scenario.Article && e.EmplacementId == emplacement.EmplacementId)
                .Select(e => (decimal?)e.QuantitePhysique)
                .SingleOrDefaultAsync(TestContext.Current.CancellationToken));

    public static Task<List<MouvementStock>> JournalAsync(this BasePostgres basePostgres, Scenario scenario) =>
        basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<StockDbContext>().Mouvements
                .AsNoTracking()
                .Where(m => m.ArticleId == scenario.Article && m.SiteId == scenario.Site)
                .OrderBy(m => m.Id)
                .ToListAsync(TestContext.Current.CancellationToken));
}
