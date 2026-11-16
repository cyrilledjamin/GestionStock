using System.Resources;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Inventaire.Application;
using GestionStock.Inventaire.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Inventaire.Endpoints;

/// <summary>Endpoints HTTP du module Inventaire (préfixes <c>/api/inventaires</c> et <c>/api/demarques</c>).</summary>
internal static class EndpointsInventaire
{
    public static ResourceManager Ressources { get; } =
        new("GestionStock.Inventaire.Ressources.ErreursInventaire", typeof(EndpointsInventaire).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var inventaires = routes.MapGroup("/api/inventaires").WithTags("Inventaire");
        inventaires.MapGet("", async (StatutInventaire? statut, ServiceInventaires service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(statut, annulation)))
            .RequireAuthorization(Permissions.InventaireConsulter);

        inventaires.MapGet("/{id:guid}", (Guid id, ServiceInventaires service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.InventaireConsulter);

        inventaires.MapPost("", (NouvelInventaire saisie, ServiceInventaires service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var inventaire = await service.CreerAsync(saisie, annulation);
                return TypedResults.Created($"/api/inventaires/{inventaire.Inventaire.Id}", inventaire);
            }))
            .RequireAuthorization(Permissions.InventaireCompter);

        inventaires.MapPost("/{id:guid}/comptages", (Guid id, Comptage comptage, ServiceInventaires service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.CompterAsync(id, comptage, annulation))))
            .RequireAuthorization(Permissions.InventaireCompter);

        inventaires.MapPost("/{id:guid}/validation", (Guid id, ServiceInventaires service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ValiderAsync(id, annulation))))
            .RequireAuthorization(Permissions.InventaireValider);

        inventaires.MapPost("/{id:guid}/annulation", (Guid id, ServiceInventaires service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.AnnulerAsync(id, annulation))))
            .RequireAuthorization(Permissions.InventaireCompter);

        var demarques = routes.MapGroup("/api/demarques").WithTags("Inventaire");
        demarques.MapGet("", async (StatutDemarque? statut, ServiceDemarques service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(statut, annulation)))
            .RequireAuthorization(Permissions.InventaireConsulter);

        demarques.MapPost("", (NouvelleDemarque saisie, ServiceDemarques service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var demarque = await service.DeclarerAsync(saisie, annulation);
                return TypedResults.Created($"/api/demarques/{demarque.Id}", demarque);
            }))
            .RequireAuthorization(Permissions.DemarqueDeclarer);

        demarques.MapPost("/{id:guid}/validation", (Guid id, ServiceDemarques service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ValiderAsync(id, annulation))))
            .RequireAuthorization(Permissions.DemarqueValider);

        demarques.MapPost("/{id:guid}/refus", (Guid id, RefusDemarque refus, ServiceDemarques service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.RefuserAsync(id, refus, annulation))))
            .RequireAuthorization(Permissions.DemarqueValider);
    }

    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionInventaire erreur)
        {
            var statut = erreur.Code switch
            {
                CodesErreurInventaire.InventaireIntrouvable or CodesErreurInventaire.DemarqueIntrouvable => StatusCodes.Status404NotFound,
                CodesErreurInventaire.InventaireTermine or CodesErreurInventaire.DemarqueDejaTraitee => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources, erreur.Detail);
        }
    }
}
