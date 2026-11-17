using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Infrastructure;

/// <summary>
/// Base d'un parcours : un contexte de navigateur neuf (stockage vide, langue de l'interface), une page, et les gestes
/// courants exprimés avec les clés de texte du client. Sur échec, la trace Playwright (captures, réseau, DOM) est gardée
/// dans E2E_TRACES (artefact de la CI), lisible avec « playwright show-trace ».
/// </summary>
public abstract class ParcoursBase(Navigateur navigateur) : IAsyncLifetime
{
    /// <summary>Langue de l'interface (« fr » ou « en ») ; elle doit être celle des comptes utilisés.</summary>
    protected virtual string Langue => "fr";

    /// <summary>Taille de l'écran : poste de bureau par défaut.</summary>
    protected virtual ViewportSize Ecran => new() { Width = 1366, Height = 900 };

    protected Textes T => Textes.Pour(Langue);

    protected IBrowserContext Contexte { get; private set; } = null!;

    protected IPage Page { get; private set; } = null!;

    protected Donnees Donnees { get; } = new();

    /// <summary>Messages des confirmations acceptées pendant le parcours, dans l'ordre.</summary>
    protected List<string> Confirmations { get; } = [];

    /// <summary>Suffixe unique : les parcours peuvent être rejoués sur la même base sans collision de codes ou de noms.</summary>
    protected string Suffixe { get; } = DateTime.UtcNow.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);

    public async ValueTask InitializeAsync()
    {
        Contexte = await navigateur.Chromium.NewContextAsync(new()
        {
            BaseURL = Environnement.Url,
            Locale = Langue == "en" ? "en-US" : "fr-FR",
            TimezoneId = "Africa/Douala",
            ViewportSize = Ecran,
        });
        await Contexte.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        await Contexte.AddInitScriptAsync($"try {{ window.localStorage.setItem('gestionstock.langue', '{Langue}'); }} catch {{ }}");
        Page = await Contexte.NewPageAsync();
        Page.SetDefaultTimeout(15_000);
        // Les confirmations (fenêtre « confirm » du navigateur) sont acceptées et gardées : le parcours a décidé du geste,
        // et peut vérifier qu'une action irréversible a bien demandé confirmation.
        Page.Dialog += async (_, dialogue) =>
        {
            Confirmations.Add(dialogue.Message);
            await dialogue.AcceptAsync();
        };
    }

    public async ValueTask DisposeAsync()
    {
        var dossier = Environment.GetEnvironmentVariable("E2E_TRACES");
        var echec = TestContext.Current.TestState?.Result == TestResult.Failed;
        await Contexte.Tracing.StopAsync(dossier is { Length: > 0 } && echec
            ? new() { Path = Path.Combine(dossier, $"{GetType().Name}-{TestContext.Current.Test?.TestDisplayName.Split('.')[^1]}.zip") }
            : new());
        await Contexte.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // --- Gestes courants ----------------------------------------------------------------------------------------------

    protected async Task SeConnecterAsync(string identifiant, string motDePasse)
    {
        await Page.GotoAsync("connexion");
        await Champ("Matricule").FillAsync(identifiant);
        await Champ("MotDePasse").FillAsync(motDePasse);
        await Bouton("SeConnecter").ClickAsync();
        // Connecté : la page de connexion est quittée (le menu peut être replié sur un écran étroit).
        await Expect(Page).Not.ToHaveURLAsync(new Regex("connexion$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
    }

    protected Task SeConnecterAsync(Compte compte) => SeConnecterAsync(compte.Matricule, compte.MotDePasse);

    protected async Task SeDeconnecterAsync()
    {
        await Bouton("Deconnexion").ClickAsync();
        await Expect(Bouton("SeConnecter")).ToBeVisibleAsync();
    }

    /// <summary>Ouvre un écran par le menu (ouvre d'abord le tiroir sur un écran étroit).</summary>
    protected async Task AllerAsync(string cle)
    {
        var ouvrir = Page.Locator("button.ouvrir-menu");
        if (await ouvrir.IsVisibleAsync())
        {
            await ouvrir.ClickAsync();
        }

        await Menu().GetByRole(AriaRole.Link, new() { Name = T[cle], Exact = true }).ClickAsync();
        await Expect(Page).ToHaveTitleAsync(new Regex($"^{Regex.Escape(T[cle])}"));
    }

    protected ILocator Menu() => Page.GetByRole(AriaRole.Navigation, new() { Name = T["Menu"] });

    /// <summary>Bouton par son libellé, reconnu par son début : certains portent leur raccourci (« Compte juste F4 »).</summary>
    protected ILocator Bouton(string cle, params object[] arguments) =>
        Page.GetByRole(AriaRole.Button, new() { NameRegex = Debut(T[cle, arguments]) });

    /// <summary>Bouton dont le libellé a des paramètres (« Clôturer avec {0} ») : n'importe quelle valeur.</summary>
    protected ILocator BoutonModele(string cle) => Page.GetByRole(AriaRole.Button, new() { NameRegex = Motif(cle) });

    /// <summary>Le texte d'une clé dont les paramètres ({0}, {1}…) acceptent n'importe quelle valeur.</summary>
    protected Regex Motif(string cle) => new(string.Join(".+?", Regex.Split(T[cle], @"\{[0-9]+\}").Select(Regex.Escape)));

    private static Regex Debut(string texte) => new($@"^\s*{Regex.Escape(texte)}(\s|$)");

    protected ILocator Lien(string cle, params object[] arguments) =>
        Page.GetByRole(AriaRole.Link, new() { Name = T[cle, arguments], Exact = true });

    /// <summary>
    /// Champ par son libellé, reconnu par son début : le libellé d'un mot de passe contient aussi le bouton « Afficher ».
    /// </summary>
    protected ILocator Champ(string cle, params object[] arguments) =>
        Page.GetByLabel(Debut(T[cle, arguments])).First;

    protected ILocator Texte(string cle, params object[] arguments) => Page.GetByText(T[cle, arguments], new() { Exact = false });

    /// <summary>La ligne d'un tableau qui contient un texte (code, numéro, nom).</summary>
    protected ILocator Ligne(string contenu) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = contenu });

    protected Task VoirAsync(string cle, params object[] arguments) => Expect(Texte(cle, arguments).First).ToBeVisibleAsync();

    /// <summary>Un message dont le texte a des paramètres (« Session clôturée… {0} ») est affiché.</summary>
    protected Task VoirModeleAsync(string cle) => Expect(Page.GetByText(Motif(cle)).First).ToBeVisibleAsync();

    /// <summary>Champ de recherche par son nom accessible (libellé, ou à défaut son texte d'aide).</summary>
    protected ILocator Recherche(string cle) => Page.GetByRole(AriaRole.Searchbox, new() { Name = T[cle], Exact = true });

    protected Task VoirTexteAsync(string texte) => Expect(Page.GetByText(texte, new() { Exact = false }).First).ToBeVisibleAsync();
}
