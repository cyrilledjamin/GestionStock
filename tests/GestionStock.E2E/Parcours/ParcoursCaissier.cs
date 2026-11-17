using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Caissier (UC17, UC18, UC24, UC27) : session, vente au comptoir, crédit, remise validée par un responsable, avoir,
/// remboursement de crédit, clôture ; et vente hors ligne (7.8, ENF08).
/// </summary>
public sealed class ParcoursCaissier(Navigateur navigateur) : ParcoursCaisseBase(navigateur)
{
    [Parcours]
    public async Task Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture()
    {
        var article = await Donnees.ArticleEnStockAsync($"SAVON{Suffixe}", $"Savon {Suffixe}", prixTtc: 500, quantite: 50, coutUnitaire: 300);
        var nomClient = $"Client {Suffixe}";
        var clientId = await Donnees.ClientAsync(nomClient, "+237 690 00 00 01", plafond: 20_000);
        var gerant = await Donnees.CompteAsync("Gerant", $"Remise{Suffixe}", ["Gerant"]);
        var caissier = await Donnees.CompteAsync("Caissier", $"Comptoir{Suffixe}", ["Caissier"]);

        await SeConnecterAsync(caissier);
        await AllerAsync("Caisse");
        await OuvrirSessionAsync(5_000);

        // Vente au comptoir : scan du code, quantité 2, compte juste, encaissement.
        await ScannerAsync(article.Code);
        await Expect(Panier()).ToContainTextAsync(article.Libelle);
        await Page.GetByRole(AriaRole.Button, new() { Name = T["Plus"], Exact = true }).ClickAsync();
        await Expect(Page.Locator(".total-grand")).ToContainTextAsync(new System.Text.RegularExpressions.Regex("1.000"));
        await Bouton("CompteJuste").ClickAsync();
        await EncaisserAsync();
        var premierTicket = await NumeroTicketAsync();
        await Bouton("NouvelleVente").ClickAsync();

        // Vente à crédit (RG38 à RG40) : client choisi, mode Crédit pour le reste.
        await ScannerAsync(article.Code);
        await Recherche("ClientCredit").FillAsync(nomClient);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new(nomClient) }).ClickAsync();
        await Bouton("Mode.Credit").ClickAsync();
        await EncaisserAsync();
        await NumeroTicketAsync();
        await Bouton("NouvelleVente").ClickAsync();

        // Remise : sans seuil fixé, elle exige la validation d'un responsable (décision 31).
        await ScannerAsync(article.Code);
        await Panier().GetByRole(AriaRole.Button, new() { Name = T["Remise"], Exact = true }).ClickAsync();
        await Panier().GetByLabel(T["Remise"]).FillAsync("50");
        await Panier().GetByLabel(T["Remise"]).PressAsync("Tab");
        await Page.GetByPlaceholder(T["Matricule"]).FillAsync(gerant.Matricule);
        await Page.GetByPlaceholder(T["MotDePasse"]).FillAsync(gerant.MotDePasse);
        await Bouton("CompteJuste").ClickAsync();
        await EncaisserAsync();
        await NumeroTicketAsync();
        await Bouton("NouvelleVente").ClickAsync();

        // Remboursement d'une partie du crédit, d'un double clic : il n'est enregistré qu'une fois.
        await Bouton("RemboursementCredit").ClickAsync();
        var formulaire = Page.Locator("form.fiche");
        await formulaire.GetByRole(AriaRole.Searchbox).FillAsync(nomClient);
        await formulaire.GetByRole(AriaRole.Button, new() { NameRegex = new(nomClient) }).ClickAsync();
        await formulaire.GetByLabel(T["Montant"]).FillAsync("200");
        await formulaire.GetByRole(AriaRole.Button, new() { Name = T["Encaisser"], Exact = true }).DblClickAsync();
        await VoirModeleAsync("RemboursementEnregistre");
        Assert.Equal(1, await Donnees.OperationsCreditAsync(clientId, "Remboursement"));

        // Avoir (UC18) sur une unité du premier ticket, remboursée en espèces.
        await Lien("TicketsDuJour").ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = premierTicket, Exact = true }).ClickAsync();
        await Ligne(article.Libelle).GetByRole(AriaRole.Spinbutton).FillAsync("1");
        await Bouton("FaireAvoir").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(T["Avoir"]);
        await Lien("RetourCaisse").ClickAsync();

        // Clôture : total saisi, écart calculé.
        await Bouton("Cloturer").ClickAsync();
        await Champ("SaisirTotal").CheckAsync();
        await Champ("EspecesComptees").FillAsync("6000");
        await Champ("EspecesComptees").PressAsync("Tab");
        await BoutonModele("CloturerAvec").ClickAsync();
        await VoirModeleAsync("SessionCloturee");
        await Expect(Bouton("OuvrirSession")).ToBeVisibleAsync();
    }

    [Parcours]
    public async Task Vend_hors_ligne_puis_synchronise_au_retour_du_reseau()
    {
        var article = await Donnees.ArticleEnStockAsync($"RIZ{Suffixe}", $"Riz {Suffixe}", prixTtc: 800, quantite: 20, coutUnitaire: 600);
        var caissier = await Donnees.CompteAsync("Caissier", $"HorsLigne{Suffixe}", ["Caissier"]);

        await SeConnecterAsync(caissier);
        await AllerAsync("Caisse");
        await OuvrirSessionAsync(0);

        // Coupure du réseau : le catalogue et les prix gardés sur le poste suffisent pour vendre.
        await Contexte.SetOfflineAsync(true);
        await ScannerAsync(article.Code);
        await Expect(Panier()).ToContainTextAsync(article.Libelle);
        await Bouton("CompteJuste").ClickAsync();
        await EncaisserAsync();
        var numero = await NumeroTicketAsync();
        await VoirAsync("HorsLigneBandeau");
        await VoirModeleAsync("OperationsEnAttente");
        await Bouton("NouvelleVente").ClickAsync();

        // Retour du réseau : la file part seule, le ticket est au serveur avec le numéro donné par le poste.
        await Contexte.SetOfflineAsync(false);
        await Expect(Page.GetByText(Motif("OperationsEnAttente"))).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await Lien("TicketsDuJour").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = numero, Exact = true })).ToBeVisibleAsync();

        await Lien("Caisse").ClickAsync();
        await Bouton("Cloturer").ClickAsync();
        await Champ("SaisirTotal").CheckAsync();
        await Champ("EspecesComptees").FillAsync("800");
        await Champ("EspecesComptees").PressAsync("Tab");
        await BoutonModele("CloturerAvec").ClickAsync();
        await VoirModeleAsync("SessionCloturee");
    }
}
