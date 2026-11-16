using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class ReferentielTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    /// <summary>Code d'article neuf pour chaque test (base partagée).</summary>
    internal static string NouveauCode() => $"ART-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    [Fact]
    public async Task Le_gerant_cree_un_article_que_tous_les_roles_peuvent_consulter()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var code = NouveauCode();

        var creation = await gerant.PostAsJsonAsync(
            "/api/referentiel/articles",
            new { code = $"  {code} ", libelle = "Riz parfumé 25 kg", libelleTraduit = "Fragrant rice 25 kg", uniteStock = "kg", precisionQuantite = 3 },
            Annulation);
        var cree = await creation.Content.ReadFromJsonAsync<Article>(Annulation);
        var liste = await caissier.GetFromJsonAsync<List<Article>>($"/api/referentiel/articles?recherche={code}", Annulation);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        Assert.Equal(code, cree!.Code);
        Assert.True(cree.Actif);
        Assert.Equal([cree], liste);
    }

    [Fact]
    public async Task La_recherche_porte_aussi_sur_le_libelle_traduit_sans_tenir_compte_de_la_casse()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var marqueur = Guid.NewGuid().ToString("N")[..8];
        await CreerAsync(gerant, NouveauCode(), $"Huile {marqueur}", libelleTraduit: $"Oil {marqueur.ToUpperInvariant()}");

        var liste = await gerant.GetFromJsonAsync<List<Article>>($"/api/referentiel/articles?recherche=oil {marqueur}", Annulation);

        Assert.Single(liste!);
    }

    [Fact]
    public async Task Un_article_desactive_disparait_de_la_liste_par_defaut_mais_reste_consultable()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var code = NouveauCode();
        var article = await CreerAsync(gerant, code, "Savon");

        var modification = await gerant.PutAsJsonAsync(
            $"/api/referentiel/articles/{article.Id}",
            new { libelle = "Savon de Marseille", libelleTraduit = (string?)null, actif = false },
            Annulation);
        var actifs = await gerant.GetFromJsonAsync<List<Article>>($"/api/referentiel/articles?recherche={code}", Annulation);
        var tous = await gerant.GetFromJsonAsync<List<Article>>($"/api/referentiel/articles?recherche={code}&inclureInactifs=true", Annulation);
        var lu = await gerant.GetFromJsonAsync<Article>($"/api/referentiel/articles/{article.Id}", Annulation);

        Assert.Equal(HttpStatusCode.OK, modification.StatusCode);
        Assert.Empty(actifs!);
        Assert.Single(tous!);
        Assert.Equal("Savon de Marseille", lu!.Libelle);
        Assert.False(lu.Actif);
    }

    [Fact]
    public async Task Un_code_deja_utilise_est_refuse()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var code = NouveauCode();
        await CreerAsync(gerant, code, "Sucre");

        var doublon = await gerant.PostAsJsonAsync(
            "/api/referentiel/articles",
            new { code, libelle = "Autre sucre", uniteStock = "pièce", precisionQuantite = 0 },
            Annulation);

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Equal("Referentiel.CodeDejaUtilise", (await IdentiteTests.LireProblemeAsync(doublon)).Code);
    }

    [Theory]
    [InlineData("", "Libellé", "pièce", 0, "Referentiel.CodeObligatoire")]
    [InlineData("CODE", " ", "pièce", 0, "Referentiel.LibelleObligatoire")]
    [InlineData("CODE", "Libellé", "", 0, "Referentiel.UniteObligatoire")]
    [InlineData("CODE", "Libellé", "kg", 4, "Referentiel.PrecisionInvalide")]
    [InlineData("CODE", "Libellé", "kg", -1, "Referentiel.PrecisionInvalide")]
    public async Task Une_saisie_invalide_est_refusee(string code, string libelle, string unite, int precision, string attendu)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var reponse = await gerant.PostAsJsonAsync(
            "/api/referentiel/articles",
            new { code, libelle, uniteStock = unite, precisionQuantite = precision },
            Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(attendu, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Le_magasinier_ne_peut_pas_creer_d_article()
    {
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);

        var reponse = await magasinier.PostAsJsonAsync(
            "/api/referentiel/articles",
            new { code = NouveauCode(), libelle = "Lait", uniteStock = "pièce", precisionQuantite = 0 },
            Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_article_inconnu_renvoie_404()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var reponse = await gerant.GetAsync($"/api/referentiel/articles/{Guid.NewGuid()}", Annulation);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task La_boutique_et_son_emplacement_de_vente_existent_des_l_installation()
    {
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);

        var site = await magasinier.GetFromJsonAsync<Site>("/api/referentiel/site", Annulation);

        Assert.Equal("BOUTIQUE", site!.Code);
        Assert.NotEqual(Guid.Empty, site.EmplacementVenteId);
    }

    internal static async Task<Article> CreerAsync(HttpClient client, string code, string libelle, string unite = "pièce", int precision = 0, string? libelleTraduit = null)
    {
        var reponse = await client.PostAsJsonAsync(
            "/api/referentiel/articles",
            new { code, libelle, libelleTraduit, uniteStock = unite, precisionQuantite = precision },
            Annulation);
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<Article>(Annulation))!;
    }

    internal sealed record Article(Guid Id, string Code, string Libelle, string? LibelleTraduit, string UniteStock, int PrecisionQuantite, bool Actif);

    internal sealed record Site(Guid SiteId, string Code, string Nom, Guid EmplacementVenteId);
}
