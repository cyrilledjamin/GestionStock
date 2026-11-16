namespace GestionStock.Rapports.Application;

/// <summary>Règle de saisie d'un rapport violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionRapports(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static class CodesErreurRapports
{
    public const string PeriodeInvalide = "Rapports.PeriodeInvalide";
    public const string JournalInconnu = "Rapports.JournalInconnu";
    public const string FormatInvalide = "Rapports.FormatInvalide";
}

/// <summary>
/// Période d'un rapport en jours de Douala, bornes incluses, convertie en bornes UTC [début, fin[ pour les requêtes.
/// Douala est à UTC+1 toute l'année (pas d'heure d'été).
/// </summary>
internal sealed record Periode(DateOnly Du, DateOnly Au)
{
    /// <summary>Une année au plus : au-delà, l'export convient mieux qu'un écran.</summary>
    public const int JoursMax = 366;

    public static readonly TimeSpan DecalageDouala = TimeSpan.FromHours(1);

    public DateTime DebutUtc => Du.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - DecalageDouala;

    public DateTime FinUtc => Au.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - DecalageDouala;

    public static Periode Creer(DateOnly? du, DateOnly? au, DateOnly aujourdhui)
    {
        var fin = au ?? aujourdhui;
        var debut = du ?? fin;
        if (debut > fin || fin.DayNumber - debut.DayNumber >= JoursMax)
        {
            throw new ExceptionRapports(CodesErreurRapports.PeriodeInvalide);
        }

        return new Periode(debut, fin);
    }

    /// <summary>Jour de Douala d'un horodatage UTC.</summary>
    public static DateOnly JourDouala(DateTime utc) => DateOnly.FromDateTime(utc + DecalageDouala);
}
