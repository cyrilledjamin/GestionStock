using GestionStock.SharedKernel;

namespace GestionStock.Infrastructure.Temps;

/// <summary>Horloge réelle, adossée à <see cref="TimeProvider"/> pour rester remplaçable en test.</summary>
internal sealed class HorlogeSysteme(TimeProvider fournisseurTemps) : IHorloge
{
    private static readonly TimeZoneInfo Douala = TrouverDouala();

    public DateTime MaintenantUtc => fournisseurTemps.GetUtcNow().UtcDateTime;

    public DateOnly AujourdhuiDouala => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(MaintenantUtc, Douala));

    private static TimeZoneInfo TrouverDouala()
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
