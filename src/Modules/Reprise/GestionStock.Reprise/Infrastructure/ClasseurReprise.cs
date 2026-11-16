using System.Globalization;
using System.Resources;
using ClosedXML.Excel;
using GestionStock.Reprise.Contracts;
using GestionStock.Reprise.Domain;

namespace GestionStock.Reprise.Infrastructure;

/// <summary>Ligne du modèle pré-rempli (exemple) : une valeur par colonne de <see cref="ColonnesReprise"/>.</summary>
internal sealed record LigneModele(
    string CodeArticle,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int? Decimales,
    string? Conditionnement,
    string Niveau,
    decimal Contenu,
    string? CodeBarres,
    decimal? PrixTtc,
    decimal? CoutAchat);

/// <summary>Lecture et écriture du classeur Excel de reprise (ClosedXML).</summary>
internal static class ClasseurReprise
{
    public static ResourceManager Textes { get; } =
        new("GestionStock.Reprise.Ressources.TextesReprise", typeof(ClasseurReprise).Assembly);

    // Largeurs fixes : mesurer le texte demanderait des polices absentes du conteneur Linux.
    private static readonly double[] Largeurs = [16, 34, 30, 12, 11, 20, 12, 10, 17, 12, 13];

    /// <summary>Lit la première feuille : lignes 2 et suivantes, lignes vides ignorées.</summary>
    /// <exception cref="InvalidDataException">Le fichier n'est pas un classeur Excel lisible.</exception>
    public static IReadOnlyList<LigneFichier> Lire(Stream flux)
    {
        XLWorkbook classeur;
        try
        {
            classeur = new XLWorkbook(flux);
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            throw new InvalidDataException("Classeur illisible.", erreur);
        }

        using (classeur)
        {
            var feuille = classeur.Worksheets.First();
            var derniere = feuille.LastRowUsed()?.RowNumber() ?? 0;
            var lignes = new List<LigneFichier>();
            for (var numero = 2; numero <= derniere; numero++)
            {
                var cellules = Enumerable.Range(1, ColonnesReprise.Toutes.Count).Select(c => Texte(feuille.Cell(numero, c))).ToList();
                if (cellules.Any(c => c.Length > 0))
                {
                    lignes.Add(new LigneFichier(numero, cellules));
                }
            }

            return lignes;
        }
    }

    /// <summary>Modèle à remplir, dans la langue demandée, vide ou avec les lignes données.</summary>
    public static byte[] Ecrire(IReadOnlyList<LigneModele> lignes, CultureInfo culture)
    {
        string T(string cle) => Textes.GetString(cle, culture) ?? cle;

        using var classeur = new XLWorkbook();
        var feuille = classeur.AddWorksheet(T("Reprise.Feuille.Articles"));
        for (var c = 0; c < ColonnesReprise.Toutes.Count; c++)
        {
            var entete = feuille.Cell(1, c + 1);
            entete.Value = T($"Reprise.Colonne.{ColonnesReprise.Toutes[c]}");
            entete.Style.Font.Bold = true;
            entete.Style.Font.FontColor = XLColor.White;
            entete.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F5F4A");
            feuille.Column(c + 1).Width = Largeurs[c];
        }

        // Codes en texte : Excel ne doit ni supprimer les zéros de tête ni passer en notation scientifique.
        feuille.Column(1).Style.NumberFormat.Format = "@";
        feuille.Column(9).Style.NumberFormat.Format = "@";
        feuille.Range(2, 7, 5001, 7).CreateDataValidation().List("\"Unite,Pack,Carton,Sac,Palette\"");
        feuille.SheetView.FreezeRows(1);

        var numero = 2;
        foreach (var ligne in lignes)
        {
            feuille.Cell(numero, 1).Value = ligne.CodeArticle;
            feuille.Cell(numero, 2).Value = ligne.Libelle;
            feuille.Cell(numero, 3).Value = ligne.LibelleTraduit ?? "";
            feuille.Cell(numero, 4).Value = ligne.UniteStock;
            if (ligne.Decimales is { } decimales)
            {
                feuille.Cell(numero, 5).Value = decimales;
            }

            feuille.Cell(numero, 6).Value = ligne.Conditionnement ?? "";
            feuille.Cell(numero, 7).Value = ligne.Niveau;
            feuille.Cell(numero, 8).Value = ligne.Contenu;
            feuille.Cell(numero, 9).Value = ligne.CodeBarres ?? "";
            if (ligne.PrixTtc is { } prix)
            {
                feuille.Cell(numero, 10).Value = prix;
            }

            if (ligne.CoutAchat is { } cout)
            {
                feuille.Cell(numero, 11).Value = cout;
            }

            numero++;
        }

        var aide = classeur.AddWorksheet(T("Reprise.Feuille.Aide"));
        aide.Column(1).Width = 120;
        var rang = 1;
        foreach (var cle in new[] { "Reprise.Aide.Titre", "Reprise.Aide.Ligne", "Reprise.Aide.Unite", "Reprise.Aide.Autres", "Reprise.Aide.Codes", "Reprise.Aide.Prix", "Reprise.Aide.Cout", "Reprise.Aide.Import" })
        {
            aide.Cell(rang, 1).Value = T(cle);
            aide.Cell(rang, 1).Style.Alignment.WrapText = true;
            rang++;
        }

        aide.Cell(1, 1).Style.Font.Bold = true;

        using var flux = new MemoryStream();
        classeur.SaveAs(flux);
        return flux.ToArray();
    }

    private static string Texte(IXLCell cellule)
    {
        var valeur = cellule.Value;
        if (valeur.IsBlank)
        {
            return "";
        }

        if (valeur.IsNumber)
        {
            return ((decimal)Math.Round(valeur.GetNumber(), 6)).ToString(CultureInfo.InvariantCulture);
        }

        return valeur.IsText ? valeur.GetText() : cellule.GetFormattedString();
    }
}
