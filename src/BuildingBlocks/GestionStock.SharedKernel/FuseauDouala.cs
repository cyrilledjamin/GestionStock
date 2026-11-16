namespace GestionStock.SharedKernel;

/// <summary>
/// Fuseau métier de la boutique (Africa/Douala, UTC+1 sans heure d'été, ENF07) : date d'un horodatage UTC, par exemple
/// pour appliquer le prix du jour d'une vente faite hors ligne.
/// </summary>
public static class FuseauDouala
{
    public static TimeZoneInfo Fuseau { get; } = Trouver();

    /// <summary>Jour à Douala d'un horodatage UTC.</summary>
    public static DateOnly Jour(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Fuseau));

    private static TimeZoneInfo Trouver()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Douala");
        }
        catch (TimeZoneNotFoundException)
        {
            // Image sans base des fuseaux : Douala est à UTC+1 toute l'année.
            return TimeZoneInfo.CreateCustomTimeZone("Africa/Douala", TimeSpan.FromHours(1), "Douala", "WAT");
        }
    }
}
