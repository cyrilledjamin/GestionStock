using GestionStock.Referentiel.Contracts;

namespace GestionStock.Referentiel.Domain;

/// <summary>Type de code (section 4.5). En V1 : codes linéaires EAN / UPC et codes internes.</summary>
internal enum TypeCode
{
    Ean13 = 1,
    Ean8 = 2,
    UpcA = 3,
    Gs1_128 = 4,
    Gs1DataMatrix = 5,
    QrGs1DigitalLink = 6,
    Code128Interne = 7,
    QrInterne = 8,
}

/// <summary>
/// Code d'un conditionnement (RG26) : un article peut en avoir plusieurs (GTIN par conditionnement, code interne).
/// Un code actif désigne un seul conditionnement (index unique filtré, 4.6). Un code réattribué est désactivé,
/// jamais supprimé. À la création, la clé de contrôle est vérifiée (RG33).
/// </summary>
internal sealed class CodeArticle
{
    public const int LongueurMaxValeur = 50;

    /// <summary>
    /// Préfixe des codes internes générés (étiquettes des articles sans GTIN fiable, RG33). Pas de préfixe « 2 » :
    /// il est réservé aux codes à poids ou prix variable (RG27).
    /// </summary>
    public const string PrefixeInterne = "INT";

    private CodeArticle()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConditionnementId { get; private set; }

    public string Valeur { get; private set; } = "";

    public TypeCode Type { get; private set; }

    public bool Principal { get; private set; }

    public bool Actif { get; private set; } = true;

    /// <summary>Code GTIN saisi ou scanné : EAN-13, EAN-8 ou UPC-A, clé de contrôle vérifiée (RG33).</summary>
    public static CodeArticle Gtin(Guid conditionnementId, string valeur, bool principal)
    {
        var nettoyee = valeur?.Trim() ?? "";
        var type = nettoyee.Length switch
        {
            13 => TypeCode.Ean13,
            8 => TypeCode.Ean8,
            12 => TypeCode.UpcA,
            _ => throw new ExceptionReferentiel(CodesErreurReferentiel.CodeInvalide),
        };

        if (!CodesBarres.EstGtinValide(nettoyee))
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.CodeInvalide);
        }

        return new CodeArticle
        {
            Id = Guid.CreateVersion7(),
            ConditionnementId = conditionnementId,
            Valeur = nettoyee,
            Type = type,
            Principal = principal,
        };
    }

    /// <summary>Code interne généré à partir d'un numéro de séquence (ex. INT000042), imprimé en Code 128.</summary>
    public static CodeArticle Interne(Guid conditionnementId, long numero, bool principal) => new()
    {
        Id = Guid.CreateVersion7(),
        ConditionnementId = conditionnementId,
        Valeur = $"{PrefixeInterne}{numero:D6}",
        Type = TypeCode.Code128Interne,
        Principal = principal,
    };

    public void Desactiver() => Actif = false;
}
