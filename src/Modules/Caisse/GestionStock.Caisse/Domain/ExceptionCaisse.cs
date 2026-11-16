namespace GestionStock.Caisse.Domain;

/// <summary>Règle de la caisse violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionCaisse(string code, string? detail = null) : Exception(code)
{
    public string Code { get; } = code;

    /// <summary>Précision facultative (article, montant…), affichée telle quelle.</summary>
    public string? Detail { get; } = detail;
}

/// <summary>Codes d'erreur du module Caisse.</summary>
internal static class CodesErreurCaisse
{
    public const string PosteInconnu = "Caisse.PosteInconnu";
    public const string FondInvalide = "Caisse.FondInvalide";
    public const string SessionDejaOuverte = "Caisse.SessionDejaOuverte";
    public const string PosteOccupe = "Caisse.PosteOccupe";
    public const string AucuneSessionOuverte = "Caisse.AucuneSessionOuverte";
    public const string SessionIntrouvable = "Caisse.SessionIntrouvable";
    public const string SessionNonComptee = "Caisse.SessionNonComptee";
    public const string ComptageInvalide = "Caisse.ComptageInvalide";
    public const string TicketSansLigne = "Caisse.TicketSansLigne";
    public const string TicketIntrouvable = "Caisse.TicketIntrouvable";
    public const string IdempotenceObligatoire = "Caisse.IdempotenceObligatoire";
    public const string ConditionnementIntrouvable = "Caisse.ConditionnementIntrouvable";
    public const string ArticleInactif = "Caisse.ArticleInactif";
    public const string PrixAbsent = "Caisse.PrixAbsent";
    public const string QuantiteInvalide = "Caisse.QuantiteInvalide";
    public const string RemiseInvalide = "Caisse.RemiseInvalide";
    public const string RemiseNonValidee = "Caisse.RemiseNonValidee";
    public const string MontantNonEntier = "Caisse.MontantNonEntier";
    public const string PaiementManquant = "Caisse.PaiementManquant";
    public const string PaiementInsuffisant = "Caisse.PaiementInsuffisant";
    public const string RenduSansEspeces = "Caisse.RenduSansEspeces";
    public const string ReferenceObligatoire = "Caisse.ReferenceObligatoire";
    public const string ModeInvalide = "Caisse.ModeInvalide";
    public const string ClientObligatoire = "Caisse.ClientObligatoire";
    public const string ClientIntrouvable = "Caisse.ClientIntrouvable";
    public const string PlafondDepasse = "Caisse.PlafondDepasse";
    public const string ValidationRefusee = "Caisse.ValidationRefusee";
    public const string AvoirSurAvoir = "Caisse.AvoirSurAvoir";
    public const string LigneOrigineIntrouvable = "Caisse.LigneOrigineIntrouvable";
    public const string QuantiteRenduTropGrande = "Caisse.QuantiteRenduTropGrande";
    public const string RemboursementInvalide = "Caisse.RemboursementInvalide";
    public const string MouvementRefuse = "Caisse.MouvementRefuse";
    public const string SessionAutreCaissier = "Caisse.SessionAutreCaissier";
    public const string VentesNonSynchronisees = "Caisse.VentesNonSynchronisees";
    public const string CommentaireObligatoire = "Caisse.CommentaireObligatoire";
    public const string AnomalieDejaTraitee = "Caisse.AnomalieDejaTraitee";
    public const string AnomalieIntrouvable = "Caisse.AnomalieIntrouvable";
}
