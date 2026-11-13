namespace GestionStock.Stock.Domain;

/// <summary>
/// Mouvement de stock immuable (RG01) : seule façon de faire varier le stock.
/// Une erreur se corrige par un mouvement inverse, jamais par modification ni suppression.
/// </summary>
public sealed class MouvementStock
{
    /// <summary>Nombre maximal de décimales d'une quantité (decimal(18,4), ENF07).</summary>
    public const int DecimalesQuantite = 4;

    /// <summary>Nombre maximal de décimales d'un coût (decimal(19,4), RG32).</summary>
    public const int DecimalesMontant = 4;

    // Types sans sens fixe ou dont l'effet n'est pas encore défini dans le dossier.
    private static readonly HashSet<TypeMouvement> TypesNonPrisEnCharge =
    [
        TypeMouvement.Blocage,
        TypeMouvement.Deblocage,
        TypeMouvement.Revalorisation,
    ];

    private MouvementStock()
    {
        TypeDocument = string.Empty;
        Utilisateur = string.Empty;
    }

    /// <summary>Identifiant séquentiel attribué par la base ; donne l'ordre du journal.</summary>
    public long Id { get; private set; }

    public DateTime DateUtc { get; private set; }

    public TypeMouvement Type { get; private set; }

    public Guid ArticleId { get; private set; }

    /// <summary>Site concerné ; un transfert entre sites produit un mouvement par site (RG14).</summary>
    public Guid SiteId { get; private set; }

    public Guid? EmplacementSourceId { get; private set; }

    public Guid? EmplacementDestinationId { get; private set; }

    /// <summary>Lot (V2) ; toujours null en V1.</summary>
    public Guid? LotId { get; private set; }

    /// <summary>Numéro de série (V2) ; toujours null en V1.</summary>
    public Guid? NumeroSerieId { get; private set; }

    /// <summary>Quantité en unité de stock, strictement positive ; le sens porte le signe.</summary>
    public decimal Quantite { get; private set; }

    /// <summary>Coût unitaire hors TVA récupérable, en XAF (RG30).</summary>
    public decimal CoutUnitaire { get; private set; }

    /// <summary>Type du document d'origine (clé générique, sans clé étrangère).</summary>
    public string TypeDocument { get; private set; }

    public Guid DocumentId { get; private set; }

    public string Utilisateur { get; private set; }

    /// <summary>Identifiant généré par le client : un renvoi après coupure n'est intégré qu'une fois.</summary>
    public Guid IdIdempotence { get; private set; }

    public SensMouvement Sens => (EmplacementSourceId, EmplacementDestinationId) switch
    {
        (null, not null) => SensMouvement.Entree,
        (not null, null) => SensMouvement.Sortie,
        _ => SensMouvement.Interne,
    };

    /// <summary>Variation de la quantité physique du site : positive en entrée, négative en sortie, nulle en interne.</summary>
    public decimal VariationSite => Sens switch
    {
        SensMouvement.Entree => Quantite,
        SensMouvement.Sortie => -Quantite,
        _ => 0m,
    };

