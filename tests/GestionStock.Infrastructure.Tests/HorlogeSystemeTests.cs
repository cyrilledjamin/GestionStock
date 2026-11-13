using GestionStock.Infrastructure.Temps;
using Microsoft.Extensions.Time.Testing;

namespace GestionStock.Infrastructure.Tests;

public sealed class HorlogeSystemeTests
{
    [Fact]
    public void MaintenantUtc_est_en_UTC_et_suit_le_fournisseur_de_temps()
    {
        var instant = new DateTimeOffset(2026, 11, 13, 15, 30, 0, TimeSpan.FromHours(1)); // heure de Douala
        var horloge = new HorlogeSysteme(new FakeTimeProvider(instant));

        Assert.Equal(DateTimeKind.Utc, horloge.MaintenantUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 13, 14, 30, 0, DateTimeKind.Utc), horloge.MaintenantUtc);
    }
}
