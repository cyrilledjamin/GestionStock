using System.Net;
using System.Net.Http.Json;
using System.Text;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

public sealed class CaisseTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    // --- Ventes --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Une_vente_en_especes_rend_la_monnaie_et_sort_le_stock_par_conditionnement()
    {
        var b = await BoutiqueAsync();

        var ticket = await VendreAsync(b, [(b.Unite, 2m), (b.Carton, 1m)], Especes(10000));
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleId}/mouvements", Annulation);

        Assert.Equal($"{b.Poste}-000001", ticket.Numero);
        Assert.Equal(6500m, ticket.TotalTtc);
        Assert.Equal(3500m, ticket.Rendu);
        Assert.Equal(0m, ticket.TauxTva);
        Assert.NotNull(journal);
        Assert.Equal([-12m, -2m], journal.Select(m => m.Variation));
        Assert.All(journal, m => Assert.Equal("VenteCaisse", m.Type));
        Assert.All(journal, m => Assert.Equal(ticket.Id, m.DocumentId));
    }

    [Fact]
    public async Task Une_vente_n_est_jamais_refusee_pour_stock_insuffisant()
    {
        var b = await BoutiqueAsync();

        await VendreAsync(b, [(b.Unite, 3m)], Especes(1500));
        var soldes = await b.Gerant.GetFromJsonAsync<List<Solde>>("/api/stock/soldes", Annulation);

        Assert.Equal(-3m, soldes!.Single(s => s.ArticleId == b.ArticleId).QuantitePhysique);
    }

    [Fact]
    public async Task Les_tickets_sont_numerotes_en_continu_par_poste_meme_en_simultane()
    {
        var b = await BoutiqueAsync();

        var tickets = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => VendreAsync(b, [(b.Unite, 1m)], Especes(500))));

        Assert.Equal(
            Enumerable.Range(1, 6).Select(n => $"{b.Poste}-{n:D6}"),
            tickets.Select(t => t.Numero).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Un_ticket_renvoye_apres_coupure_n_est_enregistre_qu_une_fois()
    {
        var b = await BoutiqueAsync();
        var demande = Demande([(b.Unite, 1m)], Especes(500));

        var premier = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", demande, Annulation);
        var renvoi = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", demande, Annulation);
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleId}/mouvements", Annulation);

        Assert.Equal(HttpStatusCode.Created, premier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renvoi.StatusCode);
        Assert.Equal((await Lire<Ticket>(premier)).Id, (await Lire<Ticket>(renvoi)).Id);
        Assert.Single(journal!);
    }

    [Fact]
    public async Task Un_ticket_combine_plusieurs_modes_de_paiement()
    {
        var b = await BoutiqueAsync();

        var ticket = await VendreAsync(b, [(b.Carton, 1m)], [Paiement("Especes", 2000), Paiement("MobileMoney", 3500, "MOMO-123456")]);

        Assert.Equal(0m, ticket.Rendu);
        Assert.Equal(["Especes", "MobileMoney"], ticket.Paiements.Select(p => p.Mode));
        Assert.Equal("MOMO-123456", ticket.Paiements[1].Reference);
    }

    [Theory]
    [InlineData("MobileMoney", 500, null, "Caisse.ReferenceObligatoire")]
    [InlineData("Carte", 1000, null, "Caisse.RenduSansEspeces")]
    [InlineData("Especes", 400, null, "Caisse.PaiementInsuffisant")]
    [InlineData("Especes", 500.5, null, "Caisse.MontantNonEntier")]
    [InlineData("Credit", 500, null, "Caisse.ClientObligatoire")]
    public async Task Un_paiement_invalide_est_refuse_sans_rien_ecrire(string mode, decimal montant, string? reference, string code)
    {
        var b = await BoutiqueAsync();

        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande([(b.Unite, 1m)], [Paiement(mode, montant, reference)]), Annulation);
        var suivant = await VendreAsync(b, [(b.Unite, 1m)], Especes(500));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
        Assert.Equal($"{b.Poste}-000001", suivant.Numero);
    }

    [Fact]
    public async Task Un_article_sans_prix_ne_se_vend_pas()
    {
        var b = await BoutiqueAsync();
        var (_, sansPrix) = await ConditionnementsTests.ArticleAvecCartonAsync(b.Gerant, 6);

        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande([(sansPrix.Id, 1m)], Especes(5000)), Annulation);

        Assert.Equal("Caisse.PrixAbsent", (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Seul_un_caissier_avec_une_session_ouverte_vend()
    {
        var b = await BoutiqueAsync(ouvrirSession: false);

        var sansSession = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande([(b.Unite, 1m)], Especes(500)), Annulation);
        var parLeGerant = await b.Gerant.PostAsJsonAsync("/api/caisse/tickets", Demande([(b.Unite, 1m)], Especes(500)), Annulation);

        Assert.Equal(HttpStatusCode.Conflict, sansSession.StatusCode);
        Assert.Equal("Caisse.AucuneSessionOuverte", (await IdentiteTests.LireProblemeAsync(sansSession)).Code);
        Assert.Equal(HttpStatusCode.Forbidden, parLeGerant.StatusCode);
    }

    // --- Remises -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Une_remise_exige_la_validation_d_un_responsable_tracee_sur_le_ticket()
    {
        var b = await BoutiqueAsync();
        var lignes = new[] { (b.Carton, 1m, 500m) };

        var sansValidation = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande(lignes, Especes(5000)), Annulation);
        var parLeCaissier = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande(lignes, Especes(5000), validation: (b.IdentifiantCaissier, ApiDeTest.MotDePasseValide)), Annulation);
        var valide = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande(lignes, Especes(5000), validation: (b.IdentifiantGerant, ApiDeTest.MotDePasseValide)), Annulation);
        var ticket = await Lire<Ticket>(valide);

        Assert.Equal("Caisse.RemiseNonValidee", (await IdentiteTests.LireProblemeAsync(sansValidation)).Code);
        Assert.Equal(HttpStatusCode.Forbidden, parLeCaissier.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valide.StatusCode);
        Assert.Equal(5000m, ticket.TotalTtc);
        Assert.Equal(b.IdentifiantGerant, ticket.RemiseValideePar);
    }

    // --- Crédit --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Une_vente_a_credit_au_dela_du_plafond_exige_le_gerant_et_n_ecrit_rien_sinon()
    {
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 6000m);

        var dansLePlafond = await VendreAsync(b, [(b.Carton, 1m)], [Paiement("Credit", 5500)], client);
        var refus = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande([(b.Carton, 1m)], [Paiement("Credit", 5500)], client), Annulation);
        var valide = await VendreAsync(b, [(b.Carton, 1m)], [Paiement("Credit", 5500)], client, (b.IdentifiantGerant, ApiDeTest.MotDePasseValide));
        var fiche = await b.Gerant.GetFromJsonAsync<FicheClient>($"/api/credit/clients/{client}", Annulation);

        Assert.Equal("Caisse.PlafondDepasse", (await IdentiteTests.LireProblemeAsync(refus)).Code);
        Assert.Equal($"{b.Poste}-000002", valide.Numero); // le ticket refusé n'a pas consommé de numéro
        Assert.Equal(b.IdentifiantGerant, valide.CreditValidePar);
        Assert.Null(dansLePlafond.CreditValidePar);
        Assert.Equal(11000m, fiche!.Client.SoldeDu);
    }

    [Fact]
    public async Task Un_remboursement_de_credit_s_encaisse_dans_la_session()
    {
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 10000m);
        await VendreAsync(b, [(b.Carton, 1m)], [Paiement("Credit", 5500)], client);

        var reponse = await b.Caissier.PostAsJsonAsync(
            "/api/caisse/remboursements",
            new { idIdempotence = Guid.NewGuid(), clientId = client, mode = "Especes", montant = 2000 },
            Annulation);
        var remboursement = await Lire<Remboursement>(reponse);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(3500m, remboursement.SoldeDuApres);
    }

    // --- Avoirs --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_avoir_rend_l_argent_remet_le_stock_et_ne_rend_pas_plus_que_vendu()
    {
        var b = await BoutiqueAsync();
        var vente = await VendreAsync(b, [(b.Unite, 3m)], Especes(1500));
        var ligne = vente.Lignes.Single().Id;

        var avoir = await FaireAvoirAsync(b, vente.Id, (ligne, 2m));
        var trop = await b.Caissier.PostAsJsonAsync("/api/caisse/avoirs", DemandeAvoir(vente.Id, (ligne, 2m)), Annulation);
        var relu = await b.Caissier.GetFromJsonAsync<Ticket>($"/api/caisse/tickets/{vente.Id}", Annulation);
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleId}/mouvements", Annulation);

        Assert.True(avoir.EstAvoir);
        Assert.Equal(vente.Id, avoir.TicketOrigineId);
        Assert.Equal($"{b.Poste}-000002", avoir.Numero);
        Assert.Equal(1000m, avoir.TotalTtc);
        Assert.Equal([new PaiementLu("Especes", 1000m, null)], avoir.Paiements);
        Assert.Equal("Caisse.QuantiteRenduTropGrande", (await IdentiteTests.LireProblemeAsync(trop)).Code);
        Assert.Equal(2m, relu!.Lignes.Single().QuantiteRendue);
        Assert.Equal(("AvoirCaisse", 2m), (journal![0].Type, journal[0].Variation));
    }

    [Fact]
    public async Task Un_avoir_sur_un_ticket_a_credit_annule_d_abord_la_dette()
    {
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 10000m);
        var vente = await VendreAsync(b, [(b.Unite, 4m)], [Paiement("Credit", 2000)], client);

        var avoir = await FaireAvoirAsync(b, vente.Id, (vente.Lignes.Single().Id, 1m));
        var fiche = await b.Gerant.GetFromJsonAsync<FicheClient>($"/api/credit/clients/{client}", Annulation);

        Assert.Equal([new PaiementLu("Credit", 500m, null)], avoir.Paiements);
        Assert.Equal(1500m, fiche!.Client.SoldeDu);
    }

    [Fact]
    public async Task Un_ticket_repartit_son_paiement_entre_les_quatre_modes()
    {
        // RG35 : espèces, carte, Mobile Money et le reste à crédit sur le même ticket.
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 10000m);

        var vente = await VendreAsync(
            b,
            [(b.Unite, 4m)],
            [Paiement("Especes", 800), Paiement("Carte", 300), Paiement("MobileMoney", 400, "MM-REPARTI"), Paiement("Credit", 500)],
            client);
        var fiche = await b.Gerant.GetFromJsonAsync<FicheClient>($"/api/credit/clients/{client}", Annulation);

        Assert.Equal(2000m, vente.TotalTtc);
        Assert.Equal(
            ["Carte 300", "Credit 500", "Especes 800", "MobileMoney 400"],
            vente.Paiements.Select(p => $"{p.Mode} {p.Montant:0}").Order());
        Assert.Equal(500m, fiche!.Client.SoldeDu);
    }

    // --- Sessions ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task La_cloture_calcule_l_ecart_que_le_comptable_valide()
    {
        var b = await BoutiqueAsync(fond: 10000m);
        var client = await ClientAsync(b, plafond: 10000m);
        var vente = await VendreAsync(b, [(b.Unite, 2m), (b.Carton, 1m)], Especes(10000)); // +6 500 en espèces
        await VendreAsync(b, [(b.Carton, 1m)], [Paiement("Credit", 5500)], client);
        await FaireAvoirAsync(b, vente.Id, (vente.Lignes[0].Id, 1m)); // -500 en espèces
        (await b.Caissier.PostAsJsonAsync("/api/caisse/remboursements", new { idIdempotence = Guid.NewGuid(), clientId = client, mode = "Especes", montant = 1000 }, Annulation))
            .EnsureSuccessStatusCode(); // +1 000 en espèces
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);

        var avantCloture = await b.Caissier.GetFromJsonAsync<Session>("/api/caisse/sessions/courante", Annulation);
        var validationPrematuree = await comptable.PostAsync($"/api/caisse/sessions/{avantCloture!.Id}/validation", null, Annulation);
        var cloture = await Lire<Session>(await b.Caissier.PostAsJsonAsync("/api/caisse/sessions/courante/cloture", new { especesComptees = 16900 }, Annulation));
        var parLeCaissier = await b.Caissier.PostAsync($"/api/caisse/sessions/{cloture.Id}/validation", null, Annulation);
        var validee = await Lire<Session>(await comptable.PostAsync($"/api/caisse/sessions/{cloture.Id}/validation", null, Annulation));
        var apres = await b.Caissier.GetAsync("/api/caisse/sessions/courante", Annulation);

        Assert.Equal(17000m, avantCloture.Totaux.EspecesTheoriques);
        Assert.Equal(5500m, avantCloture.Totaux.Credit);
        Assert.Equal(1, avantCloture.Totaux.NombreAvoirs);
        Assert.Equal(HttpStatusCode.Conflict, validationPrematuree.StatusCode);
        Assert.Equal(("Comptee", -100m), (cloture.Statut, cloture.Ecart));
        Assert.Equal(HttpStatusCode.Forbidden, parLeCaissier.StatusCode);
        Assert.Equal("Validee", validee.Statut);
        Assert.Equal(HttpStatusCode.NoContent, apres.StatusCode);
    }

    [Fact]
    public async Task Un_caissier_n_ouvre_qu_une_session_et_un_poste_n_en_porte_qu_une()
    {
        var b = await BoutiqueAsync();
        using var autreCaissier = await api.ClientAvecRoleAsync(Roles.Caissier);

        var deuxieme = await b.Caissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = ApiDeTest.ProchainPoste(), fondCaisse = 0 }, Annulation);
        var posteOccupe = await autreCaissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = b.Poste, fondCaisse = 0 }, Annulation);
        var posteInconnu = await autreCaissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = "INCONNU", fondCaisse = 0 }, Annulation);

        Assert.Equal("Caisse.SessionDejaOuverte", (await IdentiteTests.LireProblemeAsync(deuxieme)).Code);
        Assert.Equal("Caisse.PosteOccupe", (await IdentiteTests.LireProblemeAsync(posteOccupe)).Code);
        Assert.Equal("Caisse.PosteInconnu", (await IdentiteTests.LireProblemeAsync(posteInconnu)).Code);
    }

    // --- Reçu ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Le_recu_PDF_est_genere_dans_la_langue_de_l_utilisateur()
    {
        var b = await BoutiqueAsync();
        var ticket = await VendreAsync(b, [(b.Carton, 1m)], Especes(6000));
        b.Caissier.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var reponse = await b.Caissier.GetAsync($"/api/caisse/tickets/{ticket.Id}/recu.pdf", Annulation);
        var pdf = Encoding.Latin1.GetString(await reponse.Content.ReadAsByteArrayAsync(Annulation));

        Assert.Equal("application/pdf", reponse.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
        Assert.Contains(ticket.Numero, pdf, StringComparison.Ordinal);
        Assert.Contains("Change", pdf, StringComparison.Ordinal);
        Assert.Contains("Net prices", pdf, StringComparison.Ordinal);
    }

    // --- Outillage -----------------------------------------------------------------------------------------------

    /// <summary>Boutique de test : article à 500 l'unité et 5 500 le carton de 12, caissier en session sur son poste.</summary>
    private async Task<Boutique> BoutiqueAsync(bool ouvrirSession = true, decimal fond = 0m)
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 12);
        var unite = (await ConditionnementsTests.ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);
        await PrixTests.FixerAsync(gerant, unite.Id, 500m);
        await PrixTests.FixerAsync(gerant, carton.Id, 5500m);
        var poste = ApiDeTest.ProchainPoste();
        if (ouvrirSession)
        {
            (await caissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = poste, fondCaisse = fond }, Annulation)).EnsureSuccessStatusCode();
        }

        return new Boutique(gerant, caissier, await IdentifiantAsync(gerant), await IdentifiantAsync(caissier), article.Id, unite.Id, carton.Id, poste);
    }

    private async Task<Guid> ClientAsync(Boutique b, decimal plafond)
    {
        var reponse = await b.Gerant.PostAsJsonAsync("/api/credit/clients", new { nom = $"Client {Guid.NewGuid():N}"[..16], telephone = "699112233" }, Annulation);
        var client = (await Lire<ClientCredit>(reponse)).Id;
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        (await comptable.PutAsJsonAsync($"/api/credit/clients/{client}/plafond", new { plafondCredit = plafond }, Annulation)).EnsureSuccessStatusCode();
        return client;
    }

    private static object Demande(
        IEnumerable<(Guid Conditionnement, decimal Quantite)> lignes,
        IEnumerable<object> paiements,
        Guid? clientId = null,
        (string Identifiant, string MotDePasse)? validation = null) =>
        Demande(lignes.Select(l => (l.Conditionnement, l.Quantite, 0m)), paiements, clientId, validation);

    private static object Demande(
        IEnumerable<(Guid Conditionnement, decimal Quantite, decimal Remise)> lignes,
        IEnumerable<object> paiements,
        Guid? clientId = null,
        (string Identifiant, string MotDePasse)? validation = null) => new
        {
            idIdempotence = Guid.NewGuid(),
            clientId,
            lignes = lignes.Select(l => new { conditionnementId = l.Conditionnement, quantite = l.Quantite, remise = l.Remise }),
            paiements,
            validation = validation is { } v ? new { identifiant = v.Identifiant, motDePasse = v.MotDePasse } : null,
        };

    private static object DemandeAvoir(Guid ticket, params (Guid Ligne, decimal Quantite)[] lignes) => new
    {
        idIdempotence = Guid.NewGuid(),
        ticketOrigineId = ticket,
        lignes = lignes.Select(l => new { ligneOrigineId = l.Ligne, quantite = l.Quantite }),
    };

    private static object[] Especes(decimal montant) => [Paiement("Especes", montant)];

    private static object Paiement(string mode, decimal montant, string? reference = null) => new { mode, montant, reference };

    private static async Task<Ticket> VendreAsync(
        Boutique b,
        IEnumerable<(Guid, decimal)> lignes,
        IEnumerable<object> paiements,
        Guid? client = null,
        (string, string)? validation = null)
    {
        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", Demande(lignes, paiements, client, validation), Annulation);
        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<Ticket>(reponse);
    }

    private static async Task<Ticket> FaireAvoirAsync(Boutique b, Guid ticket, params (Guid, decimal)[] lignes)
    {
        var reponse = await b.Caissier.PostAsJsonAsync("/api/caisse/avoirs", DemandeAvoir(ticket, lignes), Annulation);
        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Annulation));
        return await Lire<Ticket>(reponse);
    }

    private static async Task<string> IdentifiantAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;

    private static async Task<T> Lire<T>(HttpResponseMessage reponse) => (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;

    private sealed record Boutique(HttpClient Gerant, HttpClient Caissier, string IdentifiantGerant, string IdentifiantCaissier, Guid ArticleId, Guid Unite, Guid Carton, string Poste);

    private sealed record Profil(string Identifiant);

    private sealed record PaiementLu(string Mode, decimal Montant, string? Reference);

    private sealed record LigneTicket(Guid Id, decimal Quantite, decimal QuantiteRendue, decimal MontantTtc);

    private sealed record Ticket(
        Guid Id,
        string Numero,
        bool EstAvoir,
        Guid? TicketOrigineId,
        decimal TotalTtc,
        decimal TauxTva,
        decimal Rendu,
        string? RemiseValideePar,
        string? CreditValidePar,
        List<LigneTicket> Lignes,
        List<PaiementLu> Paiements);

    private sealed record Totaux(int NombreAvoirs, decimal Credit, decimal EspecesTheoriques);

    private sealed record Session(Guid Id, string Statut, decimal? Ecart, Totaux Totaux);

    private sealed record Mouvement(string Type, decimal Variation, Guid DocumentId);

    private sealed record Solde(Guid ArticleId, decimal QuantitePhysique);

    private sealed record ClientCredit(Guid Id, decimal SoldeDu);

    private sealed record FicheClient(ClientCredit Client);

    private sealed record Remboursement(decimal SoldeDuApres);
}
