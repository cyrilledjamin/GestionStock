using System.Globalization;
using System.Text;

namespace GestionStock.Caisse.Infrastructure;

/// <summary>Ligne d'un ticket imprimé, en caractères à chasse fixe.</summary>
internal sealed record LigneImprimee(string Texte, bool Gras = false);

/// <summary>
/// Écrit un PDF d'une page au format ticket 80 mm, en Courier : l'une des 14 polices standard que tout lecteur PDF
/// possède (aucune police à embarquer), à chasse fixe comme une imprimante thermique. Aucune dépendance externe.
/// </summary>
internal static class PdfTicket
{
    /// <summary>Caractères par ligne : 42 × 0,6 × 8 pt = 201,6 pt dans 80 mm moins les marges.</summary>
    public const int Colonnes = 42;

    private const decimal LargeurPoints = 226.77m; // 80 mm
    private const decimal Marge = 12m;
    private const decimal Taille = 8m;
    private const decimal Interligne = 10m;

    public static byte[] Ecrire(IReadOnlyList<LigneImprimee> lignes)
    {
        ArgumentNullException.ThrowIfNull(lignes);
        var hauteur = (Marge * 2) + (Interligne * Math.Max(lignes.Count, 1));
        var contenu = Contenu(lignes, hauteur);

        var objets = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LargeurPoints} {hauteur}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>")),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"),
            Ascii(string.Create(CultureInfo.InvariantCulture, $"<< /Length {contenu.Length} >>\nstream\n")).Concat(contenu).Concat(Ascii("\nendstream")).ToArray(),
        };

        using var flux = new MemoryStream();
        Ecrire(flux, "%PDF-1.4\n%âãÏÓ\n");
        var positions = new List<long>();
        for (var i = 0; i < objets.Count; i++)
        {
            positions.Add(flux.Position);
            Ecrire(flux, $"{i + 1} 0 obj\n");
            flux.Write(objets[i]);
            Ecrire(flux, "\nendobj\n");
        }

        var xref = flux.Position;
        Ecrire(flux, $"xref\n0 {objets.Count + 1}\n0000000000 65535 f \n");
        foreach (var position in positions)
        {
            Ecrire(flux, $"{position:D10} 00000 n \n");
        }

        Ecrire(flux, $"trailer\n<< /Size {objets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return flux.ToArray();
    }

    private static byte[] Contenu(IReadOnlyList<LigneImprimee> lignes, decimal hauteur)
    {
        var texte = new StringBuilder();
        texte.Append(CultureInfo.InvariantCulture, $"BT\n{Interligne} TL\n{Marge} {hauteur - Marge - Taille} Td\n");
        foreach (var ligne in lignes)
        {
            texte.Append(CultureInfo.InvariantCulture, $"/{(ligne.Gras ? "F2" : "F1")} {Taille} Tf\n(");
            texte.Append(Echapper(ligne.Texte));
            texte.Append(") Tj\nT*\n");
        }

        texte.Append("ET");
        return Encoding.Latin1.GetBytes(texte.ToString());
    }

    /// <summary>Échappe la syntaxe PDF et ramène le texte aux caractères de WinAnsi communs avec Latin-1.</summary>
    private static string Echapper(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (var caractere in texte)
        {
            switch (caractere)
            {
                case '\\' or '(' or ')':
                    resultat.Append('\\').Append(caractere);
                    break;
                case ' ' or ' ' or ' ':
                    resultat.Append(' '); // espaces fines des milliers
                    break;
                case '’' or '‘':
                    resultat.Append('\'');
                    break;
                case '“' or '”':
                    resultat.Append('"');
                    break;
                case '—' or '–':
                    resultat.Append('-');
                    break;
                case 'œ':
                    resultat.Append("oe");
                    break;
                case '…':
                    resultat.Append("...");
                    break;
                default:
                    resultat.Append(caractere <= 'ÿ' ? caractere : '?');
                    break;
            }
        }

        return resultat.ToString();
    }

    private static byte[] Ascii(string texte) => Encoding.Latin1.GetBytes(texte);

    private static void Ecrire(Stream flux, string texte) => flux.Write(Encoding.Latin1.GetBytes(texte));
}
