using GestionStock.Identite.Infrastructure;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GestionStock.Identite.Application;

/// <summary>
/// Connexion par identifiant et mot de passe, et renouvellement du jeton. En cas de succès, le gestionnaire
/// de jetons porteurs écrit lui-même la réponse (jeton d'accès + jeton de renouvellement).
/// </summary>
internal sealed class ServiceConnexion(
    SignInManager<Utilisateur> connexion,
    IOptionsMonitor<BearerTokenOptions> optionsJetons,
    TimeProvider temps)
{
    public async Task<IResult> ConnecterAsync(string identifiant, string motDePasse)
    {
        connexion.AuthenticationScheme = IdentityConstants.BearerScheme;
        var resultat = await connexion.PasswordSignInAsync(identifiant, motDePasse, isPersistent: false, lockoutOnFailure: true);
        if (resultat.Succeeded)
        {
            return TypedResults.Empty;
        }

        // Un compte désactivé est verrouillé sans limite : il le dit plutôt que « réessayez plus tard ».
        var code = resultat.IsLockedOut
            ? (await connexion.UserManager.FindByNameAsync(identifiant) is { Actif: false } ? CodesErreurIdentite.CompteDesactive : CodesErreurIdentite.CompteVerrouille)
            : CodesErreurIdentite.IdentifiantsInvalides;
        return ReponsesErreur.Probleme(StatusCodes.Status401Unauthorized, code, CodesErreurIdentite.Ressources);
    }

    public async Task<IResult> RafraichirAsync(string jetonRenouvellement)
    {
        var protecteur = optionsJetons.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protecteur.Unprotect(jetonRenouvellement);

        if (ticket?.Properties.ExpiresUtc is not { } expiration
            || temps.GetUtcNow() >= expiration
            || await connexion.ValidateSecurityStampAsync(ticket.Principal) is not { } utilisateur)
        {
            return ReponsesErreur.Probleme(StatusCodes.Status401Unauthorized, CodesErreurIdentite.SessionExpiree, CodesErreurIdentite.Ressources);
        }

        var principal = await connexion.CreateUserPrincipalAsync(utilisateur);
        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }
}
