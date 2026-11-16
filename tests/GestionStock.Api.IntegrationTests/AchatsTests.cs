using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class AchatsTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deux_receptions_validees_font_entrer_le_stock_au_CUMP()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var identifiantMagasinier = (await magasinier.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Lait concentré");
        var fournisseur = await CreerFournisseurAsync(magasinier);

        var premiere = await CreerReceptionAsync(magasinier, fournisseur.Id, (article.Id, 10m, 500m));
        var premiereValidee = await ValiderAsync(magasinier, premiere.Id);
        var seconde = await CreerReceptionAsync(magasinier, fournisseur.Id, (article.Id, 10m, 700m));
        await ValiderAsync(magasinier, seconde.Id);

        var soldes = await gerant.GetFromJsonAsync<List<Solde>>("/api/stock/soldes", Annulation);
        var solde = soldes!.Single(s => s.ArticleId == article.Id);
        var journal = await gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{article.Id}/mouvements", Annulation);

        Assert.Equal("Validee", premiereValidee.Statut);
        Assert.Equal(identifiantMagasinier, premiereValidee.ValideePar);
        Assert.Equal(5000m, premiereValidee.Valeur);
        Assert.Equal(20m, solde.QuantitePhysique);
        Assert.Equal(20m, solde.QuantiteDisponible);
        Assert.Equal(600m, solde.Cump);
        Assert.Equal(2, journal!.Count);
        Assert.All(journal, m =>
        {
            Assert.Equal("EntreeReception", m.Type);
            Assert.Equal(10m, m.Variation);
            Assert.Equal("Reception", m.TypeDocument);
            Assert.Equal(identifiantMagasinier, m.Utilisateur);
        });
        Assert.Equal([seconde.Id, premiere.Id], journal.Select(m => m.DocumentId));
    }

    [Fact]
    public async Task Les_numeros_de_reception_se_suivent()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;

        var a = await CreerReceptionAsync(magasinier, fournisseur, (article, 1m, 1m));
        var b = await CreerReceptionAsync(magasinier, fournisseur, (article, 1m, 1m));

        Assert.Matches(@"^R-\d{6}$", a.Numero);
        Assert.True(string.CompareOrdinal(b.Numero, a.Numero) > 0);
    }

    [Fact]
    public async Task Une_reception_validee_ne_se_revalide_pas_et_ne_se_modifie_plus()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;
        var reception = await CreerReceptionAsync(magasinier, fournisseur, (article, 3m, 100m));
        await ValiderAsync(magasinier, reception.Id);

        var revalidation = await magasinier.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation);
        var modification = await magasinier.PutAsJsonAsync($"/api/achats/receptions/{reception.Id}", Saisie(fournisseur, (article, 5m, 100m)), Annulation);
        var journal = await magasinier.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{article}/mouvements", Annulation);

        Assert.Equal(HttpStatusCode.Conflict, revalidation.StatusCode);
        Assert.Equal("Achats.ReceptionDejaValidee", (await IdentiteTests.LireProblemeAsync(revalidation)).Code);
        Assert.Equal(HttpStatusCode.Conflict, modification.StatusCode);
        Assert.Single(journal!);
    }

    [Fact]
    public async Task Deux_validations_simultanees_n_ecrivent_qu_une_fois()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;
        using var autre = await api.ClientAvecRoleAsync(Roles.Gerant);
        var reception = await CreerReceptionAsync(magasinier, fournisseur, (article, 2m, 50m));

        var reponses = await Task.WhenAll(
            magasinier.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation),
            autre.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation));
        var journal = await magasinier.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{article}/mouvements", Annulation);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], reponses.Select(r => r.StatusCode).Order());
        Assert.Single(journal!);
    }

    [Fact]
    public async Task Si_une_ligne_est_refusee_a_la_validation_rien_n_est_ecrit()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var valide = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Farine");
        var aDesactiver = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Levure");
        var fournisseur = await CreerFournisseurAsync(magasinier);
        var reception = await CreerReceptionAsync(magasinier, fournisseur.Id, (valide.Id, 4m, 200m), (aDesactiver.Id, 1m, 50m));
        (await gerant.PutAsJsonAsync(
            $"/api/referentiel/articles/{aDesactiver.Id}",
            new { libelle = aDesactiver.Libelle, libelleTraduit = (string?)null, actif = false },
            Annulation)).EnsureSuccessStatusCode();

        var validation = await magasinier.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation);
        var relue = await magasinier.GetFromJsonAsync<Reception>($"/api/achats/receptions/{reception.Id}", Annulation);
        var journal = await magasinier.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{valide.Id}/mouvements", Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Equal("Achats.ArticleInactif", (await IdentiteTests.LireProblemeAsync(validation)).Code);
        Assert.Equal("Brouillon", relue!.Statut);
        Assert.Empty(journal!);
    }

    [Fact]
    public async Task Un_brouillon_se_modifie_en_remplacant_ses_lignes()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;
        var reception = await CreerReceptionAsync(magasinier, fournisseur, (article, 1m, 10m));

        var reponse = await magasinier.PutAsJsonAsync(
            $"/api/achats/receptions/{reception.Id}",
            Saisie(fournisseur, (article, 2m, 10m), (article, 3m, 20m)) with { NumeroBonLivraison = "BL-42" },
            Annulation);
        var modifiee = await reponse.Content.ReadFromJsonAsync<Reception>(Annulation);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("BL-42", modifiee!.NumeroBonLivraison);
        Assert.Equal([2m, 3m], modifiee.Lignes.Select(l => l.Quantite));
        Assert.Equal(80m, modifiee.Valeur);
    }

    [Theory]
    [InlineData(1.5, 100, "Achats.PrecisionQuantite")]
    [InlineData(0, 100, "Achats.QuantiteNonPositive")]
    [InlineData(1, -1, "Achats.CoutNegatif")]
    [InlineData(1, 0.00001, "Achats.PrecisionCout")]
    public async Task Une_ligne_invalide_est_refusee(decimal quantite, decimal cout, string code)
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;

        var reponse = await magasinier.PostAsJsonAsync("/api/achats/receptions", Saisie(fournisseur, (article, quantite, cout)), Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Une_reception_sans_ligne_ou_sans_fournisseur_connu_est_refusee()
    {
        var (magasinier, fournisseur, _) = await PreparerAsync();
        using var client = magasinier;

        var sansLigne = await client.PostAsJsonAsync("/api/achats/receptions", Saisie(fournisseur), Annulation);
        var sansFournisseur = await client.PostAsJsonAsync("/api/achats/receptions", Saisie(Guid.NewGuid()), Annulation);

        Assert.Equal("Achats.ReceptionSansLigne", (await IdentiteTests.LireProblemeAsync(sansLigne)).Code);
        Assert.Equal("Achats.FournisseurIntrouvable", (await IdentiteTests.LireProblemeAsync(sansFournisseur)).Code);
    }

    [Fact]
    public async Task Les_droits_suivent_les_roles()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var reception = await CreerReceptionAsync(magasinier, fournisseur, (article, 1m, 1m));

        var saisieCaissier = await caissier.PostAsJsonAsync("/api/achats/receptions", Saisie(fournisseur, (article, 1m, 1m)), Annulation);
        var lectureComptable = await comptable.GetAsync($"/api/achats/receptions/{reception.Id}", Annulation);
        var validationComptable = await comptable.PostAsync($"/api/achats/receptions/{reception.Id}/validation", null, Annulation);
        var stockCaissier = await caissier.GetAsync("/api/stock/soldes", Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, saisieCaissier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lectureComptable.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, validationComptable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stockCaissier.StatusCode);
    }

    [Fact]
    public async Task Un_fournisseur_desactive_n_est_plus_propose_ni_utilisable()
    {
        var (magasinier, fournisseur, article) = await PreparerAsync();
        using var _ = magasinier;

        (await magasinier.PutAsJsonAsync($"/api/achats/fournisseurs/{fournisseur}", new { nom = "Ancien grossiste", telephone = (string?)null, actif = false }, Annulation))
            .EnsureSuccessStatusCode();
        var actifs = await magasinier.GetFromJsonAsync<List<Fournisseur>>("/api/achats/fournisseurs", Annulation);
        var reception = await magasinier.PostAsJsonAsync("/api/achats/receptions", Saisie(fournisseur, (article, 1m, 1m)), Annulation);

        Assert.DoesNotContain(actifs!, f => f.Id == fournisseur);
        Assert.Equal("Achats.FournisseurInactif", (await IdentiteTests.LireProblemeAsync(reception)).Code);
    }

    private async Task<(HttpClient Magasinier, Guid Fournisseur, Guid Article)> PreparerAsync()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        var article = await ReferentielTests.CreerAsync(gerant, ReferentielTests.NouveauCode(), "Article de test");
        var fournisseur = await CreerFournisseurAsync(magasinier);
        return (magasinier, fournisseur.Id, article.Id);
    }

    private static async Task<Fournisseur> CreerFournisseurAsync(HttpClient client)
    {
        var reponse = await client.PostAsJsonAsync("/api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20], telephone = "+237 6 99 00 00 00" }, Annulation);
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<Fournisseur>(Annulation))!;
    }

    private static SaisieReception Saisie(Guid fournisseur, params (Guid Article, decimal Quantite, decimal Cout)[] lignes) =>
        new(fournisseur, null, [.. lignes.Select(l => new SaisieLigne(l.Article, l.Quantite, l.Cout))]);

    private static async Task<Reception> CreerReceptionAsync(HttpClient client, Guid fournisseur, params (Guid Article, decimal Quantite, decimal Cout)[] lignes)
    {
        var reponse = await client.PostAsJsonAsync("/api/achats/receptions", Saisie(fournisseur, lignes), Annulation);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<Reception>(Annulation))!;
    }

    private static async Task<Reception> ValiderAsync(HttpClient client, Guid reception)
    {
        var reponse = await client.PostAsync($"/api/achats/receptions/{reception}/validation", null, Annulation);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<Reception>(Annulation))!;
    }

    private sealed record Profil(string Identifiant);

    private sealed record Fournisseur(Guid Id, string Nom, string? Telephone, bool Actif);

    private sealed record SaisieLigne(Guid ArticleId, decimal Quantite, decimal CoutUnitaire);

    private sealed record SaisieReception(Guid FournisseurId, string? NumeroBonLivraison, List<SaisieLigne> Lignes);

    private sealed record Reception(Guid Id, string Numero, string Statut, string? NumeroBonLivraison, string? ValideePar, decimal Valeur, List<LigneLue> Lignes);

    private sealed record LigneLue(Guid ArticleId, decimal Quantite, decimal CoutUnitaire);

    private sealed record Solde(Guid ArticleId, decimal QuantitePhysique, decimal QuantiteDisponible, decimal Cump);

    private sealed record Mouvement(long Id, string Type, decimal Variation, string TypeDocument, Guid DocumentId, string Utilisateur);
}
