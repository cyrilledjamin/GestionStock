using System.Globalization;
using System.Text.RegularExpressions;
using GestionStock.Rapports.Application;
using GestionStock.Rapports.Endpoints;

namespace GestionStock.ArchitectureTests;

/// <summary>
/// Conventions du module Rapports : lecture seule des vues <c>v_*</c> publiées par les modules (section 7, CQRS côté
/// lecture), sans DbContext ni accès aux tables des autres modules ; messages dans les deux langues.
/// </summary>
public sealed partial class ConventionsRapportsTests
{
    [Fact]
    public void Les_requetes_ne_lisent_que_des_vues_publiees()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(RacineDepot(), "src", "Modules", "Rapports"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        var objets = sources.SelectMany(s => ObjetLu().Matches(s).Select(m => $"{m.Groups[1].Value}.{m.Groups[2].Value}")).Distinct().ToList();

        Assert.NotEmpty(objets);
        Assert.All(objets, objet => Assert.Matches(@"^\w+\.v_\w+$", objet));
    }

    [Fact]
    public void Le_module_n_a_pas_de_DbContext()
    {
        var types = typeof(ServiceRapports).Assembly.GetTypes();

        Assert.DoesNotContain(types, t => HeriteDe(t, "Microsoft.EntityFrameworkCore.DbContext"));
    }

    [Theory]
    [InlineData(CodesErreurRapports.PeriodeInvalide)]
    [InlineData(CodesErreurRapports.JournalInconnu)]
    [InlineData(CodesErreurRapports.FormatInvalide)]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsRapports.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsRapports.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais));
        Assert.False(string.IsNullOrWhiteSpace(anglais));
        Assert.NotEqual(francais, anglais);
    }

    [Fact]
    public void Chaque_journal_a_son_titre_dans_les_deux_langues()
    {
        Assert.All(GestionStock.Rapports.Contracts.JournauxComptables.Tous, journal =>
        {
            var francais = ServiceExports.Textes.GetString($"Journal.{journal}", CultureInfo.GetCultureInfo("fr"));
            var anglais = ServiceExports.Textes.GetString($"Journal.{journal}", CultureInfo.GetCultureInfo("en"));
            Assert.False(string.IsNullOrWhiteSpace(francais));
            Assert.NotEqual(francais, anglais);
        });
    }

    [GeneratedRegex(@"(?:FROM|JOIN)\s+(\w+)\.(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex ObjetLu();

    private static bool HeriteDe(Type type, string nomComplet)
    {
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (parent.FullName == nomComplet)
            {
                return true;
            }
        }

        return false;
    }

    private static string RacineDepot()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "GestionStock.slnx")))
            {
                return dossier.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt introuvable.");
    }
}
