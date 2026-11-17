using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

/// <summary>
/// Réponses versionnées (ENF10) : la caisse renvoie la version de ce qu'elle garde (If-None-Match) et ne retélécharge
/// rien si les données n'ont pas changé (304).
/// </summary>
public sealed class VersionsTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Une_liste_inchangee_repond_304_et_une_modification_change_sa_version()
    {
        api.ExigerDocker();
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var marqueur = $"Version {Guid.NewGuid():N}"[..20];
        var creation = await gerant.PostAsJsonAsync("/api/credit/clients", new { nom = marqueur, telephone = "+237 699 11 22 33", actif = true }, Annulation);
        var client = await creation.Content.ReadFromJsonAsync<Client>(Annulation);
        var route = $"/api/credit/clients?recherche={Uri.EscapeDataString(marqueur)}";

        using var premiere = await gerant.GetAsync(route, Annulation);
        var version = premiere.Headers.ETag;
        using var inchangee = await Demander(gerant, route, version!.Tag);
        (await comptable.PutAsJsonAsync($"/api/credit/clients/{client!.Id}/plafond", new { plafondCredit = 5000 }, Annulation)).EnsureSuccessStatusCode();
        using var modifiee = await Demander(gerant, route, version.Tag);

        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);
        Assert.Contains(marqueur, await premiere.Content.ReadAsStringAsync(Annulation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotModified, inchangee.StatusCode);
        Assert.Empty(await inchangee.Content.ReadAsByteArrayAsync(Annulation));
        Assert.Equal(HttpStatusCode.OK, modifiee.StatusCode);
        Assert.NotEqual(version.Tag, modifiee.Headers.ETag!.Tag);
        Assert.Contains("5000", await modifiee.Content.ReadAsStringAsync(Annulation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Le_catalogue_et_la_grille_de_la_caisse_portent_une_version()
    {
        api.ExigerDocker();
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);

        foreach (var route in new[] { "/api/referentiel/catalogue-caisse", "/api/prix/grille" })
        {
            using var reponse = await caissier.GetAsync(route, Annulation);
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            Assert.NotNull(reponse.Headers.ETag);
            Assert.Equal("application/json", reponse.Content.Headers.ContentType!.MediaType);
        }
    }

    private static async Task<HttpResponseMessage> Demander(HttpClient client, string route, string version)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, route);
        requete.Headers.TryAddWithoutValidation("If-None-Match", version);
        return await client.SendAsync(requete, Annulation);
    }

    private sealed record Client(Guid Id, string Nom);
}
