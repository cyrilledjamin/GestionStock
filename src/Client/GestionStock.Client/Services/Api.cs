using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace GestionStock.Client.Services;

/// <summary>
/// Résultat d'un appel : la valeur, ou le message d'erreur à afficher (déjà traduit par l'API) et son code stable
/// (ex. « Caisse.PlafondDepasse »).
/// </summary>
public sealed record Resultat<T>(T? Valeur, string? Erreur, string? Code = null)
{
    public bool Reussi => Erreur is null;
}

/// <summary>
/// Lecture versionnée : la valeur et sa version (ETag), ou <see cref="NonModifie"/> si la version connue est toujours la
/// bonne (304, rien n'est retéléchargé).
/// </summary>
public sealed record LectureVersionnee<T>(T? Valeur, string? Version, bool NonModifie);

/// <summary>
/// Appels à l'API : ajoute le jeton et la langue, renouvelle le jeton une fois s'il a expiré,
/// renvoie vers la page de connexion si la session est terminée. Un serveur injoignable ne lève pas d'erreur : le
/// résultat porte le code <see cref="CodeHorsLigne"/> et <see cref="EtatConnexion"/> passe hors ligne (7.8).
/// </summary>
public sealed class Api(HttpClient http, Session session, NavigationManager navigation, EtatConnexion etat, IStringLocalizer<Textes> textes)
{
    /// <summary>Code d'un appel qui n'a pas pu joindre le serveur (réseau coupé, serveur arrêté).</summary>
    public const string CodeHorsLigne = "HorsLigne";

    /// <summary>Délai d'un appel ordinaire : au-delà, le serveur est considéré comme injoignable (hors ligne).</summary>
    public static readonly TimeSpan DelaiParDefaut = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Délai des appels longs par nature : données de la caisse sur une connexion lente, contrôle des soldes, exports,
    /// reprise.
    /// </summary>
    public static readonly TimeSpan DelaiLong = TimeSpan.FromMinutes(5);

    public Task<Resultat<T>> GetAsync<T>(string url, TimeSpan? delai = null) => EnvoyerAsync<T>(HttpMethod.Get, url, null, delai);

    public Task<Resultat<T>> PostAsync<T>(string url, object? corps = null, TimeSpan? delai = null) => EnvoyerAsync<T>(HttpMethod.Post, url, corps, delai);

    public Task<Resultat<T>> PutAsync<T>(string url, object corps) => EnvoyerAsync<T>(HttpMethod.Put, url, corps, null);

    public Task<Resultat<T>> DeleteAsync<T>(string url) => EnvoyerAsync<T>(HttpMethod.Delete, url, null, null);

    /// <summary>
    /// Lecture d'une donnée gardée sur le poste (ENF10) : la version connue est envoyée (If-None-Match) ; le serveur
    /// répond 304 sans contenu si elle n'a pas changé.
    /// </summary>
    public Task<Resultat<LectureVersionnee<T>>> GetVersionneAsync<T>(string url, string? versionConnue) =>
        SansReseauAsync(async () =>
        {
            var reponse = await EnvoyerAvecJetonAsync(HttpMethod.Get, url, null, versionConnue, DelaiLong);
            try
            {
                if (reponse.StatusCode == HttpStatusCode.Unauthorized && await session.RafraichirAsync())
                {
                    reponse.Dispose();
                    reponse = await EnvoyerAvecJetonAsync(HttpMethod.Get, url, null, versionConnue, DelaiLong);
                }

                if (reponse.StatusCode == HttpStatusCode.NotModified)
                {
                    return new Resultat<LectureVersionnee<T>>(new LectureVersionnee<T>(default, versionConnue, true), null);
                }

                var lue = await LireAsync<T>(reponse);
                return lue.Reussi
                    ? new Resultat<LectureVersionnee<T>>(new LectureVersionnee<T>(lue.Valeur, reponse.Headers.ETag?.Tag, false), null)
                    : new Resultat<LectureVersionnee<T>>(default, lue.Erreur, lue.Code);
            }
            finally
            {
                reponse.Dispose();
            }
        });

    /// <summary>Envoie un fichier tel quel dans le corps de la requête (ex. classeur de reprise).</summary>
    public Task<Resultat<T>> EnvoyerFichierAsync<T>(string url, byte[] fichier) =>
        SansReseauAsync(async () =>
        {
            using var reponse = await EnvoyerFichierAvecJetonAsync(url, fichier);
            if (reponse.StatusCode == HttpStatusCode.Unauthorized && await session.RafraichirAsync())
            {
                using var nouvelle = await EnvoyerFichierAvecJetonAsync(url, fichier);
                return await LireAsync<T>(nouvelle);
            }

            return await LireAsync<T>(reponse);
        });

