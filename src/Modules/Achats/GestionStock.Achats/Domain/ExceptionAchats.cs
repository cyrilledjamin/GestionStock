namespace GestionStock.Achats.Domain;

/// <summary>Règle du module Achats violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionAchats(string code, string? detail = null) : Exception(code)
{
    public string Code { get; } = code;

    /// <summary>Précision facultative (ex. code de l'article concerné), affichée telle quelle.</summary>
    public string? Detail { get; } = detail;
}

/// <summary>Codes d'erreur du module Achats.</summary>
internal static class CodesErreurAchats
{
    public const string NomObligatoire = "Achats.NomObligatoire";
    public const string NomTropLong = "Achats.NomTropLong";
    public const string TelephoneTropLong = "Achats.TelephoneTropLong";
    public const string FournisseurIntrouvable = "Achats.FournisseurIntrouvable";
    public const string FournisseurInactif = "Achats.FournisseurInactif";
    public const string ReceptionIntrouvable = "Achats.ReceptionIntrouvable";
    public const string ReceptionSansLigne = "Achats.ReceptionSansLigne";
    public const string ReceptionDejaValidee = "Achats.ReceptionDejaValidee";
    public const string BonLivraisonTropLong = "Achats.BonLivraisonTropLong";
    public const string QuantiteNonPositive = "Achats.QuantiteNonPositive";
    public const string PrecisionQuantite = "Achats.PrecisionQuantite";
    public const string CoutNegatif = "Achats.CoutNegatif";
    public const string PrecisionCout = "Achats.PrecisionCout";
    public const string ArticleIntrouvable = "Achats.ArticleIntrouvable";
    public const string ArticleInactif = "Achats.ArticleInactif";
    public const string MouvementRefuse = "Achats.MouvementRefuse";
}
