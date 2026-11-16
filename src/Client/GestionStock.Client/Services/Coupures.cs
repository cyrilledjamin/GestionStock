namespace GestionStock.Client.Services;

/// <summary>
/// Coupures du franc CFA (BEAC) pour compter le tiroir à la clôture : billets de 10 000 à 1 000, pièces de 500 à 1.
/// Le total compté est la seule valeur envoyée au serveur (RG36).
/// </summary>
public static class Coupures
{
    public static IReadOnlyList<decimal> Billets { get; } = [10_000m, 5_000m, 2_000m, 1_000m];

    public static IReadOnlyList<decimal> Pieces { get; } = [500m, 100m, 50m, 25m, 10m, 5m, 2m, 1m];

    public static IEnumerable<decimal> Toutes => Billets.Concat(Pieces);

    /// <summary>Total du tiroir : somme des nombres saisis par coupure (un nombre négatif ou absent compte pour zéro).</summary>
    public static decimal Total(IReadOnlyDictionary<decimal, int> nombres)
    {
        ArgumentNullException.ThrowIfNull(nombres);
        return Toutes.Sum(coupure => nombres.TryGetValue(coupure, out var nombre) && nombre > 0 ? coupure * nombre : 0m);
    }
}
