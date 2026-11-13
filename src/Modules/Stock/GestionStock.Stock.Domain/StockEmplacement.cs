namespace GestionStock.Stock.Domain;

/// <summary>
/// Solde article × emplacement × lot : répond à « où est la marchandise ? ».
/// Projection du journal, recalculable comme <see cref="StockSite"/>.
/// </summary>
public sealed class StockEmplacement
{
    private StockEmplacement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ArticleId { get; private set; }

    public Guid EmplacementId { get; private set; }

    /// <summary>Lot (V2) ; null en V1.</summary>
    public Guid? LotId { get; private set; }

    public decimal QuantitePhysique { get; private set; }

    /// <summary>Ouvre un solde vide.</summary>
    public static StockEmplacement Ouvrir(Guid articleId, Guid emplacementId, Guid? lotId = null)
    {
        if (articleId == Guid.Empty || emplacementId == Guid.Empty || lotId == Guid.Empty)
        {
            throw new ExceptionDomaineStock(CodesErreurStock.IdentifiantObligatoire, "L'article et l'emplacement sont obligatoires.");
        }

        return new StockEmplacement
        {
            Id = Guid.CreateVersion7(),
            ArticleId = articleId,
            EmplacementId = emplacementId,
            LotId = lotId,
        };
    }

    /// <summary>
    /// Recalcule un solde en rejouant le journal, sans contrôle de négativité :
    /// l'historique est constaté, pas rejugé.
    /// </summary>
    public static StockEmplacement Recalculer(Guid articleId, Guid emplacementId, Guid? lotId, IEnumerable<MouvementStock> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var solde = Ouvrir(articleId, emplacementId, lotId);
        foreach (var mouvement in journal)
        {
            solde.QuantitePhysique += solde.Variation(mouvement);
        }

        return solde;
    }

    /// <summary>
    /// Applique un mouvement dont cet emplacement est la source ou la destination.
    /// Le stock ne peut devenir négatif (RG03), sauf sur un emplacement de vente (RG22) :
    /// un passage en caisse n'est jamais refusé pour stock insuffisant.
    /// </summary>
    /// <param name="mouvement">Mouvement à appliquer.</param>
    /// <param name="typeEmplacement">Type de cet emplacement, fourni par le Référentiel.</param>
    public void Appliquer(MouvementStock mouvement, TypeEmplacement typeEmplacement)
    {
        ArgumentNullException.ThrowIfNull(mouvement);
        var nouvelleQuantite = QuantitePhysique + Variation(mouvement);
        if (nouvelleQuantite < 0m && typeEmplacement != TypeEmplacement.Vente)
        {
            throw new ExceptionDomaineStock(
                CodesErreurStock.StockInsuffisant,
                $"Stock insuffisant sur l'emplacement : {QuantitePhysique} disponible(s), {mouvement.Quantite} demandé(s).");
        }

        QuantitePhysique = nouvelleQuantite;
    }

    private decimal Variation(MouvementStock mouvement)
    {
        if (mouvement.ArticleId != ArticleId || mouvement.LotId != LotId)
        {
            throw new ExceptionDomaineStock(CodesErreurStock.MouvementNonConcerne, "Le mouvement ne concerne pas ce solde.");
        }

        if (mouvement.EmplacementDestinationId == EmplacementId)
        {
            return mouvement.Quantite;
        }

        if (mouvement.EmplacementSourceId == EmplacementId)
        {
            return -mouvement.Quantite;
        }

        throw new ExceptionDomaineStock(CodesErreurStock.MouvementNonConcerne, "Le mouvement ne concerne pas cet emplacement.");
    }
}
