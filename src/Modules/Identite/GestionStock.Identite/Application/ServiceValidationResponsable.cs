using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace GestionStock.Identite.Application;

/// <inheritdoc />
internal sealed class ServiceValidationResponsable(UserManager<Utilisateur> utilisateurs, SignInManager<Utilisateur> connexion)
    : IValidationResponsable
{
    public async Task<string?> VerifierAsync(string identifiant, string motDePasse, string permission, CancellationToken annulation = default)
    {
        if (string.IsNullOrWhiteSpace(identifiant) || string.IsNullOrEmpty(motDePasse))
        {
            return null;
        }

        var responsable = await utilisateurs.FindByNameAsync(identifiant.Trim());
        if (responsable is null)
        {
            return null;
        }

        // Vérifie le mot de passe sans ouvrir de session ; un échec compte pour le verrouillage (5 essais).
        var verification = await connexion.CheckPasswordSignInAsync(responsable, motDePasse, lockoutOnFailure: true);
        if (!verification.Succeeded)
        {
            return null;
        }

        var roles = await utilisateurs.GetRolesAsync(responsable);
        return MatricePermissions.PermissionsDes(roles).Contains(permission, StringComparer.Ordinal)
            ? responsable.UserName
            : null;
    }
}
