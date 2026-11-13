using GestionStock.Stock.Domain;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>RG01 : un mouvement enregistré n'est jamais modifié ni supprimé, un solde jamais supprimé.</summary>
public sealed class JournalImmuableTests(BasePostgres basePostgres)
{
    [Fact]
    public async Task Modifier_un_mouvement_enregistre_est_refuse()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => basePostgres.DansPorteeAsync(async services =>
        {
            var contexte = services.GetRequiredService<StockDbContext>();
            var mouvement = await contexte.Mouvements.SingleAsync(m => m.ArticleId == scenario.Article, TestContext.Current.CancellationToken);
            contexte.Entry(mouvement).Property(m => m.Quantite).CurrentValue = 99m;
            await contexte.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Equal(10m, Assert.Single(await basePostgres.JournalAsync(scenario)).Quantite);
    }

    [Fact]
    public async Task Supprimer_un_mouvement_enregistre_est_refuse()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => basePostgres.DansPorteeAsync(async services =>
        {
            var contexte = services.GetRequiredService<StockDbContext>();
            var mouvement = await contexte.Mouvements.SingleAsync(m => m.ArticleId == scenario.Article, TestContext.Current.CancellationToken);
            contexte.Mouvements.Remove(mouvement);
            await contexte.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Single(await basePostgres.JournalAsync(scenario));
    }

    [Fact]
    public async Task Supprimer_un_solde_est_refuse()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => basePostgres.DansPorteeAsync(async services =>
        {
            var contexte = services.GetRequiredService<StockDbContext>();
            StockSite solde = await contexte.StocksSites.SingleAsync(s => s.ArticleId == scenario.Article, TestContext.Current.CancellationToken);
            contexte.StocksSites.Remove(solde);
            await contexte.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        Assert.NotNull(await basePostgres.SoldeSiteAsync(scenario));
    }
}
