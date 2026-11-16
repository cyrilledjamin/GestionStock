namespace GestionStock.Inventaire.Domain;

/// <summary>Règle de l'inventaire ou de la démarque violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionInventaire(string code, string? detail = null) : Exception(code)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}

internal static class CodesErreurInventaire
{
    public const string InventaireIntrouvable = "Inventaire.InventaireIntrouvable";
    public const string InventaireSansLigne = "Inventaire.InventaireSansLigne";
    public const string InventaireTermine = "Inventaire.InventaireTermine";
    public const string TypeInvalide = "Inventaire.TypeInvalide";
    public const string LignesNonComptees = "Inventaire.LignesNonComptees";
    public const string ArticleIntrouvable = "Inventaire.ArticleIntrouvable";
    public const string ArticleInactif = "Inventaire.ArticleInactif";
    public const string QuantiteInvalide = "Inventaire.QuantiteInvalide";
    public const string CoutInvalide = "Inventaire.CoutInvalide";
    public const string FreinteSurEcartPositif = "Inventaire.FreinteSurEcartPositif";
    public const string DemarqueIntrouvable = "Inventaire.DemarqueIntrouvable";
    public const string DemarqueDejaTraitee = "Inventaire.DemarqueDejaTraitee";
    public const string MotifInvalide = "Inventaire.MotifInvalide";
    public const string MotifRefusObligatoire = "Inventaire.MotifRefusObligatoire";
    public const string MouvementRefuse = "Inventaire.MouvementRefuse";
}

/// <summary>Type d'inventaire : d'ouverture (reprise), complet (annuel) ou partiel (tournant).</summary>
internal enum TypeInventaire
{
    Ouverture = 1,
    Complet = 2,
    Partiel = 3,
}

/// <summary>Statuts d'un inventaire (valeurs non listées par le dossier : choix provisoire).</summary>
internal enum StatutInventaire
{
    EnCours = 1,
    Valide = 2,
    Annule = 3,
}

/// <summary>Classement d'un écart négatif : sans motif (ajustement) ou freinte du détail (RG47, mouvement Demarque).</summary>
internal enum MotifEcart
{
    Aucun = 0,
    Freinte = 1,
}

/// <summary>Article à inventorier (copie figée de son code et de son libellé pour l'affichage).</summary>
internal sealed record ArticleAInventorier(Guid ArticleId, string Code, string Libelle, string UniteStock, int PrecisionQuantite, decimal? CoutOuverture = null);

/// <summary>
/// Inventaire (UC11). Chaque ligne relève la quantité théorique au moment où elle est comptée (RG16) : la boutique
/// continue de vendre pendant le comptage, l'écart reste juste. La validation écrit un mouvement par écart (RG15) ;
/// ensuite l'inventaire ne change plus.
/// </summary>
internal sealed class Inventaire
{
    private Inventaire()
    {
    }

    public Guid Id { get; private set; }

    public string Numero { get; private set; } = "";

    public TypeInventaire Type { get; private set; }

    public StatutInventaire Statut { get; private set; }

    public DateTime CreeLeUtc { get; private set; }

    public string CreePar { get; private set; } = "";

    public DateTime? TermineLeUtc { get; private set; }

    /// <summary>Comptable qui a validé, ou utilisateur qui a annulé.</summary>
    public string? TerminePar { get; private set; }

    public List<LigneInventaire> Lignes { get; private set; } = [];

