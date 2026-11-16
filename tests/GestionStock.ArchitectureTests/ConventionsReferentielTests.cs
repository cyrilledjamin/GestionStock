using System.Globalization;
using System.Reflection;
using GestionStock.Referentiel.Domain;
using GestionStock.Referentiel.Endpoints;
using GestionStock.Stock.Contracts;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Referentiel vérifiables sans base de données.</summary>
public sealed class ConventionsReferentielTests
{
    public static TheoryData<string> CodesErreur() =>
    [
        .. typeof(CodesErreurReferentiel)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsReferentiel.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsReferentiel.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }

    [Fact]
    public void Les_types_d_emplacement_du_referentiel_et_du_stock_sont_identiques()
    {
        var referentiel = Enum.GetValues<TypeEmplacement>().ToDictionary(v => v.ToString(), v => (int)v);
        var stock = Enum.GetValues<TypeEmplacementStock>().ToDictionary(v => v.ToString(), v => (int)v);

        Assert.Equal(stock.OrderBy(p => p.Key), referentiel.OrderBy(p => p.Key));
    }
}
