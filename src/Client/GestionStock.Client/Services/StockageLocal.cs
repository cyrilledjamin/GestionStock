using System.Text.Json;
using Microsoft.JSInterop;

namespace GestionStock.Client.Services;

/// <summary>
/// Stockage du poste (IndexedDB, <c>wwwroot/js/stockage-local.js</c>) : il survit à la fermeture de l'onglet et au
/// redémarrage du PC (7.8). Magasin <see cref="Cache"/> : données de la caisse ; <see cref="File"/> : opérations
/// faites hors ligne, lues dans l'ordre de leurs clés.
/// </summary>
public sealed class StockageLocal(IJSRuntime js)
{
    public const string Cache = "cache";
    public const string File = "file";

    public async Task<T?> LireAsync<T>(string magasin, string cle)
    {
        var json = await js.InvokeAsync<string?>("stockageLocal.lire", magasin, cle);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
    }

    public ValueTask EcrireAsync<T>(string magasin, string cle, T valeur) =>
        js.InvokeVoidAsync("stockageLocal.ecrire", magasin, cle, JsonSerializer.Serialize(valeur, JsonSerializerOptions.Web));

    public ValueTask SupprimerAsync(string magasin, string cle) => js.InvokeVoidAsync("stockageLocal.supprimer", magasin, cle);

    public async Task<List<T>> ListerAsync<T>(string magasin)
    {
        var valeurs = await js.InvokeAsync<string[]>("stockageLocal.lister", magasin);
        return [.. valeurs.Select(v => JsonSerializer.Deserialize<T>(v, JsonSerializerOptions.Web)!)];
    }

    /// <summary>Demande au navigateur de ne jamais vider ce stockage (ventes en attente) ; vrai s'il l'accepte.</summary>
    public ValueTask<bool> PersisterAsync() => js.InvokeAsync<bool>("stockageLocal.persister");
}
