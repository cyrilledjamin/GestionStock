using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class RepriseTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_modele_est_un_classeur_aux_onze_colonnes_dans_la_langue_de_l_utilisateur()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        gerant.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var octets = await gerant.GetByteArrayAsync("/api/reprise/modele.xlsx", Annulation);
        using var classeur = new XLWorkbook(new MemoryStream(octets));
        var feuille = classeur.Worksheet(1);

        Assert.Equal("Items", feuille.Name);
        Assert.Equal("Item code", feuille.Cell(1, 1).GetString());
        Assert.Equal("Purchase cost", feuille.Cell(1, 11).GetString());
        Assert.Equal(1, feuille.LastRowUsed()!.RowNumber());
        Assert.Equal("Help", classeur.Worksheet(2).Name);
    }

    [Fact]
    public async Task L_exemple_s_importe_avec_ses_prix_codes_et_inventaire_d_ouverture_puis_ne_se_reimporte_pas()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var exemple = await gerant.GetByteArrayAsync("/api/reprise/modele.xlsx?exemple=true", Annulation);

        var simulation = await EnvoyerAsync(gerant, "simulation", exemple);
        var import = await EnvoyerAsync(gerant, "import", exemple);
        var riz = await gerant.GetFromJsonAsync<Vendable>($"/api/referentiel/scan/{CodeBarresDe(exemple, "RIZ-PARF", "Sac")}", Annulation);
        var prixSac = await gerant.GetFromJsonAsync<Prix>($"/api/prix/conditionnements/{riz!.ConditionnementId}", Annulation);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var ouverture = await comptable.GetFromJsonAsync<DetailInventaire>($"/api/inventaires/{import.InventaireOuvertureId}", Annulation);
        var reimport = await EnvoyerAsync(gerant, "simulation", exemple);

        Assert.Empty(simulation.Erreurs);
        Assert.False(simulation.Importe);
        Assert.True(import.Importe);
        Assert.Equal(19, import.Articles);
        Assert.Equal(34, import.Conditionnements);
        Assert.Equal(34, import.CodesBarres);
        Assert.Equal((25m, "Sac de 25 kg"), (riz.QuantiteUniteStock, riz.LibelleConditionnement));
        Assert.Equal(15000m, prixSac!.PrixTtc);
        Assert.Equal(("Ouverture", 19), (ouverture!.Inventaire.Type, ouverture.Lignes.Count));
        Assert.Equal(520m, ouverture.Lignes.Single(l => l.CodeArticle == "RIZ-PARF").CoutOuverture);
        Assert.Equal(19, reimport.Erreurs.Count(e => e.Code == "Reprise.CodeArticleExistant"));
        Assert.Equal(34, reimport.Erreurs.Count(e => e.Code == "Reprise.CodeBarresAttribue"));
    }

    [Fact]
    public async Task Un_fichier_avec_des_erreurs_est_rapporte_ligne_par_ligne_et_rien_n_est_importe()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var a = Code();
        var b = Code();
        var c = Code();
        var ean = Ean();
        var fichier = Classeur(
            [a, "Sucre", "", "kg", "3", "", "Unite", "1", "", "850", "690"],
            [a, "", "", "", "2", "Sac de 50 kg", "Sac", "50", "", "40000", ""],          // ligne 3 : décimales contredites
            [b, "Savon", "", "pièce", "0", "", "Unite", "1", "4006381333932", "", "250"], // ligne 4 : clé EAN fausse
            [b, "", "", "", "", "Carton", "Carton", "2,5", "", "", ""],                  // ligne 5 : 2,5 pièces
            [c, "", "", "", "", "Carton de 6", "Carton", "6", ean, "", ""],              // ligne 6 : pas de ligne Unite
            [Code(), "Lait", "", "boîte", "0", "", "Unité", "1", ean, "", "590"],          // ligne 7 : code-barres en double
            [Code(), "Bougie", "", "pièce", "0", "", "Unite", "1", "", "-5", ""]);        // ligne 8 : prix et coût

        var rapport = await EnvoyerAsync(gerant, "import", fichier);
        var existe = await gerant.GetFromJsonAsync<List<Article>>($"/api/referentiel/articles?recherche={a}", Annulation);

        Assert.False(rapport.Importe);
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code, e.Colonne) == (3, "Reprise.IncoherenceArticle", "Décimales"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (4, "Reprise.CodeBarresInvalide"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (5, "Reprise.ContenuInvalide"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (6, "Reprise.LigneUniteManquante"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (7, "Reprise.CodeBarresEnDouble"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (8, "Reprise.PrixInvalide"));
        Assert.Contains(rapport.Erreurs, e => (e.Ligne, e.Code) == (8, "Reprise.ChampObligatoire"));
        Assert.All(rapport.Erreurs, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
        Assert.Empty(existe!);
    }

    [Fact]
    public async Task Un_fichier_qui_n_est_pas_un_classeur_est_refuse()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var rapport = await EnvoyerAsync(gerant, "simulation", "ceci n'est pas un classeur"u8.ToArray());

        Assert.Equal("Reprise.FichierIllisible", Assert.Single(rapport.Erreurs).Code);
    }

    [Fact]
    public async Task L_import_puis_l_inventaire_d_ouverture_donnent_le_stock_valorise_au_cout_du_fichier()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        var code = Code();
        var fichier = Classeur(
            [code, "Riz parfumé", "Fragrant rice", "kg", "3", "", "Unite", "1", "", "650", "520,5"],
            [code, "", "", "", "", "Sac de 25 kg", "Sac", "25", Ean(), "15 000", ""]);

        var import = await EnvoyerAsync(gerant, "import", fichier);
        var ouverture = await comptable.GetFromJsonAsync<DetailInventaire>($"/api/inventaires/{import.InventaireOuvertureId}", Annulation);
        var article = ouverture!.Lignes.Single().ArticleId;
        (await magasinier.PostAsJsonAsync($"/api/inventaires/{import.InventaireOuvertureId}/comptages", new { articleId = article, quantite = 112.75m }, Annulation)).EnsureSuccessStatusCode();
        (await comptable.PostAsync($"/api/inventaires/{import.InventaireOuvertureId}/validation", null, Annulation)).EnsureSuccessStatusCode();
        var solde = (await gerant.GetFromJsonAsync<List<Solde>>("/api/stock/soldes", Annulation))!.Single(s => s.ArticleId == article);

        Assert.True(import.Importe, string.Join(" ; ", import.Erreurs.Select(e => $"{e.Ligne} {e.Code}")));
        Assert.Equal((112.75m, 520.5m), (solde.QuantitePhysique, solde.Cump));
    }

    [Fact]
    public async Task Seuls_le_gerant_et_l_administrateur_importent()
    {
        using var magasinier = await api.ClientAvecRoleAsync(Roles.Magasinier);
        using var administrateur = await api.ClientAdministrateurAsync();

        var parLeMagasinier = await magasinier.GetAsync("/api/reprise/modele.xlsx", Annulation);
        var parLAdministrateur = await administrateur.GetAsync("/api/reprise/modele.xlsx", Annulation);

        Assert.Equal(HttpStatusCode.Forbidden, parLeMagasinier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, parLAdministrateur.StatusCode);
    }

    // --- Outillage -----------------------------------------------------------------------------------------------

    private static async Task<Rapport> EnvoyerAsync(HttpClient client, string action, byte[] fichier)
    {
        using var contenu = new ByteArrayContent(fichier);
        contenu.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var reponse = await client.PostAsync($"/api/reprise/{action}", contenu, Annulation);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<Rapport>(Annulation))!;
    }

    /// <summary>Classeur au format du modèle : en-tête en ligne 1, puis les lignes données (cellules en texte).</summary>
    private static byte[] Classeur(params string[][] lignes)
    {
        using var classeur = new XLWorkbook();
        var feuille = classeur.AddWorksheet("Articles");
        feuille.Cell(1, 1).Value = "Code article";
        for (var l = 0; l < lignes.Length; l++)
        {
            for (var c = 0; c < lignes[l].Length; c++)
            {
                feuille.Cell(l + 2, c + 1).Value = lignes[l][c];
            }
        }

        using var flux = new MemoryStream();
        classeur.SaveAs(flux);
        return flux.ToArray();
    }

    private static string CodeBarresDe(byte[] classeurOctets, string codeArticle, string niveau)
    {
        using var classeur = new XLWorkbook(new MemoryStream(classeurOctets));
        var ligne = classeur.Worksheet(1).RowsUsed().Single(r => r.Cell(1).GetString() == codeArticle && r.Cell(7).GetString() == niveau);
        return ligne.Cell(9).GetString();
    }

    private static string Code() => $"REP-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private static string Ean() => ConditionnementsTests.NouvelEan13();

    private sealed record Erreur(int Ligne, string Code, string Message, string? Colonne, string? Detail);

    private sealed record Rapport(bool Importe, int Lignes, int Articles, int Conditionnements, int CodesBarres, int Prix, List<Erreur> Erreurs, Guid? InventaireOuvertureId, string? InventaireOuvertureNumero);

    private sealed record Vendable(Guid ArticleId, Guid ConditionnementId, string LibelleConditionnement, decimal QuantiteUniteStock);

    private sealed record Prix(decimal PrixTtc);

    private sealed record Article(Guid Id, string Code);

    private sealed record Resume(string Type);

    private sealed record LigneInventaire(Guid ArticleId, string CodeArticle, decimal? CoutOuverture);

    private sealed record DetailInventaire(Resume Inventaire, List<LigneInventaire> Lignes);

    private sealed record Solde(Guid ArticleId, decimal QuantitePhysique, decimal Cump);
}
