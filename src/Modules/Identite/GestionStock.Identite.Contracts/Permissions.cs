namespace GestionStock.Identite.Contracts;

/// <summary>
/// Permissions déclarées par les endpoints (<c>RequireAuthorization(Permissions.X)</c>).
/// Chaque permission est une politique d'autorisation ; le module Identite décide quels rôles la détiennent.
/// </summary>
public static class Permissions
{
    /// <summary>Créer et consulter les comptes utilisateurs.</summary>
    public const string UtilisateursGerer = "Identite.Utilisateurs.Gerer";

    /// <summary>Consulter les articles et les emplacements.</summary>
    public const string ReferentielConsulter = "Referentiel.Consulter";

    /// <summary>Créer et modifier les articles (UC01).</summary>
    public const string ArticlesGerer = "Referentiel.Articles.Gerer";

    /// <summary>Consulter fournisseurs et réceptions.</summary>
    public const string AchatsConsulter = "Achats.Consulter";

    /// <summary>Créer et modifier les fournisseurs.</summary>
    public const string FournisseursGerer = "Achats.Fournisseurs.Gerer";

    /// <summary>Saisir une réception (UC04).</summary>
    public const string ReceptionsSaisir = "Achats.Receptions.Saisir";

    /// <summary>Valider une réception : elle écrit les mouvements d'entrée en stock.</summary>
    public const string ReceptionsValider = "Achats.Receptions.Valider";

    /// <summary>Saisir les frais d'approche d'une réception (RG30), avant ou après sa validation.</summary>
    public const string FraisSaisir = "Achats.Frais.Saisir";

    /// <summary>Enregistrer ou annuler un paiement fournisseur (décision 27).</summary>
    public const string PaiementsFournisseursSaisir = "Achats.Paiements.Saisir";

    /// <summary>Consulter les rapports (décision 33) : ventes et marge, stock, créances et dettes, caisse.</summary>
    public const string RapportsConsulter = "Rapports.Consulter";

    /// <summary>Produire les exports comptables (UC16) : journaux et valeur du stock, en Excel ou CSV.</summary>
    public const string ExportsProduire = "Rapports.Exports.Produire";

    /// <summary>Consulter les prix de vente et le régime de TVA.</summary>
    public const string PrixConsulter = "Prix.Consulter";

    /// <summary>Fixer les prix de vente (UC25).</summary>
    public const string PrixGerer = "Prix.Gerer";

    /// <summary>
    /// Valider au poste du caissier une remise ou un dépassement de plafond de crédit (RG40, décision 31).
    /// </summary>
    public const string DepassementsValider = "Caisse.Depassements.Valider";

    /// <summary>Consulter les clients, leur dette et les impayés (RG43).</summary>
    public const string CreditConsulter = "Credit.Consulter";

    /// <summary>Créer et modifier les clients (UC26).</summary>
    public const string ClientsGerer = "Credit.Clients.Gerer";

    /// <summary>Fixer les plafonds de crédit (RG40).</summary>
    public const string PlafondsGerer = "Credit.Plafonds.Gerer";

    /// <summary>Ouvrir et clôturer sa session, vendre, faire un avoir, encaisser un remboursement (UC17, UC18, UC24, UC27).</summary>
    public const string CaisseVendre = "Caisse.Vendre";

    /// <summary>Consulter les tickets et imprimer un reçu.</summary>
    public const string TicketsConsulter = "Caisse.Tickets.Consulter";

    /// <summary>Consulter toutes les sessions de caisse.</summary>
    public const string SessionsConsulter = "Caisse.Sessions.Consulter";

    /// <summary>Valider la clôture d'une session et son écart (RG36).</summary>
    public const string SessionsValider = "Caisse.Sessions.Valider";

    /// <summary>Consulter les inventaires et les déclarations de démarque.</summary>
    public const string InventaireConsulter = "Inventaire.Consulter";

    /// <summary>Créer un inventaire et compter (UC11).</summary>
    public const string InventaireCompter = "Inventaire.Compter";

    /// <summary>Valider les écarts d'un inventaire (RG15).</summary>
    public const string InventaireValider = "Inventaire.Valider";

    /// <summary>Déclarer une démarque connue (UC20).</summary>
    public const string DemarqueDeclarer = "Inventaire.Demarque.Declarer";

    /// <summary>Valider ou refuser une démarque (UC20).</summary>
    public const string DemarqueValider = "Inventaire.Demarque.Valider";

    /// <summary>Importer les données de reprise (décision 29) : articles, codes, prix, inventaire d'ouverture.</summary>
    public const string RepriseImporter = "Reprise.Importer";

    /// <summary>Consulter les disponibilités et les mouvements (UC14).</summary>
    public const string StockConsulter = "Stock.Consulter";
}
