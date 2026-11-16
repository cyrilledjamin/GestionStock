using System.Globalization;

namespace GestionStock.Client.Services;

/// <summary>Mise en forme : montants en XAF arrondis à l'unité, quantités selon la précision de l'article, heure de Douala.</summary>
public static class Format
{
    private static readonly TimeZoneInfo Douala = TrouverDouala();

    /// <summary>Montant arrondi à l'unité (RG32), ex. « 12 500 XAF ».</summary>
    public static string Montant(decimal montant) =>
        $"{decimal.Round(montant, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.CurrentCulture)} XAF";

    /// <summary>
    /// Quantité sans zéros inutiles (« 20 kg », « 2,5 kg »), au plus <paramref name="precision"/> décimales (RG46) :
    /// « 20,000 kg » se lisait « vingt mille » pour un anglophone et « 20.000 » pour un francophone.
    /// </summary>
    public static string Quantite(decimal quantite, int? precision = null)
    {
        var decimales = Math.Clamp(precision ?? 3, 0, 4);
        return quantite.ToString(decimales == 0 ? "#,##0" : $"#,##0.{new string('#', decimales)}", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Unité ou conditionnement dans la langue de l'interface : les unités courantes saisies en français sont traduites
    /// en anglais (décision du 16/11/2026) ; une unité absente de la liste s'affiche telle quelle.
    /// </summary>
    public static string Unite(string? unite)
    {
        if (string.IsNullOrWhiteSpace(unite) || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en")
        {
            return unite ?? "";
        }

        return UnitesEnAnglais.TryGetValue(SansAccents(unite.Trim()), out var traduite) ? traduite : unite;
    }

    /// <summary>Unités courantes du commerce de détail, en minuscules et sans accents, vers l'anglais.</summary>
    private static readonly Dictionary<string, string> UnitesEnAnglais = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boite"] = "box",
        ["boite de conserve"] = "can",
        ["canette"] = "can",
        ["piece"] = "piece",
        ["unite"] = "unit",
        ["paquet"] = "pack",
        ["sachet"] = "sachet",
        ["bouteille"] = "bottle",
        ["flacon"] = "bottle",
        ["bidon"] = "jerrycan",
        ["litre"] = "litre",
        ["carton"] = "carton",
        ["sac"] = "bag",
        ["rouleau"] = "roll",
        ["tablette"] = "bar",
        ["barre"] = "bar",
        ["plaquette"] = "pack",
        ["douzaine"] = "dozen",
        ["pot"] = "jar",
        ["seau"] = "bucket",
        ["tube"] = "tube",
        ["metre"] = "metre",
        ["botte"] = "bunch",
        ["regime"] = "bunch",
        ["tas"] = "heap",
        ["kilo"] = "kilo",
    };

    private static string SansAccents(string texte) =>
        string.Concat(texte.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

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
