using System.Diagnostics;
using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>
/// Connexion mobile lente (ENF10) : l'application déjà installée sur le poste, la caisse doit s'ouvrir en moins de 3 s
/// et un scan s'ajouter au panier en moins d'1 s (catalogue et prix lus sur le poste, sans aller-retour).
/// Connexion émulée par Chromium : 400 ms de latence, ≈ 400 kbit/s (« 3G lente »).
/// </summary>
public sealed class ParcoursConnexionLente(Navigateur navigateur) : ParcoursCaisseBase(navigateur)
{
    [Parcours]
    public async Task La_caisse_s_ouvre_et_scanne_vite_sur_une_connexion_lente()
    {
        var article = await Donnees.ArticleEnStockAsync($"LENT{Suffixe}", $"Lent {Suffixe}", prixTtc: 300, quantite: 10, coutUnitaire: 200);
        var caissier = await Donnees.CompteAsync("Caissier", $"Lent{Suffixe}", ["Caissier"]);

        // Réseau normal : connexion, session ouverte, données de la caisse gardées sur le poste.
        await SeConnecterAsync(caissier);
        await AllerAsync("Caisse");
        await OuvrirSessionAsync(0);
        await Page.WaitForTimeoutAsync(3_000);
        await AllerAsync("Accueil");

        await ConnexionLenteAsync();

        var chrono = Stopwatch.StartNew();
        await AllerAsync("Caisse");
        await Expect(Recherche("ScanOuRecherche")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var ouverture = chrono.Elapsed;

        chrono.Restart();
        await ScannerAsync(article.Code);
        await Expect(Panier()).ToContainTextAsync(article.Libelle);
        var scan = chrono.Elapsed;

        TestContext.Current.SendDiagnosticMessage($"Connexion lente : ouverture de la caisse {ouverture.TotalMilliseconds:0} ms, scan {scan.TotalMilliseconds:0} ms");
        Assert.True(ouverture < TimeSpan.FromSeconds(3), $"Ouverture de la caisse en {ouverture.TotalMilliseconds:0} ms (budget 3 s).");
        Assert.True(scan < TimeSpan.FromSeconds(1), $"Scan en {scan.TotalMilliseconds:0} ms (budget 1 s).");
    }

    private async Task ConnexionLenteAsync()
    {
        var cdp = await Contexte.NewCDPSessionAsync(Page);
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = 400,
            ["downloadThroughput"] = 50_000,
            ["uploadThroughput"] = 50_000,
        });
    }
}
