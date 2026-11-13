using GestionStock.SharedKernel;

namespace GestionStock.Infrastructure.Temps;

/// <summary>Horloge réelle, adossée à <see cref="TimeProvider"/> pour rester remplaçable en test.</summary>
internal sealed class HorlogeSysteme(TimeProvider fournisseurTemps) : IHorloge
{
    public DateTime MaintenantUtc => fournisseurTemps.GetUtcNow().UtcDateTime;
}
