using GestionStock.Client.Composants;

namespace GestionStock.Client.Tests;

/// <summary>Échelle des graphiques : bornes rondes qui encadrent les valeurs, zéro compris, graduations régulières.</summary>
public sealed class EchelleGraphiqueTests
{
    [Fact]
    public void Les_bornes_sont_rondes_et_encadrent_les_valeurs()
    {
        var echelle = EchelleGraphique.Pour([12_000m, 48_500m, 31_000m]);

        Assert.Equal(0m, echelle.Bas);
        Assert.Equal(60_000m, echelle.Haut);
        Assert.Equal([0m, 20_000m, 40_000m, 60_000m], echelle.Graduations);
    }

    [Fact]
    public void Une_valeur_negative_descend_sous_zero()
    {
        var echelle = EchelleGraphique.Pour([-300m, 900m]);

        Assert.True(echelle.Bas <= -300m);
        Assert.True(echelle.Haut >= 900m);
        Assert.Contains(0m, echelle.Graduations);
    }

    [Fact]
    public void Sans_valeur_ou_avec_des_zeros_l_echelle_reste_utilisable()
    {
        Assert.Equal(1m, EchelleGraphique.Pour([]).Haut);
        Assert.Equal(1m, EchelleGraphique.Pour([0m, 0m]).Haut);
    }

    [Fact]
    public void La_position_va_de_zero_en_bas_a_un_en_haut()
    {
        var echelle = EchelleGraphique.Pour([100m]);

        Assert.Equal(0d, echelle.Position(0m));
        Assert.Equal(1d, echelle.Position(echelle.Haut));
        Assert.Equal(.5d, echelle.Position(echelle.Haut / 2));
        Assert.Equal(1d, echelle.Position(echelle.Haut * 10)); // hors échelle : bornée
    }
}
