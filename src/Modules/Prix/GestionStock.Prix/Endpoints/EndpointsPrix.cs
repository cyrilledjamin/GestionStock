using System.Resources;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Prix.Application;
using GestionStock.Prix.Contracts;
using GestionStock.Prix.Domain;
using GestionStock.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Prix.Endpoints;

/// <summary>Endpoints HTTP du module Prix (préfixe <c>/api/prix</c>).</summary>
internal static class EndpointsPrix
{
    public const string Prefixe = "/api/prix";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Prix.Ressources.ErreursPrix", typeof(EndpointsPrix).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Prix");

        groupe.MapGet("/regime", (IConsultationPrix prix) => TypedResults.Ok(prix.Regime))
            .RequireAuthorization(Permissions.PrixConsulter);

        // Grille de la caisse, gardée sur le poste pour vendre hors ligne (7.8).
        groupe.MapGet("/grille", async (ServicePrix service, IHorloge horloge, CancellationToken annulation) =>
            TypedResults.Ok(await service.GrilleAsync(horloge.AujourdhuiDouala, annulation)))
            .AvecVersion()
            .RequireAuthorization(Permissions.PrixConsulter);

        groupe.MapGet("/articles/{articleId:guid}", (Guid articleId, ServicePrix service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ListerParArticleAsync(articleId, annulation))))
            .RequireAuthorization(Permissions.PrixConsulter);

        groupe.MapGet("/conditionnements/{id:guid}", async (Guid id, IConsultationPrix prix, IHorloge horloge, CancellationToken annulation) =>
            await prix.ObtenirPrixAsync(id, horloge.AujourdhuiDouala, annulation) is { } applicable
                ? Results.Ok(applicable)
                : Results.NoContent())
            .RequireAuthorization(Permissions.PrixConsulter);

        groupe.MapGet("/conditionnements/{id:guid}/historique", async (Guid id, ServicePrix service, CancellationToken annulation) =>
            TypedResults.Ok(await service.HistoriqueAsync(id, annulation)))
            .RequireAuthorization(Permissions.PrixConsulter);

        groupe.MapPost("/conditionnements/{id:guid}", (Guid id, NouveauPrix saisie, ServicePrix service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.FixerAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.PrixGerer);
    }

    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionPrix erreur)
        {
            var statut = erreur.Code is CodesErreurPrix.ConditionnementIntrouvable or CodesErreurPrix.ArticleIntrouvable
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources);
        }
    }
}
