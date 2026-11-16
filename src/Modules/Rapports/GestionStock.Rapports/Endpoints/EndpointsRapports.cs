using System.Resources;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Rapports.Application;
using GestionStock.Rapports.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Rapports.Endpoints;

/// <summary>
/// Endpoints HTTP des rapports (préfixe <c>/api/rapports</c>). Période en jours de Douala (<c>du</c>, <c>au</c>,
/// format AAAA-MM-JJ, bornes incluses) ; par défaut, aujourd'hui.
/// </summary>
internal static class EndpointsRapports
{
    public const string Prefixe = "/api/rapports";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Rapports.Ressources.ErreursRapports", typeof(EndpointsRapports).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Rapports").RequireAuthorization(Permissions.RapportsConsulter);

        groupe.MapGet("/ventes", (DateOnly? du, DateOnly? au, ServiceRapports service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.VentesAsync(Periode.Creer(du, au, service.Aujourdhui), annulation))));

        // Journaux comptables exportables (UC16), dans l'ordre de présentation.
        groupe.MapGet("/journaux", () => TypedResults.Ok(JournauxComptables.Tous));

        groupe.MapGet("/stock", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.StockAsync(annulation)));

        groupe.MapGet("/creances-dettes", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.CreancesDettesAsync(annulation)));

        groupe.MapGet("/caisse", (DateOnly? du, DateOnly? au, ServiceRapports service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.CaisseAsync(Periode.Creer(du, au, service.Aujourdhui), annulation))));
    }

    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionRapports erreur)
        {
            return ReponsesErreur.Probleme(StatusCodes.Status400BadRequest, erreur.Code, Ressources);
        }
    }
}
