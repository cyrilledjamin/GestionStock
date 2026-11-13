using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>Enregistrement d'un mouvement par le contrat public : journal et soldes écrits ensemble.</summary>
public sealed class EnregistrementMouvementTests(BasePostgres basePostgres)
{
    [Fact]
    public async Task Une_reception_ecrit_le_mouvement_le_solde_site_et_le_solde_emplacement()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        var resultat = await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m));

        Assert.False(resultat.DejaEnregistre);
        var mouvement = Assert.Single(await basePostgres.JournalAsync(scenario));
        Assert.Equal(resultat.MouvementId, mouvement.Id);
        Assert.Equal(DateTimeKind.Utc, mouvement.DateUtc.Kind);

        var solde = await basePostgres.SoldeSiteAsync(scenario);
        Assert.NotNull(solde);
        Assert.Equal(10m, solde.QuantitePhysique);
        Assert.Equal(10m, solde.QuantiteDisponible);
        Assert.Equal(100m, solde.Cump);
        Assert.Equal(10m, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Reception));
    }

    [Fact]
    public async Task Deux_receptions_donnent_le_cout_unitaire_moyen_pondere()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m));
        await basePostgres.EnregistrerAsync(scenario.Entree(30m, 200m));

        var solde = await basePostgres.SoldeSiteAsync(scenario);
        Assert.Equal(40m, solde!.QuantitePhysique);
        Assert.Equal(175m, solde.Cump);
    }

    [Fact]
    public async Task Un_renvoi_avec_le_meme_identifiant_d_idempotence_n_ecrit_rien()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        var demande = scenario.Entree(10m, 100m);

        var premier = await basePostgres.EnregistrerAsync(demande);
        var renvoi = await basePostgres.EnregistrerAsync(demande);

        Assert.True(renvoi.DejaEnregistre);
        Assert.Equal(premier.MouvementId, renvoi.MouvementId);
        Assert.Single(await basePostgres.JournalAsync(scenario));
        Assert.Equal(10m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
    }

    [Fact]
    public async Task Une_sortie_superieure_au_stock_d_un_emplacement_hors_vente_est_refusee_sans_rien_ecrire()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(5m, 100m, scenario.Stockage));

        var erreur = await Assert.ThrowsAsync<ExceptionStock>(() =>
            basePostgres.EnregistrerAsync(scenario.Expedition(8m, scenario.Stockage)));

        Assert.Equal(CodesErreurStock.StockInsuffisant, erreur.Code);
        Assert.Single(await basePostgres.JournalAsync(scenario));
        Assert.Equal(5m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
        Assert.Equal(5m, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Stockage));
    }

    [Fact]
    public async Task Une_vente_en_caisse_n_est_jamais_refusee_pour_stock_insuffisant()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();

        var resultat = await basePostgres.EnregistrerAsync(scenario.VenteCaisse(2m));

        Assert.False(resultat.DejaEnregistre);
        Assert.Equal(-2m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
        Assert.Equal(-2m, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente));
    }

    [Fact]
    public async Task Un_mouvement_interne_deplace_le_stock_sans_changer_le_solde_site()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m, scenario.Stockage));

        await basePostgres.EnregistrerAsync(scenario.Reassort(4m));

        Assert.Equal(10m, (await basePostgres.SoldeSiteAsync(scenario))!.QuantitePhysique);
        Assert.Equal(6m, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Stockage));
        Assert.Equal(4m, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente));
    }

    [Fact]
    public async Task Les_soldes_enregistres_sont_ceux_recalcules_depuis_le_journal()
    {
        basePostgres.ExigerDocker();
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m, scenario.Stockage));
        await basePostgres.EnregistrerAsync(scenario.Entree(5m, 130m, scenario.Stockage));
        await basePostgres.EnregistrerAsync(scenario.Reassort(6m));
        await basePostgres.EnregistrerAsync(scenario.VenteCaisse(9m));
        await basePostgres.EnregistrerAsync(scenario.Entree(7m, 90m, scenario.Stockage));

        var journal = await basePostgres.JournalAsync(scenario);
        var site = StockSite.Recalculer(scenario.Article, scenario.Site, journal);
        var stockage = StockEmplacement.Recalculer(scenario.Article, scenario.Stockage.EmplacementId, null, JournalDe(scenario.Stockage));
        var vente = StockEmplacement.Recalculer(scenario.Article, scenario.Vente.EmplacementId, null, JournalDe(scenario.Vente));

        // Un solde emplacement se rejoue sur les seuls mouvements dont l'emplacement est source ou destination.
        IEnumerable<MouvementStock> JournalDe(EmplacementMouvement emplacement) => journal.Where(m =>
            m.EmplacementSourceId == emplacement.EmplacementId || m.EmplacementDestinationId == emplacement.EmplacementId);

        var solde = await basePostgres.SoldeSiteAsync(scenario);
        Assert.Equal(site.QuantitePhysique, solde!.QuantitePhysique);
        Assert.Equal(site.Cump, solde.Cump);
        Assert.Equal(stockage.QuantitePhysique, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Stockage));
        Assert.Equal(vente.QuantitePhysique, await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente));
    }
}
