using System.Globalization;
using GestionStock.Identite.Contracts;
using GestionStock.Reprise.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Reprise.Endpoints;

/// <summary>
/// Endpoints HTTP du module Reprise (préfixe <c>/api/reprise</c>). Le fichier Excel est envoyé tel quel dans le corps
/// de la requête (<c>application/octet-stream</c>), 5 Mo au plus.
/// </summary>
internal static class EndpointsReprise
{
    public const string Prefixe = "/api/reprise";
    public const int TailleMax = 5 * 1024 * 1024;
    private const string TypeExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Reprise");

        // Modèle dans la langue de la requête ; ?exemple=true : rempli avec le jeu d'exemple.
        groupe.MapGet("/modele.xlsx", (bool? exemple) =>
            TypedResults.File(
                ServiceReprise.Modele(exemple == true, CultureInfo.CurrentUICulture),
                TypeExcel,
                exemple == true ? "reprise-exemple.xlsx" : "reprise-modele.xlsx"))
            .RequireAuthorization(Permissions.RepriseImporter);

        groupe.MapPost("/simulation", (HttpRequest requete, ServiceReprise service, CancellationToken annulation) =>
            AvecFichier(requete, flux => service.AnalyserAsync(flux, annulation), annulation))
            .RequireAuthorization(Permissions.RepriseImporter);

        groupe.MapPost("/import", (HttpRequest requete, ServiceReprise service, CancellationToken annulation) =>
            AvecFichier(requete, flux => service.ImporterAsync(flux, annulation), annulation))
            .RequireAuthorization(Permissions.RepriseImporter);
    }

    private static async Task<IResult> AvecFichier(HttpRequest requete, Func<Stream, Task<RapportReprise>> action, CancellationToken annulation)
    {
        if (requete.ContentLength > TailleMax)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        await using var copie = new MemoryStream();
        await requete.Body.CopyToAsync(copie, annulation);
        if (copie.Length > TailleMax)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        copie.Position = 0;
        return TypedResults.Ok(await action(copie));
    }
}
