using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionStock.Infrastructure.Http;

/// <summary>
/// Réponses versionnées (faible bande passante, ENF10) : la réponse porte une version (en-tête ETag, empreinte de son
/// contenu) ; un poste qui renvoie la version qu'il garde (If-None-Match) reçoit 304 sans contenu si rien n'a changé.
/// Pour les données que la caisse garde sur le poste (catalogue, grille de prix, clients), téléchargées à chaque
/// ouverture et rafraîchies régulièrement.
/// </summary>
public static class ReponseVersionnee
{
    /// <summary>Versionne la réponse JSON de l'endpoint sur tout son contenu.</summary>
    public static RouteHandlerBuilder AvecVersion(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter(new FiltreVersion(null));

    /// <summary>
    /// Versionne la réponse sur une partie de son contenu : <paramref name="contenu"/> écarte ce qui change à chaque
    /// appel sans changer les données (ex. heure de génération).
    /// </summary>
    public static RouteHandlerBuilder AvecVersion<T>(this RouteHandlerBuilder endpoint, Func<T, object> contenu) =>
        endpoint.AddEndpointFilter(new FiltreVersion(valeur => valeur is T typee ? contenu(typee) : valeur));

    /// <summary>Version d'un contenu : empreinte SHA-256 de sa forme JSON, entre guillemets (format ETag).</summary>
    internal static string Version(byte[] contenu) => $"\"{Convert.ToHexString(SHA256.HashData(contenu))[..32]}\"";

    private sealed class FiltreVersion(Func<object, object>? contenu) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexte, EndpointFilterDelegate suivant)
        {
            var resultat = await suivant(contexte);
            var valeur = resultat switch
            {
                IStatusCodeHttpResult { StatusCode: not (null or StatusCodes.Status200OK) } => null,
                IValueHttpResult { Value: { } v } => v,
                IResult => null,
                _ => resultat,
            };
            if (valeur is null)
            {
                return resultat;
            }

            var http = contexte.HttpContext;
            var options = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            var corps = JsonSerializer.SerializeToUtf8Bytes(valeur, valeur.GetType(), options);
            var porteeVersion = contenu is null ? corps : JsonSerializer.SerializeToUtf8Bytes(contenu(valeur), options);
            var version = Version(porteeVersion);

            http.Response.Headers.ETag = version;
            // Le poste revalide toujours (no-cache) : la version évite seulement de retélécharger un contenu inchangé.
            http.Response.Headers.CacheControl = "private, no-cache";
            return http.Request.Headers.IfNoneMatch.Any(v => v == version)
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : TypedResults.Bytes(corps, "application/json; charset=utf-8");
        }
    }
}
