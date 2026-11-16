using System.Globalization;

namespace GestionStock.Client.Services;

/// <summary>Mise en forme : montants en XAF arrondis à l'unité, quantités selon la précision de l'article, heure de Douala.</summary>
public static class Format
{
    private static readonly TimeZoneInfo Douala = TrouverDouala();

    /// <summary>Montant arrondi à l'unité (RG32), ex. « 12 500 XAF ».</summary>
    public static string Montant(decimal montant) =>
        $"{decimal.Round(montant, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.CurrentCulture)} XAF";

    /// <summary>Quantité avec exactement <paramref name="precision"/> décimales (RG46), ou sans zéros inutiles si elle est inconnue.</summary>
    public static string Quantite(decimal quantite, int? precision = null) =>
        quantite.ToString(precision is { } p ? $"N{p}" : "#,##0.###", CultureInfo.CurrentCulture);

    /// <summary>Retire les zéros de fin d'une valeur lue dans l'API (« 10.0000 » → « 10 »), pour la saisie.</summary>
    public static decimal SansZeros(decimal valeur) => valeur / 1.000000000000000000000000000000000m;

    /// <summary>
    /// Libellé de l'article dans la langue de l'interface : le libellé principal (français), ou sa traduction
    /// facultative quand l'interface est en anglais (décision 20).
    /// </summary>
    public static string Libelle(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" && !string.IsNullOrEmpty(article.LibelleTraduit)
            ? article.LibelleTraduit
            : article.Libelle;
    }

    /// <summary>Horodatage UTC affiché en heure de Douala.</summary>
    public static string Date(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Douala).ToString("g", CultureInfo.CurrentCulture);

    private static TimeZoneInfo TrouverDouala()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Douala");
        }
        catch (TimeZoneNotFoundException)
        {
            // Douala est à UTC+1 toute l'année (pas d'heure d'été).
            return TimeZoneInfo.CreateCustomTimeZone("Africa/Douala", TimeSpan.FromHours(1), "Douala", "WAT");
        }
    }
}
