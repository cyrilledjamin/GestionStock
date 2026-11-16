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
