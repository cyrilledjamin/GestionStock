namespace GestionStock.Client.Services;

// Modèles échangés avec l'API (JSON). Le client ne référence aucun projet serveur : ils sont recopiés ici.

public sealed record JetonAcces(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

public sealed record ProfilUtilisateur(string Identifiant, List<string> Roles, List<string> Permissions);

public sealed record CompteUtilisateur(Guid Id, string Identifiant, List<string> Roles);

public sealed record Article(Guid Id, string Code, string Libelle, string? LibelleTraduit, string UniteStock, int PrecisionQuantite, bool Actif);

public sealed record SiteBoutique(Guid SiteId, string Code, string Nom, Guid EmplacementVenteId);

public sealed record Fournisseur(Guid Id, string Nom, string? Telephone, bool Actif);

public sealed record ResumeReception(
    Guid Id,
    string Numero,
    Guid FournisseurId,
    string Fournisseur,
    string? NumeroBonLivraison,
    string Statut,
    DateTime DateCreationUtc,
    DateTime? DateValidationUtc,
    int NombreLignes,
    decimal Valeur);

public sealed record DetailReception(
    Guid Id,
    string Numero,
    Guid FournisseurId,
    string Fournisseur,
    string? NumeroBonLivraison,
    string Statut,
    DateTime DateCreationUtc,
    string CreePar,
    DateTime? DateValidationUtc,
    string? ValideePar,
    decimal Valeur,
    List<DetailLigneReception> Lignes);

public sealed record DetailLigneReception(
    Guid Id,
    int Ordre,
    Guid ArticleId,
    string CodeArticle,
    string LibelleArticle,
    string UniteStock,
    decimal Quantite,
    decimal CoutUnitaire,
    decimal Valeur);

public sealed record SoldeStock(
    Guid ArticleId,
    Guid SiteId,
    decimal QuantitePhysique,
    decimal QuantiteReservee,
    decimal QuantiteBloquee,
    decimal QuantiteDisponible,
    decimal Cump);

public sealed record MouvementStock(
    long Id,
    DateTime DateUtc,
    string Type,
    decimal Quantite,
    decimal Variation,
    decimal CoutUnitaire,
    string TypeDocument,
    Guid DocumentId,
    string Utilisateur);

/// <summary>Statuts de réception renvoyés par l'API.</summary>
public static class StatutsReception
{
    public const string Brouillon = "Brouillon";
    public const string Validee = "Validee";
}

/// <summary>Rôles de la V1 (mêmes noms que l'API).</summary>
public static class Roles
{
    public static IReadOnlyList<string> Tous { get; } = ["Administrateur", "Gerant", "Comptable", "Caissier", "Magasinier"];
}

/// <summary>Permissions de l'API (mêmes valeurs que <c>GestionStock.Identite.Contracts.Permissions</c>, vérifié par les tests).</summary>
public static class Permissions
{
    public const string UtilisateursGerer = "Identite.Utilisateurs.Gerer";
    public const string ReferentielConsulter = "Referentiel.Consulter";
    public const string ArticlesGerer = "Referentiel.Articles.Gerer";
    public const string AchatsConsulter = "Achats.Consulter";
    public const string FournisseursGerer = "Achats.Fournisseurs.Gerer";
    public const string ReceptionsSaisir = "Achats.Receptions.Saisir";
    public const string ReceptionsValider = "Achats.Receptions.Valider";
    public const string FraisSaisir = "Achats.Frais.Saisir";
    public const string PaiementsFournisseursSaisir = "Achats.Paiements.Saisir";
    public const string RapportsConsulter = "Rapports.Consulter";
    public const string ExportsProduire = "Rapports.Exports.Produire";
    public const string StockConsulter = "Stock.Consulter";
    public const string PrixConsulter = "Prix.Consulter";
    public const string PrixGerer = "Prix.Gerer";
    public const string DepassementsValider = "Caisse.Depassements.Valider";
    public const string CreditConsulter = "Credit.Consulter";
    public const string ClientsGerer = "Credit.Clients.Gerer";
    public const string PlafondsGerer = "Credit.Plafonds.Gerer";
    public const string CaisseVendre = "Caisse.Vendre";
    public const string TicketsConsulter = "Caisse.Tickets.Consulter";
    public const string SessionsConsulter = "Caisse.Sessions.Consulter";
    public const string SessionsValider = "Caisse.Sessions.Valider";
    public const string InventaireConsulter = "Inventaire.Consulter";
    public const string InventaireCompter = "Inventaire.Compter";
    public const string InventaireValider = "Inventaire.Valider";
    public const string DemarqueDeclarer = "Inventaire.Demarque.Declarer";
    public const string DemarqueValider = "Inventaire.Demarque.Valider";
    public const string RepriseImporter = "Reprise.Importer";
}

// --- Conditionnements, codes, prix -------------------------------------------------------------------------------

public sealed record CodeArticle(Guid Id, string Valeur, string Type, bool Principal, bool Actif);

public sealed record Conditionnement(Guid Id, Guid ArticleId, string Libelle, string Niveau, decimal QuantiteUniteStock, bool Actif, List<CodeArticle> Codes);

public sealed record ArticleVendable(
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int PrecisionQuantite,
    Guid ConditionnementId,
    string LibelleConditionnement,
    decimal QuantiteUniteStock,
    string? CodeScanne);

public sealed record PrixConditionnement(
    Guid ConditionnementId,
    string Libelle,
    decimal QuantiteUniteStock,
    bool Actif,
    decimal? PrixTtc,
    DateOnly? DebutValidite,
    decimal? ProchainPrixTtc,
    DateOnly? ProchaineValidite);

public sealed record PrixApplicable(Guid ConditionnementId, decimal PrixTtc, DateOnly DebutValidite);

public sealed record RegimeTva(bool Assujetti, decimal TauxPourcent, decimal TauxApplique);

// --- Crédit ------------------------------------------------------------------------------------------------------

public sealed record ClientCredit(Guid Id, string Nom, string Telephone, decimal PlafondCredit, int DelaiPaiementJours, decimal SoldeDu, decimal MontantEnRetard, bool Actif);

public sealed record VenteOuverte(Guid OperationId, Guid TicketId, string? NumeroTicket, DateTime DateUtc, DateTime EcheanceUtc, decimal Montant, decimal ResteDu, bool EnRetard);

public sealed record OperationCredit(Guid Id, DateTime DateUtc, string Type, decimal Montant, Guid DocumentId, string? NumeroTicket, string Utilisateur, string? ValideePar);

public sealed record FicheClient(ClientCredit Client, List<VenteOuverte> VentesOuvertes, List<OperationCredit> Operations);

// --- Caisse ------------------------------------------------------------------------------------------------------

public sealed record TotauxSession(
    int NombreVentes,
    int NombreAvoirs,
    decimal Ventes,
    decimal Avoirs,
    decimal Especes,
    decimal MobileMoney,
    decimal Carte,
    decimal Credit,
    decimal RemboursementsCredit,
    decimal EspecesTheoriques);

public sealed record SessionCaisse(
    Guid Id,
    string NumeroCaisse,
    string Caissier,
    DateTime OuvertureUtc,
    decimal FondCaisse,
    string Statut,
    DateTime? ClotureUtc,
    decimal? EspecesComptees,
    decimal? Ecart,
    string? ValideePar,
    TotauxSession Totaux);

public sealed record LigneTicket(
    Guid Id,
    int Ordre,
    Guid ArticleId,
    Guid ConditionnementId,
    string Libelle,
    string LibelleConditionnement,
    decimal Quantite,
    decimal QuantiteRendue,
    decimal PrixUnitaire,
    decimal Remise,
    decimal MontantTtc);

public sealed record PaiementTicket(string Mode, decimal Montant, string? Reference);

public sealed record TicketCaisse(
    Guid Id,
    Guid IdIdempotence,
    string Numero,
    DateTime DateUtc,
    string Caissier,
    bool EstAvoir,
    Guid? TicketOrigineId,
    Guid? ClientId,
    decimal TotalTtc,
    decimal TotalHt,
    decimal TotalTva,
    decimal TauxTva,
    decimal Rendu,
    string? RemiseValideePar,
    string? CreditValidePar,
    List<LigneTicket> Lignes,
    List<PaiementTicket> Paiements);

public sealed record RemboursementCredit(Guid Id, Guid ClientId, string Mode, decimal Montant, string? Reference, DateTime DateUtc, decimal SoldeDuApres);

/// <summary>Modes de paiement (mêmes noms que l'API).</summary>
public static class ModesPaiement
{
    public const string Especes = "Especes";
    public const string MobileMoney = "MobileMoney";
    public const string Carte = "Carte";
    public const string Credit = "Credit";

    public static IReadOnlyList<string> Tous { get; } = [Especes, MobileMoney, Carte, Credit];
}

public sealed record EnteteRecu(string Nom, string? Adresse, string? Telephone, string? NumeroContribuable, bool AssujettiTva);

// --- Inventaire, démarque, reprise -------------------------------------------------------------------------------

public sealed record ResumeInventaire(
    Guid Id,
    string Numero,
    string Type,
    string Statut,
    DateTime CreeLeUtc,
    string CreePar,
    DateTime? TermineLeUtc,
    string? TerminePar,
    int NombreLignes,
    int NombreComptees);

public sealed record LigneInventaire(
    Guid Id,
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string UniteStock,
    int PrecisionQuantite,
    decimal? QuantiteTheorique,
    decimal? QuantiteComptee,
    decimal? Ecart,
    string MotifEcart,
    decimal? CoutOuverture,
    decimal CoutUnitaire,
    decimal? ValeurEcart,
    DateTime? ComptageUtc,
    string? ComptePar);

public sealed record DetailInventaire(ResumeInventaire Inventaire, decimal ValeurEcarts, List<LigneInventaire> Lignes);

public sealed record DeclarationDemarque(
    Guid Id,
    string Numero,
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string UniteStock,
    decimal Quantite,
    string Motif,
    string? Commentaire,
    string Statut,
    DateTime DeclareeLeUtc,
    string DeclareePar,
    DateTime? TraiteeLeUtc,
    string? TraiteePar,
    string? MotifRefus);

public sealed record ErreurReprise(int Ligne, string Code, string Message, string? Colonne, string? Detail);

public sealed record RapportReprise(
    bool Importe,
    int Lignes,
    int Articles,
    int Conditionnements,
    int CodesBarres,
    int Prix,
    List<ErreurReprise> Erreurs,
    Guid? InventaireOuvertureId,
    string? InventaireOuvertureNumero);

/// <summary>Motifs de démarque (mêmes noms que l'API).</summary>
public static class MotifsDemarque
{
    public static IReadOnlyList<string> Tous { get; } = ["Casse", "Vol", "Perime", "Freinte"];
}
