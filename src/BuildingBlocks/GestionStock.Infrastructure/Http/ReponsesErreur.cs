using System.Globalization;
using System.Resources;
using Microsoft.AspNetCore.Http;

namespace GestionStock.Infrastructure.Http;

/// <summary>
/// Réponses d'erreur des endpoints : ProblemDetails dont le titre est traduit dans la langue de la requête
/// (ENF09) et qui porte le code d'erreur stable dans l'extension <c>code</c>, pour le client.
/// </summary>
public static class ReponsesErreur
{
    /// <summary>Nom de l'extension ProblemDetails qui porte le code d'erreur.</summary>
    public const string ExtensionCode = "code";

    /// <summary>Construit la réponse d'erreur ; le code sert aussi de clé dans <paramref name="ressources"/>.</summary>
    public static IResult Probleme(int statut, string code, ResourceManager ressources, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(ressources);

        var titre = ressources.GetString(code, CultureInfo.CurrentUICulture) ?? code;
        return TypedResults.Problem(
            title: titre,
            detail: detail,
            statusCode: statut,
            extensions: new Dictionary<string, object?> { [ExtensionCode] = code });
    }
}
