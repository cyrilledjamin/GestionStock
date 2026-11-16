namespace GestionStock.Referentiel.Contracts;

/// <summary>
/// Écriture du référentiel par les autres modules (reprise des données, décision 29). Chaque méthode applique les mêmes
/// règles que les écrans et participe à l'unité de travail de l'appelant.
/// </summary>
public interface IServiceReferentiel
{
    /// <summary>Crée un article et son conditionnement « Unité » (RG44).</summary>
    /// <exception cref="ErreurReferentiel">Saisie invalide ou code déjà utilisé.</exception>
    Task<ArticleCree> CreerArticleAsync(NouvelArticleReferentiel article, CancellationToken annulation = default);

    /// <summary>Ajoute un conditionnement (niveau : Pack, Carton, Sac, Palette) ; renvoie son identifiant.</summary>
    /// <exception cref="ErreurReferentiel">Saisie invalide.</exception>
    Task<Guid> AjouterConditionnementAsync(Guid articleId, string libelle, string niveau, decimal quantiteUniteStock, CancellationToken annulation = default);

    /// <summary>Attribue un GTIN (EAN-13, EAN-8, UPC-A) à un conditionnement (RG26, RG33).</summary>
    /// <exception cref="ErreurReferentiel">Code invalide ou déjà attribué.</exception>
    Task AjouterCodeAsync(Guid conditionnementId, string valeur, CancellationToken annulation = default);
}

public sealed record NouvelArticleReferentiel(string Code, string Libelle, string? LibelleTraduit, string UniteStock, int PrecisionQuantite);

public sealed record ArticleCree(Guid ArticleId, Guid ConditionnementUniteId);

/// <summary>Règle du référentiel violée ; <see cref="Code"/> est la clé de traduction (ex. « Referentiel.CodeInvalide »).</summary>
public sealed class ErreurReferentiel : Exception
{
    public ErreurReferentiel(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErreurReferentiel()
        : this("Referentiel.Inconnu", "Erreur du référentiel.")
    {
    }

    public ErreurReferentiel(string message)
        : this("Referentiel.Inconnu", message)
    {
    }

    public ErreurReferentiel(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "Referentiel.Inconnu";
    }

    public string Code { get; }
}

/// <summary>Contrôle des codes-barres GTIN (RG33), partagé par le référentiel et la reprise des données.</summary>
public static class CodesBarres
{
    /// <summary>EAN-13, EAN-8 ou UPC-A : uniquement des chiffres et une clé de contrôle GS1 modulo 10 correcte.</summary>
    public static bool EstGtinValide(string? valeur) =>
        valeur is { Length: 8 or 12 or 13 } && valeur.All(char.IsAsciiDigit) && CleValide(valeur);

    /// <summary>Clé de contrôle GS1 modulo 10 : poids 3 et 1 en partant du chiffre précédant la clé.</summary>
    public static bool CleValide(string chiffres)
    {
        ArgumentNullException.ThrowIfNull(chiffres);
        return CleAttendue(chiffres[..^1]) == chiffres[^1] - '0';
    }

    /// <summary>Clé de contrôle à ajouter à une suite de chiffres pour former un GTIN.</summary>
    public static int CleAttendue(string chiffresSansCle)
    {
        ArgumentNullException.ThrowIfNull(chiffresSansCle);
        var somme = 0;
        for (var i = chiffresSansCle.Length - 1; i >= 0; i--)
        {
            var poids = (chiffresSansCle.Length - 1 - i) % 2 == 0 ? 3 : 1;
            somme += (chiffresSansCle[i] - '0') * poids;
        }

        return (10 - (somme % 10)) % 10;
    }
}
