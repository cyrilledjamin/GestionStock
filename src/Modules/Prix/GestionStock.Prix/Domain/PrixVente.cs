namespace GestionStock.Prix.Domain;

/// <summary>Règle du module Prix violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionPrix(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static class CodesErreurPrix
{
    public const string PrixNonPositif = "Prix.PrixNonPositif";
    public const string PrecisionPrix = "Prix.PrecisionPrix";
    public const string DateDansLePasse = "Prix.DateDansLePasse";
    public const string ConditionnementIntrouvable = "Prix.ConditionnementIntrouvable";
    public const string ConditionnementInactif = "Prix.ConditionnementInactif";
    public const string ArticleIntrouvable = "Prix.ArticleIntrouvable";
}

/// <summary>
/// Prix de vente TTC d'un conditionnement à partir d'une date (UC25, RG45). Un changement de prix crée un nouveau prix
/// daté, jamais une modification : l'historique reste consultable. Le prix applicable un jour donné est celui dont la
/// date de début est la plus récente sans dépasser ce jour (à date égale, le dernier saisi).
/// </summary>
internal sealed class PrixVente
{
    /// <summary>Décimales d'un montant interne (decimal(19,4), RG32).</summary>
    public const int DecimalesMontant = 4;

    private PrixVente()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConditionnementId { get; private set; }

    public decimal PrixTtc { get; private set; }

    public DateOnly DebutValidite { get; private set; }

    public string SaisiPar { get; private set; } = "";

    public DateTime DateSaisieUtc { get; private set; }

    /// <summary>Nouveau prix ; il ne peut pas prendre effet dans le passé (l'historique ne se réécrit pas).</summary>
    public static PrixVente Fixer(Guid conditionnementId, decimal prixTtc, DateOnly debutValidite, DateOnly aujourdhui, string utilisateur, DateTime maintenantUtc)
    {
        if (prixTtc <= 0m)
        {
            throw new ExceptionPrix(CodesErreurPrix.PrixNonPositif);
        }

        if (decimal.Round(prixTtc, DecimalesMontant) != prixTtc)
        {
            throw new ExceptionPrix(CodesErreurPrix.PrecisionPrix);
        }

        if (debutValidite < aujourdhui)
        {
            throw new ExceptionPrix(CodesErreurPrix.DateDansLePasse);
        }

        return new PrixVente
        {
            Id = Guid.CreateVersion7(),
            ConditionnementId = conditionnementId,
            PrixTtc = prixTtc,
            DebutValidite = debutValidite,
            SaisiPar = utilisateur,
            DateSaisieUtc = maintenantUtc,
        };
    }
}
