using System.Globalization;
using GestionStock.Referentiel.Contracts;

namespace GestionStock.Reprise.Domain;

/// <summary>Codes d'erreur de la reprise, clés des ressources fr / en.</summary>
internal static class CodesErreurReprise
{
    public const string FichierIllisible = "Reprise.FichierIllisible";
    public const string FichierVide = "Reprise.FichierVide";
    public const string FichierTropGros = "Reprise.FichierTropGros";
    public const string ChampObligatoire = "Reprise.ChampObligatoire";
    public const string TexteTropLong = "Reprise.TexteTropLong";
    public const string NombreInvalide = "Reprise.NombreInvalide";
    public const string DecimalesInvalides = "Reprise.DecimalesInvalides";
    public const string NiveauInvalide = "Reprise.NiveauInvalide";
    public const string ContenuInvalide = "Reprise.ContenuInvalide";
    public const string LigneUniteManquante = "Reprise.LigneUniteManquante";
    public const string LigneUniteEnDouble = "Reprise.LigneUniteEnDouble";
    public const string IncoherenceArticle = "Reprise.IncoherenceArticle";
    public const string CodeArticleExistant = "Reprise.CodeArticleExistant";
    public const string CodeBarresInvalide = "Reprise.CodeBarresInvalide";
    public const string CodeBarresEnDouble = "Reprise.CodeBarresEnDouble";
    public const string CodeBarresAttribue = "Reprise.CodeBarresAttribue";
    public const string PrixInvalide = "Reprise.PrixInvalide";
    public const string CoutInvalide = "Reprise.CoutInvalide";
    public const string EcritureRefusee = "Reprise.EcritureRefusee";
}

/// <summary>Une ligne lue dans le fichier, cellules en texte (les nombres en notation invariante).</summary>
internal sealed record LigneFichier(int Numero, IReadOnlyList<string> Cellules)
{
    public string this[int colonne] => colonne < Cellules.Count ? Cellules[colonne].Trim() : "";
}

/// <summary>Erreur rattachée à une ligne du fichier (0 : le fichier entier) et éventuellement à une colonne.</summary>
internal sealed record ErreurReprise(int Ligne, string Code, string? Colonne = null, string? Detail = null);

/// <summary>Conditionnement à créer, avec son code-barres et son prix éventuels.</summary>
internal sealed record ConditionnementAImporter(int Ligne, bool EstUnite, string Libelle, string Niveau, decimal Contenu, string? CodeBarres, decimal? PrixTtc);

/// <summary>Article à créer : ses données, son coût d'achat de reprise et ses conditionnements.</summary>
internal sealed record ArticleAImporter(
    string Code,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int Decimales,
    decimal CoutAchat,
    IReadOnlyList<ConditionnementAImporter> Conditionnements);

/// <summary>
/// Contrôle du fichier de reprise, sans base de données : chaque ligne est un conditionnement ; chaque article a
/// exactement une ligne au niveau « Unite » (contenu 1) qui porte ses données (libellé, unité, décimales, coût
/// d'achat) ; les autres lignes ne doivent pas les contredire. Toutes les erreurs sont relevées, pas seulement la
/// première.
/// </summary>
internal static class AnalyseReprise
{
    public const int LignesMax = 5000;

    private const int CodeArticle = 0;
    private const int Libelle = 1;
    private const int LibelleTraduit = 2;
    private const int UniteStock = 3;
    private const int Decimales = 4;
    private const int Conditionnement = 5;
    private const int Niveau = 6;
    private const int Contenu = 7;
    private const int CodeBarres = 8;
    private const int PrixTtc = 9;
    private const int CoutAchat = 10;

    private static readonly string[] Niveaux = ["Unite", "Pack", "Carton", "Sac", "Palette"];

