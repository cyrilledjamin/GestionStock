namespace GestionStock.Rapports.Contracts;

/// <summary>Journaux comptables exportables pour le comptable externe (UC16, décision 7).</summary>
public static class JournauxComptables
{
    public static readonly string Ventes = "ventes";

    public static readonly string Achats = "achats";

    public static readonly string Encaissements = "encaissements";

    public static readonly string Creances = "creances";

    public static readonly string ValeurStock = "valeur-stock";

    public static IReadOnlyList<string> Tous { get; } = [Ventes, Achats, Encaissements, Creances, ValeurStock];
}
