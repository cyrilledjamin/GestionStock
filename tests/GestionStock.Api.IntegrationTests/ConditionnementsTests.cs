using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class ConditionnementsTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_nouvel_article_a_d_office_un_conditionnement_a_l_unite()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Savon", unite: "pièce");

        var conditionnements = await ListerAsync(gerant, article.Id);

        var unite = Assert.Single(conditionnements);
        Assert.Equal("pièce", unite.Libelle);
        Assert.Equal("Unite", unite.Niveau);
        Assert.Equal(1m, unite.QuantiteUniteStock);
    }

    [Fact]
    public async Task Un_carton_contient_un_nombre_entier_de_pieces_mais_un_sac_peut_contenir_des_kg_decimaux()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var savon = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Savon", unite: "pièce");
        var riz = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Riz", unite: "kg", precision: 3);

        var carton = await gerant.PostAsJsonAsync($"/api/referentiel/articles/{savon.Id}/conditionnements", new { libelle = "Carton de 24", niveau = "Carton", quantiteUniteStock = 24 }, Annulation);
        var cartonDecimal = await gerant.PostAsJsonAsync($"/api/referentiel/articles/{savon.Id}/conditionnements", new { libelle = "Carton", niveau = "Carton", quantiteUniteStock = 2.5m }, Annulation);
        var sac = await gerant.PostAsJsonAsync($"/api/referentiel/articles/{riz.Id}/conditionnements", new { libelle = "Sac 22,5 kg", niveau = "Sac", quantiteUniteStock = 22.5m }, Annulation);

        Assert.Equal(HttpStatusCode.Created, carton.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cartonDecimal.StatusCode);
        Assert.Equal("Referentiel.FacteurInvalide", (await IdentiteTests.LireProblemeAsync(cartonDecimal)).Code);
        Assert.Equal(HttpStatusCode.Created, sac.StatusCode);
    }

    [Fact]
    public async Task Un_code_EAN_valide_identifie_son_conditionnement_au_scan()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ArticleAvecCartonAsync(gerant, 12);
        var ean = NouvelEan13();

        var ajout = await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur = ean }, Annulation);
        var scan = await caissier.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/{ean}", Annulation);

        Assert.Equal(HttpStatusCode.OK, ajout.StatusCode);
        Assert.Equal(article.Id, scan!.ArticleId);
        Assert.Equal(carton.Id, scan.ConditionnementId);
        Assert.Equal(12m, scan.QuantiteUniteStock);
        Assert.Equal(ean, scan.CodeScanne);
    }

    [Fact]
    public async Task Un_UPC_A_se_lit_aussi_en_EAN_13_avec_un_zero_de_tete()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);
        var upc = AvecCle(ChiffresAuHasard(11));
        (await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur = upc }, Annulation)).EnsureSuccessStatusCode();

        var scan = await gerant.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/0{upc}", Annulation);

        Assert.Equal(carton.Id, scan!.ConditionnementId);
    }

    [Theory]
    [InlineData("4006381333932")] // clé fausse
    [InlineData("12345")]
    [InlineData("ABCDEFGHIJKLM")]
    public async Task Un_code_invalide_est_refuse(string valeur)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);

        var reponse = await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur }, Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("Referentiel.CodeInvalide", (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Un_code_actif_n_est_attribue_qu_une_fois_mais_peut_etre_reattribue_apres_desactivation()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, premier) = await ArticleAvecCartonAsync(gerant, 6);
        var (_, second) = await ArticleAvecCartonAsync(gerant, 10);
        var ean = NouvelEan13();
        var avecCode = await (await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{premier.Id}/codes", new { valeur = ean }, Annulation))
            .Content.ReadFromJsonAsync<Conditionnement>(Annulation);

        var doublon = await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{second.Id}/codes", new { valeur = ean }, Annulation);
        (await gerant.PostAsync($"/api/referentiel/codes/{avecCode!.Codes.Single().Id}/desactivation", null, Annulation)).EnsureSuccessStatusCode();
        var reattribution = await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{second.Id}/codes", new { valeur = ean }, Annulation);
        var scan = await gerant.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/{ean}", Annulation);

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Equal("Referentiel.CodeDejaAttribue", (await IdentiteTests.LireProblemeAsync(doublon)).Code);
        Assert.Equal(HttpStatusCode.OK, reattribution.StatusCode);
        Assert.Equal(second.Id, scan!.ConditionnementId);
    }

    [Fact]
    public async Task Un_code_interne_est_genere_et_reconnu_au_scan()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);

        var reponse = await gerant.PostAsync($"/api/referentiel/conditionnements/{carton.Id}/codes/interne", null, Annulation);
        var code = (await reponse.Content.ReadFromJsonAsync<Conditionnement>(Annulation))!.Codes.Single();
        var scan = await gerant.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/{code.Valeur}", Annulation);

        Assert.Matches("^INT\\d{6}$", code.Valeur);
        Assert.Equal("Code128Interne", code.Type);
        Assert.True(code.Principal);
        Assert.Equal(carton.Id, scan!.ConditionnementId);
    }

    [Fact]
    public async Task Le_code_de_l_article_saisi_au_clavier_donne_le_conditionnement_a_l_unite()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (article, _) = await ArticleAvecCartonAsync(gerant, 6);

        var scan = await gerant.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/{article.Code.ToLowerInvariant()}", Annulation);
        var inconnu = await gerant.GetAsync($"/api/referentiel/scan/{NouvelEan13()}", Annulation);

        Assert.Equal(1m, scan!.QuantiteUniteStock);
        Assert.Null(scan.CodeScanne);
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    [Fact]
    public async Task La_recherche_propose_chaque_conditionnement_actif_de_l_article()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (article, _) = await ArticleAvecCartonAsync(gerant, 6);

        var vendables = await gerant.GetFromJsonAsync<List<Vendable>>($"/api/referentiel/vendables?recherche={article.Code}", Annulation);

        Assert.Equal([1m, 6m], vendables!.Select(v => v.QuantiteUniteStock));
    }

    [Fact]
    public async Task Le_conditionnement_a_l_unite_ne_se_desactive_pas_et_les_autres_si()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (article, carton) = await ArticleAvecCartonAsync(gerant, 6);
        var unite = (await ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);

        var refus = await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{unite.Id}", new { libelle = unite.Libelle, actif = false }, Annulation);
        var desactivation = await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}", new { libelle = "Ancien carton", actif = false }, Annulation);
        var vendables = await gerant.GetFromJsonAsync<List<Vendable>>($"/api/referentiel/vendables?recherche={article.Code}", Annulation);

        Assert.Equal("Referentiel.UniteNonDesactivable", (await IdentiteTests.LireProblemeAsync(refus)).Code);
        Assert.Equal(HttpStatusCode.OK, desactivation.StatusCode);
        Assert.Single(vendables!);
    }

    [Fact]
    public async Task Le_gerant_choisit_les_articles_rapides_de_la_caisse()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ArticleAvecCartonAsync(gerant, 6);
        var unite = (await ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);

        var marque = await Lire<Conditionnement>(await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{unite.Id}/vente-rapide", new { venteRapide = true }, Annulation));
        (await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/vente-rapide", new { venteRapide = true }, Annulation)).EnsureSuccessStatusCode();
        var avant = await RapidesAsync(caissier);
        var demarque = await Lire<Conditionnement>(await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{unite.Id}/vente-rapide", new { venteRapide = false }, Annulation));
        var apres = await RapidesAsync(caissier);

        Assert.True(marque.VenteRapide);
        Assert.Contains(avant, v => v.ConditionnementId == unite.Id);
        Assert.Contains(avant, v => v.ConditionnementId == carton.Id);
        Assert.False(demarque.VenteRapide);
        Assert.DoesNotContain(apres, v => v.ConditionnementId == unite.Id);
        Assert.Contains(apres, v => v.ConditionnementId == carton.Id);
    }

    [Fact]
    public async Task Un_conditionnement_desactive_quitte_les_articles_rapides_et_ne_peut_y_revenir()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);
        (await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/vente-rapide", new { venteRapide = true }, Annulation)).EnsureSuccessStatusCode();

        var desactive = await Lire<Conditionnement>(await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}", new { libelle = "Ancien carton", actif = false }, Annulation));
        var refus = await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/vente-rapide", new { venteRapide = true }, Annulation);

        Assert.False(desactive.VenteRapide);
        Assert.DoesNotContain(await RapidesAsync(gerant), v => v.ConditionnementId == carton.Id);
        Assert.Equal((HttpStatusCode.BadRequest, "Referentiel.ConditionnementInactif"), (refus.StatusCode, (await IdentiteTests.LireProblemeAsync(refus)).Code));
    }

    [Fact]
    public async Task Le_caissier_voit_les_articles_rapides_sans_pouvoir_les_choisir()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);

        var choix = await caissier.PutAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/vente-rapide", new { venteRapide = true }, Annulation);
        var liste = await caissier.GetAsync("/api/referentiel/vendables/rapides", Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, choix.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);
    }

    [Fact]
    public async Task Le_catalogue_de_la_caisse_donne_chaque_conditionnement_actif_avec_ses_codes_actifs()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ArticleAvecCartonAsync(gerant, 12);
        var (_, ancien) = await ArticleAvecCartonAsync(gerant, 6);
        var ean = NouvelEan13();
        var desactive = NouvelEan13();
        (await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur = ean }, Annulation)).EnsureSuccessStatusCode();
        var avecCode = await Lire<Conditionnement>(await gerant.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur = desactive }, Annulation));
        (await gerant.PostAsync($"/api/referentiel/codes/{avecCode.Codes.Single(c => c.Valeur == desactive).Id}/desactivation", null, Annulation)).EnsureSuccessStatusCode();
        (await gerant.PutAsJsonAsync($"/api/referentiel/conditionnements/{ancien.Id}", new { libelle = "Ancien carton", actif = false }, Annulation)).EnsureSuccessStatusCode();

        var catalogue = await caissier.GetFromJsonAsync<Catalogue>("/api/referentiel/catalogue-caisse", Annulation);

        var duCarton = catalogue!.Vendables.Single(v => v.ConditionnementId == carton.Id);
        var unite = catalogue.Vendables.Single(v => v.ArticleId == article.Id && v.EstUnite);
        Assert.Equal([ean], duCarton.Codes);
        Assert.Equal((12m, false), (duCarton.QuantiteUniteStock, duCarton.EstUnite));
        Assert.Equal(article.Code, unite.CodeArticle);
        Assert.DoesNotContain(catalogue.Vendables, v => v.ConditionnementId == ancien.Id);
    }

    [Fact]
    public async Task Le_magasinier_ne_gere_pas_les_codes()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var (_, carton) = await ArticleAvecCartonAsync(gerant, 6);

        var reponse = await magasinier.PostAsJsonAsync($"/api/referentiel/conditionnements/{carton.Id}/codes", new { valeur = NouvelEan13() }, Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    /// <summary>EAN-13 valide et neuf (base partagée entre les tests).</summary>
    internal static string NouvelEan13() => AvecCle(ChiffresAuHasard(12));

    internal static async Task<(ReferentielTests.Article Article, Conditionnement Carton)> ArticleAvecCartonAsync(HttpClient gerant, decimal facteur)
    {
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Article conditionné");
        var reponse = await gerant.PostAsJsonAsync(
            $"/api/referentiel/articles/{article.Id}/conditionnements",
            new { libelle = $"Carton de {facteur}", niveau = "Carton", quantiteUniteStock = facteur },
            Annulation);
        reponse.EnsureSuccessStatusCode();
        return (article, (await reponse.Content.ReadFromJsonAsync<Conditionnement>(Annulation))!);
    }

    private static async Task<List<Vendable>> RapidesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Vendable>>("/api/referentiel/vendables/rapides", Annulation))!;

    private static async Task<T> Lire<T>(HttpResponseMessage reponse)
    {
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;
    }

    internal static async Task<List<Conditionnement>> ListerAsync(HttpClient client, Guid articleId) =>
        (await client.GetFromJsonAsync<List<Conditionnement>>($"/api/referentiel/articles/{articleId}/conditionnements", Annulation))!;

    private static string ChiffresAuHasard(int nombre) =>
        string.Concat(Enumerable.Range(0, nombre).Select(_ => (char)('0' + RandomNumberGenerator.GetInt32(10))));

    /// <summary>Ajoute la clé de contrôle GS1 modulo 10.</summary>
    private static string AvecCle(string chiffres)
    {
        var somme = chiffres.Reverse().Select((c, i) => (c - '0') * (i % 2 == 0 ? 3 : 1)).Sum();
        return chiffres + (char)('0' + ((10 - (somme % 10)) % 10));
    }

    internal sealed record Code(Guid Id, string Valeur, string Type, bool Principal, bool Actif);

    internal sealed record Conditionnement(Guid Id, Guid ArticleId, string Libelle, string Niveau, decimal QuantiteUniteStock, bool Actif, List<Code> Codes, bool VenteRapide = false);

    private sealed record VendableCatalogue(Guid ArticleId, string CodeArticle, Guid ConditionnementId, decimal QuantiteUniteStock, bool EstUnite, bool VenteRapide, List<string> Codes);

    private sealed record Catalogue(DateTime GenereLeUtc, List<VendableCatalogue> Vendables);

    internal sealed record Vendable(Guid ArticleId, string CodeArticle, Guid ConditionnementId, string LibelleConditionnement, decimal QuantiteUniteStock, string? CodeScanne);
}