    private async Task<HttpResponseMessage> EnvoyerFichierAvecJetonAsync(string url, byte[] fichier)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(fichier) };
        requete.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (session.JetonAcces is { } jeton)
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        Session.AjouterLangue(requete);
        using var limite = new CancellationTokenSource(DelaiLong);
        return await http.SendAsync(requete, limite.Token);
    }

    /// <summary>Contenu binaire (ex. reçu PDF) ; null en cas d'erreur.</summary>
    public async Task<byte[]?> GetOctetsAsync(string url) =>
        (await SansReseauAsync(async () => new Resultat<byte[]>(await LireOctetsAsync(url), null))).Valeur;

    private async Task<byte[]?> LireOctetsAsync(string url)
    {
        using var reponse = await EnvoyerAvecJetonAsync(HttpMethod.Get, url, null, delai: DelaiLong);
        if (reponse.StatusCode == HttpStatusCode.Unauthorized && await session.RafraichirAsync())
        {
            using var nouvelle = await EnvoyerAvecJetonAsync(HttpMethod.Get, url, null, delai: DelaiLong);
            return nouvelle.IsSuccessStatusCode ? await nouvelle.Content.ReadAsByteArrayAsync() : null;
        }

        return reponse.IsSuccessStatusCode ? await reponse.Content.ReadAsByteArrayAsync() : null;
    }

    /// <summary>Titre traduit d'une réponse ProblemDetails, ou le statut HTTP à défaut.</summary>
    public static async Task<string> LireErreurAsync(HttpResponseMessage reponse) => (await LireProblemeAsync(reponse)).Titre;

    /// <summary>Titre traduit et code stable d'une réponse ProblemDetails.</summary>
    public static async Task<(string Titre, string? Code)> LireProblemeAsync(HttpResponseMessage reponse)
    {
        ArgumentNullException.ThrowIfNull(reponse);
        try
        {
            using var document = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
            var racine = document.RootElement;
            var code = racine.TryGetProperty("code", out var c) ? c.GetString() : null;
            var detail = racine.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (racine.TryGetProperty("title", out var titre) && titre.GetString() is { Length: > 0 } texte)
            {
                return (string.IsNullOrEmpty(detail) ? texte : $"{texte} ({detail})", code);
            }
        }
        catch (JsonException)
        {
            // Corps vide ou non JSON : on retombe sur le statut.
        }

        return ($"{(int)reponse.StatusCode} {reponse.ReasonPhrase}", null);
    }

    private Task<Resultat<T>> EnvoyerAsync<T>(HttpMethod methode, string url, object? corps, TimeSpan? delai) =>
        SansReseauAsync(async () =>
        {
            var reponse = await EnvoyerAvecJetonAsync(methode, url, corps, delai: delai);
            try
            {
                // Base interrompue (503, pool vidé par le serveur) : une lecture est redemandée une fois après le délai
                // annoncé. Une écriture ne l'est jamais automatiquement : son message invite à réessayer.
                if (reponse.StatusCode == HttpStatusCode.ServiceUnavailable && methode == HttpMethod.Get)
                {
                    reponse.Dispose();
                    await Task.Delay(DelaiAvantNouvelEssai);
                    reponse = await EnvoyerAvecJetonAsync(methode, url, corps, delai: delai);
                }

                if (reponse.StatusCode == HttpStatusCode.Unauthorized && await session.RafraichirAsync())
                {
                    using var nouvelle = await EnvoyerAvecJetonAsync(methode, url, corps, delai: delai);
                    return await LireAsync<T>(nouvelle);
                }

                return await LireAsync<T>(reponse);
            }
            finally
            {
                reponse.Dispose();
            }
        });

    /// <summary>Délai avant de redemander une lecture refusée par une coupure de la base (en-tête Retry-After : 1 s).</summary>
    private static readonly TimeSpan DelaiAvantNouvelEssai = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Exécute un appel ; une erreur réseau (serveur injoignable) devient un résultat <see cref="CodeHorsLigne"/> et
    /// fait passer le poste hors ligne ; toute réponse du serveur le remet en ligne.
    /// </summary>
    private async Task<Resultat<T>> SansReseauAsync<T>(Func<Task<Resultat<T>>> appel)
    {
        try
        {
            var resultat = await appel();
            etat.SignalerReussite();
            return resultat;
        }
        catch (Exception erreur) when (erreur is HttpRequestException or TaskCanceledException)
        {
            // Réseau coupé, serveur arrêté, ou pas de réponse dans le délai du client (15 s) : hors ligne.
            etat.SignalerEchec();
            return new(default, textes["ServeurInjoignable"], CodeHorsLigne);
        }
    }

    private async Task<HttpResponseMessage> EnvoyerAvecJetonAsync(HttpMethod methode, string url, object? corps, string? versionConnue = null, TimeSpan? delai = null)
    {
        using var requete = new HttpRequestMessage(methode, url);
        if (versionConnue is not null)
        {
            requete.Headers.TryAddWithoutValidation("If-None-Match", versionConnue);
        }

        if (corps is not null)
        {
            requete.Content = JsonContent.Create(corps);
        }

        if (session.JetonAcces is { } jeton)
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        Session.AjouterLangue(requete);
        // Délai par appel (le HttpClient n'en a pas) : 15 s par défaut, plus pour les appels longs par nature.
        using var limite = new CancellationTokenSource(delai ?? DelaiParDefaut);
        return await http.SendAsync(requete, limite.Token);
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
            var (titre, code) = await LireProblemeAsync(reponse);
            return new(default, titre, code);
        }

        // 204 : réponse vide attendue (ex. pas de session ouverte, pas de prix).
        if (reponse.StatusCode == HttpStatusCode.NoContent)
        {
            return new(default, null);
        }

        return new(await reponse.Content.ReadFromJsonAsync<T>(), null);
    }
}
