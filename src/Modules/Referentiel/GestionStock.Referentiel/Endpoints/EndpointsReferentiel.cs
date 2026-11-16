using System.Resources;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Referentiel.Application;
using GestionStock.Referentiel.Contracts;
using GestionStock.Referentiel.Domain;
using GestionStock.SharedKernel;
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

        // Conditionnements et codes (RG18, RG26, RG33)
        groupe.MapGet("/articles/{id:guid}/conditionnements", (Guid id, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ListerAsync(id, annulation))))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        groupe.MapPost("/articles/{id:guid}/conditionnements", (Guid id, NouveauConditionnement saisie, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var conditionnement = await service.CreerAsync(id, saisie, annulation);
                return TypedResults.Created($"{Prefixe}/conditionnements/{conditionnement.Id}", conditionnement);
            }))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapPut("/conditionnements/{id:guid}", (Guid id, ModificationConditionnement saisie, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        // Grille d'articles rapides de la caisse (choix du Gérant)
        groupe.MapPut("/conditionnements/{id:guid}/vente-rapide", (Guid id, ChoixVenteRapide saisie, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ChoisirVenteRapideAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapPost("/conditionnements/{id:guid}/codes", (Guid id, NouveauCode saisie, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.AjouterCodeAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapPost("/conditionnements/{id:guid}/codes/interne", (Guid id, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.GenererCodeInterneAsync(id, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        groupe.MapPost("/codes/{id:guid}/desactivation", (Guid id, ServiceConditionnements service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.DesactiverCodeAsync(id, annulation))))
            .RequireAuthorization(Permissions.ArticlesGerer);

        // Identification d'un article vendable : scan (UC22) ou recherche par code / libellé.
        groupe.MapGet("/scan/{valeur}", async (string valeur, ServiceConditionnements service, CancellationToken annulation) =>
            await service.ResoudreAsync(valeur, annulation) is { } article
                ? Results.Ok(article)
                : ReponsesErreur.Probleme(StatusCodes.Status404NotFound, CodesErreurReferentiel.CodeIntrouvable, Ressources, valeur))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        // Catalogue complet de la caisse, gardé sur le poste pour vendre hors ligne (7.8).
        groupe.MapGet("/catalogue-caisse", async (ServiceConditionnements service, IHorloge horloge, CancellationToken annulation) =>
            TypedResults.Ok(await service.CatalogueCaisseAsync(horloge.MaintenantUtc, annulation)))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        groupe.MapGet("/vendables/rapides", async (ServiceConditionnements service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerVenteRapideAsync(annulation)))
            .RequireAuthorization(Permissions.ReferentielConsulter);

        groupe.MapGet("/vendables", async (string? recherche, ServiceConditionnements service, CancellationToken annulation) =>
            TypedResults.Ok(await service.RechercherAsync(recherche, annulation)))
            .RequireAuthorization(Permissions.ReferentielConsulter);

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
                CodesErreurReferentiel.ArticleIntrouvable or CodesErreurReferentiel.ConditionnementIntrouvable
                    or CodesErreurReferentiel.CodeIntrouvable => StatusCodes.Status404NotFound,
                CodesErreurReferentiel.CodeDejaUtilise or CodesErreurReferentiel.CodeDejaAttribue => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources);
        }
    }
}
