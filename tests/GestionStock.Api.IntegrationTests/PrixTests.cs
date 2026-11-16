using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class PrixTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    /// <summary>Date du jour à Douala (UTC+1, sans heure d'été).</summary>
    internal static DateOnly AujourdhuiDouala => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    [Fact]
    public async Task Le_gerant_fixe_un_prix_par_conditionnement_que_la_caisse_lit()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 24);
        var unite = (await ConditionnementsTests.ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);

        await FixerAsync(gerant, unite.Id, 250m);
        await FixerAsync(gerant, carton.Id, 5500m); // RG45 : le carton coûte moins que 24 unités
        var parArticle = await caissier.GetFromJsonAsync<List<PrixConditionnement>>($"/api/prix/articles/{article.Id}", Annulation);
        var prixCarton = await caissier.GetFromJsonAsync<PrixApplicable>($"/api/prix/conditionnements/{carton.Id}", Annulation);

        Assert.Equal([250m, 5500m], parArticle!.Select(p => p.PrixTtc));
        Assert.Equal(5500m, prixCarton!.PrixTtc);
        Assert.Equal(AujourdhuiDouala, prixCarton.DebutValidite);
    }

    [Fact]
    public async Task Un_prix_programme_ne_s_applique_qu_a_sa_date()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (article, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 6);
        var dans7Jours = AujourdhuiDouala.AddDays(7);

        await FixerAsync(gerant, carton.Id, 1200m);
        await FixerAsync(gerant, carton.Id, 1300m, dans7Jours);
        var prix = (await gerant.GetFromJsonAsync<List<PrixConditionnement>>($"/api/prix/articles/{article.Id}", Annulation))!
            .Single(p => p.ConditionnementId == carton.Id);
        var applicable = await gerant.GetFromJsonAsync<PrixApplicable>($"/api/prix/conditionnements/{carton.Id}", Annulation);

        Assert.Equal(1200m, prix.PrixTtc);
        Assert.Equal(1300m, prix.ProchainPrixTtc);
        Assert.Equal(dans7Jours, prix.ProchaineValidite);
        Assert.Equal(1200m, applicable!.PrixTtc);
    }

    [Fact]
    public async Task Un_nouveau_prix_le_meme_jour_remplace_le_precedent_sans_effacer_l_historique()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 6);

        await FixerAsync(gerant, carton.Id, 1000m);
        await FixerAsync(gerant, carton.Id, 1100m);
        var applicable = await gerant.GetFromJsonAsync<PrixApplicable>($"/api/prix/conditionnements/{carton.Id}", Annulation);
        var historique = await gerant.GetFromJsonAsync<List<Historique>>($"/api/prix/conditionnements/{carton.Id}/historique", Annulation);

        Assert.Equal(1100m, applicable!.PrixTtc);
        Assert.Equal([1100m, 1000m], historique!.Select(h => h.PrixTtc));
    }

    [Fact]
    public async Task Un_conditionnement_sans_prix_n_a_pas_de_prix_applicable()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 6);

        var reponse = await gerant.GetAsync($"/api/prix/conditionnements/{carton.Id}", Annulation);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    [Theory]
    [InlineData(0, 0, "Prix.PrixNonPositif")]
    [InlineData(100.00001, 0, "Prix.PrecisionPrix")]
    [InlineData(100, -1, "Prix.DateDansLePasse")]
    public async Task Un_prix_invalide_est_refuse(decimal prix, int decalageJours, string code)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 6);

        var reponse = await gerant.PostAsJsonAsync(
            $"/api/prix/conditionnements/{carton.Id}",
            new { prixTtc = prix, debutValidite = AujourdhuiDouala.AddDays(decalageJours) },
            Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Seul_le_gerant_fixe_les_prix()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var (_, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 6);

        var reponse = await comptable.PostAsJsonAsync($"/api/prix/conditionnements/{carton.Id}", new { prixTtc = 100 }, Annulation);
        var inconnu = await gerant.PostAsJsonAsync($"/api/prix/conditionnements/{Guid.NewGuid()}", new { prixTtc = 100 }, Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    [Fact]
    public async Task Le_regime_de_TVA_par_defaut_est_non_assujetti_au_taux_legal()
    {
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);

        var regime = await caissier.GetFromJsonAsync<Regime>("/api/prix/regime", Annulation);

        Assert.False(regime!.Assujetti);
        Assert.Equal(19.25m, regime.TauxPourcent);
        Assert.Equal(0m, regime.TauxApplique);
    }

    internal static async Task FixerAsync(HttpClient gerant, Guid conditionnementId, decimal prixTtc, DateOnly? debut = null)
    {
        var reponse = await gerant.PostAsJsonAsync($"/api/prix/conditionnements/{conditionnementId}", new { prixTtc, debutValidite = debut }, Annulation);
        reponse.EnsureSuccessStatusCode();
    }

    private sealed record PrixConditionnement(Guid ConditionnementId, decimal? PrixTtc, decimal? ProchainPrixTtc, DateOnly? ProchaineValidite);

    private sealed record PrixApplicable(Guid ConditionnementId, decimal PrixTtc, DateOnly DebutValidite);

    private sealed record Historique(decimal PrixTtc, DateOnly DebutValidite, string SaisiPar);

    private sealed record Regime(bool Assujetti, decimal TauxPourcent, decimal TauxApplique);
}
