namespace GestionStock.Reprise.Contracts;

/// <summary>
/// Colonnes du fichier de reprise, dans l'ordre (une ligne par conditionnement). Le fichier est lu par position :
/// l'en-tête peut être en français ou en anglais.
/// </summary>
public static class ColonnesReprise
{
    public static readonly IReadOnlyList<string> Toutes =
    [
        "CodeArticle",
        "Libelle",
        "LibelleTraduit",
        "UniteStock",
        "Decimales",
        "Conditionnement",
        "Niveau",
        "Contenu",
        "CodeBarres",
        "PrixTtc",
        "CoutAchat",
    ];
}
