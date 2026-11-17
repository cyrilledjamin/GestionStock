using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Infrastructure;

/// <summary>Gestes de la caisse partagés par les parcours du caissier (bureau, téléphone, anglais).</summary>
public abstract class ParcoursCaisseBase(Navigateur navigateur) : ParcoursBase(navigateur)
{
    protected ILocator Panier() => Page.Locator("ul.panier");

    protected async Task OuvrirSessionAsync(decimal fond)
    {
        await Donnees.LibererPostesAsync();
        await Page.ReloadAsync();
        await Champ("FondCaisse").FillAsync(fond.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Bouton("OuvrirSession").ClickAsync();
        await Expect(Recherche("ScanOuRecherche")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Scanne un code et attend que l'article arrive au panier : la caisse remet alors le curseur dans le champ de scan
    /// (douchette), un geste suivant ne doit pas commencer avant.
    /// </summary>
    protected async Task ScannerAsync(string code)
    {
        var lignes = await Panier().Locator("li.ligne-panier").CountAsync();
        var scan = Recherche("ScanOuRecherche");
        await scan.FillAsync(code);
        await scan.PressAsync("Enter");
        await Expect(Panier().Locator("li.ligne-panier")).ToHaveCountAsync(lignes + 1);
        await Expect(scan).ToBeFocusedAsync();
    }

    protected async Task EncaisserAsync() =>
        await Page.Locator("aside.encaissement").GetByRole(AriaRole.Button, new() { NameRegex = new($"^{T["Encaisser"]}") }).ClickAsync();

    /// <summary>Le reçu est affiché : renvoie le numéro du ticket (titre « Ticket CAISSE-1-000012 »).</summary>
    protected async Task<string> NumeroTicketAsync()
    {
        var titre = Page.GetByRole(AriaRole.Heading, new() { Level = 1 });
        await Expect(titre).ToContainTextAsync(T["Ticket"]);
        return (await titre.InnerTextAsync()).Replace(T["Ticket"], "", StringComparison.Ordinal).Trim();
    }

    /// <summary>Clôture la session : total des espèces saisi, écart calculé.</summary>
    protected async Task CloturerAsync(decimal especes)
    {
        await Bouton("Cloturer").ClickAsync();
        await Champ("SaisirTotal").CheckAsync();
        await Champ("EspecesComptees").FillAsync(especes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Champ("EspecesComptees").PressAsync("Tab");
        await BoutonModele("CloturerAvec").ClickAsync();
        await VoirModeleAsync("SessionCloturee");
    }
}
