using GestionStock.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace GestionStock.Identite.Application;

/// <summary>Auteur de la requête HTTP en cours, lu dans son jeton (identifiant de connexion).</summary>
internal sealed class UtilisateurCourant(IHttpContextAccessor accesseur) : IUtilisateurCourant
{
    public string Identifiant =>
        accesseur.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } nom }
            ? nom
            : throw new InvalidOperationException("Aucun utilisateur authentifié : une opération métier exige un auteur (ENF03).");
}
