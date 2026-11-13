namespace GestionStock.SharedKernel;

/// <summary>
/// Utilisateur à l'origine de l'opération, pour l'audit (ENF03) :
/// chaque mouvement, ticket, remise et annulation porte son auteur.
/// </summary>
public interface IUtilisateurCourant
{
    /// <summary>Identifiant de connexion de l'utilisateur ; jamais vide pour une opération métier.</summary>
    string Identifiant { get; }
}
