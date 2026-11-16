using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Referentiel.Contracts;
using GestionStock.Stock.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Api.IntegrationTests;

/// <summary>Frais d'approche des réceptions (RG30, décision 18) et revalorisation des frais tardifs.</summary>
public sealed class FraisApprocheTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Les_frais_saisis_avant_validation_entrent_dans_le_cout_d_entree()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 10m, 500m), (b.ArticleB, 20m, 250m));

        // Transport au prorata de la valeur (5 000 / 5 000) : 500 par ligne ; manutention au prorata des quantités
        // (10 / 20) : 100 et 200.
        await AjouterFraisAsync(b.Comptable, reception.Id, "Transport", 1000m, null);
        var detail = await AjouterFraisAsync(b.Comptable, reception.Id, "Autre", 300m, "Quantite", "Manutention");
        await ValiderAsync(b, reception.Id);

        Assert.Equal((10000m, 1300m, 11300m), (detail.Valeur, detail.TotalFrais, detail.MontantTotal));
        Assert.Equal([(600m, 560m), (700m, 285m)], detail.Lignes.Select(l => (l.FraisApprocheAlloues, l.CoutEntree)));
        Assert.Equal(560m, (await SoldeAsync(b, b.ArticleA)).Cump);
        Assert.Equal(285m, (await SoldeAsync(b, b.ArticleB)).Cump);
    }

    [Fact]
    public async Task Le_reliquat_d_arrondi_va_a_une_ligne_et_la_somme_tombe_juste()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 1m, 100m), (b.ArticleB, 1m, 100m), (b.ArticleC, 1m, 100m));

        var detail = await AjouterFraisAsync(b.Gerant, reception.Id, "Douane", 100m, "Valeur");

        Assert.Equal([33.3334m, 33.3333m, 33.3333m], detail.Lignes.Select(l => l.FraisApprocheAlloues));
        Assert.Equal(100m, detail.Lignes.Sum(l => l.FraisApprocheAlloues));
    }

    [Fact]
    public async Task Les_frais_d_un_brouillon_suivent_ses_lignes_et_peuvent_etre_retires()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 10m, 100m));
        var avecFrais = await AjouterFraisAsync(b.Comptable, reception.Id, "Transport", 400m, null);

        var modifiee = await Lire<Reception>(await b.Magasinier.PutAsJsonAsync(
            $"/api/achats/receptions/{reception.Id}",
            Saisie(b, (b.ArticleA, 10m, 100m), (b.ArticleB, 10m, 300m)),
            Annulation));
        var sansFrais = await Lire<Reception>(await b.Comptable.DeleteAsync($"/api/achats/receptions/{reception.Id}/frais/{avecFrais.Frais.Single().Id}", Annulation));

        Assert.Equal([100m, 300m], modifiee.Lignes.Select(l => l.FraisApprocheAlloues));
        Assert.Empty(sansFrais.Frais);
        Assert.All(sansFrais.Lignes, l => Assert.Equal(0m, l.FraisApprocheAlloues));
    }

    [Fact]
    public async Task Un_frais_tardif_revalorise_le_stock_restant_et_garde_la_part_deja_vendue()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 10m, 1000m));
        await ValiderAsync(b, reception.Id);
        await VendreAsync(b.ArticleA, 4m);

        // 2 000 XAF de douane pour 10 unités : 200 par unité ; 6 encore en stock, 4 déjà vendues.
        var detail = await AjouterFraisAsync(b.Comptable, reception.Id, "Douane", 2000m, null);
        var solde = await SoldeAsync(b, b.ArticleA);
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleA}/mouvements", Annulation);

        var frais = detail.Frais.Single();
        Assert.True(frais.ApresValidation);
        Assert.Equal(800m, frais.MontantNonIncorpore);
        Assert.Equal(6m, solde.QuantitePhysique);
        Assert.Equal(1200m, solde.Cump);
        Assert.Equal(("Revalorisation", 0m, 200m, reception.Id), (journal![0].Type, journal[0].Variation, journal[0].CoutUnitaire, journal[0].DocumentId));
        Assert.Equal(12000m, detail.MontantTotal);
    }

    [Fact]
    public async Task Un_frais_tardif_sur_une_marchandise_toute_vendue_n_entre_pas_dans_le_stock()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 5m, 1000m));
        await ValiderAsync(b, reception.Id);
        await VendreAsync(b.ArticleA, 5m);

        var detail = await AjouterFraisAsync(b.Comptable, reception.Id, "Transport", 500m, null);
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleA}/mouvements", Annulation);

        Assert.Equal(500m, detail.Frais.Single().MontantNonIncorpore);
        Assert.DoesNotContain(journal!, m => m.Type == "Revalorisation");
    }

    [Fact]
    public async Task Un_frais_ne_se_retire_plus_apres_la_validation()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 1m, 100m));
        var detail = await AjouterFraisAsync(b.Comptable, reception.Id, "Transport", 10m, null);
        await ValiderAsync(b, reception.Id);

        var retrait = await b.Comptable.DeleteAsync($"/api/achats/receptions/{reception.Id}/frais/{detail.Frais.Single().Id}", Annulation);

        Assert.Equal(HttpStatusCode.Conflict, retrait.StatusCode);
        Assert.Equal("Achats.ReceptionDejaValidee", (await IdentiteTests.LireProblemeAsync(retrait)).Code);
    }

    [Theory]
    [InlineData("Transport", 0, "Valeur", "Achats.MontantFraisInvalide")]
    [InlineData("Transport", 10.12345, "Valeur", "Achats.MontantFraisInvalide")]
    [InlineData("Transport", 100, "Valeur", "Achats.RepartitionImpossible")]
    public async Task Un_frais_invalide_est_refuse(string type, decimal montant, string cle, string code)
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 5m, 0m));

        var reponse = await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception.Id}/frais", new { type, montant, cle }, Annulation);
        var parQuantite = await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception.Id}/frais", new { type, montant = 100m, cle = "Quantite" }, Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
        Assert.Equal(HttpStatusCode.OK, parQuantite.StatusCode);
    }

    [Fact]
    public async Task Seuls_le_Comptable_et_le_Gerant_saisissent_les_frais()
    {
        var b = await BoutiqueAsync();
        var reception = await CreerReceptionAsync(b, (b.ArticleA, 1m, 100m));

        var magasinier = await b.Magasinier.PostAsJsonAsync($"/api/achats/receptions/{reception.Id}/frais", new { type = "Transport", montant = 10m }, Annulation);
        var gerant = await b.Gerant.PostAsJsonAsync($"/api/achats/receptions/{reception.Id}/frais", new { type = "Transport", montant = 10m }, Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, magasinier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gerant.StatusCode);
    }

    // --- Outils --------------------------------------------------------------------------------------------------

    private async Task<Boutique> BoutiqueAsync()
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var articles = new List<Guid>();
        foreach (var libelle in new[] { "Riz frais A", "Huile frais B", "Sucre frais C" })
        {
            articles.Add((await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), libelle)).Id);
        }

        var fournisseur = await Lire<Identifiant>(await magasinier.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20] }, Annulation));
        return new Boutique(gerant, magasinier, comptable, fournisseur.Id, articles[0], articles[1], articles[2]);
    }

    private static object Saisie(Boutique b, params (Guid Article, decimal Quantite, decimal Cout)[] lignes) =>
        new { fournisseurId = b.Fournisseur, lignes = lignes.Select(l => new { articleId = l.Article, quantite = l.Quantite, coutUnitaire = l.Cout }) };

    private static async Task<Reception> CreerReceptionAsync(Boutique b, params (Guid Article, decimal Quantite, decimal Cout)[] lignes)
    {
        var reponse = await b.Magasinier.PostAsJsonAsync("/api/achats/receptions", Saisie(b, lignes), Annulation);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return await Lire<Reception>(reponse);
    }

    private static async Task<Reception> AjouterFraisAsync(HttpClient client, Guid reception, string type, decimal montant, string? cle, string? libelle = null)
    {
        var reponse = await client.PostAsJsonAsync($"/api/achats/receptions/{reception}/frais", new { type, montant, cle, libelle }, Annulation);
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<Reception>(reponse);
    }

    private static async Task ValiderAsync(Boutique b, Guid reception) =>
        (await b.Magasinier.PostAsync($"/api/achats/receptions/{reception}/validation", null, Annulation)).EnsureSuccessStatusCode();

    private static async Task<Solde> SoldeAsync(Boutique b, Guid article) =>
        (await b.Gerant.GetFromJsonAsync<List<Solde>>("/api/stock/soldes", Annulation))!.Single(s => s.ArticleId == article);

    /// <summary>Sortie de caisse écrite directement par le contrat du module Stock (sans session de caisse).</summary>
    private async Task VendreAsync(Guid article, decimal quantite)
    {
        await using var portee = api.Services.CreateAsyncScope();
        portee.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "caisse-test")], "Test")),
        };
        var site = await portee.ServiceProvider.GetRequiredService<IConsultationReferentiel>().ObtenirSiteBoutiqueAsync(Annulation);
        var mouvements = portee.ServiceProvider.GetRequiredService<IServiceMouvementsStock>();
        await portee.ServiceProvider.GetRequiredService<IUniteDeTravail>().ExecuterAsync(
            jeton => mouvements.EnregistrerAsync(
                new DemandeMouvement(
                    TypeMouvementStock.VenteCaisse,
                    article,
                    site.SiteId,
                    new EmplacementMouvement(site.EmplacementVenteId, TypeEmplacementStock.Vente),
                    Destination: null,
                    quantite,
                    0m,
                    "TicketCaisse",
                    Guid.NewGuid(),
                    "caisse-test",
                    Guid.NewGuid()),
                jeton),
            Annulation);
    }

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Magasinier, HttpClient Comptable, Guid Fournisseur, Guid ArticleA, Guid ArticleB, Guid ArticleC);

    private sealed record Identifiant(Guid Id);

    private sealed record Reception(Guid Id, string Statut, decimal Valeur, decimal TotalFrais, decimal MontantTotal, List<Ligne> Lignes, List<Frais> Frais);

    private sealed record Ligne(Guid ArticleId, decimal Quantite, decimal CoutUnitaire, decimal FraisApprocheAlloues, decimal CoutEntree);

    private sealed record Frais(Guid Id, string Type, decimal Montant, string Cle, bool ApresValidation, decimal MontantNonIncorpore);

    private sealed record Solde(Guid ArticleId, decimal QuantitePhysique, decimal Cump);

    private sealed record Mouvement(string Type, decimal Variation, decimal CoutUnitaire, Guid DocumentId);
}
