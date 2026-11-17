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
        // articles : identifiants séparés par des virgules (écran du stock, une page d'articles) ; sans, tous les soldes.
        groupe.MapGet("/soldes", async (Guid? siteId, string? articles, LecturesStock lectures, CancellationToken annulation) =>
            TypedResults.Ok(await lectures.ListerSoldesAsync(siteId, annulation, LireIdentifiants(articles))))
            .RequireAuthorization(Permissions.StockConsulter);

        // Valeur totale du stock (au CUMP) et nombre d'articles en stock, sans télécharger tous les soldes.
        groupe.MapGet("/valeur", async (Guid? siteId, LecturesStock lectures, CancellationToken annulation) =>
            TypedResults.Ok(await lectures.ValeurAsync(siteId, annulation)))
            .RequireAuthorization(Permissions.StockConsulter);

        // Journal d'un article, du plus récent au plus ancien.
        groupe.MapGet("/articles/{articleId:guid}/mouvements", async (Guid articleId, Guid? siteId, int? limite, LecturesStock lectures, CancellationToken annulation) =>
            TypedResults.Ok(await lectures.ListerMouvementsAsync(articleId, siteId, limite, annulation)))
            .RequireAuthorization(Permissions.StockConsulter);
    }

    /// <summary>Identifiants d'une liste « id1,id2,… » ; null si la liste est absente (tous les articles).</summary>
    private static IReadOnlyCollection<Guid>? LireIdentifiants(string? liste) =>
        string.IsNullOrWhiteSpace(liste)
            ? null
            : [.. liste.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(valeur => Guid.TryParse(valeur, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)];
}
