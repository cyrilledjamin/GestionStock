using System.Net;
using System.Net.Http.Json;
using GestionStock.Identite.Contracts;

namespace GestionStock.Api.IntegrationTests;

/// <summary>
/// Opérations faites sur un poste coupé du serveur (7.8, ENF08) et rejouées : sans doublon, avec le numéro (RG34), l'heure
/// et le prix du terrain ; chaque écart devient une anomalie, jamais un refus. Clôture gardée tant qu'il en manque (RG36).
/// </summary>
public sealed class HorsLigneTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Une_vente_rejouee_deux_fois_n_est_enregistree_qu_une_fois_avec_son_numero_et_son_heure()
    {
        var b = await BoutiqueAsync();
        var heure = HeureDansLaSession(b);
        var vente = DemandeHorsLigne(b, numero: 1, heure, [(b.Unite, 2m, 500m)], Especes(1000));

        var premiere = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", vente, Annulation));
        var renvoi = await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", vente, Annulation);
        var suivante = await VendreEnLigneAsync(b, [(b.Unite, 1m)], Especes(500));
        var journal = await b.Gerant.GetFromJsonAsync<List<Mouvement>>($"/api/stock/articles/{b.ArticleId}/mouvements", Annulation);

        Assert.Equal($"{b.Poste}-000001", premiere.Numero);
        Assert.Equal(heure, premiere.DateUtc, TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, renvoi.StatusCode); // déjà enregistré : renvoyé tel quel
        Assert.Equal(premiere.Id, (await Lire<Ticket>(renvoi)).Id);
        Assert.Equal($"{b.Poste}-000002", suivante.Numero); // le compteur du poste a suivi
        Assert.Single(journal!, m => m.DocumentId == premiere.Id);
        Assert.Empty(await AnomaliesAsync(b, premiere.Id));
    }

    [Fact]
    public async Task Un_numero_saute_est_garde_et_signale()
    {
        var b = await BoutiqueAsync();

        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 3, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation));

        Assert.Equal($"{b.Poste}-000003", ticket.Numero);
        var anomalie = Assert.Single(await AnomaliesAsync(b, ticket.Id));
        Assert.Equal(("NumeroManquant", "1-2"), (anomalie.Type, anomalie.Detail));
    }

    [Fact]
    public async Task Un_numero_deja_pris_recoit_le_numero_libre_suivant_et_est_signale()
    {
        var b = await BoutiqueAsync();
        await VendreEnLigneAsync(b, [(b.Unite, 1m)], Especes(500)); // n° 1

        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation));

        Assert.Equal($"{b.Poste}-000002", ticket.Numero);
        Assert.Equal("NumeroReattribue", Assert.Single(await AnomaliesAsync(b, ticket.Id)).Type);
    }

    [Fact]
    public async Task Le_prix_du_poste_fait_foi_et_un_ecart_avec_le_prix_du_jour_est_signale()
    {
        var b = await BoutiqueAsync();

        // Le poste vendait l'unité 450 (ancienne grille) ; le prix du jour est 500.
        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow, [(b.Unite, 2m, 450m)], Especes(900)), Annulation));

        Assert.Equal(900m, ticket.TotalTtc);
        var anomalie = Assert.Single(await AnomaliesAsync(b, ticket.Id));
        Assert.Equal("PrixDifferent", anomalie.Type);
        Assert.EndsWith(": 450 / 500", anomalie.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_vente_a_credit_au_dela_du_plafond_est_enregistree_et_signalee()
    {
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 5000m);

        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow, [(b.Carton, 1m, 5500m)], [Paiement("Credit", 5500)], client), Annulation));
        var fiche = await b.Gerant.GetFromJsonAsync<FicheClient>($"/api/credit/clients/{client}", Annulation);

        Assert.Null(ticket.CreditValidePar);
        Assert.Equal(5500m, fiche!.Client.SoldeDu);
        Assert.Equal("PlafondDepasse", Assert.Single(await AnomaliesAsync(b, ticket.Id)).Type);
    }

    [Fact]
    public async Task Une_vente_arrivee_apres_la_cloture_par_le_gerant_est_enregistree_et_signalee()
    {
        var b = await BoutiqueAsync();
        var session = (await b.Caissier.GetFromJsonAsync<Session>("/api/caisse/sessions/courante", Annulation))!;
        (await b.Gerant.PostAsJsonAsync($"/api/caisse/sessions/{session.Id}/cloture", new { especesComptees = 0 }, Annulation)).EnsureSuccessStatusCode();

        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500), sessionId: session.Id), Annulation));

        Assert.Equal($"{b.Poste}-000001", ticket.Numero);
        Assert.Equal("SessionCloturee", Assert.Single(await AnomaliesAsync(b, ticket.Id)).Type);
    }

    [Fact]
    public async Task Une_heure_du_poste_dans_le_futur_est_ramenee_a_l_heure_du_serveur_et_signalee()
    {
        var b = await BoutiqueAsync();

        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow.AddHours(3), [(b.Unite, 1m, 500m)], Especes(500)), Annulation));

        Assert.True(ticket.DateUtc <= DateTime.UtcNow.AddSeconds(5));
        Assert.Equal("DateCorrigee", Assert.Single(await AnomaliesAsync(b, ticket.Id)).Type);
    }

    [Fact]
    public async Task Un_avoir_hors_ligne_reprend_un_ticket_fait_hors_ligne_designe_par_son_idempotence()
    {
        var b = await BoutiqueAsync();
        var vente = DemandeHorsLigne(b, numero: 1, HeureDansLaSession(b), [(b.Unite, 3m, 500m)], Especes(1500));
        (await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", vente, Annulation)).EnsureSuccessStatusCode();

        var avoir = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/avoirs", new
        {
            idIdempotence = Guid.NewGuid(),
            ticketOrigineIdIdempotence = vente.IdIdempotence,
            lignes = new[] { new { ordreOrigine = 1, quantite = 1m } },
            horsLigne = new { sessionId = b.SessionId, numero = 2, dateUtc = DateTime.UtcNow },
        }, Annulation));

        Assert.True(avoir.EstAvoir);
        Assert.Equal(($"{b.Poste}-000002", 500m), (avoir.Numero, avoir.TotalTtc));
    }

    [Fact]
    public async Task Un_remboursement_de_credit_hors_ligne_garde_son_heure_et_n_est_compte_qu_une_fois()
    {
        var b = await BoutiqueAsync();
        var client = await ClientAsync(b, plafond: 10000m);
        await VendreEnLigneAsync(b, [(b.Carton, 1m)], [Paiement("Credit", 5500)], client);
        var heure = HeureDansLaSession(b);
        var remboursement = new
        {
            idIdempotence = Guid.NewGuid(),
            clientId = client,
            mode = "Especes",
            montant = 2000,
            horsLigne = new { sessionId = b.SessionId, numero = (long?)null, dateUtc = heure },
        };

        var premier = await Lire<Remboursement>(await b.Caissier.PostAsJsonAsync("/api/caisse/remboursements", remboursement, Annulation));
        (await b.Caissier.PostAsJsonAsync("/api/caisse/remboursements", remboursement, Annulation)).EnsureSuccessStatusCode();
        var fiche = await b.Gerant.GetFromJsonAsync<FicheClient>($"/api/credit/clients/{client}", Annulation);

        Assert.Equal(heure, premier.DateUtc, TimeSpan.FromSeconds(1));
        Assert.Equal(3500m, fiche!.Client.SoldeDu);
    }

    [Fact]
    public async Task La_cloture_attend_les_ventes_hors_ligne_que_le_poste_annonce()
    {
        var b = await BoutiqueAsync();
        await VendreEnLigneAsync(b, [(b.Unite, 1m)], Especes(500)); // n° 1, reçu
        // Le poste a numéroté le ticket 2 hors ligne, pas encore envoyé.

        var avant = await b.Caissier.PostAsJsonAsync("/api/caisse/sessions/courante/cloture", new { especesComptees = 1000, dernierNumero = 2 }, Annulation);
        (await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 2, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation)).EnsureSuccessStatusCode();
        var apres = await b.Caissier.PostAsJsonAsync("/api/caisse/sessions/courante/cloture", new { especesComptees = 1000, dernierNumero = 2 }, Annulation);

        Assert.Equal((HttpStatusCode.Conflict, "Caisse.VentesNonSynchronisees"), (avant.StatusCode, (await IdentiteTests.LireProblemeAsync(avant)).Code));
        Assert.Equal(HttpStatusCode.OK, apres.StatusCode);
    }

    [Fact]
    public async Task Les_ventes_d_un_caissier_ne_s_envoient_pas_sous_le_compte_d_un_autre()
    {
        var b = await BoutiqueAsync();
        using var autre = await api.ClientAvecRoleAsync(Roles.Caissier);

        var reponse = await autre.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 1, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation);

        Assert.Equal((HttpStatusCode.Forbidden, "Caisse.SessionAutreCaissier"), (reponse.StatusCode, (await IdentiteTests.LireProblemeAsync(reponse)).Code));
    }

    [Fact]
    public async Task Le_gerant_traite_une_anomalie_avec_un_commentaire_et_une_seule_fois()
    {
        var b = await BoutiqueAsync();
        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 5, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation));
        var anomalie = Assert.Single(await AnomaliesAsync(b, ticket.Id));
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);

        var sansCommentaire = await b.Gerant.PostAsJsonAsync($"/api/caisse/anomalies/{anomalie.Id}/traitement", new { commentaire = " " }, Annulation);
        var parLeComptable = await comptable.PostAsJsonAsync($"/api/caisse/anomalies/{anomalie.Id}/traitement", new { commentaire = "Vu" }, Annulation);
        var traitee = await Lire<Anomalie>(await b.Gerant.PostAsJsonAsync($"/api/caisse/anomalies/{anomalie.Id}/traitement", new { commentaire = "Tickets 1 à 4 annulés sur le poste avant la coupure" }, Annulation));
        var deuxFois = await b.Gerant.PostAsJsonAsync($"/api/caisse/anomalies/{anomalie.Id}/traitement", new { commentaire = "Encore" }, Annulation);

        Assert.Equal("Caisse.CommentaireObligatoire", (await IdentiteTests.LireProblemeAsync(sansCommentaire)).Code);
        Assert.Equal(HttpStatusCode.Forbidden, parLeComptable.StatusCode);
        Assert.Equal((true, b.IdentifiantGerant), (traitee.Traitee, traitee.TraiteePar));
        Assert.Equal(HttpStatusCode.Conflict, deuxFois.StatusCode);
    }

    [Fact]
    public async Task Une_anomalie_figure_dans_le_rapport_de_caisse_et_sur_l_accueil_tant_qu_elle_n_est_pas_traitee()
    {
        var b = await BoutiqueAsync();
        var ticket = await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", DemandeHorsLigne(b, numero: 4, DateTime.UtcNow, [(b.Unite, 1m, 500m)], Especes(500)), Annulation));
        var anomalie = Assert.Single(await AnomaliesAsync(b, ticket.Id));

        var rapport = await b.Gerant.GetFromJsonAsync<RapportCaisse>("/api/rapports/caisse", Annulation);
        var avant = await b.Gerant.GetFromJsonAsync<TableauDeBord>("/api/rapports/tableau-de-bord", Annulation);
        (await b.Gerant.PostAsJsonAsync($"/api/caisse/anomalies/{anomalie.Id}/traitement", new { commentaire = "Tickets 1 à 3 retrouvés sur papier" }, Annulation)).EnsureSuccessStatusCode();
        var relu = await b.Gerant.GetFromJsonAsync<RapportCaisse>("/api/rapports/caisse", Annulation);

        var dansLeRapport = Assert.Single(rapport!.Anomalies, a => a.AnomalieId == anomalie.Id);
        Assert.Equal(("NumeroManquant", ticket.Numero, false), (dansLeRapport.Type, dansLeRapport.NumeroDocument, dansLeRapport.Traitee));
        Assert.True(avant!.AnomaliesATraiter >= 1);
        Assert.True(Assert.Single(relu!.Anomalies, a => a.AnomalieId == anomalie.Id).Traitee);
    }

    // --- Outillage -----------------------------------------------------------------------------------------------

    /// <summary>Boutique de test : unité à 500, carton de 12 à 5 500, caissier en session sur son poste.</summary>
    private async Task<Boutique> BoutiqueAsync()
    {
        var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);
        var (article, carton) = await ConditionnementsTests.ArticleAvecCartonAsync(gerant, 12);
        var unite = (await ConditionnementsTests.ListerAsync(gerant, article.Id)).Single(c => c.QuantiteUniteStock == 1m);
        await PrixTests.FixerAsync(gerant, unite.Id, 500m);
        await PrixTests.FixerAsync(gerant, carton.Id, 5500m);
        var poste = ApiDeTest.ProchainPoste();
        var session = await Lire<Session>(await caissier.PostAsJsonAsync("/api/caisse/sessions", new { numeroCaisse = poste, fondCaisse = 0 }, Annulation));
        var identifiantGerant = (await gerant.GetFromJsonAsync<Profil>("/api/identite/moi", Annulation))!.Identifiant;
        return new Boutique(gerant, caissier, identifiantGerant, article.Id, unite.Id, carton.Id, poste, session.Id, session.OuvertureUtc);
    }

    private async Task<Guid> ClientAsync(Boutique b, decimal plafond)
    {
        var client = await Lire<Identifiant>(await b.Gerant.PostAsJsonAsync("/api/credit/clients", new { nom = $"Client {Guid.NewGuid():N}"[..16], telephone = "699112233" }, Annulation));
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        (await comptable.PutAsJsonAsync($"/api/credit/clients/{client.Id}/plafond", new { plafondCredit = plafond }, Annulation)).EnsureSuccessStatusCode();
        return client.Id;
    }

    private static DemandeTerrain DemandeHorsLigne(
        Boutique b,
        long numero,
        DateTime heureUtc,
        IEnumerable<(Guid Conditionnement, decimal Quantite, decimal Prix)> lignes,
        IEnumerable<object> paiements,
        Guid? clientId = null,
        Guid? sessionId = null) => new(
        Guid.NewGuid(),
        clientId,
        [.. lignes.Select(l => (object)new { conditionnementId = l.Conditionnement, quantite = l.Quantite, prixUnitaire = l.Prix })],
        [.. paiements],
        new { sessionId = sessionId ?? b.SessionId, numero, dateUtc = heureUtc });

    private static async Task<Ticket> VendreEnLigneAsync(Boutique b, IEnumerable<(Guid Conditionnement, decimal Quantite)> lignes, IEnumerable<object> paiements, Guid? clientId = null) =>
        await Lire<Ticket>(await b.Caissier.PostAsJsonAsync("/api/caisse/tickets", new
        {
            idIdempotence = Guid.NewGuid(),
            clientId,
            lignes = lignes.Select(l => new { conditionnementId = l.Conditionnement, quantite = l.Quantite }),
            paiements,
        }, Annulation));

    private static async Task<List<Anomalie>> AnomaliesAsync(Boutique b, Guid documentId) =>
        [.. (await b.Gerant.GetFromJsonAsync<List<Anomalie>>("/api/caisse/anomalies", Annulation))!.Where(a => a.DocumentId == documentId)];

    /// <summary>Heure passée mais postérieure à l'ouverture de la session : le serveur la garde telle quelle.</summary>
    private static DateTime HeureDansLaSession(Boutique b) => b.OuvertureUtc + ((DateTime.UtcNow - b.OuvertureUtc) / 2);

    private static object[] Especes(decimal montant) => [Paiement("Especes", montant)];

    private static object Paiement(string mode, decimal montant) => new { mode, montant };

    private static async Task<T> Lire<T>(HttpResponseMessage reponse)
    {
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync(Annulation));
        return (await reponse.Content.ReadFromJsonAsync<T>(Annulation))!;
    }

    private sealed record Boutique(HttpClient Gerant, HttpClient Caissier, string IdentifiantGerant, Guid ArticleId, Guid Unite, Guid Carton, string Poste, Guid SessionId, DateTime OuvertureUtc);

    private sealed record DemandeTerrain(Guid IdIdempotence, Guid? ClientId, object[] Lignes, object[] Paiements, object HorsLigne);

    private sealed record Profil(string Identifiant);

    private sealed record Identifiant(Guid Id);

    private sealed record Session(Guid Id, DateTime OuvertureUtc);

    private sealed record Ticket(Guid Id, string Numero, DateTime DateUtc, bool EstAvoir, decimal TotalTtc, string? CreditValidePar);

    private sealed record Remboursement(Guid Id, DateTime DateUtc, decimal SoldeDuApres);

    private sealed record Mouvement(Guid DocumentId);

    private sealed record ClientCredit(Guid Id, decimal SoldeDu);

    private sealed record FicheClient(ClientCredit Client);

    private sealed record Anomalie(Guid Id, string Type, Guid DocumentId, string Detail, bool Traitee, string? TraiteePar);

    private sealed record AnomalieRapport(Guid AnomalieId, string Type, string? NumeroDocument, bool Traitee);

    private sealed record RapportCaisse(List<AnomalieRapport> Anomalies);

    private sealed record TableauDeBord(int AnomaliesATraiter);
}
