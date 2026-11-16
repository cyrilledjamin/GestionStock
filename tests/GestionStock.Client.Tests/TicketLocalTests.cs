using System.Text.Json;
using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Ticket fait hors ligne : mêmes montants que le serveur, numéro du poste, plafond de crédit hors ligne.</summary>
public sealed class TicketLocalTests
{
    private static readonly ArticleVendable Lait = new(Guid.NewGuid(), "LAIT-397", "Lait concentré", null, "boîte", 0, Guid.NewGuid(), "boîte", 1m, "6170000000011");
    private static readonly ArticleVendable Carton = new(Lait.ArticleId, "LAIT-397", "Lait concentré", null, "boîte", 0, Guid.NewGuid(), "Carton de 48", 48m, null);

    [Fact]
    public void Le_numero_imprime_suit_le_format_du_poste_et_se_relit()
    {
        Assert.Equal("CAISSE-1-000042", TicketLocal.Numero("CAISSE-1", 42));
        Assert.Equal(42, TicketLocal.Rang("CAISSE-1-000042"));
        Assert.Equal(0, TicketLocal.Rang(null));
    }

    [Fact]
    public void Une_vente_locale_a_les_montants_du_serveur_et_le_rendu_en_especes()
    {
        var vente = new VenteEnCours();
        vente.Ajouter(Lait, 450m).Quantite = 2;
        vente.Ajouter(Carton, 20_000m);
        vente.AjouterEspeces(25_000m);
        var id = Guid.NewGuid();

        var ticket = TicketLocal.Vente(vente, id, "CAISSE-1-000007", DateTime.UtcNow, "caissier", "Paul Etoa (caissier)", null, 19.25m);

        Assert.Equal((id, id, "CAISSE-1-000007"), (ticket.Id, ticket.IdIdempotence, ticket.Numero));
        Assert.Equal(20_900m, ticket.TotalTtc);
        Assert.Equal(17_526.2055m, ticket.TotalHt); // 20 900 / 1,1925 arrondi à 4 décimales, comme le serveur
        Assert.Equal(ticket.TotalTtc - ticket.TotalHt, ticket.TotalTva);
        Assert.Equal(4_100m, ticket.Rendu);
        Assert.Equal(["Lait concentré", "Lait concentré (Carton de 48)"], ticket.Lignes.Select(l => l.Libelle));
        Assert.Equal([1, 2], ticket.Lignes.Select(l => l.Ordre));
    }

    [Fact]
    public void Un_avoir_local_reprend_le_prix_d_origine_et_rend_la_remise_au_prorata()
    {
        var origine = new TicketCaisse(Guid.NewGuid(), Guid.NewGuid(), "CAISSE-1-000003", DateTime.UtcNow, "caissier", "Paul Etoa (caissier)", false, null, null,
            1_300m, 1_300m, 0m, 0m, 0m, "gerant", null,
            [new LigneTicket(Guid.NewGuid(), 1, Lait.ArticleId, Lait.ConditionnementId, "Lait concentré", "boîte", 3m, 0m, 450m, 50m, 1_300m)],
            [new PaiementTicket(ModesPaiement.Especes, 1_300m, null)]);

        var avoir = TicketLocal.Avoir(origine, new Dictionary<int, decimal> { [1] = 1m }, Guid.NewGuid(), "CAISSE-1-000004", DateTime.UtcNow, "caissier", "Paul Etoa (caissier)", ModesPaiement.Especes, null);

        Assert.True(avoir.EstAvoir);
        Assert.Equal(origine.Id, avoir.TicketOrigineId);
        Assert.Equal(433m, avoir.TotalTtc); // 450 moins 17 (50 × 1/3 arrondi)
        Assert.Equal([new PaiementTicket(ModesPaiement.Especes, 433m, null)], avoir.Paiements);
    }

    [Fact]
    public void Le_plafond_hors_ligne_compte_le_credit_deja_en_file_et_les_remboursements()
    {
        var client = new ClientCredit(Guid.NewGuid(), "Mme Ngo Mballa", "699112233", 30_000m, 30, 20_000m, 0m, true);
        var vide = JsonSerializer.SerializeToElement(new { });
        OperationEnAttente Op(decimal credit) => new("1", OperationEnAttente.Vente, Guid.NewGuid(), "caissier", Guid.NewGuid(), "api", vide, client.Id, credit, "", DateTime.UtcNow);

        Assert.Equal(10_000m, PlafondHorsLigne.Disponible(client, []));
        Assert.Equal(4_000m, PlafondHorsLigne.Disponible(client, [Op(6_000m)]));
        Assert.Equal(9_000m, PlafondHorsLigne.Disponible(client, [Op(6_000m), Op(-5_000m)]));
        Assert.Equal(0m, PlafondHorsLigne.Disponible(client, [Op(15_000m)]));
    }

    [Fact]
    public void Un_ticket_paye_a_credit_exige_le_serveur_pour_son_avoir()
    {
        var ticket = new TicketCaisse(Guid.NewGuid(), Guid.NewGuid(), "CAISSE-1-000005", DateTime.UtcNow, "caissier", "", false, null, Guid.NewGuid(),
            450m, 450m, 0m, 0m, 0m, null, null, [], [new PaiementTicket(ModesPaiement.Credit, 450m, null)]);

        Assert.True(TicketLocal.AUnePartACredit(ticket));
    }
}
