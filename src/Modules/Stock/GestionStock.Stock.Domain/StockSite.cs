namespace GestionStock.Stock.Domain;

/// <summary>
/// Solde article × site : physique, réservé, bloqué et CUMP. Projection du journal des mouvements,
/// mise à jour dans la même transaction que le mouvement et toujours recalculable (ENF04).
/// C'est la ligne contrôlée lors d'une réservation (RG04, section 7.3).
/// </summary>
public sealed class StockSite
{
    private StockSite()
    {
    }

    public Guid ArticleId { get; private set; }

    public Guid SiteId { get; private set; }

    public decimal QuantitePhysique { get; private set; }

    /// <summary>Réservé par les commandes clients (V2) ; reste à 0 en V1.</summary>
    public decimal QuantiteReservee { get; private set; }

    /// <summary>Bloqué (quarantaine, lot bloqué ou périmé) ; reste à 0 en V1.</summary>
    public decimal QuantiteBloquee { get; private set; }

    /// <summary>Coût unitaire moyen pondéré, en XAF avec 4 décimales (RG17, RG32).</summary>
    public decimal Cump { get; private set; }

    /// <summary>Disponible = physique − réservé − bloqué (RG02).</summary>
    public decimal QuantiteDisponible => QuantitePhysique - QuantiteReservee - QuantiteBloquee;

    /// <summary>Valeur du stock du site au CUMP.</summary>
    public decimal Valeur => decimal.Round(QuantitePhysique * Cump, MouvementStock.DecimalesMontant, MidpointRounding.AwayFromZero);

    /// <summary>Ouvre un solde vide pour un article sur un site.</summary>
    public static StockSite Ouvrir(Guid articleId, Guid siteId)
    {
        if (articleId == Guid.Empty || siteId == Guid.Empty)
        {
            throw new ExceptionDomaineStock(CodesErreurStock.IdentifiantObligatoire, "L'article et le site sont obligatoires.");
        }

        return new StockSite { ArticleId = articleId, SiteId = siteId };
    }

    /// <summary>Recalcule un solde en rejouant le journal dans l'ordre (contrôle de cohérence, ENF04).</summary>
    public static StockSite Recalculer(Guid articleId, Guid siteId, IEnumerable<MouvementStock> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var solde = Ouvrir(articleId, siteId);
        foreach (var mouvement in journal)
        {
            solde.Appliquer(mouvement);
        }

        return solde;
    }

    /// <summary>
    /// Applique un mouvement. Une entrée recalcule le CUMP (RG17) ; une sortie ne le change pas ;
    /// un mouvement interne ne change pas le site ; une revalorisation (RG30) répartit son montant sur le stock
    /// physique présent, sans changer la quantité. Le site peut devenir négatif par les ventes en caisse (RG22) :
    /// le contrôle de RG03 se fait par emplacement (<see cref="StockEmplacement"/>).
    /// </summary>
    public void Appliquer(MouvementStock mouvement)
    {
        ArgumentNullException.ThrowIfNull(mouvement);
        if (mouvement.ArticleId != ArticleId || mouvement.SiteId != SiteId)
        {
            throw new ExceptionDomaineStock(CodesErreurStock.MouvementNonConcerne, "Le mouvement ne concerne pas ce solde.");
        }

        if (mouvement.Sens == SensMouvement.Entree)
        {
            Cump = NouveauCump(mouvement.Quantite, mouvement.CoutUnitaire);
        }
        else if (mouvement.Type == TypeMouvement.Revalorisation)
        {
            Revaloriser(mouvement);
        }

        QuantitePhysique += mouvement.VariationSite;
    }

    /// <summary>
    /// La quantité revalorisée doit encore être en stock : la part des frais qui concerne de la marchandise déjà
    /// sortie n'entre pas dans le CUMP (le document d'origine la conserve).
    /// </summary>
    private void Revaloriser(MouvementStock mouvement)
    {
        if (QuantitePhysique <= 0m || mouvement.Quantite > QuantitePhysique)
        {
            throw new ExceptionDomaineStock(
                CodesErreurStock.RevalorisationSansStock,
                $"Revalorisation de {mouvement.Quantite} unité(s) impossible : {QuantitePhysique} en stock.");
        }

        var cump = Cump + (mouvement.MontantRevalorisation / QuantitePhysique);
        Cump = decimal.Round(cump, MouvementStock.DecimalesMontant, MidpointRounding.AwayFromZero);
    }

    private decimal NouveauCump(decimal quantiteEntree, decimal coutEntree)
    {
        // Stock nul ou négatif (ventes en caisse avant réception) : le coût de l'entrée devient le CUMP,
        // sans quoi la moyenne pondérée serait indéfinie ou absurde.
        if (QuantitePhysique <= 0m)
        {
            return coutEntree;
        }

        var cump = ((QuantitePhysique * Cump) + (quantiteEntree * coutEntree)) / (QuantitePhysique + quantiteEntree);
        return decimal.Round(cump, MouvementStock.DecimalesMontant, MidpointRounding.AwayFromZero);
    }
}
