using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace GestionStock.Client.Services;

/// <summary>Résultat d'un appel : la valeur, ou le message d'erreur à afficher (déjà traduit par l'API).</summary>
public sealed record Resultat<T>(T? Valeur, string? Erreur)
{
    public bool Reussi => Erreur is null;
}

/// <summary>
/// Appels à l'API : ajoute le jeton et la langue, renouvelle le jeton une fois s'il a expiré,
/// renvoie vers la page de connexion si la session est terminée.
/// </summary>
public sealed class Api(HttpClient http, Session session, NavigationManager navigation)
{
    public Task<Resultat<T>> GetAsync<T>(string url) => EnvoyerAsync<T>(HttpMethod.Get, url, null);

    public Task<Resultat<T>> PostAsync<T>(string url, object? corps = null) => EnvoyerAsync<T>(HttpMethod.Post, url, corps);

    public Task<Resultat<T>> PutAsync<T>(string url, object corps) => EnvoyerAsync<T>(HttpMethod.Put, url, corps);

    /// <summary>Titre traduit d'une réponse ProblemDetails, ou le statut HTTP à défaut.</summary>
    public static async Task<string> LireErreurAsync(HttpResponseMessage reponse)
    {
        ArgumentNullException.ThrowIfNull(reponse);
        try
        {
            using var document = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("title", out var titre) && titre.GetString() is { Length: > 0 } texte)
            {
                return texte;
            }
        }
        catch (JsonException)
        {
            // Corps vide ou non JSON : on retombe sur le statut.
        }

        return $"{(int)reponse.StatusCode} {reponse.ReasonPhrase}";
    }

    private async Task<Resultat<T>> EnvoyerAsync<T>(HttpMethod methode, string url, object? corps)
    {
        using var reponse = await EnvoyerAvecJetonAsync(methode, url, corps);
        if (reponse.StatusCode == HttpStatusCode.Unauthorized && await session.RafraichirAsync())
        {
            using var nouvelle = await EnvoyerAvecJetonAsync(methode, url, corps);
            return await LireAsync<T>(nouvelle);
        }

        return await LireAsync<T>(reponse);
    }

    private async Task<HttpResponseMessage> EnvoyerAvecJetonAsync(HttpMethod methode, string url, object? corps)
    {
        using var requete = new HttpRequestMessage(methode, url);
        if (corps is not null)
        {
            requete.Content = JsonContent.Create(corps);
        }

        if (session.JetonAcces is { } jeton)
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        Session.AjouterLangue(requete);
        return await http.SendAsync(requete);
    }

    private async Task<Resultat<T>> LireAsync<T>(HttpResponseMessage reponse)
    {
        if (reponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            await session.DeconnecterAsync();
            navigation.NavigateTo("connexion");
            return new(default, await LireErreurAsync(reponse));
        }

        if (!reponse.IsSuccessStatusCode)
        {
            return new(default, await LireErreurAsync(reponse));
        }

        return new(await reponse.Content.ReadFromJsonAsync<T>(), null);
    }
}
