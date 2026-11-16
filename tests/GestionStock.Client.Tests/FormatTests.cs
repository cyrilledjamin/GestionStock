using System.Globalization;
using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Quantités sans zéros inutiles et unités traduites selon la langue de l'interface.</summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData("fr-CM", 20, 3, "20")]
    [InlineData("fr-CM", 2.5, 3, "2,5")]
    [InlineData("en-GB", 2.5, 3, "2.5")]
    [InlineData("en-GB", 20, 3, "20")]
    [InlineData("en-GB", 1.2345, 2, "1.23")]
    [InlineData("en-GB", 1500, 0, "1,500")]
    public void Une_quantite_s_affiche_sans_zeros_inutiles(string culture, double quantite, int precision, string attendu)
    {
        using var _ = Culture(culture);

        Assert.Equal(attendu, Format.Quantite((decimal)quantite, precision));
    }

    [Theory]
    [InlineData("boîte", "box")]
    [InlineData("Boite", "box")]
    [InlineData("pièce", "piece")]
    [InlineData("Sac", "bag")]
    [InlineData("kg", "kg")]
    [InlineData("Carton de 48", "Carton de 48")]
    public void Une_unite_courante_est_traduite_en_anglais(string unite, string attendue)
    {
        using var _ = Culture("en-GB");

        Assert.Equal(attendue, Format.Unite(unite));
    }

    [Fact]
    public void En_francais_l_unite_reste_celle_saisie()
    {
        using var _ = Culture("fr-CM");

        Assert.Equal("boîte", Format.Unite("boîte"));
        Assert.Equal("", Format.Unite(null));
    }

    private static CultureRestauree Culture(string nom) => new(CultureInfo.GetCultureInfo(nom));

    private sealed class CultureRestauree : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _interface = CultureInfo.CurrentUICulture;

        public CultureRestauree(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _interface;
        }
    }
}
