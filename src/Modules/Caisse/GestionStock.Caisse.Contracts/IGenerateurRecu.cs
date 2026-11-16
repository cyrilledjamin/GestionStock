namespace GestionStock.Caisse.Contracts;

/// <summary>
/// Génération du reçu d'un ticket, isolée derrière une interface (décision 17) : la facturation électronique DGI,
/// quand ses spécifications seront publiées, fournira sa propre implémentation sans toucher à la caisse.
/// </summary>
public interface IGenerateurRecu
{
    /// <summary>Reçu au format PDF (ticket 80 mm), dans la langue demandée (« fr » ou « en », ENF09).</summary>
    byte[] GenererPdf(Recu recu, string langue);
}

/// <summary>Données d'un reçu, figées au moment de la vente (RG34 : un ticket n'est jamais modifié).</summary>
public sealed record Recu(
    string Entreprise,
    string? Adresse,
    string? Telephone,
    string? NumeroContribuable,
    string NumeroTicket,
    DateTime DateUtc,
    string Caissier,
    bool EstAvoir,
    string? NumeroTicketOrigine,
    string? Client,
    IReadOnlyList<LigneRecu> Lignes,
    decimal TotalTtc,
    bool AssujettiTva,
    decimal TauxTva,
    decimal TotalHt,
    decimal TotalTva,
    IReadOnlyList<PaiementRecu> Paiements,
    decimal Rendu);

public sealed record LigneRecu(string Libelle, decimal Quantite, decimal PrixUnitaire, decimal Remise, decimal Montant);

/// <summary>Paiement imprimé : mode (Especes, MobileMoney, Carte, Credit), montant, référence éventuelle.</summary>
public sealed record PaiementRecu(string Mode, decimal Montant, string? Reference);

/// <summary>Types de documents de la Caisse dans le journal des mouvements de stock (décision 28).</summary>
public static class TypesDocumentsCaisse
{
    /// <summary>Ticket de vente ou d'avoir : <c>DocumentId</c> est l'identifiant du ticket.</summary>
    public static readonly string Ticket = "TicketCaisse";
}