    public static Inventaire Creer(string numero, TypeInventaire type, IReadOnlyList<ArticleAInventorier> articles, string utilisateur, DateTime maintenantUtc)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ExceptionInventaire(CodesErreurInventaire.TypeInvalide);
        }

        // Un inventaire partiel peut démarrer vide : les articles s'ajoutent au fil du comptage (scan).
        if (articles.Count == 0 && type != TypeInventaire.Partiel)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.InventaireSansLigne);
        }

        var inventaire = new Inventaire
        {
            Id = Guid.CreateVersion7(),
            Numero = numero,
            Type = type,
            Statut = StatutInventaire.EnCours,
            CreeLeUtc = maintenantUtc,
            CreePar = utilisateur,
        };
        foreach (var article in articles.DistinctBy(a => a.ArticleId))
        {
            inventaire.Lignes.Add(LigneInventaire.Creer(inventaire.Id, article));
        }

        return inventaire;
    }

    /// <summary>
    /// Enregistre (ou corrige) le comptage d'un article ; un article absent de la liste y est ajouté (comptage par
    /// scan d'un article oublié). La quantité théorique est relevée maintenant.
    /// </summary>
    public LigneInventaire Compter(ArticleAInventorier article, decimal quantite, decimal quantiteTheorique, MotifEcart motif, string utilisateur, DateTime maintenantUtc)
    {
        ArgumentNullException.ThrowIfNull(article);
        ExigerEnCours();
        var ligne = Lignes.SingleOrDefault(l => l.ArticleId == article.ArticleId);
        if (ligne is null)
        {
            ligne = LigneInventaire.Creer(Id, article);
            Lignes.Add(ligne);
        }

        ligne.Compter(quantite, quantiteTheorique, motif, utilisateur, maintenantUtc);
        return ligne;
    }

    /// <summary>Validation : toutes les lignes doivent être comptées (pas de zéro implicite).</summary>
    public void Valider(string utilisateur, DateTime maintenantUtc)
    {
        ExigerEnCours();
        if (Lignes.Count == 0)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.InventaireSansLigne);
        }

        var nonComptees = Lignes.Where(l => l.QuantiteComptee is null).Select(l => l.CodeArticle).ToList();
        if (nonComptees.Count > 0)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.LignesNonComptees, string.Join(", ", nonComptees.Take(10)) + (nonComptees.Count > 10 ? "…" : ""));
        }

        Statut = StatutInventaire.Valide;
        TerminePar = utilisateur;
        TermineLeUtc = maintenantUtc;
    }

    public void Annuler(string utilisateur, DateTime maintenantUtc)
    {
        ExigerEnCours();
        Statut = StatutInventaire.Annule;
        TerminePar = utilisateur;
        TermineLeUtc = maintenantUtc;
    }

    private void ExigerEnCours()
    {
        if (Statut != StatutInventaire.EnCours)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.InventaireTermine);
        }
    }
}

/// <summary>Ligne d'inventaire : un article, compté en unités de stock.</summary>
internal sealed class LigneInventaire
{
    private LigneInventaire()
    {
    }

    public Guid Id { get; private set; }

    public Guid InventaireId { get; private set; }

    public Guid ArticleId { get; private set; }

    public string CodeArticle { get; private set; } = "";

    public string Libelle { get; private set; } = "";

    public string UniteStock { get; private set; } = "";

    public int PrecisionQuantite { get; private set; }

    /// <summary>Coût d'achat de reprise (inventaire d'ouverture) : valorise l'entrée en stock.</summary>
    public decimal? CoutOuverture { get; private set; }

    public decimal? QuantiteComptee { get; private set; }

    /// <summary>Stock physique du site relevé au moment du comptage (RG16).</summary>
    public decimal? QuantiteTheorique { get; private set; }

    public DateTime? ComptageUtc { get; private set; }

    public string? ComptePar { get; private set; }

    public MotifEcart MotifEcart { get; private set; }

    /// <summary>Compté moins théorique : positif s'il y a plus que prévu.</summary>
    public decimal? Ecart => QuantiteComptee - QuantiteTheorique;

    public static LigneInventaire Creer(Guid inventaireId, ArticleAInventorier article)
    {
        if (article.CoutOuverture is < 0m || (article.CoutOuverture is { } cout && decimal.Round(cout, 4) != cout))
        {
            throw new ExceptionInventaire(CodesErreurInventaire.CoutInvalide, article.Code);
        }

        return new LigneInventaire
        {
            Id = Guid.CreateVersion7(),
            InventaireId = inventaireId,
            ArticleId = article.ArticleId,
            CodeArticle = article.Code,
            Libelle = article.Libelle,
            UniteStock = article.UniteStock,
            PrecisionQuantite = article.PrecisionQuantite,
            CoutOuverture = article.CoutOuverture,
        };
    }

    public void Compter(decimal quantite, decimal quantiteTheorique, MotifEcart motif, string utilisateur, DateTime maintenantUtc)
    {
        if (quantite < 0m || decimal.Round(quantite, PrecisionQuantite) != quantite)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.QuantiteInvalide, CodeArticle);
        }

        if (!Enum.IsDefined(motif))
        {
            throw new ExceptionInventaire(CodesErreurInventaire.MotifInvalide);
        }

        // RG47 : la freinte est une perte ; elle ne qualifie qu'un écart négatif.
        if (motif == MotifEcart.Freinte && quantite >= quantiteTheorique)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.FreinteSurEcartPositif, CodeArticle);
        }

        QuantiteComptee = quantite;
        QuantiteTheorique = quantiteTheorique;
        MotifEcart = motif;
        ComptePar = utilisateur;
        ComptageUtc = maintenantUtc;
    }
}
