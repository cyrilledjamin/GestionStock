using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GestionStock.Identite.Contracts;
using GestionStock.Stock.Contracts;

namespace GestionStock.ArchitectureTests;

/// <summary>
/// Conventions du client Blazor, vérifiées sur ses sources : le client WebAssembly n'est pas chargé par les tests
/// (il s'exécute dans le navigateur), d'où une lecture des fichiers du dépôt.
/// </summary>
public sealed partial class ConventionsClientTests
{
    private static readonly string RepertoireClient = Path.Combine(RacineDepot(), "src", "Client", "GestionStock.Client");

    [Fact]
    public void Le_client_ne_reference_aucun_projet_serveur()
    {
        var projet = XDocument.Load(Path.Combine(RepertoireClient, "GestionStock.Client.csproj"));

        var references = projet.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include"));

        Assert.Empty(references);
    }

    [Fact]
    public void Les_permissions_du_client_sont_celles_de_l_API()
    {
        var client = ConstanteChaine().Matches(BlocClasse("Permissions")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var api = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal(api.OrderBy(p => p.Key), client.OrderBy(p => p.Key));
    }

    [Fact]
    public void Les_roles_du_client_sont_ceux_de_l_API()
    {
        var ligne = BlocClasse("Roles").Split('\n').Single(l => l.Contains("Tous { get; } = [", StringComparison.Ordinal));

        var client = Regex.Matches(ligne, "\"([^\"]+)\"").Select(m => m.Groups[1].Value);

        Assert.Equal(Roles.Tous, client);
    }

    [Fact]
    public void Chaque_texte_utilise_par_l_interface_existe_en_francais_et_en_anglais()
    {
        var francais = Cles("Textes.resx");
        var anglais = Cles("Textes.en.resx");

        var utilisees = Directory.EnumerateFiles(RepertoireClient, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => CleStatique().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        // Clés construites à l'exécution : rôles, statuts, types de mouvement, modes de paiement, opérations, niveaux.
        utilisees.UnionWith(Roles.Tous.Select(r => $"Role.{r}"));
        utilisees.UnionWith(["Statut.Brouillon", "Statut.Validee"]);
        utilisees.UnionWith(Enum.GetNames<TypeMouvementStock>().Select(t => $"Mouvement.{t}"));
        utilisees.UnionWith(["Mode.Especes", "Mode.MobileMoney", "Mode.Carte", "Mode.Credit"]);
        utilisees.UnionWith(["StatutSession.Ouverte", "StatutSession.Comptee", "StatutSession.Validee"]);
        utilisees.UnionWith(["Operation.VenteCredit", "Operation.Remboursement", "Operation.Annulation"]);
        utilisees.UnionWith(["Niveau.Unite", "Niveau.Pack", "Niveau.Carton", "Niveau.Sac", "Niveau.Palette"]);

        Assert.Empty(utilisees.Except(francais));
        Assert.Empty(utilisees.Except(anglais));
        Assert.Equal(francais.Order(), anglais.Order());
    }

    /// <summary>Corps d'une classe statique de Services/Modeles.cs, de sa déclaration à son accolade fermante.</summary>
    private static string BlocClasse(string nom)
    {
        var source = File.ReadAllText(Path.Combine(RepertoireClient, "Services", "Modeles.cs")).ReplaceLineEndings("\n");
        var debut = source.IndexOf($"public static class {nom}\n", StringComparison.Ordinal);
        Assert.True(debut >= 0, $"Classe {nom} introuvable dans Modeles.cs.");
        var fin = source.IndexOf("\n}", debut, StringComparison.Ordinal);
        return source[debut..fin];
    }

    private static HashSet<string> Cles(string fichier) =>
        [.. XDocument.Load(Path.Combine(RepertoireClient, "Ressources", fichier))
            .Descendants("data")
            .Select(d => (string)d.Attribute("name")!)];

    private static string RacineDepot()
    {
        for (var dossier = new DirectoryInfo(AppContext.BaseDirectory); dossier is not null; dossier = dossier.Parent)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "GestionStock.slnx")))
            {
                return dossier.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt (GestionStock.slnx) introuvable.");
    }

    [GeneratedRegex("public const string (\\w+) = \"([^\"]+)\";")]
    private static partial Regex ConstanteChaine();

    [GeneratedRegex("L\\[\"([^\"$]+)\"")]
    private static partial Regex CleStatique();
}
