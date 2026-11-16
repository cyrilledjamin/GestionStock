using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Api.IntegrationTests;

public sealed class InventaireTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    // --- Inventaires ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_manque_constate_et_valide_sort_du_stock_au_CUMP()
    {
        var b = await BoutiqueAsync(stockInitial: 10m, cout: 500m);

        var inventaire = await CreerPartielAsync(b, b.Article);
        var compte = (await CompterAsync(b, inventaire, b.Article, 8m)).Lignes.Single();
        var valide = await ValiderAsync(b, inventaire);
        var journal = await JournalAsync(b);

        Assert.Equal((10m, 8m, -2m), (compte.QuantiteTheorique!.Value, compte.QuantiteComptee!.Value, compte.Ecart!.Value));
        Assert.Equal("Valide", valide.Inventaire.Statut);
        Assert.Equal(("AjustementInventaire", -2m, 500m), (journal[0].Type, journal[0].Variation, journal[0].CoutUnitaire));
        Assert.Equal(8m, await SoldeAsync(b));
    }

    [Fact]
    public async Task Un_inventaire_partiel_demarre_vide_et_se_remplit_au_comptage()
    {
        var b = await BoutiqueAsync(stockInitial: 10m, cout: 500m);

        var inventaire = await CreerPartielAsync(b);
        var vide = await b.Comptable.PostAsync($"/api/inventaires/{inventaire}/validation", null, Annulation);
        var compte = await CompterAsync(b, inventaire, b.Article, 9m);
        await ValiderAsync(b, inventaire);

        Assert.Equal(HttpStatusCode.BadRequest, vide.StatusCode);
        Assert.Contains("Inventaire.InventaireSansLigne", await vide.Content.ReadAsStringAsync(Annulation), StringComparison.Ordinal);
        Assert.Equal(-1m, compte.Lignes.Single().Ecart);
        Assert.Equal(9m, await SoldeAsync(b));
    }

    [Fact]
    public async Task Un_surplus_entre_au_CUMP_sans_le_modifier()
    {
        var b = await BoutiqueAsync(stockInitial: 10m, cout: 500m);

        var inventaire = await CreerPartielAsync(b, b.Article);
        await CompterAsync(b, inventaire, b.Article, 13m);
        await ValiderAsync(b, inventaire);
        var solde = await SoldeCompletAsync(b);

        Assert.Equal(13m, solde.QuantitePhysique);
        Assert.Equal(500m, solde.Cump);
    }

    [Fact]
    public async Task L_inventaire_d_ouverture_valorise_le_stock_compte_au_cout_de_reprise()
    {
        var b = await BoutiqueAsync();
        var ouverture = await AppelerAsync(services => services.GetRequiredService<IServiceInventaire>()
            .CreerOuvertureAsync([new LigneOuverture(b.Article, 725m)], Annulation));

        await CompterAsync(b, ouverture.Id, b.Article, 40m);
        var valide = await ValiderAsync(b, ouverture.Id);
        var solde = await SoldeCompletAsync(b);

        Assert.Matches("^INV-\\d{6}$", ouverture.Numero);
        Assert.Equal("Ouverture", valide.Inventaire.Type);
        Assert.Equal((40m, 725m), (solde.QuantitePhysique, solde.Cump));
    }

    [Fact]
    public async Task Une_marchandise_arrivee_apres_le_comptage_n_est_pas_effacee_par_la_validation()
    {
        var b = await BoutiqueAsync(stockInitial: 10m, cout: 500m);
        var inventaire = await CreerPartielAsync(b, b.Article);
        await CompterAsync(b, inventaire, b.Article, 10m); // théorique relevé : 10, écart nul

        await ReceptionnerAsync(b, 5m, 500m); // la boutique continue de vivre pendant l'inventaire (RG16)
        var valide = await ValiderAsync(b, inventaire);

        Assert.Equal(0m, valide.Lignes.Single().Ecart);
        Assert.Equal(15m, await SoldeAsync(b));
    }

    [Fact]
    public async Task Un_manque_classe_en_freinte_devient_une_demarque()
    {
        var b = await BoutiqueAsync(stockInitial: 25m, cout: 600m, unite: "kg", precision: 3);
        var inventaire = await CreerPartielAsync(b, b.Article);

        var surplusEnFreinte = await b.Magasinier.PostAsJsonAsync($"/api/inventaires/{inventaire}/comptages", new { articleId = b.Article, quantite = 26, motifEcart = "Freinte" }, Annulation);
        await CompterAsync(b, inventaire, b.Article, 24.35m, "Freinte");
        await ValiderAsync(b, inventaire);
        var journal = await JournalAsync(b);

        Assert.Equal("Inventaire.FreinteSurEcartPositif", (await IdentiteTests.LireProblemeAsync(surplusEnFreinte)).Code);
        Assert.Equal(("Demarque", -0.65m), (journal[0].Type, journal[0].Variation));
    }

    [Fact]
    public async Task Un_inventaire_complet_porte_sur_tous_les_articles_actifs_et_exige_qu_ils_soient_comptes()
    {
        var b = await BoutiqueAsync(stockInitial: 3m, cout: 100m);

        var reponse = await b.Magasinier.PostAsJsonAsync("/api/inventaires", new { type = "Complet" }, Annulation);
        var complet = await Lire<Detail>(reponse);
        var validation = await b.Comptable.PostAsync($"/api/inventaires/{complet.Inventaire.Id}/validation", null, Annulation);
        var annule = await Lire<Detail>(await b.Magasinier.PostAsync($"/api/inventaires/{complet.Inventaire.Id}/annulation", null, Annulation));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.Contains(complet.Lignes, l => l.ArticleId == b.Article);
        Assert.Equal("Inventaire.LignesNonComptees", (await IdentiteTests.LireProblemeAsync(validation)).Code);
        Assert.Equal("Annule", annule.Inventaire.Statut);
    }

    [Fact]
    public async Task Un_article_scanne_hors_liste_est_ajoute_a_l_inventaire_partiel()
    {
        var b = await BoutiqueAsync(stockInitial: 4m, cout: 100m);
        var autre = await ReferentielTests.CreerAsync(b.Gerant, ReferentielTests.NouveauCode(), "Article oublié");
        var inventaire = await CreerPartielAsync(b, b.Article);

        var detail = await CompterAsync(b, inventaire, autre.Id, 2m);

        Assert.Equal(2, detail.Lignes.Count);
    }

    [Fact]
    public async Task Seul_le_comptable_valide_et_un_inventaire_termine_ne_change_plus()
    {
        var b = await BoutiqueAsync(stockInitial: 5m, cout: 100m);
        var inventaire = await CreerPartielAsync(b, b.Article);
        await CompterAsync(b, inventaire, b.Article, 5m);

        var parLeMagasinier = await b.Magasinier.PostAsync($"/api/inventaires/{inventaire}/validation", null, Annulation);
        await ValiderAsync(b, inventaire);
        var revalidation = await b.Comptable.PostAsync($"/api/inventaires/{inventaire}/validation", null, Annulation);
        var recomptage = await b.Magasinier.PostAsJsonAsync($"/api/inventaires/{inventaire}/comptages", new { articleId = b.Article, quantite = 1 }, Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, parLeMagasinier.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, revalidation.StatusCode);
        Assert.Equal("Inventaire.InventaireTermine", (await IdentiteTests.LireProblemeAsync(recomptage)).Code);
    }

    [Fact]
    public async Task Une_quantite_comptee_respecte_les_decimales_de_l_article()
    {
        var b = await BoutiqueAsync(stockInitial: 5m, cout: 100m);
        var inventaire = await CreerPartielAsync(b, b.Article);

        var reponse = await b.Magasinier.PostAsJsonAsync($"/api/inventaires/{inventaire}/comptages", new { articleId = b.Article, quantite = 2.5 }, Annulation);

        Assert.Equal("Inventaire.QuantiteInvalide", (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    // --- Démarque ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Une_casse_declaree_puis_validee_sort_du_stock_au_CUMP()
    {
        var b = await BoutiqueAsync(stockInitial: 12m, cout: 400m);

        var declaree = await Lire<Demarque>(await b.Magasinier.PostAsJsonAsync("/api/demarques", new { articleId = b.Article, quantite = 2, motif = "Casse", commentaire = "Carton tombé" }, Annulation));
        var parLeMagasinier = await b.Magasinier.PostAsync($"/api/demarques/{declaree.Id}/validation", null, Annulation);
        var validee = await Lire<Demarque>(await b.Gerant.PostAsync($"/api/demarques/{declaree.Id}/validation", null, Annulation));
        var revalidation = await b.Comptable.PostAsync($"/api/demarques/{declaree.Id}/validation", null, Annulation);
        var journal = await JournalAsync(b);

        Assert.Matches("^DEM-\\d{6}$", declaree.Numero);
        Assert.Equal(HttpStatusCode.Forbidden, parLeMagasinier.StatusCode);
        Assert.Equal("Validee", validee.Statut);
        Assert.Equal(HttpStatusCode.Conflict, revalidation.StatusCode);
        Assert.Equal(("Demarque", -2m, 400m, declaree.Id), (journal[0].Type, journal[0].Variation, journal[0].CoutUnitaire, journal[0].DocumentId));
        Assert.Equal(10m, await SoldeAsync(b));
    }

    [Fact]
    public async Task Une_demarque_refusee_exige_un_motif_et_ne_touche_pas_au_stock()
    {
        var b = await BoutiqueAsync(stockInitial: 6m, cout: 400m);
        var declaree = await Lire<Demarque>(await b.Magasinier.PostAsJsonAsync("/api/demarques", new { articleId = b.Article, quantite = 1, motif = "Vol" }, Annulation));

        var sansMotif = await b.Comptable.PostAsJsonAsync($"/api/demarques/{declaree.Id}/refus", new { motif = " " }, Annulation);
        var refusee = await Lire<Demarque>(await b.Comptable.PostAsJsonAsync($"/api/demarques/{declaree.Id}/refus", new { motif = "Retrouvé en réserve" }, Annulation));

        Assert.Equal("Inventaire.MotifRefusObligatoire", (await IdentiteTests.LireProblemeAsync(sansMotif)).Code);
        Assert.Equal(("Refusee", "Retrouvé en réserve"), (refusee.Statut, refusee.MotifRefus));
        Assert.Equal(6m, await SoldeAsync(b));
    }

    // --- Outillage -----------------------------------------------------------------------------------------------

    private async Task<Boutique> BoutiqueAsync(decimal stockInitial = 0m, decimal cout = 0m, string unite = "pièce", int precision = 0)
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Article inventorié", unite, precision);
        var b = new Boutique(gerant, magasinier, comptable, article.Id);
        if (stockInitial > 0m)
        {
            await ReceptionnerAsync(b, stockInitial, cout);
        }

        return b;
    }

    private static async Task ReceptionnerAsync(Boutique b, decimal quantite, decimal cout)
    {
        var fournisseur = await Lire<Identifiant>(await b.Magasinier.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = "Grossiste inventaire" }, Annulation));
        var reception = await Lire<Identifiant>(await b.Magasinier.PostAsJsonAsync(
            "/api/achats/receptions",
            new { fournisseurId = fournisseur.Id, lignes = new[] { new { articleId = b.Article, quantite, coutUnitaire = cout } } },
            Annulation));
        (await b.Magasinier.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation)).EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreerPartielAsync(Boutique b, params Guid[] articles)
    {
        var reponse = await b.Magasinier.PostAsJsonAsync("/api/inventaires", new { type = "Partiel", articles }, Annulation);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await Lire<Detail>(reponse)).Inventaire.Id;
    }

    private static async Task<Detail> CompterAsync(Boutique b, Guid inventaire, Guid article, decimal quantite, string motifEcart = "Aucun")
    {
        var reponse = await b.Magasinier.PostAsJsonAsync($"/api/inventaires/{inventaire}/comptages", new { articleId = article, quantite, motifEcart }, Annulation);
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<Detail>(reponse);
    }

    private static async Task<Detail> ValiderAsync(Boutique b, Guid inventaire)
    {
        var reponse = await b.Comptable.PostAsync($"/api/inventaires/{inventaire}/validation", null, Annulation);
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<Detail>(reponse);
    }

    private static async Task<List<Mouvement>> JournalAsync(Boutique b) =>
        (await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.Article}/mouvements", Annulation))!;

    private static async Task<Solde> SoldeCompletAsync(Boutique b) =>
        (await b.Gerant.GetFromJsonAsync<List<Solde>>("/api/stock/soldes", Annulation))!.Single(s => s.ArticleId == b.Article);

    private static async Task<decimal> SoldeAsync(Boutique b) => (await SoldeCompletAsync(b)).QuantitePhysique;

    private async Task<T> AppelerAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        // Comme la reprise : dans une unité de travail, avec un utilisateur connecté (le contrat trace l'auteur).
        await using var portee = api.Services.CreateAsyncScope();
        var contexteHttp = portee.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        contexteHttp.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "reprise")], "Test")),
        };
        return await portee.ServiceProvider.GetRequiredService<IUniteDeTravail>().ExecuterAsync(_ => action(portee.ServiceProvider), Annulation);
    }

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Magasinier, HttpClient Comptable, Guid Article);

    private sealed record Identifiant(Guid Id);

    private sealed record Resume(Guid Id, string Numero, string Type, string Statut);

    private sealed record Ligne(Guid ArticleId, decimal? QuantiteTheorique, decimal? QuantiteComptee, decimal? Ecart);

    private sealed record Detail(Resume Inventaire, List<Ligne> Lignes);

    private sealed record Demarque(Guid Id, string Numero, string Statut, string? MotifRefus);

    private sealed record Mouvement(string Type, decimal Variation, decimal CoutUnitaire, Guid DocumentId);

    private sealed record Solde(Guid ArticleId, decimal QuantitePhysique, decimal Cump);
}
