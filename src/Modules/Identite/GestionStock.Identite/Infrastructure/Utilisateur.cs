using Microsoft.AspNetCore.Identity;

namespace GestionStock.Identite.Infrastructure;

/// <summary>
/// Compte nominatif (ENF06). L'identifiant de connexion est <see cref="IdentityUser{TKey}.UserName"/> ;
/// aucune donnée personnelle n'est stockée au-delà de cet identifiant.
/// </summary>
internal sealed class Utilisateur : IdentityUser<Guid>
{
    public Utilisateur()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }
}
