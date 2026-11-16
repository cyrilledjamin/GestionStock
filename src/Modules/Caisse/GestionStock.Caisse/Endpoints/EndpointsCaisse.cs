using System.Globalization;
using System.Resources;
using GestionStock.Caisse.Application;
using GestionStock.Caisse.Domain;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Caisse.Endpoints;

/// <summary>Endpoints HTTP du module Caisse (préfixe <c>/api/caisse</c>).</summary>
internal static class EndpointsCaisse
{
    public const string Prefixe = "/api/caisse";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Caisse.Ressources.ErreursCaisse", typeof(EndpointsCaisse).Assembly);

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Caisse");

        // Sessions du caissier connecté (UC24)
        groupe.MapGet("/postes", (ServiceSessions sessions) => TypedResults.Ok(sessions.Postes))
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapPost("/sessions", (OuvertureSession saisie, ServiceSessions sessions, CancellationToken annulation) =>
            Executer(async () =>
            {
                var session = await sessions.OuvrirAsync(saisie, annulation);
                return TypedResults.Created($"{Prefixe}/sessions/{session.Id}", session);
            }))
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapGet("/sessions/courante", async (ServiceSessions sessions, CancellationToken annulation) =>
            await sessions.CouranteAsync(annulation) is { } session ? Results.Ok(session) : Results.NoContent())
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapPost("/sessions/courante/cloture", (ClotureSession saisie, ServiceSessions sessions, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await sessions.CloturerAsync(saisie, annulation))))
            .RequireAuthorization(Permissions.CaisseVendre);

        // Supervision des sessions (Gérant, Comptable)
        groupe.MapGet("/sessions", async (StatutSession? statut, ServiceSessions sessions, CancellationToken annulation) =>
            TypedResults.Ok(await sessions.ListerAsync(statut, annulation)))
            .RequireAuthorization(Permissions.SessionsConsulter);

        groupe.MapGet("/sessions/{id:guid}", (Guid id, ServiceSessions sessions, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await sessions.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.SessionsConsulter);

        groupe.MapPost("/sessions/{id:guid}/validation", (Guid id, ServiceSessions sessions, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await sessions.ValiderAsync(id, annulation))))
            .RequireAuthorization(Permissions.SessionsValider);

        // Tickets, avoirs, remboursements (UC17, UC18, UC27)
        groupe.MapPost("/tickets", (DemandeVente demande, ServiceTickets tickets, CancellationToken annulation) =>
            Executer(async () =>
            {
                var (ticket, deja) = await tickets.VendreAsync(demande, annulation);
                return deja ? TypedResults.Ok(ticket) : TypedResults.Created($"{Prefixe}/tickets/{ticket.Id}", ticket);
            }))
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapPost("/avoirs", (DemandeAvoir demande, ServiceTickets tickets, CancellationToken annulation) =>
            Executer(async () =>
            {
                var (avoir, deja) = await tickets.FaireAvoirAsync(demande, annulation);
                return deja ? TypedResults.Ok(avoir) : TypedResults.Created($"{Prefixe}/tickets/{avoir.Id}", avoir);
            }))
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapPost("/remboursements", (DemandeRemboursement demande, ServiceTickets tickets, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await tickets.RembourserAsync(demande, annulation))))
            .RequireAuthorization(Permissions.CaisseVendre);

        groupe.MapGet("/sessions/{id:guid}/tickets", async (Guid id, ServiceTickets tickets, CancellationToken annulation) =>
            TypedResults.Ok(await tickets.ListerSessionAsync(id, annulation)))
            .RequireAuthorization(Permissions.TicketsConsulter);

        groupe.MapGet("/tickets/{id:guid}", (Guid id, ServiceTickets tickets, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await tickets.ObtenirAsync(id, annulation))))
            .RequireAuthorization(Permissions.TicketsConsulter);

        groupe.MapGet("/tickets/numero/{numero}", (string numero, ServiceTickets tickets, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await tickets.ObtenirParNumeroAsync(numero, annulation))))
            .RequireAuthorization(Permissions.TicketsConsulter);

        // Reçu PDF dans la langue de la requête (ENF09) ; l'impression se fait sur le poste client.
        groupe.MapGet("/tickets/{id:guid}/recu.pdf", (Guid id, ServiceRecus recus, CancellationToken annulation) =>
            Executer(async () =>
            {
                var langue = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                var (pdf, numero) = await recus.GenererPdfAsync(id, langue, annulation);
                return TypedResults.File(pdf, "application/pdf", $"recu-{numero}.pdf");
            }))
            .RequireAuthorization(Permissions.TicketsConsulter);
    }

    /// <summary>Traduit les règles violées en réponses 400 / 404 / 409.</summary>
    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionCaisse erreur)
        {
            var statut = erreur.Code switch
            {
                CodesErreurCaisse.SessionIntrouvable or CodesErreurCaisse.TicketIntrouvable => StatusCodes.Status404NotFound,
                CodesErreurCaisse.SessionDejaOuverte or CodesErreurCaisse.PosteOccupe or CodesErreurCaisse.AucuneSessionOuverte
                    or CodesErreurCaisse.SessionNonComptee => StatusCodes.Status409Conflict,
                CodesErreurCaisse.ValidationRefusee => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, Ressources, erreur.Detail);
        }
    }
}
