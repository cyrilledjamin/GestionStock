namespace GestionStock.Identite.Contracts;

/// <summary>
/// Désignation d'un membre du personnel dans les documents (reçu, rapports) : « Prénom Nom (matricule) », ou le
/// matricule seul pour un compte antérieur sans prénom. Le matricule reste l'identifiant tracé (étape 31).
/// </summary>
public interface IAnnuairePersonnel
{
    /// <summary>Désignation du compte de ce matricule ; le matricule lui-même s'il est inconnu.</summary>
    Task<string> DesignationAsync(string matricule, CancellationToken annulation = default);

    /// <summary>
    /// Désignations de plusieurs matricules (les absents et les vides sont ignorés) : la table « matricule → nom » qu'un
    /// document joint pour afficher les personnes qu'il cite, sans perdre le matricule tracé.
    /// </summary>
    async Task<IReadOnlyDictionary<string, string>> DesignationsAsync(IEnumerable<string?> matricules, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(matricules);
        var designations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var matricule in matricules.OfType<string>().Where(m => m.Length > 0).Distinct(StringComparer.Ordinal))
        {
            designations[matricule] = await DesignationAsync(matricule, annulation);
        }

        return designations;
    }
}
