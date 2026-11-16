using System.Resources;
using GestionStock.Achats.Application;
using GestionStock.Achats.Domain;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using GestionStock.Stock.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Achats.Endpoints;

/// <summary>Endpoints HTTP du module Achats (préfixe <c>/api/achats</c>).</summary>
internal static class EndpointsAchats
{
    public const string Prefixe = "/api/achats";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Achats.Ressources.ErreursAchats", typeof(EndpointsAchats).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Achats");

        // Fournisseurs
        groupe.MapGet("/fournisseurs", async (bool? inclureInactifs, ServiceFournisseurs service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(inclureInactifs ?? false, annulation)))
            .RequireAuthorization(Permissions.AchatsConsulter);

        groupe.MapPost("/fournisseurs", (SaisieFournisseur saisie, ServiceFournisseurs service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var fournisseur = await service.CreerAsync(saisie, annulation);
                return TypedResults.Created($"{Prefixe}/fournisseurs/{fournisseur.Id}", fournisseur);
            }))
            .RequireAuthorization(Permissions.FournisseursGerer);

        groupe.MapPut("/fournisseurs/{id:guid}", (Guid id, SaisieFournisseur saisie, ServiceFournisseurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.FournisseursGerer);

        // Réceptions
        groupe.MapGet("/receptions", async (ServiceReceptions service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(annulation)))
            .RequireAuthorization(Permissions.AchatsConsulter);

        groupe.MapGet("/receptions/{id:guid}", (Guid id, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.AchatsConsulter);

        groupe.MapPost("/receptions", (SaisieReception saisie, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var reception = await service.CreerAsync(saisie, annulation);
                return TypedResults.Created($"{Prefixe}/receptions/{reception.Id}", reception);
            }))
            .RequireAuthorization(Permissions.ReceptionsSaisir);

        groupe.MapPut("/receptions/{id:guid}", (Guid id, SaisieReception saisie, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ReceptionsSaisir);

        groupe.MapPost("/receptions/{id:guid}/validation", (Guid id, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ValiderAsync(id, annulation))))
            .RequireAuthorization(Permissions.ReceptionsValider);

        // Frais d'approche (RG30) : avant la validation, ou après (revalorisation du stock présent).
        groupe.MapPost("/receptions/{id:guid}/frais", (Guid id, SaisieFrais saisie, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.AjouterFraisAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.FraisSaisir);

        groupe.MapDelete("/receptions/{id:guid}/frais/{fraisId:guid}", (Guid id, Guid fraisId, ServiceReceptions service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.RetirerFraisAsync(id, fraisId, annulation))))
            .RequireAuthorization(Permissions.FraisSaisir);

        // Dettes fournisseurs (décision 27)
        groupe.MapGet("/dettes", async (Guid? fournisseurId, bool? nonSoldees, ServiceDettes service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(fournisseurId, nonSoldees ?? false, annulation)))
            .RequireAuthorization(Permissions.AchatsConsulter);

        groupe.MapGet("/receptions/{id:guid}/dette", (Guid id, ServiceDettes service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.AchatsConsulter);

        groupe.MapPost("/receptions/{id:guid}/paiements", (Guid id, SaisiePaiement saisie, ServiceDettes service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.PayerAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.PaiementsFournisseursSaisir);

        groupe.MapPost("/paiements/{id:guid}/annulation", (Guid id, SaisieAnnulationPaiement saisie, ServiceDettes service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.AnnulerAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.PaiementsFournisseursSaisir);
    }

    /// <summary>Traduit les règles violées en réponses 400 / 404 / 409.</summary>
    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionAchats erreur)
        {
            var statut = erreur.Code switch
            {
                CodesErreurAchats.ReceptionIntrouvable or CodesErreurAchats.FraisIntrouvable or CodesErreurAchats.PaiementIntrouvable
                    => StatusCodes.Status404NotFound,
                CodesErreurAchats.ReceptionDejaValidee or CodesErreurAchats.PaiementSurBrouillon or CodesErreurAchats.PaiementDejaAnnule
                    => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources, erreur.Detail);
        }
        catch (ExceptionStock erreur)
        {
            // Règle du stock violée malgré les contrôles préalables : rien n'a été écrit (transaction annulée).
            return ReponsesErreur.Probleme(StatusCodes.Status400BadRequest, CodesErreurAchats.MouvementRefuse, Ressources, erreur.Code);
        }
    }
}
