namespace GestionStock.Referentiel.Domain;

/// <summary>Niveau d'un conditionnement (section 4.5).</summary>
internal enum NiveauConditionnement
{
    Unite = 1,
    Pack = 2,
    Carton = 3,
    Sac = 4,
    Palette = 5,
}

/// <summary>
/// Conditionnement d'achat ou de vente d'un article (RG18) : il se convertit en unités de stock par un facteur fixe.
/// Codes et prix sont rattachés au conditionnement (4.3) : scanner un carton compte ses unités et applique son prix.
/// Chaque article a d'office un conditionnement « Unité » de facteur 1. Le facteur ne change plus après la création
/// (les ventes l'utilisent) ; un conditionnement n'est jamais supprimé, il est désactivé.
/// </summary>
internal sealed class Conditionnement
{
    public const int LongueurMaxLibelle = 50;

    private Conditionnement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ArticleId { get; private set; }

    public string Libelle { get; private set; } = "";

    public NiveauConditionnement Niveau { get; private set; }

    /// <summary>Nombre d'unités de stock contenues (facteur de conversion, RG18).</summary>
    public decimal QuantiteUniteStock { get; private set; }

    public bool Actif { get; private set; } = true;

    /// <summary>Conditionnement « Unité » créé avec l'article : facteur 1, toujours actif.</summary>
    public bool EstUnite => Niveau == NiveauConditionnement.Unite && QuantiteUniteStock == 1m;

    public static Conditionnement Unite(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return new Conditionnement
        {
            Id = Guid.CreateVersion7(),
            ArticleId = article.Id,
            Libelle = article.UniteStock,
            Niveau = NiveauConditionnement.Unite,
            QuantiteUniteStock = 1m,
        };
    }

    /// <summary>
    /// Nouveau conditionnement. Le facteur est strictement positif et respecte la précision de l'article
    /// (RG46) : un carton d'un article vendu à la pièce contient un nombre entier de pièces.
    /// </summary>
    public static Conditionnement Creer(Article article, string libelle, NiveauConditionnement niveau, decimal quantiteUniteStock)
    {
        ArgumentNullException.ThrowIfNull(article);
        if (!Enum.IsDefined(niveau))
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.NiveauInvalide);
        }

        if (quantiteUniteStock <= 0m || decimal.Round(quantiteUniteStock, article.PrecisionQuantite) != quantiteUniteStock)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.FacteurInvalide);
        }

        var conditionnement = new Conditionnement
        {
            Id = Guid.CreateVersion7(),
            ArticleId = article.Id,
            Niveau = niveau,
            QuantiteUniteStock = quantiteUniteStock,
        };
        conditionnement.Modifier(libelle, actif: true);
        return conditionnement;
    }

    public void Modifier(string libelle, bool actif)
    {
        var nettoye = libelle?.Trim() ?? "";
        if (nettoye.Length == 0)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.LibelleObligatoire);
        }

        if (nettoye.Length > LongueurMaxLibelle)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.LibelleTropLong);
        }

        if (EstUnite && !actif)
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.UniteNonDesactivable);
        }

        Libelle = nettoye;
        Actif = actif;
    }
}
