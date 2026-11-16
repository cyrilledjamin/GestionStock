using System.Resources;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Referentiel.Application;
using GestionStock.Referentiel.Contracts;
using GestionStock.Referentiel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Referentiel.Endpoints;

/// <summary>Endpoints HTTP du module Referentiel (préfixe <c>/api/referentiel</c>).</summary>
internal static class EndpointsReferentiel
{
    public const string Prefixe = "/api/referentiel";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Referentiel.Ressources.ErreursReferentiel", typeof(EndpointsReferentiel).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Referentiel");

        groupe.MapGet("/articles", async (string? recherche, bool? inclureInactifs, ServiceArticles service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(recherche, inclureInactifs ?? false, annulation)))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        groupe.MapGet("/articles/{id:guid}", (Guid id, ServiceArticles service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        groupe.MapPost("/articles", (NouvelArticle saisie, ServiceArticles service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var article = await service.CreerAsync(saisie, annulation);
                return TypedResults.Created($"{Prefixe}/articles/{article.Id}", article);
            }))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapPut("/articles/{id:guid}", (Guid id, ModificationArticle saisie, ServiceArticles service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapGet("/site", async (IConsultationReferentiel referentiel, CancellationToken annulation) =>
            TypedResults.Ok(await referentiel.ObtenirSiteBoutiqueAsync(annulation)))
            .RequireAuthorization(Permissions.ReferentielConsulter);
    }

    /// <summary>Traduit les règles violées en réponses 400 / 404 / 409.</summary>
    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionReferentiel erreur)
        {
            var statut = erreur.Code switch
            {
                CodesErreurReferentiel.ArticleIntrouvable => StatusCodes.Status404NotFound,
                CodesErreurReferentiel.CodeDejaUtilise => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources);
        }
    }
}
