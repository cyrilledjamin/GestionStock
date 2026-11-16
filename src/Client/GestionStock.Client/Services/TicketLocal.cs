using System.Globalization;
using System.Text.Json;

namespace GestionStock.Client.Services;

/// <summary>
/// Opération faite hors ligne, en attente d'envoi (7.8) : l'appel à rejouer tel quel (même identifiant d'idempotence,
/// donc sans doublon), son caissier et sa session, et de quoi l'afficher et contrôler le crédit en attendant.
/// </summary>
public sealed record OperationEnAttente(
    string Cle,
    string Type,
    Guid IdIdempotence,
    string Caissier,
    Guid SessionId,
    string Url,
    JsonElement Corps,
    Guid? ClientId,
    decimal MontantCredit,
    string Resume,
    DateTime DateUtc,
    string? Erreur = null)
{
    public const string Vente = "Vente";
    public const string Avoir = "Avoir";
    public const string Remboursement = "Remboursement";
}

/// <summary>
/// Ticket fait hors ligne, construit sur le poste avec les calculs du serveur (montant de ligne arrondi au franc, HT et
/// TVA d'après le régime, rendu) : le reçu imprimé est celui que le serveur enregistrera (7.8, RG34, RG37).
/// </summary>
public static class TicketLocal
{
    /// <summary>Numéro imprimé d'un ticket du poste (RG34), ex. « CAISSE-1-000042 ».</summary>
    public static string Numero(string poste, long numero) => $"{poste}-{numero.ToString("D6", CultureInfo.InvariantCulture)}";

    /// <summary>Rang du ticket dans la suite de son poste, lu dans son numéro imprimé ; 0 s'il n'y en a pas.</summary>
    public static long Rang(string? numeroComplet)
    {
        var tiret = numeroComplet?.LastIndexOf('-') ?? -1;
        return tiret >= 0 && long.TryParse(numeroComplet![(tiret + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var rang) ? rang : 0;
    }

    public static TicketCaisse Vente(
        VenteEnCours vente,
        Guid idIdempotence,
        string numero,
        DateTime dateUtc,
        string caissier,
        string designationCaissier,
        Guid? clientId,
        decimal tauxTva)
    {
        ArgumentNullException.ThrowIfNull(vente);
        var lignes = vente.Lignes.Select((l, i) => new LigneTicket(
            Guid.NewGuid(),
            i + 1,
            l.Vendable.ArticleId,
            l.Vendable.ConditionnementId,
            l.Vendable.QuantiteUniteStock == 1m ? l.Vendable.Libelle : $"{l.Vendable.Libelle} ({l.Vendable.LibelleConditionnement})",
            l.Vendable.LibelleConditionnement,
            l.Quantite,
            0m,
            l.Prix,
            l.Remise,
            l.Montant)).ToList();
        var paiements = vente.Paiements.Where(p => p.Montant > 0).Select(p => new PaiementTicket(p.Mode, p.Montant, p.Reference)).ToList();
        return Ticket(idIdempotence, numero, dateUtc, caissier, designationCaissier, estAvoir: false, origine: null, clientId, lignes, paiements, tauxTva);
    }

    /// <summary>Avoir sur un ticket du poste : lignes reprises au prix d'origine, remise rendue au prorata, remboursement dans le mode choisi.</summary>
    public static TicketCaisse Avoir(
        TicketCaisse origine,
        IReadOnlyDictionary<int, decimal> quantitesParRang,
        Guid idIdempotence,
        string numero,
        DateTime dateUtc,
        string caissier,
        string designationCaissier,
        string modeRemboursement,
        string? reference)
    {
        ArgumentNullException.ThrowIfNull(origine);
        ArgumentNullException.ThrowIfNull(quantitesParRang);
        var lignes = origine.Lignes
            .Where(l => quantitesParRang.GetValueOrDefault(l.Ordre) > 0)
            .Select((l, i) =>
            {
                var quantite = quantitesParRang[l.Ordre];
                var remise = decimal.Round(l.Remise * quantite / l.Quantite, 0, MidpointRounding.AwayFromZero);
                return new LigneTicket(Guid.NewGuid(), i + 1, l.ArticleId, l.ConditionnementId, l.Libelle, l.LibelleConditionnement, quantite, 0m, l.PrixUnitaire, remise,
                    decimal.Round(quantite * l.PrixUnitaire, 0, MidpointRounding.AwayFromZero) - remise);
            })
            .ToList();
        var total = lignes.Sum(l => l.MontantTtc);
        return Ticket(idIdempotence, numero, dateUtc, caissier, designationCaissier, estAvoir: true, origine.Id, origine.ClientId, lignes,
            [new PaiementTicket(modeRemboursement, total, reference)], origine.TauxTva);
    }

    /// <summary>Le ticket a-t-il une part payée à crédit ? Son avoir exige alors le serveur (dette à annuler).</summary>
    public static bool AUnePartACredit(TicketCaisse ticket) =>
        ticket?.Paiements.Any(p => p.Mode == ModesPaiement.Credit) == true;

    private static TicketCaisse Ticket(
        Guid idIdempotence,
        string numero,
        DateTime dateUtc,
        string caissier,
        string designation,
        bool estAvoir,
        Guid? origine,
        Guid? clientId,
        List<LigneTicket> lignes,
        List<PaiementTicket> paiements,
        decimal tauxTva)
    {
        var total = lignes.Sum(l => l.MontantTtc);
        var ht = decimal.Round(total / (1m + (tauxTva / 100m)), 4, MidpointRounding.AwayFromZero);
        var rendu = estAvoir ? 0m : Math.Max(0m, paiements.Sum(p => p.Montant) - total);

        // Identifiant local = identifiant d'idempotence, remplacé par celui du serveur à la synchronisation.
        return new TicketCaisse(idIdempotence, idIdempotence, numero, dateUtc, caissier, designation, estAvoir, origine, clientId,
            total, ht, total - ht, tauxTva, rendu, null, null, lignes, paiements);
    }
}

/// <summary>Contrôle du plafond de crédit hors ligne (7.8, décision 39) : dernier solde connu, plus le crédit déjà en attente.</summary>
public static class PlafondHorsLigne
{
    /// <summary>Montant encore autorisé à crédit pour ce client sur le poste.</summary>
    public static decimal Disponible(ClientCredit client, IEnumerable<OperationEnAttente> enAttente)
    {
        ArgumentNullException.ThrowIfNull(client);
        var enCours = enAttente.Where(o => o.ClientId == client.Id).Sum(o => o.MontantCredit);
        return Math.Max(0m, client.PlafondCredit - client.SoldeDu - enCours);
    }
}
