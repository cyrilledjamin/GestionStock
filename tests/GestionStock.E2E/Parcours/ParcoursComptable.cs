using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Comptable (UC16, RG30, décisions 18 et 27) : frais d'approche, paiement fournisseur et dette, plafond de crédit,
/// validation d'un inventaire et d'une démarque, export comptable.
/// </summary>
public sealed class ParcoursComptable(Navigateur navigateur) : ParcoursBase(navigateur)
{
    [Parcours]
    public async Task Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte()
    {
        var article = await Donnees.ArticleEnStockAsync($"HUILE{Suffixe}", $"Huile {Suffixe}", prixTtc: 1500, quantite: 20, coutUnitaire: 1000);
        var reception = await Donnees.ReceptionValideeAsync(article.ArticleId, quantite: 10, coutUnitaire: 1100);
        var inventaire = await Donnees.InventaireCompteAsync(article.ArticleId, quantite: 29);
        var demarque = await Donnees.DemarqueDeclareeAsync(article.ArticleId, quantite: 1);
        var client = $"Client Plafond {Suffixe}";
        await Donnees.ClientAsync(client, "+237 655 00 00 00", plafond: 5_000);
        var comptable = await Donnees.CompteAsync("Comptable", $"Chiffres{Suffixe}", ["Comptable"]);

        await SeConnecterAsync(comptable);

        // Frais d'approche (RG30) et paiement partiel de la dette fournisseur (décision 27).
        await AllerAsync("Receptions");
        await Page.GetByRole(AriaRole.Link, new() { Name = reception, Exact = true }).ClickAsync();
        var frais = Page.Locator("form").Filter(new() { Has = Bouton("AjouterFrais") });
        await frais.GetByLabel(T["Montant"]).FillAsync("1000");
        await Bouton("AjouterFrais").ClickAsync();
        await VoirAsync("FraisAjoute");
        var paiement = Page.Locator("form").Filter(new() { Has = Bouton("EnregistrerPaiement") });
        await paiement.GetByLabel(T["Montant"]).FillAsync("5000");
        await Bouton("EnregistrerPaiement").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 2 }).Filter(new() { HasText = T["DetteFournisseur"] }))
            .ToContainTextAsync(T["StatutDette.PartiellementPayee"]);
        await CapturerAsync("reception");

        await AllerAsync("DettesFournisseurs");
        await Expect(Ligne(reception)).ToBeVisibleAsync();
        await CapturerAsync("dettes");

        // Plafond de crédit (RG38).
        await AllerAsync("Clients");
        await Page.GetByRole(AriaRole.Link, new() { Name = client, Exact = true }).ClickAsync();
        await Champ("Plafond").FillAsync("15000");
        await Bouton("Fixer").ClickAsync();
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("15.000")).First).ToBeVisibleAsync();

        // Validation de l'inventaire (écart d'une unité) et de la démarque.
        await AllerAsync("Inventaires");
        await Page.GetByRole(AriaRole.Link, new() { Name = inventaire, Exact = true }).ClickAsync();
        await Bouton("ValiderInventaire").ClickAsync();
        await VoirAsync("InventaireValide");
        Assert.Contains(Confirmations, message => Motif("ConfirmerValidationInventaire").IsMatch(message));

        await AllerAsync("Demarque");
        var ligneDemarque = Ligne(demarque);
        await ligneDemarque.GetByRole(AriaRole.Button, new() { Name = T["Valider"], Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Combobox).First).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Combobox).First.SelectOptionAsync(new SelectOptionValue { Label = T["TousStatuts"] });
        await Expect(Ligne(demarque)).ToContainTextAsync(T["StatutDemarque.Validee"]);
        Assert.Contains(T["ConfirmerValidationDemarque"], Confirmations);

        // Export comptable (UC16) : un fichier est téléchargé.
        await AllerAsync("Exports");
        var telechargement = await Page.RunAndWaitForDownloadAsync(() => Bouton("Telecharger").ClickAsync());
        Assert.False(string.IsNullOrEmpty(telechargement.SuggestedFilename));

        // Exemples gardés avec les traces (E2E_TRACES) : journal des ventes en Excel et en CSV, pour la documentation.
        if (Environment.GetEnvironmentVariable("E2E_TRACES") is { Length: > 0 } dossier)
        {
            await telechargement.SaveAsAsync(Path.Combine(dossier, telechargement.SuggestedFilename));
            await Champ("Format").SelectOptionAsync("csv");
            var csv = await Page.RunAndWaitForDownloadAsync(() => Bouton("Telecharger").ClickAsync());
            await csv.SaveAsAsync(Path.Combine(dossier, csv.SuggestedFilename));
        }
    }
}
