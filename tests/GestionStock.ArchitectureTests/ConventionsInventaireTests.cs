using System.Globalization;
using System.Reflection;
using GestionStock.Inventaire.Domain;
using GestionStock.Inventaire.Endpoints;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Inventaire vérifiables sans base de données.</summary>
public sealed class ConventionsInventaireTests
{
    public static TheoryData<string> CodesErreur() =>
    [
        .. typeof(CodesErreurInventaire)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsInventaire.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsInventaire.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }
}
