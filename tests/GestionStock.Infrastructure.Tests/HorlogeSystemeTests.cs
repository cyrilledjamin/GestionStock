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

    [Theory]
    [InlineData(22, 59, 13)]
    [InlineData(23, 0, 14)]
    [InlineData(23, 30, 14)]
    public void La_date_du_jour_est_celle_de_Douala(int heureUtc, int minuteUtc, int jourAttendu)
    {
        var horloge = new HorlogeSysteme(new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, heureUtc, minuteUtc, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 11, jourAttendu), horloge.AujourdhuiDouala);
    }
}
