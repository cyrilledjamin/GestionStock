using System.Resources;
using GestionStock.Credit.Application;
using GestionStock.Credit.Contracts;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Credit.Endpoints;

/// <summary>
/// Endpoints HTTP du module Credit (préfixe <c>/api/credit</c>). Les ventes à crédit et les remboursements passent
/// par la Caisse (ticket, session) ; ici : clients, plafonds, fiches et impayés.
/// </summary>
internal static class EndpointsCredit
{
    public const string Prefixe = "/api/credit";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Credit.Ressources.ErreursCredit", typeof(EndpointsCredit).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Credit");

        groupe.MapGet("/clients", async (string? recherche, ServiceCredit service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(recherche, seulementEnRetard: false, annulation)))
            .RequireAuthorization(Permissions.CreditConsulter);

        // RG43 : clients dont une dette dépasse le délai de paiement.
        groupe.MapGet("/impayes", async (ServiceCredit service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(null, seulementEnRetard: true, annulation)))
            .RequireAuthorization(Permissions.CreditConsulter);

        groupe.MapGet("/clients/{id:guid}", (Guid id, ServiceCredit service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirFicheAsync(id, annulation))))
            .RequireAuthorization(Permissions.CreditConsulter);

        groupe.MapPost("/clients", (SaisieClient saisie, ServiceCredit service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var client = await service.CreerAsync(saisie, annulation);
                return TypedResults.Created($"{Prefixe}/clients/{client.Id}", client);
            }))
            .RequireAuthorization(Permissions.ClientsGerer);

        groupe.MapPut("/clients/{id:guid}", (Guid id, SaisieClient saisie, ServiceCredit service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.ClientsGerer);

        groupe.MapPut("/clients/{id:guid}/plafond", (Guid id, SaisiePlafond saisie, ServiceCredit service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.FixerPlafondAsync(id, saisie, annulation))))
            .RequireAuthorization(Permissions.PlafondsGerer);
    }

    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionCredit erreur)
        {
            var statut = erreur.Code == CodesErreurCredit.ClientIntrouvable ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources);
        }
    }
}
