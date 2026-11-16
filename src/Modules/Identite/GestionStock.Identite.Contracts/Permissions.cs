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

    /// <summary>Consulter les prix de vente et le régime de TVA.</summary>
    public const string PrixConsulter = "Prix.Consulter";

    /// <summary>Fixer les prix de vente (UC25).</summary>
    public const string PrixGerer = "Prix.Gerer";

    /// <summary>Consulter les disponibilités et les mouvements (UC14).</summary>
    public const string StockConsulter = "Stock.Consulter";
}
