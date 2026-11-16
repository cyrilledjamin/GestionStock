using System.Security.Claims;
using GestionStock.Identite.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GestionStock.Identite.Application;

/// <summary>Revendications propres au module, portées par le jeton.</summary>
internal static class RevendicationsIdentite
{
    /// <summary>Présente tant que le mot de passe est provisoire : toute permission est alors refusée.</summary>
    public const string MotDePasseProvisoire = "gestionstock:mdp_provisoire";
}

/// <summary>Ajoute au jeton l'indicateur de mot de passe provisoire (relu à chaque connexion et renouvellement).</summary>
internal sealed class FabriquePrincipal(
    UserManager<Utilisateur> utilisateurs,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Utilisateur, IdentityRole<Guid>>(utilisateurs, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Utilisateur user)
    {
        var identite = await base.GenerateClaimsAsync(user);
        if (user.MotDePasseProvisoire)
        {
            identite.AddClaim(new Claim(RevendicationsIdentite.MotDePasseProvisoire, "true"));
        }

        return identite;
    }
}
