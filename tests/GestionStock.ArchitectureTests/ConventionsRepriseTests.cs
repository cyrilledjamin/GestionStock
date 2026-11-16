using System.Globalization;
using System.Reflection;
using GestionStock.Reprise.Contracts;
using GestionStock.Reprise.Domain;
using GestionStock.Reprise.Infrastructure;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Reprise : messages et en-têtes du modèle dans les deux langues.</summary>
public sealed class ConventionsRepriseTests
{
    public static TheoryData<string> Cles() =>
    [
        .. typeof(CodesErreurReprise)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
        .. ColonnesReprise.Toutes.Select(c => $"Reprise.Colonne.{c}"),
    ];

    [Theory]
    [MemberData(nameof(Cles))]
    public void Chaque_texte_existe_en_francais_et_en_anglais(string cle)
    {
        var francais = ClasseurReprise.Textes.GetString(cle, CultureInfo.GetCultureInfo("fr"));
        var anglais = ClasseurReprise.Textes.GetString(cle, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Texte français absent pour {cle}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Texte anglais absent pour {cle}.");
        Assert.NotEqual(francais, anglais);
    }
}
