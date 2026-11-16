using System.Globalization;
using System.Reflection;
using GestionStock.Reprise.Contracts;
using GestionStock.Reprise.Domain;
using GestionStock.Reprise.Infrastructure;

namespace GestionStock.ArchitectureTests;

/// <summary>
/// Conventions du module Reprise : messages et en-têtes du modèle dans les deux langues ; aucun schéma propre
/// (la reprise n'écrit que par les contrats d'écriture de Referentiel, Prix et Inventaire).
/// </summary>
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

    [Fact]
    public void La_reprise_n_a_pas_de_DbContext_et_n_ecrit_que_par_les_contrats_des_autres_modules()
    {
        var reprise = typeof(ClasseurReprise).Assembly;

        var contextes = reprise.GetTypes().Where(EstUnDbContext).Select(t => t.FullName).ToList();

        Assert.Empty(contextes);
        Assert.Contains("GestionStock.Referentiel.Contracts", reprise.GetReferencedAssemblies().Select(r => r.Name));
        Assert.Contains("GestionStock.Inventaire.Contracts", reprise.GetReferencedAssemblies().Select(r => r.Name));
    }

    private static bool EstUnDbContext(Type type)
    {
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (parent.FullName == "Microsoft.EntityFrameworkCore.DbContext")
            {
                return true;
            }
        }

        return false;
    }
}
