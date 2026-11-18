using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Vérification de la refonte (étape 64) : chaque écran du menu, ouvert par le menu lui-même (tiroir sur écran étroit),
/// sur un PC, une tablette et un téléphone. Aucun écran ne déborde en largeur ; une capture de chacun est gardée avec
/// les traces (E2E_TRACES) pour la revue visuelle. Sur téléphone, Échap referme le tiroir et rend le focus à son bouton.
/// </summary>
public abstract class ParcoursCapturesBase(Navigateur navigateur) : ParcoursBase(navigateur)
{
    /// <summary>Écrans du menu, par la clé de leur titre.</summary>
    private static readonly string[] Ecrans =
    [
        "Accueil", "Caisse", "SessionsCaisse", "Anomalies", "Clients", "Stock", "Receptions", "Fournisseurs", "DettesFournisseurs",
        "Articles", "Inventaires", "Demarque", "Rapports", "Exports", "Reprise", "Controles", "Sauvegardes", "Utilisateurs",
        "MonCompte", "GuideStyle",
    ];

    private static readonly (string Nom, int Largeur, int Hauteur)[] Largeurs = [("pc", 1366, 900), ("tablette", 768, 1024), ("telephone", 390, 844)];

    // 20 écrans × 3 largeurs, une capture chacun : plus long qu'un parcours ordinaire.
    [Parcours(Timeout = 600_000)]
    public async Task Chaque_ecran_tient_sur_un_pc_une_tablette_et_un_telephone()
    {
        var compte = await Donnees.CompteAsync("Revue", $"Ecrans{Suffixe}", ["Gerant", "Comptable", "Magasinier", "Caissier", "Administrateur"], Langue);
        await SeConnecterAsync(compte);

        var debordements = new List<string>();
        foreach (var (nom, largeur, hauteur) in Largeurs)
        {
            await Page.SetViewportSizeAsync(largeur, hauteur);
            foreach (var ecran in Ecrans)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                await AllerAsync(ecran);
                // Plus rien en attente : listes, indicateurs et graphiques sont arrivés (le guide de style montre l'attente en
                // exemple, à dessein).
                if (ecran != "GuideStyle")
                {
                    await Expect(Page.Locator(".attente")).ToHaveCountAsync(0);
                }
                if (!await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"))
                {
                    debordements.Add($"{ecran} ({nom})");
                }

                await CapturerAsync($"ecran-{ecran}-{nom}");
            }
        }

        Assert.Empty(debordements);

        // Tiroir du téléphone : Échap le referme et rend le focus au bouton « Menu ».
        var bouton = Page.Locator("button.ouvrir-menu:visible");
        await bouton.ClickAsync();
        await Expect(Menu()).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Menu()).Not.ToBeVisibleAsync();
        await Expect(bouton).ToBeFocusedAsync();
        await Expect(bouton).ToHaveAttributeAsync("aria-expanded", "false");
    }
}

public sealed class ParcoursCaptures(Navigateur navigateur) : ParcoursCapturesBase(navigateur);

public sealed class ParcoursCapturesAnglais(Navigateur navigateur) : ParcoursCapturesBase(navigateur)
{
    protected override string Langue => "en";
}
