using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Api.IntegrationTests;

/// <summary>Comptes du personnel : fiche, matricule, rôles, mot de passe provisoire, désactivation, journal.</summary>
public sealed class ComptesTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_compte_cree_sans_matricule_en_recoit_un_genere()
    {
        using var administrateur = await api.ClientAdministrateurAsync();

        var premier = await CreerAsync(administrateur, new { prenom = "Awa", nom = "Ngono", roles = new[] { Roles.Caissier }, motDePasse = ApiDeTest.MotDePasseProvisoire });
        var second = await CreerAsync(administrateur, new { prenom = "Paul", nom = "Etoa", roles = new[] { Roles.Magasinier }, motDePasse = ApiDeTest.MotDePasseProvisoire });

        Assert.Matches("^S[0-9]{6}$", premier.Compte.Matricule);
        Assert.Matches("^S[0-9]{6}$", second.Compte.Matricule);
        Assert.NotEqual(premier.Compte.Matricule, second.Compte.Matricule);
        Assert.Equal(("Awa Ngono", true, true), (premier.Compte.NomComplet, premier.Compte.Actif, premier.Compte.MotDePasseProvisoire));
        Assert.Equal("Creation", premier.Journal.Single().Action);
    }

    [Fact]
    public async Task Une_creation_refusee_ne_consomme_pas_de_matricule()
    {
        using var administrateur = await api.ClientAdministrateurAsync();

        // D'autres tests créent des comptes en parallèle : la séquence peut avancer pour eux entre deux lectures.
        // Avec le défaut, chaque refus la fait avancer ; il suffit donc d'un essai sans avance pour conclure.
        var sansAvance = false;
        for (var essai = 0; essai < 5 && !sansAvance; essai++)
        {
            var avant = await DernierNumeroMatriculeAsync();
            var refus = await administrateur.PostAsJsonAsync("/api/identite/utilisateurs", new { prenom = "Awa", nom = "Ngono", roles = new[] { Roles.Caissier }, motDePasse = "abc" }, Annulation);
            Assert.Equal("Identite.MotDePasseRefuse", (await IdentiteTests.LireProblemeAsync(refus)).Code);
            sansAvance = await DernierNumeroMatriculeAsync() == avant;
        }

        Assert.True(sansAvance, "Chaque création refusée a consommé un numéro de matricule.");
    }

    [Fact]
    public async Task Un_compte_peut_cumuler_plusieurs_roles()
    {
        using var client = await api.ClientAvecRoleAsync(Roles.Gerant, Roles.Caissier);

        var profil = await client.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation);

        Assert.Equal([Roles.Caissier, Roles.Gerant], profil!.Roles);
        Assert.Contains(Permissions.CaisseVendre, profil.Permissions);
        Assert.Contains(Permissions.PrixGerer, profil.Permissions);
        Assert.Equal(("Test Gerant", false), (profil.NomComplet, profil.MotDePasseProvisoire));
    }

    [Fact]
    public async Task Avec_un_mot_de_passe_provisoire_seul_le_changement_est_permis()
    {
        var matricule = await api.CreerCompteAsync([Roles.Magasinier]);
        using var provisoire = await api.ClientConnecteAsync(matricule, ApiDeTest.MotDePasseProvisoire);

        var profil = await provisoire.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation);
        var stock = await provisoire.GetAsync("/api/stock/soldes", Annulation);
        var mauvais = await provisoire.PostAsJsonAsync("/api/identite/moi/mot-de-passe", new { actuel = "Faux1234!", nouveau = ApiDeTest.MotDePasseValide }, Annulation);
        var identique = await provisoire.PostAsJsonAsync("/api/identite/moi/mot-de-passe", new { actuel = ApiDeTest.MotDePasseProvisoire, nouveau = ApiDeTest.MotDePasseProvisoire }, Annulation);
        var change = await provisoire.PostAsJsonAsync("/api/identite/moi/mot-de-passe", new { actuel = ApiDeTest.MotDePasseProvisoire, nouveau = ApiDeTest.MotDePasseValide }, Annulation);
        using var definitif = await api.ClientConnecteAsync(matricule, ApiDeTest.MotDePasseValide);

        Assert.True(profil!.MotDePasseProvisoire);
        Assert.Empty(profil.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        Assert.Equal("Identite.MotDePasseActuelInvalide", (await IdentiteTests.LireProblemeAsync(mauvais)).Code);
        Assert.Equal("Identite.MotDePasseIdentique", (await IdentiteTests.LireProblemeAsync(identique)).Code);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await definitif.GetAsync("/api/stock/soldes", Annulation)).StatusCode);
    }

    [Fact]
    public async Task La_modification_des_roles_ferme_les_sessions_en_cours()
    {
        using var administrateur = await api.ClientAdministrateurAsync();
        using var client = api.Client();
        var matricule = await CompteDefinitifAsync([Roles.Caissier]);
        var jeton = await ApiDeTest.ConnecterAsync(client, matricule, ApiDeTest.MotDePasseValide);
        var id = (await ListerAsync(administrateur)).Single(c => c.Matricule == matricule).Id;

        var fiche = await Lire<Fiche>(await administrateur.PutAsJsonAsync(
            $"/api/identite/utilisateurs/{id}",
            new { prenom = "Awa", nom = "Ngono Mballa", telephone = "+237 677 11 22 33", roles = new[] { Roles.Caissier, Roles.Magasinier }, langue = "en" },
            Annulation));
        var renouvellement = await client.PostAsJsonAsync("/api/identite/rafraichir", new { jetonRenouvellement = jeton.RefreshToken }, Annulation);

        Assert.Equal((matricule, "Awa Ngono Mballa", "+237 677 11 22 33", "en"), (fiche.Compte.Matricule, fiche.Compte.NomComplet, fiche.Compte.Telephone, fiche.Compte.Langue));
        Assert.Equal([Roles.Caissier, Roles.Magasinier], fiche.Compte.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, renouvellement.StatusCode);
        Assert.Equal("Modification", fiche.Journal[0].Action);
    }

    [Fact]
    public async Task Un_compte_desactive_ne_se_connecte_plus_et_ne_valide_plus_rien_jusqu_a_sa_reactivation()
    {
        using var administrateur = await api.ClientAdministrateurAsync();
        using var client = api.Client();
        var matricule = await CompteDefinitifAsync([Roles.Gerant]);
        var jeton = await ApiDeTest.ConnecterAsync(client, matricule, ApiDeTest.MotDePasseValide);
        var id = (await ListerAsync(administrateur)).Single(c => c.Matricule == matricule).Id;

        var desactive = await Lire<Fiche>(await administrateur.PostAsync($"/api/identite/utilisateurs/{id}/desactivation", null, Annulation));
        var connexion = await client.PostAsJsonAsync("/api/identite/connexion", new { identifiant = matricule, motDePasse = ApiDeTest.MotDePasseValide }, Annulation);
        var renouvellement = await client.PostAsJsonAsync("/api/identite/rafraichir", new { jetonRenouvellement = jeton.RefreshToken }, Annulation);
        var validation = await ValiderAsync(matricule);
        await administrateur.PostAsync($"/api/identite/utilisateurs/{id}/reactivation", null, Annulation);
        var reconnexion = await client.PostAsJsonAsync("/api/identite/connexion", new { identifiant = matricule, motDePasse = ApiDeTest.MotDePasseValide }, Annulation);

        Assert.False(desactive.Compte.Actif);
        Assert.Equal((HttpStatusCode.Unauthorized, "Identite.CompteDesactive"), (connexion.StatusCode, (await IdentiteTests.LireProblemeAsync(connexion)).Code));
        Assert.Equal(HttpStatusCode.Unauthorized, renouvellement.StatusCode);
        Assert.Null(validation);
        Assert.Equal(HttpStatusCode.OK, reconnexion.StatusCode);
        Assert.Equal(matricule, await ValiderAsync(matricule));
    }

    [Fact]
    public async Task Un_mot_de_passe_oublie_est_remplace_par_un_provisoire()
    {
        using var administrateur = await api.ClientAdministrateurAsync();
        var matricule = await CompteDefinitifAsync([Roles.Comptable]);
        var id = (await ListerAsync(administrateur)).Single(c => c.Matricule == matricule).Id;

        var faible = await administrateur.PostAsJsonAsync($"/api/identite/utilisateurs/{id}/mot-de-passe", new { motDePasse = "abc" }, Annulation);
        var fiche = await Lire<Fiche>(await administrateur.PostAsJsonAsync($"/api/identite/utilisateurs/{id}/mot-de-passe", new { motDePasse = "Oubli2026!" }, Annulation));
        using var client = api.Client();
        var ancien = await client.PostAsJsonAsync("/api/identite/connexion", new { identifiant = matricule, motDePasse = ApiDeTest.MotDePasseValide }, Annulation);
        using var provisoire = await api.ClientConnecteAsync(matricule, "Oubli2026!");

        Assert.Equal("Identite.MotDePasseRefuse", (await IdentiteTests.LireProblemeAsync(faible)).Code);
        Assert.True(fiche.Compte.MotDePasseProvisoire);
        Assert.Equal("ReinitialisationMotDePasse", fiche.Journal[0].Action);
        Assert.DoesNotContain(fiche.Journal, j => j.Detail?.Contains("Oubli2026!", StringComparison.Ordinal) == true);
        Assert.Equal(HttpStatusCode.Unauthorized, ancien.StatusCode);
        Assert.True((await provisoire.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.MotDePasseProvisoire);
    }

    [Fact]
    public async Task Le_dernier_administrateur_actif_ne_peut_pas_etre_desactive()
    {
        using var administrateur = await api.ClientAdministrateurAsync();
        var second = await api.CreerCompteAsync([Roles.Administrateur]);
        var comptes = await ListerAsync(administrateur);
        var idSecond = comptes.Single(c => c.Matricule == second).Id;
        var idInitial = comptes.Single(c => c.Matricule == ApiDeTest.Administrateur).Id;

        // Le second administrateur part : l'initial reste le seul actif.
        (await administrateur.PostAsync($"/api/identite/utilisateurs/{idSecond}/desactivation", null, Annulation)).EnsureSuccessStatusCode();
        var refus = await administrateur.PostAsync($"/api/identite/utilisateurs/{idInitial}/desactivation", null, Annulation);
        var retrait = await administrateur.PutAsJsonAsync(
            $"/api/identite/utilisateurs/{idInitial}",
            new { prenom = "Administrateur", nom = "Initial", roles = new[] { Roles.Gerant } },
            Annulation);

        Assert.Equal((HttpStatusCode.Conflict, "Identite.DernierAdministrateur"), (refus.StatusCode, (await IdentiteTests.LireProblemeAsync(refus)).Code));
        Assert.Equal(HttpStatusCode.Conflict, retrait.StatusCode);
    }

    [Fact]
    public async Task Seul_l_administrateur_gere_les_comptes()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var creation = await gerant.PostAsJsonAsync("/api/identite/utilisateurs", new { prenom = "X", nom = "Y", roles = new[] { Roles.Caissier }, motDePasse = ApiDeTest.MotDePasseValide }, Annulation);
        var langue = await gerant.PutAsJsonAsync("/api/identite/moi/langue", new { langue = "en" }, Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, langue.StatusCode);
        Assert.Equal("en", (await gerant.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Langue);
    }

    // --- Outils --------------------------------------------------------------------------------------------------

    private async Task<string> CompteDefinitifAsync(string[] roles)
    {
        using var client = await api.ClientAvecRoleAsync(roles[0], roles[1..]);
        return (await client.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;
    }

    private static async Task<Fiche> CreerAsync(HttpClient administrateur, object saisie)
    {
        var reponse = await administrateur.PostAsJsonAsync("/api/identite/utilisateurs", saisie, Annulation);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return await Lire<Fiche>(reponse);
    }

    private static async Task<List<Compte>> ListerAsync(HttpClient administrateur) =>
        (await administrateur.GetFromJsonAsync<List<Compte>>("/api/identite/utilisateurs", Annulation))!;

    private async Task<string?> ValiderAsync(string matricule)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<IValidationResponsable>()
            .VerifierAsync(matricule, ApiDeTest.MotDePasseValide, Permissions.DepassementsValider, Annulation);
    }

    /// <summary>Dernière valeur tirée de la séquence des matricules (la lire n'en consomme pas).</summary>
    private async Task<long> DernierNumeroMatriculeAsync()
    {
        await using var portee = api.Services.CreateAsyncScope();
        var contexte = portee.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        return await contexte.Database
            .SqlQuery<long>($"SELECT last_value AS \"Value\" FROM identite.numero_matricule")
            .SingleAsync(Annulation);
    }

    private static async Task<T> Lire<T>(HttpResponseMessage reponse)
    {
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;
    }

    private sealed record Profil(string Identifiant, string NomComplet, string Langue, bool MotDePasseProvisoire, List<string> Roles, List<string> Permissions);

    private sealed record Compte(Guid Id, string Matricule, string NomComplet, string? Telephone, List<string> Roles, string Langue, bool Actif, bool MotDePasseProvisoire);

    private sealed record LigneJournal(DateTime DateUtc, string Action, string Auteur, string? Detail);

    private sealed record Fiche(Compte Compte, List<LigneJournal> Journal);
}
