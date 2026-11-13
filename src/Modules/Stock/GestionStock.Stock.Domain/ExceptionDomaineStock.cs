namespace GestionStock.Stock.Domain;

/// <summary>
/// Violation d'une règle du domaine Stock. Le <see cref="Code"/> est stable : l'interface le traduit
/// via les fichiers de ressources fr / en ; le message n'est destiné qu'aux journaux techniques.
/// </summary>
public sealed class ExceptionDomaineStock : Exception
{
    public ExceptionDomaineStock(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ExceptionDomaineStock()
        : this(CodesErreurStock.Inconnu, "Erreur du domaine Stock.")
    {
    }

    public ExceptionDomaineStock(string message)
        : this(CodesErreurStock.Inconnu, message)
    {
    }

    public ExceptionDomaineStock(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = CodesErreurStock.Inconnu;
    }

    /// <summary>Code d'erreur stable, clé des ressources de traduction.</summary>
    public string Code { get; }
}

/// <summary>Codes d'erreur du domaine Stock.</summary>
public static class CodesErreurStock
{
    public const string Inconnu = "Stock.Inconnu";
    public const string QuantiteNonPositive = "Stock.QuantiteNonPositive";
    public const string PrecisionQuantite = "Stock.PrecisionQuantite";
    public const string CoutNegatif = "Stock.CoutNegatif";
    public const string SensIncompatible = "Stock.SensIncompatible";
    public const string SourceEgaleDestination = "Stock.SourceEgaleDestination";
    public const string TypeNonPrisEnCharge = "Stock.TypeNonPrisEnCharge";
    public const string DocumentObligatoire = "Stock.DocumentObligatoire";
    public const string UtilisateurObligatoire = "Stock.UtilisateurObligatoire";
    public const string IdempotenceObligatoire = "Stock.IdempotenceObligatoire";
    public const string IdentifiantObligatoire = "Stock.IdentifiantObligatoire";
    public const string DateNonUtc = "Stock.DateNonUtc";
    public const string MouvementNonConcerne = "Stock.MouvementNonConcerne";
    public const string StockInsuffisant = "Stock.StockInsuffisant";
}
