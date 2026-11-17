using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>Magasinier (UC04, UC11, UC14, UC20) : fournisseur, réception, stock, comptage d'inventaire, démarque.</summary>
public sealed class ParcoursMagasinier(Navigateur navigateur) : ParcoursBase(navigateur)
{
    [Parcours]
    public async Task Receptionne_une_livraison_compte_le_stock_et_declare_une_casse()
    {
        var article = await Donnees.ArticleEnStockAsync($"SUCRE{Suffixe}", $"Sucre {Suffixe}", prixTtc: 900, quantite: 30, coutUnitaire: 700);
        var magasinier = await Donnees.CompteAsync("Magasinier", $"Depot{Suffixe}", ["Magasinier"]);
        var fournisseur = $"Grossiste {Suffixe}";

        await SeConnecterAsync(magasinier);

        // Fournisseur.
        await AllerAsync("Fournisseurs");
        await Bouton("NouveauFournisseur").ClickAsync();
        await Champ("Nom").FillAsync(fournisseur);
        await Bouton("Enregistrer").ClickAsync();
        await Expect(Ligne(fournisseur)).ToBeVisibleAsync();

        // Réception (UC04) : brouillon, puis validation qui fait entrer 10 unités au CUMP.
        await AllerAsync("Receptions");
        await Lien("NouvelleReception").ClickAsync();
        await Champ("Fournisseur").SelectOptionAsync(new SelectOptionValue { Label = fournisseur });
        var ligne = Page.Locator("table tbody tr").First;
        // Article choisi par recherche (pas de liste de tout le catalogue).
        await ligne.GetByRole(AriaRole.Searchbox, new() { Name = T["Article"] }).FillAsync(article.Code);
        await ligne.GetByRole(AriaRole.Button, new() { NameRegex = new($"^{article.Code} — ") }).ClickAsync();
        await ligne.GetByRole(AriaRole.Spinbutton).Nth(0).FillAsync("10");
        await ligne.GetByRole(AriaRole.Spinbutton).Nth(1).FillAsync("750");
        await Bouton("EnregistrerBrouillon").ClickAsync();
        await VoirAsync("BrouillonEnregistre");
        await Bouton("ValiderReception").ClickAsync();
        await VoirAsync("ReceptionValidee");

        // Stock (UC14) : 30 + 10 unités.
        await AllerAsync("Stock");
        await Page.GetByRole(AriaRole.Searchbox).FillAsync(article.Code);
        await Expect(Ligne(article.Code)).ToContainTextAsync("40");
        await CapturerAsync("stock");

        // Inventaire partiel (UC11) : l'article est ajouté et compté au scan, l'inventaire reste à valider.
        await AllerAsync("Inventaires");
        await Bouton("NouvelInventairePartiel").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(T["StatutInventaire.EnCours"]);
        var scan = Page.GetByPlaceholder(T["ScanInventaireAide"]);
        for (var i = 0; i < 2; i++)
        {
            await scan.FillAsync(article.Code);
            await scan.PressAsync("Enter");
            await VoirTexteAsync($"{article.Code} : {i + 1}");
        }

        await Expect(Ligne(article.Code)).ToBeVisibleAsync();

        // Démarque (UC20) : casse déclarée, en attente de validation.
        await AllerAsync("Demarque");
        await Page.GetByRole(AriaRole.Searchbox, new() { Name = T["RechercherArticle"] }).FillAsync(article.Code);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new($"^{article.Code} — ") }).ClickAsync();
        await Champ("Quantite").FillAsync("1");
        await Champ("Motif").SelectOptionAsync(new SelectOptionValue { Label = T["MotifDemarque.Casse"] });
        await Bouton("Declarer").ClickAsync();
        await VoirModeleAsync("DemarqueDeclaree");
        await Expect(Ligne(article.Code)).ToContainTextAsync(T["StatutDemarque.Declaree"]);
        await CapturerAsync("demarques");
    }
}
