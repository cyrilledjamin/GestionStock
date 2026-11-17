using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Interface en anglais (ENF09) : un caissier dont le compte est en anglais vend et clôture ; les mêmes clés de texte
/// sont cherchées dans Textes.en.resx.
/// </summary>
public sealed class ParcoursAnglais(Navigateur navigateur) : ParcoursCaisseBase(navigateur)
{
    protected override string Langue => "en";

    [Parcours]
    public async Task Un_caissier_anglophone_vend_et_cloture()
    {
        var article = await Donnees.ArticleEnStockAsync($"SOAP{Suffixe}", $"Soap {Suffixe}", prixTtc: 400, quantite: 10, coutUnitaire: 250);
        var caissier = await Donnees.CompteAsync("Cashier", $"English{Suffixe}", ["Caissier"], langue: "en");

        await SeConnecterAsync(caissier);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(Motif("Bonjour"));
        await AllerAsync("Caisse");
        await OuvrirSessionAsync(1_000);

        await ScannerAsync(article.Code);
        await Bouton("CompteJuste").ClickAsync();
        await EncaisserAsync();
        await NumeroTicketAsync();
        await Bouton("NouvelleVente").ClickAsync();

        await CloturerAsync(1_400);
    }
}
