using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Stock.Endpoints;

/// <summary>
/// Endpoints HTTP du module Stock (préfixe <c>/api/stock</c>).
/// Aucun endpoint métier tant que le module Identite n'existe pas : chaque endpoint devra déclarer
/// sa permission (RequireAuthorization), la politique par défaut de l'hôte refusant tout le reste.
/// </summary>
internal static class EndpointsStock
{
    public const string Prefixe = "/api/stock";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _ = routes.MapGroup(Prefixe).WithTags("Stock");
    }
}
