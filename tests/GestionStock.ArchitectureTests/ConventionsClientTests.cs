using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GestionStock.Identite.Contracts;
using GestionStock.Rapports.Contracts;
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

        // Clés construites à l'exécution : rôles, statuts, types de mouvement, modes de paiement, opérations, niveaux,
        // types et statuts d'inventaire, statuts et motifs de démarque, frais, dettes, rapports et journaux.
        utilisees.UnionWith(Roles.Tous.Select(r => $"Role.{r}"));
        utilisees.UnionWith(["Statut.Brouillon", "Statut.Validee"]);
        utilisees.UnionWith(Enum.GetNames<TypeMouvementStock>().Select(t => $"Mouvement.{t}"));
        utilisees.UnionWith(["Mode.Especes", "Mode.MobileMoney", "Mode.Carte", "Mode.Credit"]);
        utilisees.UnionWith(["StatutSession.Ouverte", "StatutSession.Comptee", "StatutSession.Validee"]);
        utilisees.UnionWith(["Operation.VenteCredit", "Operation.Remboursement", "Operation.Annulation"]);
        utilisees.UnionWith(["Niveau.Unite", "Niveau.Pack", "Niveau.Carton", "Niveau.Sac", "Niveau.Palette"]);
        utilisees.UnionWith(["TypeInventaire.Ouverture", "TypeInventaire.Complet", "TypeInventaire.Partiel"]);
        utilisees.UnionWith(["StatutInventaire.EnCours", "StatutInventaire.Valide", "StatutInventaire.Annule"]);
        utilisees.UnionWith(["StatutDemarque.Declaree", "StatutDemarque.Validee", "StatutDemarque.Refusee"]);
        utilisees.UnionWith(["MotifDemarque.Casse", "MotifDemarque.Vol", "MotifDemarque.Perime", "MotifDemarque.Freinte"]);
        utilisees.UnionWith(["TypeFrais.Transport", "TypeFrais.Transit", "TypeFrais.Douane", "TypeFrais.Autre", "Cle.Valeur", "Cle.Quantite"]);
        utilisees.UnionWith(["Mode.Virement", "Mode.Cheque", "StatutDette.NonPayee", "StatutDette.PartiellementPayee", "StatutDette.Payee"]);
        utilisees.UnionWith(["Rapport.Ventes", "Rapport.Stock", "Rapport.CreancesDettes", "Rapport.Caisse"]);
        utilisees.UnionWith(JournauxComptables.Tous.SelectMany(j => new[] { $"Journal.{j}", $"JournalAide.{j}" }));
        utilisees.UnionWith(["ActionCompte.Creation", "ActionCompte.Modification", "ActionCompte.Desactivation", "ActionCompte.Reactivation"]);
        utilisees.UnionWith(["ActionCompte.ReinitialisationMotDePasse", "ActionCompte.ChangementMotDePasse"]);
        utilisees.UnionWith(["FicheEnregistree", "CompteDesactive", "CompteReactive", "MotDePasseReinitialise"]);
        utilisees.UnionWith(Enum.GetNames<GestionStock.Caisse.Domain.TypeAnomalie>().SelectMany(t => new[] { $"Anomalie.{t}", $"AnomalieAide.{t}" }));

        // Exploitation : statuts, soldes et grandeurs du contrôle (ENF04), types de sauvegarde (décision 32).
        utilisees.UnionWith(Enum.GetNames<GestionStock.Exploitation.Domain.StatutControle>().Select(t => $"StatutControle.{t}"));
        utilisees.UnionWith(Enum.GetNames<GestionStock.Exploitation.Domain.StatutEcart>().Select(t => $"StatutEcart.{t}"));
        utilisees.UnionWith(Enum.GetNames<GestionStock.Exploitation.Domain.DomaineSolde>().Select(t => $"DomaineSolde.{t}"));
        utilisees.UnionWith(Enum.GetNames<GestionStock.Exploitation.Domain.GrandeurSolde>().Select(t => $"GrandeurSolde.{t}"));
        utilisees.UnionWith(Enum.GetNames<GestionStock.Exploitation.Contracts.TypeSauvegarde>().Select(t => $"TypeSauvegarde.{t}"));

        Assert.Empty(utilisees.Except(francais));
        Assert.Empty(utilisees.Except(anglais));
        Assert.Equal(francais.Order(), anglais.Order());
    }

    [Fact]
    public void Les_couleurs_ne_sont_definies_que_dans_les_jetons_de_la_feuille_de_style()
    {
        var feuille = File.ReadAllText(Path.Combine(RepertoireClient, "wwwroot", "css", "app.css")).ReplaceLineEndings("\n");
        var finJetons = feuille.IndexOf("\n}", feuille.IndexOf(":root {", StringComparison.Ordinal), StringComparison.Ordinal);
        var horsJetons = feuille[finJetons..];

        var dansLaFeuille = Couleur().Matches(horsJetons).Select(m => m.Value);
        var dansLesComposants = Directory.EnumerateFiles(RepertoireClient, "*.razor", SearchOption.AllDirectories)
            .Where(f => StyleOuCouleur().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName);

        Assert.Empty(dansLaFeuille);
        Assert.Empty(dansLesComposants);
    }

    [Fact]
    public void Chaque_icone_utilisee_existe()
    {
        var disponibles = NomIcone().Matches(File.ReadAllText(Path.Combine(RepertoireClient, "Composants", "Icone.razor")))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var utilisees = Directory.EnumerateFiles(RepertoireClient, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => IconeUtilisee().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        utilisees.UnionWith(["erreur", "succes"]); // Message : nom choisi à l'exécution.

        Assert.NotEmpty(disponibles);
        Assert.Empty(utilisees.Except(disponibles));
    }

    /// <summary>
    /// Contrastes des couples de jetons utilisés ensemble (WCAG 2.2 AA) : 4,5:1 pour le texte, 3:1 pour les traits et
    /// formes qui portent un sens (cadre des champs, barres des graphiques).
    /// </summary>
    [Theory]
    [InlineData("texte", "fond", 4.5)]
    [InlineData("texte", "surface", 4.5)]
    [InlineData("texte-menu", "surface", 4.5)]
    [InlineData("discret", "surface", 4.5)]
    [InlineData("discret", "fond", 4.5)]
    [InlineData("discret", "surface-douce", 4.5)]
    [InlineData("discret", "neutre-clair", 4.5)]
    [InlineData("accent", "surface", 4.5)]
    [InlineData("accent", "fond", 4.5)]
    [InlineData("accent", "surface-douce", 4.5)]
    [InlineData("accent", "accent-clair", 4.5)]
    [InlineData("accent-fonce", "accent-clair", 4.5)]
    [InlineData("sur-accent", "accent", 4.5)]
    [InlineData("sur-accent", "accent-fonce", 4.5)]
    [InlineData("sur-accent", "danger", 4.5)]
    [InlineData("sur-accent", "danger-fonce", 4.5)]
    [InlineData("succes", "succes-clair", 4.5)]
    [InlineData("succes", "surface", 4.5)]
    [InlineData("attention", "attention-clair", 4.5)]
    [InlineData("danger", "danger-clair", 4.5)]
    [InlineData("danger", "surface", 4.5)]
    [InlineData("info", "info-clair", 4.5)]
    [InlineData("neutre", "neutre-clair", 4.5)]
    [InlineData("bordure-forte", "surface", 3.0)]
    [InlineData("accent-doux", "surface", 3.0)]
    public void Les_couleurs_des_jetons_sont_assez_contrastees(string premier, string second, double minimum)
    {
        var jetons = JetonCouleur().Matches(File.ReadAllText(Path.Combine(RepertoireClient, "wwwroot", "css", "app.css")))
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

        var rapport = Contraste(jetons[premier], jetons[second]);

        Assert.True(rapport >= minimum, $"--{premier} sur --{second} : {rapport:0.00}:1, il faut {minimum}:1.");
    }

    [Fact]
    public void Les_confirmations_passent_par_le_dialogue_de_l_application()
    {
        // La fenêtre « confirm » du navigateur n'est pas traduite et bloque la page : Confirmation et son dialogue.
        var appels = Directory.EnumerateFiles(RepertoireClient, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("\"confirm\"", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        Assert.Empty(appels);
    }

    [Fact]
    public void La_police_est_embarquee_et_gardee_hors_ligne()
    {
        var feuille = File.ReadAllText(Path.Combine(RepertoireClient, "wwwroot", "css", "app.css"));
        var serviceWorker = File.ReadAllText(Path.Combine(RepertoireClient, "wwwroot", "service-worker.published.js"));

        Assert.Contains("url(\"../polices/inter-latin.woff2\")", feuille, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepertoireClient, "wwwroot", "polices", "inter-latin.woff2")));
        Assert.True(File.Exists(Path.Combine(RepertoireClient, "wwwroot", "polices", "OFL-Inter.txt")));
        Assert.Contains(@"/\.woff2?$/", serviceWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis", feuille, StringComparison.Ordinal);
    }

    private static double Contraste(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
    }

    private static double Luminance(string hexa)
    {
        double Canal(int debut)
        {
            var c = Convert.ToInt32(hexa.Substring(debut, 2), 16) / 255.0;
            return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }

        return .2126 * Canal(1) + .7152 * Canal(3) + .0722 * Canal(5);
    }

    [Fact]
    public void La_page_s_adapte_a_la_largeur_de_l_ecran()
    {
        var page = File.ReadAllText(Path.Combine(RepertoireClient, "wwwroot", "index.html"));

        Assert.Contains("name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"", page, StringComparison.Ordinal);
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

    [GeneratedRegex(@"--([a-z-]+):\s*(#[0-9a-fA-F]{6});")]
    private static partial Regex JetonCouleur();

    [GeneratedRegex("public const string (\\w+) = \"([^\"]+)\";")]
    private static partial Regex ConstanteChaine();

    [GeneratedRegex("L\\[\"([^\"$]+)\"")]
    private static partial Regex CleStatique();

    [GeneratedRegex("#[0-9a-fA-F]{3,8}\\b|rgba?\\(|hsla?\\(")]
    private static partial Regex Couleur();

    [GeneratedRegex("style=\"|(?<![\\w&])#[0-9a-fA-F]{6}\\b")]
    private static partial Regex StyleOuCouleur();

    [GeneratedRegex("\\[\"([a-z]+)\"\\] = \\[")]
    private static partial Regex NomIcone();

    [GeneratedRegex("(?:<Icone Nom|NomIcone)=\"([a-z]+)\"")]
    private static partial Regex IconeUtilisee();
}
