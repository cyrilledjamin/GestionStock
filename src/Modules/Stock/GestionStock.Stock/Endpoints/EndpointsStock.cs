using GestionStock.Identite.Contracts;
using GestionStock.Stock.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Stock.Endpoints;

/// <summary>
/// Endpoints HTTP du module Stock (préfixe <c>/api/stock</c>) : lecture seule. Le stock ne s'écrit que par les
/// documents des autres modules (réception, vente…), via le contrat <c>IServiceMouvementsStock</c> (RG01).
/// </summary>
internal static class EndpointsStock
{
    public const string Prefixe = "/api/stock";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Stock");

        // Soldes de tous les articles du site (par défaut : la boutique).
        groupe.MapGet("/soldes", async (Guid? siteId, LecturesStock lectures, CancellationToken annulation) =>
            TypedResults.Ok(await lectures.ListerSoldesAsync(siteId, annulation)))
            .RequireAuthorization(Permissions.StockConsulter);

        // Journal d'un article, du plus récent au plus ancien.
        groupe.MapGet("/articles/{articleId:guid}/mouvements", async (Guid articleId, Guid? siteId, int? limite, LecturesStock lectures, CancellationToken annulation) =>
            TypedResults.Ok(await lectures.ListerMouvementsAsync(articleId, siteId, limite, annulation)))
            .RequireAuthorization(Permissions.StockConsulter);
    }
}
