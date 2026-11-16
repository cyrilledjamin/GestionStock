namespace GestionStock.Achats.Domain;

/// <summary>Statut d'une réception simple (V1, sans commande fournisseur).</summary>
internal enum StatutReception
{
    /// <summary>En saisie : modifiable, aucun effet sur le stock.</summary>
    Brouillon = 1,

    /// <summary>Validée : les mouvements d'entrée sont écrits ; plus jamais modifiée.</summary>
    Validee = 2,
}

/// <summary>Ligne saisie : article, quantité en unité de stock et coût unitaire.</summary>
internal sealed record SaisieLigneReception(Guid ArticleId, decimal Quantite, decimal CoutUnitaire);

/// <summary>
/// Réception d'une livraison fournisseur (UC04), en version simple : sans commande fournisseur, sans lots
/// ni frais d'approche. Saisie en brouillon, puis validée : la validation écrit un mouvement EntreeReception par ligne.
/// </summary>
internal sealed class Reception
{
    public const int LongueurMaxBonLivraison = 50;

    /// <summary>Décimales d'un coût unitaire (calculs internes en 4 décimales, RG32).</summary>
    public const int DecimalesCout = 4;

    private Reception()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Numéro lisible, attribué à la création (ex. R-000001).</summary>
    public string Numero { get; private set; } = "";

    public Guid FournisseurId { get; private set; }

    public string? NumeroBonLivraison { get; private set; }

    public StatutReception Statut { get; private set; }

    public DateTime DateCreationUtc { get; private set; }

    public string CreePar { get; private set; } = "";

    public DateTime? DateValidationUtc { get; private set; }

    public string? ValideePar { get; private set; }

    public List<LigneReception> Lignes { get; private set; } = [];

    public static Reception Creer(
        string numero,
        Guid fournisseurId,
        string? numeroBonLivraison,
        IReadOnlyList<SaisieLigneReception> lignes,
        string utilisateur,
        DateTime maintenantUtc)
    {
        var reception = new Reception
        {
            Id = Guid.CreateVersion7(),
            Numero = numero,
            Statut = StatutReception.Brouillon,
            DateCreationUtc = maintenantUtc,
            CreePar = utilisateur,
        };
        reception.Remplacer(fournisseurId, numeroBonLivraison, lignes);
        return reception;
    }

    /// <summary>Remplace l'en-tête et toutes les lignes d'un brouillon.</summary>
    public void Remplacer(Guid fournisseurId, string? numeroBonLivraison, IReadOnlyList<SaisieLigneReception> lignes)
    {
        ArgumentNullException.ThrowIfNull(lignes);
        ExigerBrouillon();

        var bonLivraison = numeroBonLivraison?.Trim();
        if (bonLivraison?.Length > LongueurMaxBonLivraison)
        {
            throw new ExceptionAchats(CodesErreurAchats.BonLivraisonTropLong);
        }

        if (lignes.Count == 0)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionSansLigne);
        }

        foreach (var ligne in lignes)
        {
            if (ligne.Quantite <= 0m)
            {
                throw new ExceptionAchats(CodesErreurAchats.QuantiteNonPositive);
            }

            if (ligne.CoutUnitaire < 0m)
            {
                throw new ExceptionAchats(CodesErreurAchats.CoutNegatif);
            }

            if (decimal.Round(ligne.CoutUnitaire, DecimalesCout) != ligne.CoutUnitaire)
            {
                throw new ExceptionAchats(CodesErreurAchats.PrecisionCout);
            }
        }

        FournisseurId = fournisseurId;
        NumeroBonLivraison = string.IsNullOrEmpty(bonLivraison) ? null : bonLivraison;
        Lignes.Clear();
        Lignes.AddRange(lignes.Select((l, index) => new LigneReception(Id, index + 1, l.ArticleId, l.Quantite, l.CoutUnitaire)));
    }

    public void Valider(string utilisateur, DateTime maintenantUtc)
    {
        ExigerBrouillon();
        Statut = StatutReception.Validee;
        ValideePar = utilisateur;
        DateValidationUtc = maintenantUtc;
    }

    private void ExigerBrouillon()
    {
        if (Statut == StatutReception.Validee)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionDejaValidee);
        }
    }
}

/// <summary>
/// Ligne de réception. Le coût unitaire n'existe pas dans le modèle UML (il vient de la commande fournisseur) :
/// il est saisi ici faute de commande en V1 (point ouvert du README).
/// </summary>
internal sealed class LigneReception
{
    public LigneReception(Guid receptionId, int ordre, Guid articleId, decimal quantite, decimal coutUnitaire)
    {
        Id = Guid.CreateVersion7();
        ReceptionId = receptionId;
        Ordre = ordre;
        ArticleId = articleId;
        Quantite = quantite;
        CoutUnitaire = coutUnitaire;
    }

    /// <summary>Sert aussi d'identifiant d'idempotence du mouvement de stock de la ligne.</summary>
    public Guid Id { get; private set; }

    public Guid ReceptionId { get; private set; }

    public int Ordre { get; private set; }

    public Guid ArticleId { get; private set; }

    /// <summary>Quantité en unité de stock de l'article.</summary>
    public decimal Quantite { get; private set; }

    /// <summary>Coût unitaire hors TVA récupérable, en XAF.</summary>
    public decimal CoutUnitaire { get; private set; }
}
