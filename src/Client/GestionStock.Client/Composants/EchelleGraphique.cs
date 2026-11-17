namespace GestionStock.Client.Composants;

/// <summary>Point d'un graphique : libellé (jour, mois…), valeur et, facultativement, valeur d'une seconde série.</summary>
public sealed record PointGraphique(string Libelle, decimal Valeur, decimal? Secondaire = null);

/// <summary>
/// Échelle verticale d'un graphique : bornes « rondes » (pas de 1, 2, 2,5 ou 5 × 10ⁿ) qui encadrent toutes les valeurs,
/// zéro compris, et graduations régulières entre elles.
/// </summary>
public sealed record EchelleGraphique(decimal Bas, decimal Haut, IReadOnlyList<decimal> Graduations)
{
    private static readonly decimal[] PasRonds = [1m, 2m, 2.5m, 5m, 10m];

    /// <summary>Échelle de <paramref name="valeurs"/> avec environ <paramref name="graduations"/> intervalles.</summary>
    public static EchelleGraphique Pour(IEnumerable<decimal> valeurs, int graduations = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(graduations, 1);
        var liste = valeurs.ToList();
        var min = Math.Min(0m, liste.Count == 0 ? 0m : liste.Min());
        var max = Math.Max(0m, liste.Count == 0 ? 0m : liste.Max());
        if (min == max)
        {
            max = min + 1m;
        }

        var pas = PasRond((max - min) / graduations);
        var bas = Math.Floor(min / pas) * pas;
        var haut = Math.Ceiling(max / pas) * pas;
        var reperes = new List<decimal>();
        for (var repere = bas; repere <= haut; repere += pas)
        {
            reperes.Add(repere);
        }

        return new EchelleGraphique(bas, haut, reperes);
    }

    /// <summary>Position de <paramref name="valeur"/> entre le bas (0) et le haut (1) de l'échelle.</summary>
    public double Position(decimal valeur) =>
        Haut == Bas ? 0 : (double)((Math.Clamp(valeur, Bas, Haut) - Bas) / (Haut - Bas));

    private static decimal PasRond(decimal brut)
    {
        var puissance = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)brut)));
        return PasRonds.Select(p => p * puissance).First(p => p >= brut);
    }
}
