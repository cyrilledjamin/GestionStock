using System.Globalization;
using System.Reflection;
using System.Resources;
using GestionStock.Stock;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Stock vérifiables sans base de données.</summary>
public sealed class ConventionsStockTests
{
    private static readonly ResourceManager Ressources =
        new("GestionStock.Stock.Ressources.ErreursStock", typeof(ModuleStock).Assembly);

    public static TheoryData<string> CodesErreur()
    {
        var codes = typeof(CodesErreurStock)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);
        return [.. codes];
    }

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }

    [Fact]
    public void Les_types_de_mouvement_du_contrat_et_du_domaine_sont_identiques()
    {
        AssertEnumerationsIdentiques<TypeMouvementStock, TypeMouvement>();
    }

    [Fact]
    public void Les_types_d_emplacement_du_contrat_et_du_domaine_sont_identiques()
    {
        AssertEnumerationsIdentiques<TypeEmplacementStock, TypeEmplacement>();
    }

    private static void AssertEnumerationsIdentiques<TContrat, TDomaine>()
        where TContrat : struct, Enum
        where TDomaine : struct, Enum
    {
        var contrat = Enum.GetValues<TContrat>().ToDictionary(v => v.ToString(), v => Convert.ToInt32(v, CultureInfo.InvariantCulture));
        var domaine = Enum.GetValues<TDomaine>().ToDictionary(v => v.ToString(), v => Convert.ToInt32(v, CultureInfo.InvariantCulture));

        Assert.Equal(domaine.OrderBy(p => p.Key), contrat.OrderBy(p => p.Key));
    }
}
