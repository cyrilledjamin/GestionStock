using System.Net.Http.Json;
using GestionStock.Identite.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Api.IntegrationTests;

public sealed class ValidationResponsableTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_gerant_valide_un_depassement_avec_ses_identifiants()
    {
        var gerant = await CreerCompteAsync(Roles.Gerant);

        var valideur = await VerifierAsync(gerant, ApiDeTest.MotDePasseValide);

        Assert.Equal(gerant, valideur);
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_ou_un_compte_inconnu_est_refuse()
    {
        var gerant = await CreerCompteAsync(Roles.Gerant);

        Assert.Null(await VerifierAsync(gerant, "Faux123!"));
        Assert.Null(await VerifierAsync("inconnu-" + Guid.NewGuid().ToString("N")[..8], ApiDeTest.MotDePasseValide));
        Assert.Null(await VerifierAsync(gerant, ""));
    }

    [Fact]
    public async Task Un_caissier_ne_peut_pas_valider_lui_meme_un_depassement()
    {
        var caissier = await CreerCompteAsync(Roles.Caissier);

        Assert.Null(await VerifierAsync(caissier, ApiDeTest.MotDePasseValide));
    }

    [Fact]
    public async Task Cinq_echecs_verrouillent_le_compte_du_responsable()
    {
        var gerant = await CreerCompteAsync(Roles.Gerant);
        for (var essai = 0; essai < 5; essai++)
        {
            await VerifierAsync(gerant, "Faux123!");
        }

        var avecLeBonMotDePasse = await VerifierAsync(gerant, ApiDeTest.MotDePasseValide);
        using var client = api.Client();
        var connexion = await client.PostAsJsonAsync("/api/identite/connexion", new { identifiant = gerant, motDePasse = ApiDeTest.MotDePasseValide }, Annulation);

        Assert.Null(avecLeBonMotDePasse);
        Assert.Equal("Identite.CompteVerrouille", (await IdentiteTests.LireProblemeAsync(connexion)).Code);
    }

    private async Task<string?> VerifierAsync(string identifiant, string motDePasse)
    {
        await using var portee = api.Services.CreateAsyncScope();
        var validation = portee.ServiceProvider.GetRequiredService<IValidationResponsable>();
        return await validation.VerifierAsync(identifiant, motDePasse, Permissions.DepassementsValider, Annulation);
    }

    private async Task<string> CreerCompteAsync(string role)
    {
        using var client = await api.ClientAvecRoleAsync(role);
        var profil = await client.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation);
        return profil!.Identifiant;
    }

    private sealed record Profil(string Identifiant);
}
