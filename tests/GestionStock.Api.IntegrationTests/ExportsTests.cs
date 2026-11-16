using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

/// <summary>
/// Exports comptables (UC16). La base est partagée entre les tests : chaque test cherche ses propres pièces
/// (numéros de ticket et de réception, client, article).
/// </summary>
public sealed class ExportsTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_journal_des_ventes_en_CSV_met_les_avoirs_en_negatif()
    {
        var b = await BoutiqueAsync();
        var vente = await VendreAsync(b, 3m, 1500m);
        var ligne = vente.GetProperty("lignes")[0].GetProperty("id").GetGuid();
        var avoir = await Lire<JsonElement>(await b.Caissier.PostAsJsonAsync(
            "/api/caisse/avoirs",
            new { idIdempotence = Guid.NewGuid(), ticketOrigineId = vente.GetProperty("id").GetGuid(), lignes = new[] { new { ligneOrigineId = ligne, quantite = 1m } } },
            Annulation));

        var (octets, lignes) = await CsvAsync(b.Comptable, "ventes");
        var numeroVente = vente.GetProperty("numero").GetString()!;
        var ligneAvoir = Cellules(lignes.Single(l => l.Contains(avoir.GetProperty("numero").GetString()!, StringComparison.Ordinal)));

        Assert.Equal(Encoding.UTF8.GetPreamble(), octets[..3]);
        Assert.Equal("Date;Pièce;Opération;Caissier;Client;Ticket d'origine;Remise;Total HT;TVA;Total TTC", lignes[0]);
        Assert.Equal("1500", lignes.Select(Cellules).Single(c => c[1] == numeroVente)[9]);
        Assert.Equal(("Avoir", numeroVente, "-500"), (ligneAvoir[2], ligneAvoir[5], ligneAvoir[9]));
    }

    [Fact]
    public async Task Le_journal_des_encaissements_retient_les_especes_moins_le_rendu()
    {
        var b = await BoutiqueAsync();
        var vente = await VendreAsync(b, 3m, 2000m);

        var (_, lignes) = await CsvAsync(b.Comptable, "encaissements");
        var cellules = Cellules(lignes.Single(l => l.Contains(vente.GetProperty("numero").GetString()!, StringComparison.Ordinal)));

        Assert.Equal(("Vente", "Espèces", "1500"), (cellules[2], cellules[5], cellules[7]));
    }

    [Fact]
    public async Task Le_journal_des_achats_en_Excel_reprend_receptions_frais_et_paiements()
    {
        var b = await BoutiqueAsync();
        var reception = await Lire<JsonElement>(await b.Gerant.PostAsJsonAsync(
            "/api/achats/receptions",
            new { fournisseurId = b.Fournisseur, lignes = new[] { new { articleId = b.Article, quantite = 24m, coutUnitaire = 300m } } },
            Annulation));
        var id = reception.GetProperty("id").GetGuid();
        var numero = reception.GetProperty("numero").GetString()!;
        (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{id}/frais", new { type = "Transport", montant = 200m }, Annulation)).EnsureSuccessStatusCode();
        (await b.Gerant.PostAsync($"/api/achats/receptions/{id}/validation", null, Annulation)).EnsureSuccessStatusCode();
        (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{id}/paiements", new { montant = 1000m, mode = "Virement", reference = "VIR-77" }, Annulation)).EnsureSuccessStatusCode();
        (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{id}/frais", new { type = "Douane", montant = 50m, libelle = "Dédouanement" }, Annulation)).EnsureSuccessStatusCode();

        var reponse = await b.Comptable.GetAsync("/api/exports/achats?format=xlsx", Annulation);
        using var classeur = new XLWorkbook(new MemoryStream(await reponse.Content.ReadAsByteArrayAsync(Annulation)));
        var feuille = classeur.Worksheet(1);
        var lignes = feuille.RowsUsed().Where(r => r.Cell(2).GetString() == numero).ToList();

        Assert.Equal("Journal des achats", feuille.Name);
        Assert.Equal("Montant dû", feuille.Cell(1, 8).GetString());
        Assert.Equal(
            [("Réception", 7400m, 0m), ("Paiement", 0m, 1000m), ("Frais d'approche tardif", 50m, 0m)],
            lignes.Select(r => (r.Cell(3).GetString(), r.Cell(8).GetValue<decimal>(), r.Cell(9).GetValue<decimal>())));
        Assert.Equal("VIR-77", lignes[1].Cell(7).GetString());
        Assert.Equal("Douane - Dédouanement", lignes[2].Cell(5).GetString());
    }

    [Fact]
    public async Task Le_journal_des_creances_reprend_les_ventes_a_credit()
    {
        var b = await BoutiqueAsync();
        var nom = $"Client {Guid.NewGuid():N}"[..16];
        var client = await Lire<JsonElement>(await b.Gerant.PostAsJsonAsync("/api/credit/clients", new { nom, telephone = "699112233" }, Annulation));
        var clientId = client.GetProperty("id").GetGuid();
        (await b.Comptable.PutAsJsonAsync($"/api/credit/clients/{clientId}/plafond", new { plafondCredit = 20000m }, Annulation)).EnsureSuccessStatusCode();
        var vente = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", new
        {
            idIdempotence = Guid.NewGuid(),
            clientId,
            lignes = new[] { new { conditionnementId = b.Unite, quantite = 2m, remise = 0m } },
            paiements = new[] { new { mode = "Credit", montant = 1000m } },
        }, Annulation);
        Assert.Equal(HttpStatusCode.Created, vente.StatusCode);

        var (_, lignes) = await CsvAsync(b.Comptable, "creances");
        var cellules = Cellules(lignes.Single(l => l.Contains(nom, StringComparison.Ordinal)));

        Assert.Equal(("699112233", "Vente à crédit", "1000"), (cellules[2], cellules[3], cellules[7]));
        Assert.NotEqual("", cellules[5]);
    }

    [Fact]
    public async Task La_valeur_du_stock_est_exportee_par_article()
    {
        var b = await BoutiqueAsync();
        var reception = await Lire<JsonElement>(await b.Gerant.PostAsJsonAsync(
            "/api/achats/receptions",
            new { fournisseurId = b.Fournisseur, lignes = new[] { new { articleId = b.Article, quantite = 10m, coutUnitaire = 333m } } },
            Annulation));
        (await b.Gerant.PostAsync($"/api/achats/receptions/{reception.GetProperty("id").GetGuid()}/validation", null, Annulation)).EnsureSuccessStatusCode();

        var reponse = await b.Gerant.GetAsync("/api/exports/valeur-stock", Annulation);
        using var classeur = new XLWorkbook(new MemoryStream(await reponse.Content.ReadAsByteArrayAsync(Annulation)));
        var ligne = classeur.Worksheet(1).RowsUsed().Single(r => r.Cell(1).GetString() == b.CodeArticle);

        Assert.StartsWith("valeur-stock-", reponse.Content.Headers.ContentDisposition!.FileNameStar ?? reponse.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);
        Assert.Equal((10m, 333m, 3330m), (ligne.Cell(4).GetValue<decimal>(), ligne.Cell(5).GetValue<decimal>(), ligne.Cell(6).GetValue<decimal>()));
    }

    [Fact]
    public async Task Les_en_tetes_suivent_la_langue_de_la_requete()
    {
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        comptable.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var (_, lignes) = await CsvAsync(comptable, "encaissements");

        Assert.Equal("Date;Document;Transaction;Cashier;Customer;Payment method;Reference;Amount", lignes[0]);
    }

    [Fact]
    public async Task Un_journal_ou_un_format_inconnu_et_les_droits_sont_controles()
    {
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);

        var inconnu = await comptable.GetAsync("/api/exports/grand-livre", Annulation);
        var format = await comptable.GetAsync("/api/exports/ventes?format=pdf", Annulation);

        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
        Assert.Equal("Rapports.JournalInconnu", (await IdentiteTests.LireProblemeAsync(inconnu)).Code);
        Assert.Equal("Rapports.FormatInvalide", (await IdentiteTests.LireProblemeAsync(format)).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await caissier.GetAsync("/api/exports/ventes", Annulation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await magasinier.GetAsync("/api/exports/valeur-stock", Annulation)).StatusCode);
    }

    // --- Outils --------------------------------------------------------------------------------------------------

    private async Task<Boutique> BoutiqueAsync()
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var (article, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 12);
        var unite = (await ConditionnementsTests.ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);
        await PrixTests.FixerAsync(gerant, unite.Id, 500m);
        await PrixTests.FixerAsync(gerant, carton.Id, 5500m);
        (await caissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = ApiDeTest.ProchainPoste(), fondCaisse = 0m }, Annulation)).EnsureSuccessStatusCode();
        var fournisseur = await Lire<JsonElement>(await gerant.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20] }, Annulation));
        return new Boutique(gerant, caissier, comptable, article.Id, article.Code, unite.Id, fournisseur.GetProperty("id").GetGuid());
    }

    /// <summary>Vente d'unités à 500 XAF, payée en espèces avec la somme donnée (le surplus est rendu).</summary>
    private static async Task<JsonElement> VendreAsync(Boutique b, decimal quantite, decimal especesDonnees)
    {
        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", new
        {
            idIdempotence = Guid.NewGuid(),
            lignes = new[] { new { conditionnementId = b.Unite, quantite, remise = 0m } },
            paiements = new[] { new { mode = "Especes", montant = especesDonnees } },
        }, Annulation);
        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<JsonElement>(reponse);
    }

    private static async Task<(byte[] Octets, string[] Lignes)> CsvAsync(HttpClient client, string journal)
    {
        var reponse = await client.GetAsync($"/api/exports/{journal}?format=csv", Annulation);
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        var octets = await reponse.Content.ReadAsByteArrayAsync(Annulation);
        var texte = Encoding.UTF8.GetString(octets).TrimStart('﻿');
        return (octets, texte.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] Cellules(string ligne) => ligne.Split(';');

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Caissier, HttpClient Comptable, Guid Article, string CodeArticle, Guid Unite, Guid Fournisseur);
}
