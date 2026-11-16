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
    private readonly SemaphoreSlim _rafraichissement = new(1, 1);

    private JetonAcces? _jetons;

    /// <summary>Signale une connexion ou une déconnexion (le menu se met à jour).</summary>
    public event Action? Changement;

    public ProfilUtilisateur? Profil { get; private set; }

    public bool EstConnecte => Profil is not null;

    public string? JetonAcces => _jetons?.AccessToken;

    public bool A(string permission) => Profil?.Permissions.Contains(permission) == true;

    /// <summary>Reprend la session de l'onglet après un rechargement de page.</summary>
    public async Task InitialiserAsync()
    {
        var json = await js.InvokeAsync<string?>("sessionStorage.getItem", CleJetons);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        _jetons = JsonSerializer.Deserialize<JetonAcces>(json, JsonSerializerOptions.Web);
        if (!await ChargerProfilAsync() && !(await RafraichirAsync() && await ChargerProfilAsync()))
        {
            await OublierAsync();
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
    }
}
