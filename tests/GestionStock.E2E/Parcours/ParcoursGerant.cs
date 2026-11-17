using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Gérant (UC01, UC24, UC25, UC26, décisions 36 et 37) : article, conditionnement, prix, article rapide, client à crédit,
/// clôture d'une session restée ouverte et validation de son écart, rapports.
/// </summary>
public sealed class ParcoursGerant(Navigateur navigateur) : ParcoursBase(navigateur)
{
    [Parcours]
    public async Task Cree_un_article_avec_son_carton_et_ses_prix_et_un_client()
    {
        var gerant = await Donnees.CompteAsync("Gerant", $"Boutique{Suffixe}", ["Gerant"]);
        var code = $"LAIT{Suffixe}";
        var client = $"Mme Parcours {Suffixe}";

        await SeConnecterAsync(gerant);

        // Article (UC01) : code, libellé, unité de stock.
        await AllerAsync("Articles");
        await Bouton("NouvelArticle").ClickAsync();
        await Champ("Code").FillAsync(code);
        await Champ("Libelle").FillAsync($"Lait {Suffixe}");
        await Champ("UniteStock").FillAsync("boîte");
        await Bouton("Enregistrer").ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = code, Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(code);

        // Conditionnement « carton de 12 » (RG18).
        await Champ("Libelle").FillAsync("Carton");
        await Champ("Niveau").SelectOptionAsync(new SelectOptionValue { Label = T["Niveau.Carton"] });
        await Champ("Contenu").FillAsync("12");
        await Bouton("Ajouter").Last.ClickAsync();
        await Expect(Ligne("Carton")).ToBeVisibleAsync();

        // Prix de l'unité et du carton (UC25), unité en article rapide de la caisse (décision 37).
        foreach (var (conditionnement, prix) in new[] { (T["Niveau.Unite"], "500"), ("Carton", "5500") })
        {
            var ligne = Ligne(conditionnement).First;
            await ligne.GetByPlaceholder(T["NouveauPrix"]).FillAsync(prix);
            await ligne.GetByRole(AriaRole.Button, new() { Name = T["Fixer"], Exact = true }).ClickAsync();
            await Expect(ligne).ToContainTextAsync(new System.Text.RegularExpressions.Regex(prix[..^3] + ".?" + prix[^3..] + ".XAF"));
        }

        await Ligne(T["Niveau.Unite"]).First.GetByLabel(T["VenteRapide"]).CheckAsync();
        await Expect(Ligne(T["Niveau.Unite"]).First.GetByLabel(T["VenteRapide"])).ToBeCheckedAsync();

        // Client à crédit (UC26).
        await AllerAsync("Clients");
        await Bouton("NouveauClient").ClickAsync();
        await Champ("Nom").FillAsync(client);
        await Champ("Telephone").FillAsync("+237 677 00 00 00");
        await Bouton("Enregistrer").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = client, Exact = true })).ToBeVisibleAsync();
    }

    [Parcours]
    public async Task Cloture_une_session_restee_ouverte_valide_l_ecart_et_consulte_les_rapports()
    {
        var gerant = await Donnees.CompteAsync("Gerant", $"Superviseur{Suffixe}", ["Gerant"]);
        var caissier = await Donnees.CompteAsync("Caissier", $"Absent{Suffixe}", ["Caissier"]);
        await Donnees.SessionOuverteAsync(caissier, fond: 2_000);

        await SeConnecterAsync(gerant);

        // Session restée ouverte (décision 36) : le Gérant compte et clôture à la place du titulaire.
        await AllerAsync("SessionsCaisse");
        // À corriger à l'étape 55 : l'écran montre le matricule du caissier, pas son nom.
        var session = Ligne(caissier.Matricule);
        await session.GetByRole(AriaRole.Button, new() { Name = T["CloturerAPlace"], Exact = true }).ClickAsync();
        await Champ("SaisirTotal").CheckAsync();
        await Champ("EspecesComptees").FillAsync("1500");
        await Champ("EspecesComptees").PressAsync("Tab");
        await BoutonModele("CloturerAvec").ClickAsync();
        await Expect(session).ToContainTextAsync(T["StatutSession.Comptee"]);
        await Expect(session).ToContainTextAsync(T["CompteePar", gerant.Matricule]);

        // Validation de l'écart (RG36).
        await session.GetByRole(AriaRole.Button, new() { Name = T["ValiderEcart"], Exact = true }).ClickAsync();
        await Expect(session).ToContainTextAsync(T["StatutSession.Validee"]);

        // Rapports (décision 33) : les quatre onglets s'affichent.
        await AllerAsync("Rapports");
        foreach (var onglet in new[] { "Ventes", "Stock", "CreancesDettes", "Caisse" })
        {
            await Bouton($"Rapport.{onglet}").ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Table).First).ToBeVisibleAsync();
        }

        await AllerAsync("Anomalies");
        await Expect(Page.GetByRole(AriaRole.Table)).ToBeVisibleAsync();
    }
}
