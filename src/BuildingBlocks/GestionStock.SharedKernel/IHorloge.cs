namespace GestionStock.SharedKernel;

/// <summary>
/// Source unique de l'heure courante, injectable pour les tests.
/// Toujours en UTC : l'affichage en Africa/Douala se fait côté interface (ENF07).
/// </summary>
public interface IHorloge
{
    /// <summary>Horodatage courant, de genre <see cref="DateTimeKind.Utc"/>.</summary>
    DateTime MaintenantUtc { get; }
}
