using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

/// <summary>Dettes fournisseurs (décision 27) : montant dû par réception, paiements partiels, annulation.</summary>
public sealed class DettesFournisseursTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Une_dette_se_regle_en_plusieurs_paiements()
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionValideeAsync(b, quantite: 10m, cout: 1000m, frais: 500m);

        var initiale = await DetteAsync(b, reception);
        var partielle = await PayerAsync(b, reception, 4000m, "Especes", null);
        var soldee = await PayerAsync(b, reception, 6500m, "Virement", "VIR-2026-118");
        var nonSoldees = await b.Comptable.GetFromJsonAsync<Dettes>($"/api/achats/dettes?fournisseurId={b.Fournisseur}&nonSoldees=true", Annulation);
        var toutes = await b.Comptable.GetFromJsonAsync<Dettes>($"/api/achats/dettes?fournisseurId={b.Fournisseur}", Annulation);

        Assert.Equal((10500m, 0m, "NonPayee"), (initiale.Dette.MontantDu, initiale.Dette.MontantPaye, initiale.Dette.Statut));
        Assert.Equal((6500m, "PartiellementPayee"), (partielle.Dette.ResteDu, partielle.Dette.Statut));
        Assert.Equal((0m, "Payee"), (soldee.Dette.ResteDu, soldee.Dette.Statut));
        Assert.Equal([("Especes", (string?)null), ("Virement", "VIR-2026-118")], soldee.Paiements.Select(p => (p.Mode, p.Reference)));
        Assert.Empty(nonSoldees!.Receptions);
        Assert.Equal((1, 10500m, 0m), (toutes!.Fournisseurs.Single().NombreReceptions, toutes.Fournisseurs.Single().MontantDu, toutes.Fournisseurs.Single().ResteDu));
    }

    [Fact]
    public async Task Un_frais_tardif_augmente_la_dette()
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionValideeAsync(b, quantite: 5m, cout: 2000m, frais: null);
        await PayerAsync(b, reception, 10000m, "MobileMoney", "MM-55");

        (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception}/frais", new { type = "Transport", montant = 300m }, Annulation)).EnsureSuccessStatusCode();
        var dette = await DetteAsync(b, reception);

        Assert.Equal((10300m, 300m, "PartiellementPayee"), (dette.Dette.MontantDu, dette.Dette.ResteDu, dette.Dette.Statut));
    }

    [Fact]
    public async Task Une_annulation_journalisee_retablit_le_reste_du()
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionValideeAsync(b, quantite: 1m, cout: 5000m, frais: null);
        var paye = await PayerAsync(b, reception, 2000m, "Cheque", "CHQ-0042");
        var paiement = paye.Paiements.Single().Id;

        var sansMotif = await b.Comptable.PostAsJsonAsync($"/api/achats/paiements/{paiement}/annulation", new { motif = " " }, Annulation);
        var annulee = await Lire<DetailDette>(await b.Comptable.PostAsJsonAsync($"/api/achats/paiements/{paiement}/annulation", new { motif = "Chèque sans provision" }, Annulation));
        var deuxieme = await b.Comptable.PostAsJsonAsync($"/api/achats/paiements/{paiement}/annulation", new { motif = "Encore" }, Annulation);
        var annulationAnnulee = await b.Comptable.PostAsJsonAsync($"/api/achats/paiements/{annulee.Paiements[1].Id}/annulation", new { motif = "Non" }, Annulation);

        Assert.Equal("Achats.MotifAnnulationObligatoire", (await IdentiteTests.LireProblemeAsync(sansMotif)).Code);
        Assert.Equal((5000m, "NonPayee"), (annulee.Dette.ResteDu, annulee.Dette.Statut));
        Assert.Equal([(2000m, true), (-2000m, false)], annulee.Paiements.Select(p => (p.Montant, p.EstAnnule)));
        Assert.Equal(("Chèque sans provision", paiement), (annulee.Paiements[1].Motif, annulee.Paiements[1].PaiementAnnuleId));
        Assert.Equal(HttpStatusCode.Conflict, deuxieme.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, annulationAnnulee.StatusCode);
    }

    [Theory]
    [InlineData(6000, "Especes", null, "Achats.MontantSuperieurAuReste")]
    [InlineData(0, "Especes", null, "Achats.MontantPaiementInvalide")]
    [InlineData(100, "MobileMoney", null, "Achats.ReferenceObligatoire")]
    [InlineData(100, "Virement", " ", "Achats.ReferenceObligatoire")]
    public async Task Un_paiement_invalide_est_refuse(decimal montant, string mode, string? reference, string code)
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionValideeAsync(b, quantite: 1m, cout: 5000m, frais: null);

        var reponse = await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception}/paiements", new { montant, mode, reference }, Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Un_brouillon_n_a_pas_de_dette()
    {
        var b = await BoutiqueAsync();
        var brouillon = await Lire<Identifiant>(await b.Magasinier.PostAsJsonAsync("/api/achats/receptions", Saisie(b, 1m, 100m), Annulation));

        var paiement = await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{brouillon.Id}/paiements", new { montant = 10m, mode = "Especes" }, Annulation);
        var dettes = await b.Comptable.GetFromJsonAsync<Dettes>($"/api/achats/dettes?fournisseurId={b.Fournisseur}", Annulation);

        Assert.Equal(HttpStatusCode.Conflict, paiement.StatusCode);
        Assert.Equal("Achats.PaiementSurBrouillon", (await IdentiteTests.LireProblemeAsync(paiement)).Code);
        Assert.Empty(dettes!.Receptions);
    }

    [Fact]
    public async Task Seul_le_Comptable_enregistre_les_paiements()
    {
        var b = await BoutiqueAsync();
        var reception = await ReceptionValideeAsync(b, quantite: 1m, cout: 5000m, frais: null);

        var gerant = await b.Gerant.PostAsJsonAsync($"/api/achats/receptions/{reception}/paiements", new { montant = 10m, mode = "Especes" }, Annulation);
        var magasinier = await b.Magasinier.PostAsJsonAsync($"/api/achats/receptions/{reception}/paiements", new { montant = 10m, mode = "Especes" }, Annulation);
        var lectureMagasinier = await b.Magasinier.GetAsync("/api/achats/dettes", Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, gerant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, magasinier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lectureMagasinier.StatusCode);
    }

    // --- Outils --------------------------------------------------------------------------------------------------

    private async Task<Boutique> BoutiqueAsync()
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Article de dette");
        var fournisseur = await Lire<Identifiant>(await magasinier.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20] }, Annulation));
        return new Boutique(gerant, magasinier, comptable, fournisseur.Id, article.Id);
    }

    private static object Saisie(Boutique b, decimal quantite, decimal cout) =>
        new { fournisseurId = b.Fournisseur, lignes = new[] { new { articleId = b.Article, quantite, coutUnitaire = cout } } };

    private static async Task<Guid> ReceptionValideeAsync(Boutique b, decimal quantite, decimal cout, decimal? frais)
    {
        var reception = await Lire<Identifiant>(await b.Magasinier.PostAsJsonAsync("/api/achats/receptions", Saisie(b, quantite, cout), Annulation));
        if (frais is { } montant)
        {
            (await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception.Id}/frais", new { type = "Transport", montant }, Annulation)).EnsureSuccessStatusCode();
        }

        (await b.Magasinier.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation)).EnsureSuccessStatusCode();
        return reception.Id;
    }

    private static async Task<DetailDette> DetteAsync(Boutique b, Guid reception) =>
        (await b.Comptable.GetFromJsonAsync<DetailDette>($"/api/achats/receptions/{reception}/dette", Annulation))!;

    private static async Task<DetailDette> PayerAsync(Boutique b, Guid reception, decimal montant, string mode, string? reference)
    {
        var reponse = await b.Comptable.PostAsJsonAsync($"/api/achats/receptions/{reception}/paiements", new { montant, mode, reference }, Annulation);
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<DetailDette>(reponse);
    }

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Magasinier, HttpClient Comptable, Guid Fournisseur, Guid Article);

    private sealed record Identifiant(Guid Id);

    private sealed record Dette(Guid ReceptionId, decimal MontantDu, decimal MontantPaye, decimal ResteDu, string Statut);

    private sealed record DetteParFournisseur(Guid FournisseurId, int NombreReceptions, decimal MontantDu, decimal ResteDu);

    private sealed record Dettes(List<DetteParFournisseur> Fournisseurs, List<Dette> Receptions);

    private sealed record Paiement(Guid Id, decimal Montant, string Mode, string? Reference, Guid? PaiementAnnuleId, string? Motif, bool EstAnnule);

    private sealed record DetailDette(Dette Dette, List<Paiement> Paiements);
}