    /// <summary>Crée un mouvement après contrôle de toutes ses règles.</summary>
    /// <exception cref="ExceptionDomaineStock">Si une règle est violée.</exception>
    public static MouvementStock Creer(
        TypeMouvement type,
        Guid articleId,
        Guid siteId,
        Guid? emplacementSourceId,
        Guid? emplacementDestinationId,
        decimal quantite,
        decimal coutUnitaire,
        string typeDocument,
        Guid documentId,
        string utilisateur,
        Guid idIdempotence,
        DateTime dateUtc,
        Guid? lotId = null,
        Guid? numeroSerieId = null)
    {
        if (TypesNonPrisEnCharge.Contains(type))
        {
            throw new ExceptionDomaineStock(CodesErreurStock.TypeNonPrisEnCharge, $"Le type de mouvement {type} n'est pas encore pris en charge.");
        }

        Exiger(articleId != Guid.Empty, CodesErreurStock.IdentifiantObligatoire, "L'article est obligatoire.");
        Exiger(siteId != Guid.Empty, CodesErreurStock.IdentifiantObligatoire, "Le site est obligatoire.");
        Exiger(emplacementSourceId != Guid.Empty && emplacementDestinationId != Guid.Empty, CodesErreurStock.IdentifiantObligatoire, "Un emplacement fourni ne peut pas être vide.");
        Exiger(quantite > 0m, CodesErreurStock.QuantiteNonPositive, "La quantité doit être strictement positive.");
        Exiger(decimal.Round(quantite, DecimalesQuantite) == quantite, CodesErreurStock.PrecisionQuantite, $"La quantité admet au plus {DecimalesQuantite} décimales.");
        Exiger(coutUnitaire >= 0m, CodesErreurStock.CoutNegatif, "Le coût unitaire ne peut pas être négatif.");
        Exiger(decimal.Round(coutUnitaire, DecimalesMontant) == coutUnitaire, CodesErreurStock.PrecisionQuantite, $"Le coût unitaire admet au plus {DecimalesMontant} décimales.");
        Exiger(!string.IsNullOrWhiteSpace(typeDocument) && documentId != Guid.Empty, CodesErreurStock.DocumentObligatoire, "Le document d'origine est obligatoire.");
        Exiger(!string.IsNullOrWhiteSpace(utilisateur), CodesErreurStock.UtilisateurObligatoire, "L'utilisateur est obligatoire.");
        Exiger(idIdempotence != Guid.Empty, CodesErreurStock.IdempotenceObligatoire, "L'identifiant d'idempotence est obligatoire.");
        Exiger(dateUtc.Kind == DateTimeKind.Utc, CodesErreurStock.DateNonUtc, "La date doit être en UTC.");
        Exiger(emplacementSourceId is not null || emplacementDestinationId is not null, CodesErreurStock.SensIncompatible, "Un mouvement a au moins une source ou une destination.");
        Exiger(emplacementSourceId is null || emplacementSourceId != emplacementDestinationId, CodesErreurStock.SourceEgaleDestination, "La source et la destination doivent différer.");

        var mouvement = new MouvementStock
        {
            Type = type,
            ArticleId = articleId,
            SiteId = siteId,
            EmplacementSourceId = emplacementSourceId,
            EmplacementDestinationId = emplacementDestinationId,
            LotId = lotId,
            NumeroSerieId = numeroSerieId,
            Quantite = quantite,
            CoutUnitaire = coutUnitaire,
            TypeDocument = typeDocument.Trim(),
            DocumentId = documentId,
            Utilisateur = utilisateur.Trim(),
            IdIdempotence = idIdempotence,
            DateUtc = dateUtc,
        };

        Exiger(SensAutorises(type).Contains(mouvement.Sens), CodesErreurStock.SensIncompatible, $"Un mouvement {type} ne peut pas être de sens {mouvement.Sens}.");
        return mouvement;
    }

    /// <summary>Sens admis pour chaque type de mouvement.</summary>
    public static IReadOnlySet<SensMouvement> SensAutorises(TypeMouvement type) => type switch
    {
        TypeMouvement.EntreeReception or TypeMouvement.TransfertEntree or TypeMouvement.AvoirCaisse or TypeMouvement.RetourClient
            => EnsembleEntree,
        TypeMouvement.SortieExpedition or TypeMouvement.TransfertSortie or TypeMouvement.VenteCaisse or TypeMouvement.RetourFournisseur or TypeMouvement.Rebut
            => EnsembleSortie,
        TypeMouvement.Rangement or TypeMouvement.Reassort
            => EnsembleInterne,

        // Écart d'inventaire positif (entrée) ou négatif (sortie).
        TypeMouvement.AjustementInventaire => EnsembleEntreeOuSortie,

        // Démarque : sortie du stock, ou transfert vers l'emplacement Démarque du magasin (RG20, RG23).
        TypeMouvement.Demarque => EnsembleSortieOuInterne,
        _ => EnsembleVide,
    };

    private static readonly HashSet<SensMouvement> EnsembleEntree = [SensMouvement.Entree];
    private static readonly HashSet<SensMouvement> EnsembleSortie = [SensMouvement.Sortie];
    private static readonly HashSet<SensMouvement> EnsembleInterne = [SensMouvement.Interne];
    private static readonly HashSet<SensMouvement> EnsembleEntreeOuSortie = [SensMouvement.Entree, SensMouvement.Sortie];
    private static readonly HashSet<SensMouvement> EnsembleSortieOuInterne = [SensMouvement.Sortie, SensMouvement.Interne];
    private static readonly HashSet<SensMouvement> EnsembleVide = [];

    private static void Exiger(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new ExceptionDomaineStock(code, message);
        }
    }
}
