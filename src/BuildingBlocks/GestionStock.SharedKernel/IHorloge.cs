namespace GestionStock.SharedKernel;

/// <summary>
/// Source unique de l'heure courante, injectable pour les tests.
/// Toujours en UTC : l'affichage en Africa/Douala se fait côté interface (ENF07).
/// </summary>
public interface IHorloge
{
    /// <summary>Horodatage courant, de genre <see cref="DateTimeKind.Utc"/>.</summary>
    DateTime MaintenantUtc { get; }

    /// <summary>
    /// Date du jour à Douala (Africa/Douala, UTC+1 sans heure d'été) : date métier des prix et des DLC.
    /// Entre 23 h et minuit UTC, c'est déjà le lendemain à Douala.
    /// </summary>
    DateOnly AujourdhuiDouala { get; }
}
