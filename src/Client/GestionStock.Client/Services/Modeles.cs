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
    public const string StockConsulter = "Stock.Consulter";
    public const string PrixConsulter = "Prix.Consulter";
    public const string PrixGerer = "Prix.Gerer";
}
