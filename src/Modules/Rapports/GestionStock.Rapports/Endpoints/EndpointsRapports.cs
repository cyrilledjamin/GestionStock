using System.Globalization;
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
    public const string PrefixeExports = "/api/exports";

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

        groupe.MapGet("/tableau-de-bord", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.TableauDeBordAsync(annulation)));

        // Ventes et marge des 30 derniers jours (graphique de l'accueil), à part : le tableau de bord n'attend pas ce calcul.
        groupe.MapGet("/tendance", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.TendanceAsync(annulation)));

        groupe.MapGet("/stock", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.StockAsync(annulation)));

        groupe.MapGet("/creances-dettes", async (ServiceRapports service, CancellationToken annulation) =>
            TypedResults.Ok(await service.CreancesDettesAsync(annulation)));

        groupe.MapGet("/caisse", (DateOnly? du, DateOnly? au, ServiceRapports service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.CaisseAsync(Periode.Creer(du, au, service.Aujourdhui), annulation))));

        // Exports comptables (UC16) : en-têtes dans la langue de la requête ; valeur-stock ignore la période.
        routes.MapGroup(PrefixeExports).WithTags("Rapports").RequireAuthorization(Permissions.ExportsProduire)
            .MapGet("/{journal}", (string journal, DateOnly? du, DateOnly? au, string? format, ServiceExports service, CancellationToken annulation) =>
                Executer(async () =>
                {
                    var fichier = await service.ExporterAsync(journal, Periode.Creer(du, au, service.Aujourdhui), format, CultureInfo.CurrentUICulture, annulation);
                    return TypedResults.File(fichier.Contenu, fichier.TypeMime, fichier.NomFichier);
                }));
    }

    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionRapports erreur)
        {
            var statut = erreur.Code == CodesErreurRapports.JournalInconnu ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources);
        }
    }
}
