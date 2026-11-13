using GestionStock.Stock.Contracts;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>
/// Mouvements simultanés sur le même article, chacun dans sa portée et sa connexion, comme plusieurs
/// caisses : aucun ne doit échouer en conflit, et aucune quantité ne doit être perdue.
/// </summary>
public sealed class ConcurrenceTests(BasePostgres basePostgres)
{
    private const int NombreAppels = 10;

    [Fact]
    public async Task Des_ventes_simultanees_du_meme_article_sont_toutes_enregistrees()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(100m, 50m));

        await Task.WhenAll(Enumerable.Range(0, NombreAppels)
            .Select(_ => Task.Run(() => basePostgres.EnregistrerAsync(scenario.VenteCaisse(1m)), TestContext.Current.CancellationToken)));

        Assert.Equal(NombreAppels + 1, (await basePostgres.JournalAsync(scenario)).Count);
        Assert.Equal(100m - NombreAppels, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
        Assert.Equal(-NombreAppels, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente));
    }

    [Fact]
    public async Task Les_premiers_mouvements_simultanes_d_un_article_creent_un_seul_solde()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        await Task.WhenAll(Enumerable.Range(0, NombreAppels)
            .Select(_ => Task.Run(() => basePostgres.EnregistrerAsync(scenario.Entree(2m, 100m)), TestContext.Current.CancellationToken)));

        Assert.Equal(2m * NombreAppels, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
        var lignesEmplacement = await basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<StockDbContext>().StocksEmplacements
                .CountAsync(e => e.ArticleId == scenario.Article, TestContext.Current.CancellationToken));
        Assert.Equal(1, lignesEmplacement);
    }

    [Fact]
    public async Task Des_renvois_simultanes_de_la_meme_demande_n_enregistrent_qu_un_mouvement()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(1m, 100m));
        var demande = scenario.Entree(5m, 100m);

        var resultats = await Task.WhenAll(Enumerable.Range(0, NombreAppels)
            .Select(_ => Task.Run(() => basePostgres.EnregistrerAsync(demande), TestContext.Current.CancellationToken)));

        Assert.Single(resultats, r => !r.DejaEnregistre);
        Assert.Single(resultats.Select(r => r.MouvementId).Distinct());
        Assert.Equal(6m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
    }
}