    public static (IReadOnlyList<ArticleAImporter> Articles, IReadOnlyList<ErreurReprise> Erreurs) Analyser(IReadOnlyList<LigneFichier> lignes)
    {
        var erreurs = new List<ErreurReprise>();
        if (lignes.Count == 0)
        {
            return ([], [new ErreurReprise(0, CodesErreurReprise.FichierVide)]);
        }

        if (lignes.Count > LignesMax)
        {
            return ([], [new ErreurReprise(0, CodesErreurReprise.FichierTropGros, Detail: $"{LignesMax}")]);
        }

        var articles = new List<ArticleAImporter>();
        foreach (var groupe in lignes.GroupBy(l => l[CodeArticle].ToUpperInvariant()))
        {
            if (groupe.Key.Length == 0)
            {
                erreurs.AddRange(groupe.Select(l => new ErreurReprise(l.Numero, CodesErreurReprise.ChampObligatoire, ColonnesNom(CodeArticle))));
                continue;
            }

            var article = AnalyserArticle([.. groupe], erreurs);
            if (article is not null)
            {
                articles.Add(article);
            }
        }

        // Un code-barres ne désigne qu'un conditionnement (RG26) : contrôlé sur toutes les lignes, y compris celles
        // d'un article déjà en erreur.
        foreach (var doublon in lignes.Where(l => l[CodeBarres].Length > 0).GroupBy(l => l[CodeBarres]).Where(g => g.Count() > 1))
        {
            erreurs.AddRange(doublon.Skip(1).Select(l => new ErreurReprise(l.Numero, CodesErreurReprise.CodeBarresEnDouble, ColonnesNom(CodeBarres), l[CodeBarres])));
        }

        return (articles, erreurs);
    }

