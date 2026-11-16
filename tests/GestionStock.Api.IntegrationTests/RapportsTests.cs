using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

/// <summary>
/// Rapports V1 (décision 33). La base est partagée entre les tests : chaque test ne regarde que ses propres articles,
/// caissiers, clients et fournisseurs.
/// </summary>
public sealed class RapportsTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_rapport_des_ventes_deduit_les_avoirs_et_calcule_la_marge_au_CUMP()
    {
        var b = await BoutiqueAsync();
        await ReceptionnerAsync(b, 24m, 300m);

        // Un carton de 12 (5 500) et 3 unités (1 500), puis l'avoir d'une unité (500) : 14 unités, 6 500 TTC, coût 4 200.
        var vente = await VendreAsync(b, (b.Carton, 1m), (b.Unite, 3m));
        var ligneUnite = vente.GetProperty("lignes").EnumerateArray().Single(l => l.GetProperty("quantite").GetDecimal() == 3m).GetProperty("id").GetGuid();
        var avoir = await b.Caissier.PostAsJsonAsync(
            "/api/caisse/avoirs",
            new { idIdempotence = Guid.NewGuid(), ticketOrigineId = vente.GetProperty("id").GetGuid(), lignes = new[] { new { ligneOrigineId = ligneUnite, quantite = 1m } } },
            Annulation);
        Assert.Equal(HttpStatusCode.Created, avoir.StatusCode);

        var rapport = await b.Comptable.GetFromJsonAsync<RapportVentes>("/api/rapports/ventes", Annulation);
        var article = rapport!.ParArticle.Single(a => a.ArticleId == b.Article);
        var caissier = rapport.ParCaissier.Single(c => c.Caissier == $"Test Caissier ({b.IdentifiantCaissier})");

        Assert.Equal((14m, 4200m), (article.Quantite, article.CoutDesVentes));
        Assert.Equal(article.ChiffreAffairesHt - 4200m, article.Marge);
        Assert.True(article.ChiffreAffairesHt is > 0m and <= 6500m);
        Assert.Equal((2, 6500m), (caissier.NombreTickets, caissier.ChiffreAffairesTtc));
        Assert.Contains(rapport.ParJour, j => j.Jour == rapport.Au);
        Assert.True(rapport.Totaux.NombreAvoirs >= 1);
    }

    [Fact]
    public async Task Le_tableau_de_bord_reprend_les_ventes_du_jour_la_caisse_et_les_points_a_surveiller()
    {
        var b = await BoutiqueAsync();
        await ReceptionnerAsync(b, 2m, 300m);
        var avant = await b.Gerant.GetFromJsonAsync<TableauDeBord>("/api/rapports/tableau-de-bord", Annulation);

        await VendreAsync(b, (b.Unite, 5m)); // 2 en stock : l'article passe en négatif
        (await b.Caissier.PostAsJsonAsync("/api/caisse/sessions/courante/cloture", new { especesComptees = 2400 }, Annulation)).EnsureSuccessStatusCode();
        var apres = await b.Comptable.GetFromJsonAsync<TableauDeBord>("/api/rapports/tableau-de-bord", Annulation);
        var caissier = await b.Caissier.GetAsync("/api/rapports/tableau-de-bord", Annulation);

        // Base partagée avec les autres tests, qui vendent et font des avoirs en parallèle : le nombre de ventes ne peut
        // qu'augmenter, mais le chiffre d'affaires du jour peut baisser entre deux lectures (avoirs).
        Assert.True(apres!.VentesDuJour.NombreVentes >= avant!.VentesDuJour.NombreVentes + 1);
        Assert.True(apres.VentesDuJour.ChiffreAffairesTtc > 0m);
        Assert.True(apres.SessionsAValider >= 1);
        Assert.True(apres.StocksNegatifs >= 1);
        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Africa/Douala")), apres.Jour);
        Assert.Equal(HttpStatusCode.Forbidden, caissier.StatusCode);
    }

    [Fact]
    public async Task Le_rapport_de_stock_valorise_chaque_article_au_CUMP()
    {
        var b = await BoutiqueAsync();
        await ReceptionnerAsync(b, 10m, 250m);
        await VendreAsync(b, (b.Unite, 4m));

        var rapport = await b.Gerant.GetFromJsonAsync<RapportStock>("/api/rapports/stock", Annulation);
        var ligne = rapport!.Articles.Single(a => a.ArticleId == b.Article);

        Assert.Equal((6m, 250m, 1500m), (ligne.Quantite, ligne.Cump, ligne.Valeur));
        Assert.DoesNotContain(rapport.StocksNegatifs, a => a.ArticleId == b.Article);
        Assert.True(rapport.ValeurTotale >= 1500m);
    }

    [Fact]
    public async Task Une_vente_sans_stock_apparait_dans_les_anomalies_de_caisse()
    {
        var b = await BoutiqueAsync();
        await VendreAsync(b, (b.Unite, 2m));

        var caisse = await b.Gerant.GetFromJsonAsync<RapportCaisse>("/api/rapports/caisse", Annulation);
        var stock = await b.Gerant.GetFromJsonAsync<RapportStock>("/api/rapports/stock", Annulation);

        Assert.Equal(-2m, caisse!.StocksNegatifs.Single(a => a.ArticleId == b.Article).Quantite);
        Assert.Contains(stock!.StocksNegatifs, a => a.ArticleId == b.Article);
        Assert.Contains(caisse.Sessions, s => s.NumeroCaisse == b.Poste && s.NombreVentes == 1);
    }

    [Fact]
    public async Task Le_rapport_de_caisse_liste_les_avoirs_et_les_frais_non_incorpores()
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionnerAsync(b, 2m, 300m);
        var vente = await VendreAsync(b, (b.Unite, 2m));
        var ligne = vente.GetProperty("lignes")[0].GetProperty("id").GetGuid();
        (await b.Caissier.PostAsJsonAsync(
            "/api/caisse/avoirs",
            new { idIdempotence = Guid.NewGuid(), ticketOrigineId = vente.GetProperty("id").GetGuid(), lignes = new[] { new { ligneOrigineId = ligne, quantite = 2m } } },
            Annulation)).EnsureSuccessStatusCode();
        await VendreAsync(b, (b.Unite, 2m));

        // Toute la marchandise est vendue : le transport arrivé ensuite n'entre plus dans le stock.
        (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception}/frais", new { type = "Transport", montant = 400m }, Annulation)).EnsureSuccessStatusCode();
        var rapport = await b.Comptable.GetFromJsonAsync<RapportCaisse>("/api/rapports/caisse", Annulation);

        Assert.Contains(rapport!.Avoirs, a => a.TicketOrigine == vente.GetProperty("numero").GetString());
        Assert.Equal(400m, rapport.FraisNonIncorpores.Single(f => f.ReceptionId == reception).MontantNonIncorpore);
        Assert.Contains(rapport.Sessions, s => s.NumeroCaisse == b.Poste && s.NombreVentes == 2 && s.NombreAvoirs == 1);
    }

    [Fact]
    public async Task Le_rapport_des_creances_et_dettes_reprend_clients_et_fournisseurs()
    {
        var b = await BoutiqueAsync();
        await ReceptionnerAsync(b, 24m, 300m);
        var client = await ClientAsync(b, plafond: 20000m);
        var vente = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeCredit(client, 1000m, (b.Unite, 2m)), Annulation);
        Assert.Equal(HttpStatusCode.Created, vente.StatusCode);

        var rapport = await b.Comptable.GetFromJsonAsync<RapportCreancesDettes>("/api/rapports/creances-dettes", Annulation);

        Assert.Equal((1000m, 0m), (rapport!.Clients.Single(c => c.ClientId == client).SoldeDu, rapport.Clients.Single(c => c.ClientId == client).MontantEnRetard));
        Assert.Equal((1, 7200m), (rapport.Fournisseurs.Single(f => f.FournisseurId == b.Fournisseur).NombreReceptions, rapport.Fournisseurs.Single(f => f.FournisseurId == b.Fournisseur).ResteDu));
        Assert.True(rapport.TotalDettes >= 7200m && rapport.TotalCreances >= 1000m);
    }

    [Theory]
    [InlineData("du=2026-11-10&au=2026-11-01")]
    [InlineData("du=2024-01-01&au=2026-01-01")]
    public async Task Une_periode_invalide_est_refusee(string requete)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var reponse = await gerant.GetAsync($"/api/rapports/ventes?{requete}", Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("Rapports.PeriodeInvalide", (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Seuls_le_Gerant_et_le_Comptable_consultent_les_rapports()
    {
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);

        Assert.Equal(HttpStatusCode.Forbidden, (await caissier.GetAsync("/api/rapports/ventes", Annulation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await magasinier.GetAsync("/api/rapports/stock", Annulation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await comptable.GetAsync("/api/rapports/caisse?du=2026-01-01&au=2026-01-31", Annulation)).StatusCode);
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
        var poste = ApiDeTest.ProchainPoste();
        (await caissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = poste, fondCaisse = 0m }, Annulation)).EnsureSuccessStatusCode();
        var fournisseur = await Lire<Identifiant>(await gerant.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20] }, Annulation));
        var identifiantCaissier = (await caissier.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;
        return new Boutique(gerant, caissier, comptable, identifiantCaissier, article.Id, unite.Id, carton.Id, poste, fournisseur.Id);
    }

    private static async Task<Guid> ReceptionnerAsync(Boutique b, decimal quantite, decimal cout)
    {
        var reception = await Lire<Identifiant>(await b.Gerant.PostAsJsonAsync(
            "/api/achats/receptions",
            new { fournisseurId = b.Fournisseur, lignes = new[] { new { articleId = b.Article, quantite, coutUnitaire = cout } } },
            Annulation));
        (await b.Gerant.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation)).EnsureSuccessStatusCode();
        return reception.Id;
    }

    private async Task<Guid> ClientAsync(Boutique b, decimal plafond)
    {
        var client = await Lire<Identifiant>(await b.Gerant.PostAsJsonAsync("/api/credit/clients", new { nom = $"Client {Guid.NewGuid():N}"[..16], telephone = "699112233" }, Annulation));
        (await b.Comptable.PutAsJsonAsync($"/api/credit/clients/{client.Id}/plafond", new { plafondCredit = plafond }, Annulation)).EnsureSuccessStatusCode();
        return client.Id;
    }

    /// <summary>Vente entièrement à crédit pour le client.</summary>
    private static object DemandeCredit(Guid client, decimal montant, params (Guid Conditionnement, decimal Quantite)[] lignes) => new
    {
        idIdempotence = Guid.NewGuid(),
        clientId = client,
        lignes = lignes.Select(l => new { conditionnementId = l.Conditionnement, quantite = l.Quantite, remise = 0m }),
        paiements = new[] { new { mode = "Credit", montant } },
    };

    private static async Task<JsonElement> VendreAsync(Boutique b, params (Guid Conditionnement, decimal Quantite)[] lignes)
    {
        var total = lignes.Sum(l => l.Quantite * (l.Conditionnement == b.Carton ? 5500m : 500m));
        var demande = new
        {
            idIdempotence = Guid.NewGuid(),
            lignes = lignes.Select(l => new { conditionnementId = l.Conditionnement, quantite = l.Quantite, remise = 0m }),
            paiements = new[] { new { mode = "Especes", montant = total } },
        };
        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", demande, Annulation);
        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<JsonElement>(reponse);
    }

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Caissier, HttpClient Comptable, string IdentifiantCaissier, Guid Article, Guid Unite, Guid Carton, string Poste, Guid Fournisseur);

    private sealed record Identifiant(Guid Id);

    private sealed record Profil(string Identifiant);

    private sealed record Totaux(int NombreVentes, int NombreAvoirs, decimal ChiffreAffairesTtc, decimal ChiffreAffairesHt, decimal CoutDesVentes, decimal Marge);

    private sealed record VenteJour(DateOnly Jour, int NombreTickets);

    private sealed record Article(Guid ArticleId, decimal Quantite, decimal ChiffreAffairesHt, decimal CoutDesVentes, decimal Marge);

    private sealed record VenteCaissier(string Caissier, int NombreTickets, decimal ChiffreAffairesTtc);

    private sealed record RapportVentes(DateOnly Du, DateOnly Au, Totaux Totaux, List<VenteJour> ParJour, List<Article> ParArticle, List<VenteCaissier> ParCaissier);

    private sealed record TableauDeBord(DateOnly Jour, Totaux VentesDuJour, int SessionsOuvertes, int SessionsAValider, decimal EcartsAValider, int StocksNegatifs, decimal TotalCreances, decimal TotalDettes);

    private sealed record LigneStock(Guid ArticleId, decimal Quantite, decimal Cump, decimal Valeur);

    private sealed record RapportStock(decimal ValeurTotale, List<LigneStock> Articles, List<LigneStock> StocksNegatifs);

    private sealed record Client(Guid ClientId, decimal SoldeDu, decimal MontantEnRetard);

    private sealed record Fournisseur(Guid FournisseurId, int NombreReceptions, decimal ResteDu);

    private sealed record RapportCreancesDettes(decimal TotalCreances, decimal TotalDettes, List<Client> Clients, List<Fournisseur> Fournisseurs);

    private sealed record Session(string NumeroCaisse, int NombreVentes, int NombreAvoirs);

    private sealed record Signale(string Numero, string? TicketOrigine);

    private sealed record Frais(Guid ReceptionId, decimal MontantNonIncorpore);

    private sealed record RapportCaisse(List<Session> Sessions, List<Signale> Avoirs, List<LigneStock> StocksNegatifs, List<Frais> FraisNonIncorpores);
}
