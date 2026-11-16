using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class IdentiteTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task L_administrateur_initial_se_connecte_et_voit_ses_permissions()
    {
        using var client = await api.ClientAdministrateurAsync();

        var profil = await client.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation);

        Assert.Equal(ApiDeTest.Administrateur, profil!.Identifiant);
        Assert.Equal([Roles.Administrateur], profil.Roles);
        Assert.Contains(Permissions.UtilisateursGerer, profil.Permissions);
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_est_refuse_avec_un_code_et_un_message_traduit()
    {
        using var client = api.Client();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var reponse = await client.PostAsJsonAsync(
            "/api/identite/connexion",
            new { identifiant = ApiDeTest.Administrateur, motDePasse = "faux" },
            Annulation);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        var probleme = await LireProblemeAsync(reponse);
        Assert.Equal("Identite.IdentifiantsInvalides", probleme.Code);
        Assert.Equal("Incorrect username or password.", probleme.Titre);
    }

    [Fact]
    public async Task Sans_jeton_le_profil_est_refuse()
    {
        using var client = api.Client();

        var reponse = await client.GetAsync("/api/identite/moi", Annulation);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_endpoints_techniques_restent_anonymes()
    {
        using var client = api.Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Annulation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/pret", Annulation)).StatusCode);
    }

    [Fact]
    public async Task Un_magasinier_cree_par_l_administrateur_a_ses_permissions_mais_pas_celles_de_l_administrateur()
    {
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);

        var profil = await magasinier.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation);
        var utilisateurs = await magasinier.GetAsync("/api/identite/utilisateurs", Annulation);

        Assert.Equal([Roles.Magasinier], profil!.Roles);
        Assert.Contains(Permissions.ReceptionsSaisir, profil.Permissions);
        Assert.DoesNotContain(Permissions.UtilisateursGerer, profil.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, utilisateurs.StatusCode);
    }

    [Fact]
    public async Task L_administrateur_liste_les_comptes_avec_leur_role()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var identifiantGerant = (await gerant.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;
        using var administrateur = await api.ClientAdministrateurAsync();

        var comptes = await administrateur.GetFromJsonAsync<List<Compte>>("/api/identite/utilisateurs", Annulation);

        Assert.Contains(comptes!, c => c.Identifiant == identifiantGerant && c.Roles.SequenceEqual([Roles.Gerant]));
    }

    [Theory]
    [InlineData("admin", ApiDeTest.MotDePasseValide, Roles.Caissier, HttpStatusCode.Conflict, "Identite.IdentifiantDejaUtilise")]
    [InlineData("nouveau-role", ApiDeTest.MotDePasseValide, "Acheteur", HttpStatusCode.BadRequest, "Identite.RoleInconnu")]
    [InlineData("mdp-faible", "abc", Roles.Caissier, HttpStatusCode.BadRequest, "Identite.MotDePasseRefuse")]
    [InlineData("  ", ApiDeTest.MotDePasseValide, Roles.Caissier, HttpStatusCode.BadRequest, "Identite.IdentifiantObligatoire")]
    public async Task Une_creation_de_compte_invalide_est_refusee(string identifiant, string motDePasse, string role, HttpStatusCode attendu, string code)
    {
        using var administrateur = await api.ClientAdministrateurAsync();

        var reponse = await administrateur.PostAsJsonAsync("/api/identite/utilisateurs", new { identifiant, motDePasse, role }, Annulation);

        Assert.Equal(attendu, reponse.StatusCode);
        Assert.Equal(code, (await LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Le_jeton_de_renouvellement_donne_un_nouveau_jeton_d_acces_valide()
    {
        using var client = api.Client();
        var premier = await ApiDeTest.ConnecterAsync(client, ApiDeTest.Administrateur, ApiDeTest.MotDePasseAdministrateur);

        var reponse = await client.PostAsJsonAsync("/api/identite/rafraichir", new { jetonRenouvellement = premier.RefreshToken }, Annulation);
        reponse.EnsureSuccessStatusCode();
        var second = await reponse.Content.ReadFromJsonAsync<JetonAcces>(Annulation);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second!.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identite/moi", Annulation)).StatusCode);
    }

    [Fact]
    public async Task Un_jeton_de_renouvellement_invalide_est_refuse()
    {
        using var client = api.Client();

        var reponse = await client.PostAsJsonAsync("/api/identite/rafraichir", new { jetonRenouvellement = "invalide" }, Annulation);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal("Identite.SessionExpiree", (await LireProblemeAsync(reponse)).Code);
    }

    internal static async Task<(string Code, string Titre)> LireProblemeAsync(HttpResponseMessage reponse)
    {
        using var document = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(Annulation));
        return (document.RootElement.GetProperty("code").GetString()!, document.RootElement.GetProperty("title").GetString()!);
    }

    private sealed record Profil(string Identifiant, List<string> Roles, List<string> Permissions);

    private sealed record Compte(Guid Id, string Identifiant, List<string> Roles);
}
