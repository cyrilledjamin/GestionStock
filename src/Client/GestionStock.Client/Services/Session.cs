using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace GestionStock.Client.Services;

/// <summary>
/// Session de l'utilisateur connecté : jetons et profil (permissions). Les jetons sont gardés dans le
/// sessionStorage du navigateur : ils disparaissent à la fermeture de l'onglet (poste partagé en boutique).
/// </summary>
public sealed class Session(IJSRuntime js, HttpClient brut)
{
    private const string CleJetons = "gestionstock.jetons";
    private const string CleProfil = "gestionstock.profil";
    private readonly SemaphoreSlim _rafraichissement = new(1, 1);

    private JetonAcces? _jetons;

    /// <summary>Signale une connexion ou une déconnexion (le menu se met à jour).</summary>
    public event Action? Changement;

    public ProfilUtilisateur? Profil { get; private set; }

    public bool EstConnecte => Profil is not null;

    public string? JetonAcces => _jetons?.AccessToken;

    public bool A(string permission) => Profil?.Permissions.Contains(permission) == true;

    /// <summary>Mot de passe provisoire : l'utilisateur doit le changer avant toute autre action.</summary>
    public bool DoitChangerMotDePasse => Profil?.MotDePasseProvisoire == true;

    /// <summary>
    /// Change le mot de passe de l'utilisateur connecté, puis se reconnecte avec le nouveau (l'API ferme les sessions
    /// ouvertes avec l'ancien). Renvoie le message d'erreur traduit, ou null en cas de succès.
    /// </summary>
    public async Task<string?> ChangerMotDePasseAsync(string actuel, string nouveau)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, "api/identite/moi/mot-de-passe")
        {
            Content = JsonContent.Create(new { actuel, nouveau }),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jetons?.AccessToken);
        AjouterLangue(requete);
        using var reponse = await brut.SendAsync(requete);
        if (!reponse.IsSuccessStatusCode)
        {
            return await Api.LireErreurAsync(reponse);
        }

        return await ConnecterAsync(Profil!.Identifiant, nouveau);
    }

    /// <summary>Reprend la session de l'onglet après un rechargement de page.</summary>
    public async Task InitialiserAsync()
    {
        var json = await js.InvokeAsync<string?>("sessionStorage.getItem", CleJetons);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        _jetons = JsonSerializer.Deserialize<JetonAcces>(json, JsonSerializerOptions.Web);
        try
        {
            if (!await ChargerProfilAsync() && !(await RafraichirAsync() && await ChargerProfilAsync()))
            {
                await OublierAsync();
            }
        }
        catch (HttpRequestException)
        {
            // Serveur injoignable pendant un rechargement de la page : la caisse continue avec le profil de l'onglet
            // (7.8). Ce n'est pas une connexion hors ligne : sans onglet ouvert, il faut le serveur (décision 41).
            var profil = await js.InvokeAsync<string?>("sessionStorage.getItem", CleProfil);
            Profil = string.IsNullOrEmpty(profil) ? null : JsonSerializer.Deserialize<ProfilUtilisateur>(profil, JsonSerializerOptions.Web);
        }
    }

    /// <summary>Connexion ; renvoie le message d'erreur traduit par l'API, ou null en cas de succès.</summary>
    public async Task<string?> ConnecterAsync(string identifiant, string motDePasse)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, "api/identite/connexion")
        {
            Content = JsonContent.Create(new { identifiant, motDePasse }),
        };
        AjouterLangue(requete);
        using var reponse = await brut.SendAsync(requete);
        if (!reponse.IsSuccessStatusCode)
        {
            return await Api.LireErreurAsync(reponse);
        }

        await MemoriserAsync((await reponse.Content.ReadFromJsonAsync<JetonAcces>())!);
        await ChargerProfilAsync();
        Changement?.Invoke();
        return null;
    }

    /// <summary>Obtient un nouveau jeton d'accès avec le jeton de renouvellement ; faux si la session a expiré.</summary>
    public async Task<bool> RafraichirAsync()
    {
        var jetonsUtilises = _jetons;
        await _rafraichissement.WaitAsync();
        try
        {
            if (_jetons is null)
            {
                return false;
            }

            if (!ReferenceEquals(_jetons, jetonsUtilises))
            {
                return true; // Déjà renouvelé par une autre requête.
            }

            using var reponse = await brut.PostAsJsonAsync("api/identite/rafraichir", new { jetonRenouvellement = _jetons.RefreshToken });
            if (!reponse.IsSuccessStatusCode)
            {
                return false;
            }

            await MemoriserAsync((await reponse.Content.ReadFromJsonAsync<JetonAcces>())!);
            return true;
        }
        finally
        {
            _rafraichissement.Release();
        }
    }

    public async Task DeconnecterAsync()
    {
        await OublierAsync();
        Changement?.Invoke();
    }

    /// <summary>Langue de l'interface, transmise à l'API pour la traduction des messages d'erreur.</summary>
    public static void AjouterLangue(HttpRequestMessage requete)
    {
        ArgumentNullException.ThrowIfNull(requete);
        requete.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }

    private async Task<bool> ChargerProfilAsync()
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, "api/identite/moi");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jetons?.AccessToken);
        using var reponse = await brut.SendAsync(requete);
        Profil = reponse.IsSuccessStatusCode ? await reponse.Content.ReadFromJsonAsync<ProfilUtilisateur>() : null;
        if (Profil is not null)
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", CleProfil, JsonSerializer.Serialize(Profil, JsonSerializerOptions.Web));
        }

        return Profil is not null;
    }

    private async Task MemoriserAsync(JetonAcces jetons)
    {
        _jetons = jetons;
        await js.InvokeVoidAsync("sessionStorage.setItem", CleJetons, JsonSerializer.Serialize(jetons, JsonSerializerOptions.Web));
    }

    private async Task OublierAsync()
    {
        _jetons = null;
        Profil = null;
        await js.InvokeVoidAsync("sessionStorage.removeItem", CleJetons);
        await js.InvokeVoidAsync("sessionStorage.removeItem", CleProfil);
    }
}
