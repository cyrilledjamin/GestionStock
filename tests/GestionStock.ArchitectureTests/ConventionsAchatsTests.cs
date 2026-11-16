using System.Globalization;
using System.Reflection;
using GestionStock.Achats.Domain;
using GestionStock.Achats.Endpoints;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Achats vérifiables sans base de données.</summary>
public sealed class ConventionsAchatsTests
{
    public static TheoryData<string> CodesErreur() =>
    [
        .. typeof(CodesErreurAchats)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsAchats.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsAchats.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }
}
