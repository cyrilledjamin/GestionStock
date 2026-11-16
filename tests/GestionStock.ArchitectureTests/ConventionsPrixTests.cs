using System.Globalization;
using System.Reflection;
using GestionStock.Prix.Contracts;
using GestionStock.Prix.Domain;
using GestionStock.Prix.Endpoints;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions et calculs du module Prix vérifiables sans base de données.</summary>
public sealed class ConventionsPrixTests
{
    public static TheoryData<string> CodesErreur() =>
    [
        .. typeof(CodesErreurPrix)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsPrix.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsPrix.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }

    [Fact]
    public void Assujettie_l_entreprise_decompose_le_TTC_en_HT_et_TVA_a_19_25()
    {
        var (ht, tva) = new RegimeTva(Assujetti: true, TauxPourcent: 19.25m).Decomposer(11925m);

        Assert.Equal(10000m, ht);
        Assert.Equal(1925m, tva);
    }

    [Fact]
    public void Non_assujettie_l_entreprise_vend_a_prix_nets_sans_TVA()
    {
        var (ht, tva) = new RegimeTva(Assujetti: false, TauxPourcent: 19.25m).Decomposer(5000m);

        Assert.Equal(5000m, ht);
        Assert.Equal(0m, tva);
    }
}
