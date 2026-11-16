using System.Security.Claims;
using GestionStock.Identite.Application;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionStock.Identite.Endpoints;

/// <summary>Connexion par matricule (l'identifiant de connexion) et mot de passe.</summary>
internal sealed record DemandeConnexion(string Identifiant, string MotDePasse);

internal sealed record DemandeRafraichissement(string JetonRenouvellement);

internal sealed record DemandeMotDePasse(string? MotDePasse);

internal sealed record DemandeChangementMotDePasse(string? Actuel, string? Nouveau);

internal sealed record DemandeLangue(string? Langue);

/// <summary>Utilisateur connecté : fiche, permissions (le client adapte son menu) et mot de passe provisoire.</summary>
internal sealed record ProfilUtilisateur(
    string Identifiant,
    string Prenom,
    string Nom,
    string NomComplet,
    string Langue,
    bool MotDePasseProvisoire,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

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
            service.ConnecterAsync(demande.Identifiant?.Trim() ?? "", demande.MotDePasse ?? "")).AllowAnonymous();
        groupe.MapPost("/rafraichir", (DemandeRafraichissement demande, ServiceConnexion service) =>
            service.RafraichirAsync(demande.JetonRenouvellement ?? "")).AllowAnonymous();

        // Tout utilisateur connecté, y compris avec un mot de passe provisoire (sans permission).
        groupe.MapGet("/moi", async (ClaimsPrincipal utilisateur, ServiceUtilisateurs service, CancellationToken annulation) =>
        {
            var roles = utilisateur.FindAll(ClaimTypes.Role).Select(c => c.Value).Order(StringComparer.Ordinal).ToList();
            var provisoire = utilisateur.HasClaim(c => c.Type == RevendicationsIdentite.MotDePasseProvisoire);
            var compte = await service.ProfilAsync(utilisateur.Identity!.Name!, annulation);
            return TypedResults.Ok(new ProfilUtilisateur(
                utilisateur.Identity!.Name!,
                compte?.Prenom ?? "",
                compte?.Nom ?? "",
                compte?.NomComplet ?? utilisateur.Identity.Name!,
                compte?.Langue ?? "fr",
                provisoire,
                roles,
                provisoire ? [] : [.. MatricePermissions.PermissionsDes(roles)]));
        }).RequireAuthorization();

        groupe.MapPost("/moi/mot-de-passe", (DemandeChangementMotDePasse demande, ClaimsPrincipal utilisateur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () =>
            {
                await service.ChangerMonMotDePasseAsync(utilisateur.Identity!.Name!, demande.Actuel, demande.Nouveau, annulation);
                return TypedResults.NoContent();
            })).RequireAuthorization();

        groupe.MapPut("/moi/langue", (DemandeLangue demande, ClaimsPrincipal utilisateur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () =>
            {
                await service.ChangerMaLangueAsync(utilisateur.Identity!.Name!, demande.Langue, annulation);
                return TypedResults.NoContent();
            })).RequireAuthorization();

        // Gestion des comptes : Administrateur (8.3).
        var comptes = groupe.MapGroup("/utilisateurs").RequireAuthorization(Permissions.UtilisateursGerer);

        comptes.MapGet("", async (ServiceUtilisateurs service, CancellationToken annulation) =>
            TypedResults.Ok(await service.ListerAsync(annulation)));

        comptes.MapGet("/{id:guid}", (Guid id, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ObtenirAsync(id, annulation))));

        comptes.MapPost("", (SaisieCompte saisie, ClaimsPrincipal auteur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () =>
            {
                var fiche = await service.CreerAsync(saisie, auteur.Identity!.Name!, motDePasseProvisoire: true, annulation);
                return TypedResults.Created($"{Prefixe}/utilisateurs/{fiche.Compte.Id}", fiche);
            }));

        comptes.MapPut("/{id:guid}", (Guid id, SaisieFiche saisie, ClaimsPrincipal auteur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ModifierAsync(id, saisie, auteur.Identity!.Name!, annulation))));

        comptes.MapPost("/{id:guid}/desactivation", (Guid id, ClaimsPrincipal auteur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.DesactiverAsync(id, auteur.Identity!.Name!, annulation))));

        comptes.MapPost("/{id:guid}/reactivation", (Guid id, ClaimsPrincipal auteur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ReactiverAsync(id, auteur.Identity!.Name!, annulation))));

        comptes.MapPost("/{id:guid}/mot-de-passe", (Guid id, DemandeMotDePasse demande, ClaimsPrincipal auteur, ServiceUtilisateurs service, CancellationToken annulation) =>
            Executer(async () => TypedResults.Ok(await service.ReinitialiserMotDePasseAsync(id, demande.MotDePasse, auteur.Identity!.Name!, annulation))));
    }

    /// <summary>Traduit les règles violées en réponses 400 / 404 / 409.</summary>
    private static async Task<IResult> Executer(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionIdentite erreur)
        {
            var statut = erreur.Code switch
            {
                CodesErreurIdentite.CompteIntrouvable => StatusCodes.Status404NotFound,
                CodesErreurIdentite.IdentifiantDejaUtilise or CodesErreurIdentite.DernierAdministrateur => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return ReponsesErreur.Probleme(statut, erreur.Code, CodesErreurIdentite.Ressources, erreur.Detail);
        }
    }
}
