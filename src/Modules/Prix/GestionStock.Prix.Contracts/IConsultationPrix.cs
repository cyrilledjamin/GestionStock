namespace GestionStock.Prix.Contracts;

/// <summary>Lecture des prix de vente par les autres modules (Caisse). L'application est la seule référence des prix (RG37).</summary>
public interface IConsultationPrix
{
    /// <summary>Prix TTC applicable au conditionnement à la date donnée (Douala) ; null s'il n'a pas de prix.</summary>
    Task<PrixApplicable?> ObtenirPrixAsync(Guid conditionnementId, DateOnly date, CancellationToken annulation = default);

    /// <summary>Régime de TVA de l'entreprise (paramètre de configuration).</summary>
    RegimeTva Regime { get; }
}

/// <summary>Prix de vente TTC d'un conditionnement (RG45 : le prix est fixé par conditionnement).</summary>
public sealed record PrixApplicable(Guid ConditionnementId, decimal PrixTtc, DateOnly DebutValidite);

/// <summary>
/// Régime de TVA (RG37) : assujettie, l'entreprise détaille HT, TVA et TTC sur le reçu ; sinon les prix sont nets
/// et le taux appliqué est nul.
/// </summary>
public sealed record RegimeTva(bool Assujetti, decimal TauxPourcent)
{
    /// <summary>Taux à appliquer aux ventes : le taux légal si l'entreprise est assujettie, 0 sinon.</summary>
    public decimal TauxApplique => Assujetti ? TauxPourcent : 0m;

    /// <summary>Décompose un montant TTC en HT et TVA (4 décimales, RG32).</summary>
    public (decimal Ht, decimal Tva) Decomposer(decimal montantTtc)
    {
        var ht = decimal.Round(montantTtc / (1m + (TauxApplique / 100m)), 4, MidpointRounding.AwayFromZero);
        return (ht, montantTtc - ht);
    }
}

/// <summary>Écriture des prix par les autres modules (reprise des données) : mêmes règles que l'écran des prix.</summary>
public interface IServicePrix
{
    /// <summary>Fixe le prix TTC d'un conditionnement à partir d'aujourd'hui (Douala).</summary>
    /// <exception cref="ErreurPrix">Prix invalide, conditionnement introuvable ou inactif.</exception>
    Task FixerPrixAsync(Guid conditionnementId, decimal prixTtc, CancellationToken annulation = default);
}

/// <summary>Règle des prix violée ; <see cref="Code"/> est la clé de traduction (ex. « Prix.PrixNonPositif »).</summary>
public sealed class ErreurPrix : Exception
{
    public ErreurPrix(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErreurPrix()
        : this("Prix.Inconnu", "Erreur des prix.")
    {
    }

    public ErreurPrix(string message)
        : this("Prix.Inconnu", message)
    {
    }

    public ErreurPrix(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "Prix.Inconnu";
    }

    public string Code { get; }
}