    private static ArticleAImporter? AnalyserArticle(IReadOnlyList<LigneFichier> lignes, List<ErreurReprise> erreurs)
    {
        var avant = erreurs.Count;
        var unites = lignes.Where(l => NiveauNormalise(l[Niveau]) == "Unite").ToList();
        if (unites.Count == 0)
        {
            erreurs.Add(new ErreurReprise(lignes[0].Numero, CodesErreurReprise.LigneUniteManquante, ColonnesNom(Niveau), lignes[0][CodeArticle]));
            return null;
        }

        if (unites.Count > 1)
        {
            erreurs.AddRange(unites.Skip(1).Select(l => new ErreurReprise(l.Numero, CodesErreurReprise.LigneUniteEnDouble, ColonnesNom(Niveau), l[CodeArticle])));
        }

        var u = unites[0];
        var code = Texte(u, CodeArticle, 50, obligatoire: true, erreurs);
        var libelle = Texte(u, Libelle, 200, obligatoire: true, erreurs);
        var traduit = Texte(u, LibelleTraduit, 200, obligatoire: false, erreurs);
        var unite = Texte(u, UniteStock, 20, obligatoire: true, erreurs);
        var decimales = Entier(u, Decimales, erreurs);

        var cout = Nombre(u, CoutAchat, obligatoire: true, erreurs);
        if (cout is < 0m || (cout is { } c && decimal.Round(c, 4) != c))
        {
            erreurs.Add(new ErreurReprise(u.Numero, CodesErreurReprise.CoutInvalide, ColonnesNom(CoutAchat)));
        }

        // Les autres lignes ne contredisent pas les données de l'article.
        foreach (var ligne in lignes.Where(l => l != u))
        {
            foreach (var colonne in new[] { Libelle, LibelleTraduit, UniteStock, Decimales, CoutAchat })
            {
                if (ligne[colonne].Length > 0 && !string.Equals(ligne[colonne], u[colonne], StringComparison.OrdinalIgnoreCase))
                {
                    erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.IncoherenceArticle, ColonnesNom(colonne), u[CodeArticle]));
                }
            }
        }

        var conditionnements = new List<ConditionnementAImporter>();
        foreach (var ligne in lignes)
        {
            var estUnite = ligne == u;
            var niveau = NiveauNormalise(ligne[Niveau]);
            if (niveau is null)
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.NiveauInvalide, ColonnesNom(Niveau), ligne[Niveau]));
                continue;
            }

            if (niveau == "Unite" && !estUnite)
            {
                continue; // ligne Unité en double, déjà signalée
            }

            var contenu = estUnite ? 1m : Nombre(ligne, Contenu, obligatoire: true, erreurs) ?? 0m;
            if (estUnite && ligne[Contenu].Length > 0 && Nombre(ligne, Contenu, obligatoire: false, erreurs) is not 1m)
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.ContenuInvalide, ColonnesNom(Contenu)));
            }

            if (!estUnite && (contenu <= 0m || (decimales is { } d and >= 0 and <= 3 && decimal.Round(contenu, d) != contenu)))
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.ContenuInvalide, ColonnesNom(Contenu)));
            }

            var libelleConditionnement = estUnite ? unite ?? "" : Texte(ligne, Conditionnement, 50, obligatoire: true, erreurs) ?? "";
            var codeBarres = ligne[CodeBarres].Length == 0 ? null : ligne[CodeBarres];
            if (codeBarres is not null && !CodesBarres.EstGtinValide(codeBarres))
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.CodeBarresInvalide, ColonnesNom(CodeBarres), codeBarres));
            }

            var prix = Nombre(ligne, PrixTtc, obligatoire: false, erreurs);
            if (prix is <= 0m || (prix is { } p && decimal.Round(p, 4) != p))
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.PrixInvalide, ColonnesNom(PrixTtc)));
            }

            conditionnements.Add(new ConditionnementAImporter(ligne.Numero, estUnite, libelleConditionnement, niveau, contenu, codeBarres, prix));
        }

        return erreurs.Count > avant
            ? null
            : new ArticleAImporter(code!, libelle!, traduit, unite!, decimales!.Value, cout!.Value, conditionnements);
    }

    /// <summary>Niveau reconnu sans tenir compte de la casse ni de l'accent (« Unité ») ; null s'il est inconnu.</summary>
    public static string? NiveauNormalise(string valeur)
    {
        var sansAccent = valeur.Trim().Replace('é', 'e').Replace('É', 'E');
        return Niveaux.FirstOrDefault(n => string.Equals(n, sansAccent, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Texte(LigneFichier ligne, int colonne, int longueurMax, bool obligatoire, List<ErreurReprise> erreurs)
    {
        var valeur = ligne[colonne];
        if (valeur.Length == 0)
        {
            if (obligatoire)
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.ChampObligatoire, ColonnesNom(colonne)));
            }

            return null;
        }

        if (valeur.Length > longueurMax)
        {
            erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.TexteTropLong, ColonnesNom(colonne), $"{longueurMax}"));
        }

        return valeur;
    }

    private static decimal? Nombre(LigneFichier ligne, int colonne, bool obligatoire, List<ErreurReprise> erreurs)
    {
        // Séparateurs de milliers d'Excel en français (espaces, insécables ou fines) ignorés ; virgule décimale acceptée.
        var valeur = new string([.. ligne[colonne].Where(c => c is not (' ' or ' ' or ' '))]).Replace(',', '.');
        if (valeur.Length == 0)
        {
            if (obligatoire)
            {
                erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.ChampObligatoire, ColonnesNom(colonne)));
            }

            return null;
        }

        if (decimal.TryParse(valeur, NumberStyles.Number, CultureInfo.InvariantCulture, out var nombre))
        {
            return nombre;
        }

        erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.NombreInvalide, ColonnesNom(colonne), ligne[colonne]));
        return null;
    }

    private static int? Entier(LigneFichier ligne, int colonne, List<ErreurReprise> erreurs)
    {
        if (Nombre(ligne, colonne, obligatoire: true, erreurs) is not { } nombre)
        {
            return null;
        }

        if (decimal.Truncate(nombre) != nombre || nombre is < 0m or > 3m)
        {
            erreurs.Add(new ErreurReprise(ligne.Numero, CodesErreurReprise.DecimalesInvalides, ColonnesNom(colonne)));
            return null;
        }

        return (int)nombre;
    }

    private static string ColonnesNom(int colonne) => Contracts.ColonnesReprise.Toutes[colonne];
}
