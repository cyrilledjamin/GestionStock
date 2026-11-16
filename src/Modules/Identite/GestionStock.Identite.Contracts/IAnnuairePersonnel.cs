namespace GestionStock.Identite.Contracts;

/// <summary>
/// Désignation d'un membre du personnel dans les documents (reçu, rapports) : « Prénom Nom (matricule) », ou le
/// matricule seul pour un compte antérieur sans prénom. Le matricule reste l'identifiant tracé (étape 31).
/// </summary>
public interface IAnnuairePersonnel
{
    /// <summary>Désignation du compte de ce matricule ; le matricule lui-même s'il est inconnu.</summary>
    Task<string> DesignationAsync(string matricule, CancellationToken annulation = default);
}
