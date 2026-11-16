namespace GestionStock.Inventaire.Domain;

/// <summary>Motifs de démarque connue (RG23, RG47).</summary>
internal enum MotifDemarque
{
    Casse = 1,
    Vol = 2,
    Perime = 3,
    Freinte = 4,
}

internal enum StatutDemarque
{
    Declaree = 1,
    Validee = 2,
    Refusee = 3,
}

/// <summary>
/// Démarque connue (UC20) : déclarée par le Magasinier, validée ou refusée par le Comptable ou le Gérant (8.3). La
/// validation écrit un mouvement Demarque ; le motif est porté par ce document, que le mouvement référence.
/// </summary>
internal sealed class DeclarationDemarque
{
    public const int LongueurMaxCommentaire = 500;

    private DeclarationDemarque()
    {
    }

    public Guid Id { get; private set; }

    public string Numero { get; private set; } = "";

    public Guid ArticleId { get; private set; }

    public string CodeArticle { get; private set; } = "";

    public string Libelle { get; private set; } = "";

    public string UniteStock { get; private set; } = "";

    /// <summary>Quantité perdue, en unités de stock.</summary>
    public decimal Quantite { get; private set; }

    public MotifDemarque Motif { get; private set; }

    public string? Commentaire { get; private set; }

    public StatutDemarque Statut { get; private set; }

    public DateTime DeclareeLeUtc { get; private set; }

    public string DeclareePar { get; private set; } = "";

    public DateTime? TraiteeLeUtc { get; private set; }

    public string? TraiteePar { get; private set; }

    public string? MotifRefus { get; private set; }

    public static DeclarationDemarque Declarer(string numero, ArticleAInventorier article, decimal quantite, MotifDemarque motif, string? commentaire, string utilisateur, DateTime maintenantUtc)
    {
        ArgumentNullException.ThrowIfNull(article);
        if (quantite <= 0m || decimal.Round(quantite, article.PrecisionQuantite) != quantite)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.QuantiteInvalide, article.Code);
        }

        if (!Enum.IsDefined(motif))
        {
            throw new ExceptionInventaire(CodesErreurInventaire.MotifInvalide);
        }

        var note = commentaire?.Trim();
        return new DeclarationDemarque
        {
            Id = Guid.CreateVersion7(),
            Numero = numero,
            ArticleId = article.ArticleId,
            CodeArticle = article.Code,
            Libelle = article.Libelle,
            UniteStock = article.UniteStock,
            Quantite = quantite,
            Motif = motif,
            Commentaire = string.IsNullOrEmpty(note) ? null : note[..Math.Min(note.Length, LongueurMaxCommentaire)],
            Statut = StatutDemarque.Declaree,
            DeclareeLeUtc = maintenantUtc,
            DeclareePar = utilisateur,
        };
    }

    public void Valider(string utilisateur, DateTime maintenantUtc)
    {
        ExigerDeclaree();
        Statut = StatutDemarque.Validee;
        TraiteePar = utilisateur;
        TraiteeLeUtc = maintenantUtc;
    }

    public void Refuser(string motifRefus, string utilisateur, DateTime maintenantUtc)
    {
        ExigerDeclaree();
        var motif = motifRefus?.Trim() ?? "";
        if (motif.Length == 0)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.MotifRefusObligatoire);
        }

        Statut = StatutDemarque.Refusee;
        MotifRefus = motif[..Math.Min(motif.Length, LongueurMaxCommentaire)];
        TraiteePar = utilisateur;
        TraiteeLeUtc = maintenantUtc;
    }

    private void ExigerDeclaree()
    {
        if (Statut != StatutDemarque.Declaree)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.DemarqueDejaTraitee);
        }
    }
}
