using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Comptage du tiroir par coupure du franc CFA.</summary>
public sealed class CoupuresTests
{
    [Fact]
    public void Le_total_additionne_chaque_coupure_multipliee_par_son_nombre()
    {
        var nombres = new Dictionary<decimal, int> { [10_000m] = 2, [2_000m] = 1, [500m] = 3, [25m] = 4, [1m] = 7 };

        Assert.Equal(23_607m, Coupures.Total(nombres));
    }

    [Fact]
    public void Un_nombre_negatif_ou_une_coupure_inconnue_ne_compte_pas()
    {
        var nombres = new Dictionary<decimal, int> { [5_000m] = -2, [3m] = 10, [1_000m] = 1 };

        Assert.Equal(1_000m, Coupures.Total(nombres));
    }

    [Fact]
    public void Les_coupures_vont_du_plus_gros_billet_a_la_plus_petite_piece_sans_doublon()
    {
        var toutes = Coupures.Toutes.ToList();

        Assert.Equal(toutes.OrderDescending(), toutes);
        Assert.Equal(toutes.Count, toutes.Distinct().Count());
        Assert.Equal((10_000m, 1m), (toutes[0], toutes[^1]));
    }
}
