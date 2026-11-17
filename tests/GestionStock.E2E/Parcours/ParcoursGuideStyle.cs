using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Système de design (étape 60) : le guide de style affiche les composants, et le dialogue de confirmation de
/// l'application répond « non » à Échap, avec le focus d'abord sur Annuler. Une capture de la page est gardée avec les
/// traces (E2E_TRACES) pour la revue visuelle.
/// </summary>
public sealed class ParcoursGuideStyle(Navigateur navigateur) : ParcoursBase(navigateur)
{
    [Parcours]
    public async Task Le_guide_montre_les_composants_et_le_dialogue_se_ferme_par_echap()
    {
        await SeConnecterAsync(Environnement.Administrateur, Environnement.MotDePasseAdministrateur);
        await AllerAsync("GuideStyle");

        await Expect(Page.GetByRole(AriaRole.Img, new() { Name = T["GuideVentes30"], Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Img, new() { Name = T["GuideVentesMois"], Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("svg .barre-graphique")).ToHaveCountAsync(30);
        await Expect(Page.Locator("svg .graduation").First).ToBeVisibleAsync();

        // Ce parcours répond lui-même au dialogue.
        await Page.RemoveLocatorHandlerAsync(DialogueConfirmation);

        // Échap : « non », sans rien envoyer.
        await Bouton("GuideDialogueOuvrir").First.ClickAsync();
        await Expect(DialogueConfirmation).ToBeVisibleAsync();
        await Expect(DialogueConfirmation.GetByRole(AriaRole.Button, new() { Name = T["Annuler"], Exact = true })).ToBeFocusedAsync();
        await Expect(DialogueConfirmation).ToContainTextAsync(T["GuideDialogueExemple"]);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(DialogueConfirmation).ToHaveCountAsync(0);
        await VoirAsync("GuideAnnule");

        // Action qui retire : bouton rouge, libellé de l'action ; « oui ».
        await Bouton("GuideDialogueOuvrir").Last.ClickAsync();
        await DialogueConfirmation.GetByRole(AriaRole.Button, new() { Name = T["GuideDanger"], Exact = true }).ClickAsync();
        await Expect(DialogueConfirmation).ToHaveCountAsync(0);
        await VoirAsync("GuideConfirme");

        var dossier = Environment.GetEnvironmentVariable("E2E_TRACES");
        if (dossier is { Length: > 0 })
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dossier, $"guide-style-{Langue}.png"), FullPage = true });
        }
    }
}
