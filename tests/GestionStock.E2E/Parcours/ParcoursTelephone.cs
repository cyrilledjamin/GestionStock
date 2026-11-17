using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Téléphone (390 × 844, tactile) : menu en tiroir, vente avec la barre du total en bas de l'écran, stock en cartes.
/// </summary>
public sealed class ParcoursTelephone(Navigateur navigateur) : ParcoursCaisseBase(navigateur)
{
    protected override ViewportSize Ecran => new() { Width = 390, Height = 844 };

    [Parcours]
    public async Task Vend_depuis_un_telephone_avec_la_barre_du_total()
    {
        var article = await Donnees.ArticleEnStockAsync($"PAIN{Suffixe}", $"Pain {Suffixe}", prixTtc: 250, quantite: 10, coutUnitaire: 150);
        var caissier = await Donnees.CompteAsync("Caissier", $"Mobile{Suffixe}", ["Caissier"]);

        await SeConnecterAsync(caissier);
        // Écran étroit : le menu est replié derrière le bouton « Menu ».
        await Expect(Menu()).Not.ToBeInViewportAsync();
        await AllerAsync("Caisse");
        await OuvrirSessionAsync(0);

        await ScannerAsync(article.Code);
        var barre = Page.Locator(".barre-total");
        await Expect(barre).ToBeVisibleAsync();
        await barre.GetByRole(AriaRole.Button, new() { NameRegex = new($"^{T["Payer"]}") }).ClickAsync();
        await Bouton("CompteJuste").ClickAsync();
        await barre.GetByRole(AriaRole.Button, new() { Name = T["Encaisser"], Exact = true }).ClickAsync();
        await NumeroTicketAsync();
        await Bouton("NouvelleVente").ClickAsync();
        await CloturerAsync(250);

        await AllerAsync("Accueil");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
    }
}
