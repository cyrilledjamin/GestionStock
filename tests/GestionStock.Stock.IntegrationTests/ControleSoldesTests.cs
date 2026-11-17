using GestionStock.Stock.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>
/// Contrôle de cohérence des soldes (ENF04) par le contrat public. La base est partagée : chaque test ne regarde que les
/// écarts de son propre article.
/// </summary>
public sealed class ControleSoldesTests(BasePostgres basePostgres)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Des_soldes_ecrits_par_les_mouvements_sont_conformes_a_leur_journal()
    {
        basePostgres.ExigerDocker();
        var scenario = await HistoriqueAsync();

        var controle = await ControlerAsync();

        Assert.DoesNotContain(controle.Ecarts, e => e.ArticleId == scenario.Article);
        Assert.True(controle.SoldesControles >= 3); // le site et ses deux emplacements
    }

    [Fact]
    public async Task Un_solde_fausse_donne_un_ecart_exact_par_grandeur_et_par_emplacement()
    {
        basePostgres.ExigerDocker();
        var scenario = await HistoriqueAsync();
        await ExecuterSqlAsync(
            "UPDATE stock.stock_site SET quantite_physique = quantite_physique + 3, cump = 999 WHERE article_id = @article",
            scenario.Article);
        await ExecuterSqlAsync(
            "UPDATE stock.stock_emplacement SET quantite_physique = 0 WHERE article_id = @article AND emplacement_id = @emplacement",
            scenario.Article,
            scenario.Vente.EmplacementId);

        var ecarts = (await ControlerAsync()).Ecarts.Where(e => e.ArticleId == scenario.Article).ToList();

        // Journal : 10 à 100, 30 à 200 (CUMP 175), réassort de 8 vers la vente, vente de 5 → 35 sur le site, 3 en vente.
        Assert.Equal(3, ecarts.Count);
        Assert.Contains(new EcartStock(SoldeStockControle.Site, GrandeurStock.QuantitePhysique, scenario.Article, scenario.Site, null, null, 35m, 38m), ecarts);
        Assert.Contains(new EcartStock(SoldeStockControle.Site, GrandeurStock.Cump, scenario.Article, scenario.Site, null, null, 175m, 999m), ecarts);
        Assert.Contains(new EcartStock(SoldeStockControle.Emplacement, GrandeurStock.QuantitePhysique, scenario.Article, scenario.Site, scenario.Vente.EmplacementId, null, 3m, 0m), ecarts);
    }

    [Fact]
    public async Task La_reconstruction_remet_les_soldes_au_journal_sans_ecrire_de_mouvement()
    {
        basePostgres.ExigerDocker();
        var scenario = await HistoriqueAsync();
        var journalAvant = (await basePostgres.JournalAsync(scenario)).Count;
        await ExecuterSqlAsync("UPDATE stock.stock_site SET quantite_physique = -7, cump = 1 WHERE article_id = @article", scenario.Article);
        await ExecuterSqlAsync("UPDATE stock.stock_emplacement SET quantite_physique = 50 WHERE article_id = @article", scenario.Article);

        var corriges = await ReconstruireAsync(scenario.Article);
        var deNouveau = await ReconstruireAsync(scenario.Article);

        Assert.Equal(4, corriges.Count); // physique et CUMP du site, stockage et vente
        Assert.Empty(deNouveau);
        Assert.DoesNotContain((await ControlerAsync()).Ecarts, e => e.ArticleId == scenario.Article);
        var solde = await basePostgres.SoldeSiteAsync(scenario);
        Assert.Equal((35m, 175m), (solde!.QuantitePhysique, solde.Cump));
        Assert.Equal((32m, 3m), (await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Stockage), await basePostgres.QuantiteEmplacementAsync(scenario, scenario.Vente)));
        Assert.Equal(journalAvant, (await basePostgres.JournalAsync(scenario)).Count);
    }

    [Fact]
    public async Task Un_solde_qu_aucun_mouvement_n_explique_est_attendu_a_zero()
    {
        basePostgres.ExigerDocker();
        var article = Guid.NewGuid();
        var site = Guid.NewGuid();
        await ExecuterSqlAsync(
            "INSERT INTO stock.stock_site (article_id, site_id, quantite_physique, quantite_reservee, quantite_bloquee, cump) VALUES (@article, @site, 4, 0, 0, 10)",
            article,
            site);

        var ecarts = (await ControlerAsync()).Ecarts.Where(e => e.ArticleId == article).ToList();
        await ReconstruireAsync(article);

        Assert.Equal([(GrandeurStock.QuantitePhysique, 0m, 4m), (GrandeurStock.Cump, 0m, 10m)], ecarts.Select(e => (e.Grandeur, e.Journal, e.Enregistre)));
        Assert.DoesNotContain((await ControlerAsync()).Ecarts, e => e.ArticleId == article);
    }

    /// <summary>Réception de 10 à 100 et 30 à 200 en stockage, réassort de 8 vers la vente, vente de 5.</summary>
    private async Task<Scenario> HistoriqueAsync()
    {
        var scenario = new Scenario();
        await basePostgres.EnregistrerAsync(scenario.Entree(10m, 100m, scenario.Stockage));
        await basePostgres.EnregistrerAsync(scenario.Entree(30m, 200m, scenario.Stockage));
        await basePostgres.EnregistrerAsync(scenario.Reassort(8m));
        await basePostgres.EnregistrerAsync(scenario.VenteCaisse(5m));
        return scenario;
    }

    private Task<ControleStock> ControlerAsync() =>
        basePostgres.DansPorteeAsync(services => services.GetRequiredService<IControleSoldesStock>().ControlerAsync(Annulation));

    private Task<IReadOnlyList<EcartStock>> ReconstruireAsync(Guid article) =>
        basePostgres.DansPorteeAsync(services => services.GetRequiredService<IControleSoldesStock>().ReconstruireAsync(article, Annulation));

    /// <summary>Fausse un solde comme le ferait une écriture hors du module (ce que le contrôle doit débusquer).</summary>
    private async Task ExecuterSqlAsync(string sql, Guid article, Guid? autre = null)
    {
        await using var commande = basePostgres.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(sql);
        commande.Parameters.AddWithValue("article", article);
        if (autre is { } valeur)
        {
            commande.Parameters.AddWithValue(sql.Contains("@site", StringComparison.Ordinal) ? "site" : "emplacement", valeur);
        }

        await commande.ExecuteNonQueryAsync(Annulation);
    }
}
