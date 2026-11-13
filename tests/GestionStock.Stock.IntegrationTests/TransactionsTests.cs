using GestionStock.Infrastructure.Persistance;
using GestionStock.Stock.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>
/// Unité de travail partagée : un module appelant (la caisse, par exemple) englobe le mouvement de stock
/// dans sa propre transaction ; tout est validé ou tout est annulé.
/// </summary>
public sealed class TransactionsTests(BasePostgres basePostgres)
{
    [Fact]
    public async Task L_echec_du_module_appelant_annule_le_mouvement_et_les_soldes()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        await Assert.ThrowsAsync<InvalidOperationException>(() => basePostgres.DansPorteeAsync(async services =>
        {
            var uniteDeTravail = services.GetRequiredService<IUniteDeTravail>();
            var stock = services.GetRequiredService<IServiceMouvementsStock>();
            await uniteDeTravail.ExecuterAsync(
                async jeton =>
                {
                    await stock.EnregistrerAsync(scenario.VenteCaisse(1m), jeton);
                    throw new InvalidOperationException("Échec simulé de l'enregistrement du ticket.");
                },
                TestContext.Current.CancellationToken);
        }));

        Assert.Empty(await basePostgres.JournalAsync(scenario));
        Assert.Null(await basePostgres.SoldeSiteAsync(scenario));
        Assert.Null(await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente));
    }

    [Fact]
    public async Task Plusieurs_mouvements_d_une_meme_unite_de_travail_sont_valides_ensemble()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        await basePostgres.DansPorteeAsync(async services =>
        {
            var uniteDeTravail = services.GetRequiredService<IUniteDeTravail>();
            var stock = services.GetRequiredService<IServiceMouvementsStock>();
            await uniteDeTravail.ExecuterAsync(
                async jeton =>
                {
                    Assert.True(uniteDeTravail.EstActive);
                    await stock.EnregistrerAsync(scenario.VenteCaisse(1m), jeton);
                    await stock.EnregistrerAsync(scenario.VenteCaisse(2m), jeton);
                },
                TestContext.Current.CancellationToken);
            Assert.False(uniteDeTravail.EstActive);
        });

        Assert.Equal(2, (await basePostgres.JournalAsync(scenario)).Count);
        Assert.Equal(-3m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
    }

    [Fact]
    public async Task Un_refus_du_stock_annule_aussi_les_mouvements_precedents_de_la_meme_unite_de_travail()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        await Assert.ThrowsAsync<ExceptionStock>(() => basePostgres.DansPorteeAsync(async services =>
        {
            var uniteDeTravail = services.GetRequiredService<IUniteDeTravail>();
            var stock = services.GetRequiredService<IServiceMouvementsStock>();
            await uniteDeTravail.ExecuterAsync(
                async jeton =>
                {
                    await stock.EnregistrerAsync(scenario.Entree(3m, 100m, scenario.Stockage), jeton);
                    await stock.EnregistrerAsync(scenario.Expedition(5m, scenario.Stockage), jeton);
                },
                TestContext.Current.CancellationToken);
        }));

        Assert.Empty(await basePostgres.JournalAsync(scenario));
        Assert.Null(await basePostgres.SoldeSiteAsync(scenario));
    }
}
