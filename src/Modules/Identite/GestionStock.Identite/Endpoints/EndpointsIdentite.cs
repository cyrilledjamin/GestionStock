using System.Security.Claims;
using GestionStock.Identite.Application;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Identite.Endpoints;

internal sealed record DemandeConnexion(string Identifiant, string MotDePasse);

internal sealed record DemandeRafraichissement(string JetonRenouvellement);

internal sealed record DemandeCreationUtilisateur(string Identifiant, string MotDePasse, string Role);

/// <summary>Utilisateur connecté, avec ses permissions (le client adapte son menu).</summary>
internal sealed record ProfilUtilisateur(string Identifiant, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

/// <summary>Endpoints HTTP du module Identite (préfixe <c>/api/identite</c>).</summary>
internal static class EndpointsIdentite
{
    public const string Prefixe = "/api/identite";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var groupe = routes.MapGroup(Prefixe).WithTags("Identite");

        // Seuls endpoints anonymes du module, par choix explicite.
        groupe.MapPost("/connexion", (DemandeConnexion demande, ServiceConnexion service) =>
            service.ConnecterAsync(demande.Identifiant ?? "", demande.MotDePasse ?? "")).AllowAnonymous();
        groupe.MapPost("/rafraichir", (DemandeRafraichissement demande, ServiceConnexion service) =>
            service.RafraichirAsync(demande.JetonRenouvellement ?? "")).AllowAnonymous();

        // Tout utilisateur connecté.
        groupe.MapGet("/moi", (ClaimsPrincipal utilisateur) =>
        {
            var roles = utilisateur.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal).ToList();
            return TypedResults.Ok(new ProfilUtilisateur(utilisateur.Identity!.Name!, roles, [.. MatricePermissions.PermissionsDes(roles)]));
        }).RequireAuthorization();

        groupe.MapGet("/utilisateurs", async (ServiceUtilisateurs service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(annulation))).RequireAuthorization(Permissions.UtilisateursGerer);

        groupe.MapPost("/utilisateurs", async (DemandeCreationUtilisateur demande, ServiceUtilisateurs service, CancellationToken annulation) =>
        {
            try
            {
                var compte = await service.CreerAsync(demande.Identifiant, demande.MotDePasse, demande.Role, annulation);
                return TypedResults.Created($"{Prefixe}/utilisateurs/{compte.Id}", compte);
            }
            catch (ExceptionIdentite erreur)
            {
                var statut = erreur.Code == CodesErreurIdentite.IdentifiantDejaUtilise
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                return ReponsesErreur.Probleme(statut, erreur.Code, CodesErreurIdentite.Ressources, erreur.Detail);
            }
        }).RequireAuthorization(Permissions.UtilisateursGerer);
    }
}
