namespace GestionStock.Referentiel.Domain;

/// <summary>Mode de suivi d'un article (section 4.5). Seul <see cref="Aucun"/> est utilisé en V1.</summary>
internal enum ModeSuivi
{
    Aucun = 0,
    Lot = 1,
    Serie = 2,
}

/// <summary>
/// Article géré en stock (UC01). Son unité de stock est la plus petite unité vendue (RG44) ; ses quantités
/// ont au plus <see cref="PrecisionQuantite"/> décimales (RG46). Le code, l'unité et la précision ne changent
/// plus après la création : les mouvements déjà enregistrés sont exprimés dans cette unité.
/// Jamais supprimé : désactivé.
/// </summary>
internal sealed class Article
{
    public const int LongueurMaxCode = 50;
    public const int LongueurMaxLibelle = 200;
    public const int LongueurMaxUnite = 20;
    public const int PrecisionMax = 3;

    private Article()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = "";

    /// <summary>Libellé principal, obligatoire (décision 20).</summary>
    public string Libelle { get; private set; } = "";

    /// <summary>Traduction facultative du libellé dans l'autre langue de l'interface (décision 20).</summary>
    public string? LibelleTraduit { get; private set; }

    public string UniteStock { get; private set; } = "";

    public int PrecisionQuantite { get; private set; }

    /// <summary>Prévu par le modèle ; V1 sans lots ni séries.</summary>
    public ModeSuivi ModeSuivi { get; private set; } = ModeSuivi.Aucun;

    /// <summary>Prévu par le modèle ; V1 sans DLC.</summary>
    public bool GereDlc { get; private set; }

    public bool Actif { get; private set; } = true;

    public static Article Creer(string code, string libelle, string? libelleTraduit, string uniteStock, int precisionQuantite)
    {
        var article = new Article
        {
            Id = Guid.CreateVersion7(),
            Code = Exiger(code, LongueurMaxCode, CodesErreurReferentiel.CodeObligatoire, CodesErreurReferentiel.CodeTropLong),
            UniteStock = Exiger(uniteStock, LongueurMaxUnite, CodesErreurReferentiel.UniteObligatoire, CodesErreurReferentiel.UniteTropLongue),
        };

        if (precisionQuantite is < 0 or > PrecisionMax)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.PrecisionInvalide);
        }

        article.PrecisionQuantite = precisionQuantite;
        article.Modifier(libelle, libelleTraduit, actif: true);
        return article;
    }

    public void Modifier(string libelle, string? libelleTraduit, bool actif)
    {
        Libelle = Exiger(libelle, LongueurMaxLibelle, CodesErreurReferentiel.LibelleObligatoire, CodesErreurReferentiel.LibelleTropLong);
        var traduit = libelleTraduit?.Trim();
        if (traduit?.Length > LongueurMaxLibelle)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.LibelleTropLong);
        }

        LibelleTraduit = string.IsNullOrEmpty(traduit) ? null : traduit;
        Actif = actif;
    }

    private static string Exiger(string? valeur, int longueurMax, string codeVide, string codeTropLong)
    {
        var nettoyee = valeur?.Trim() ?? "";
        if (nettoyee.Length == 0)
        {
            throw new ExceptionReferentiel(codeVide);
        }

        return nettoyee.Length <= longueurMax ? nettoyee : throw new ExceptionReferentiel(codeTropLong);
    }
}
