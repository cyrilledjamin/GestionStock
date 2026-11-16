namespace GestionStock.Inventaire.Contracts;

/// <summary>Inventaire vu par les autres modules (reprise des données, décision 29).</summary>
public interface IServiceInventaire
{
    /// <summary>
    /// Crée l'inventaire d'ouverture de la boutique : un article par ligne, à compter, avec son coût d'achat de reprise
    /// qui valorisera le stock compté (CUMP). Participe à l'unité de travail de l'appelant.
    /// </summary>
    Task<InventaireCree> CreerOuvertureAsync(IReadOnlyList<LigneOuverture> lignes, CancellationToken annulation = default);
}

/// <summary>Article à compter à l'ouverture et son coût unitaire d'achat (hors TVA récupérable, RG30).</summary>
public sealed record LigneOuverture(Guid ArticleId, decimal CoutUnitaire);

public sealed record InventaireCree(Guid Id, string Numero);

/// <summary>Types de documents du module dans le journal des mouvements de stock (décision 28).</summary>
public static class TypesDocumentsInventaire
{
    public static readonly string Inventaire = "Inventaire";

    public static readonly string Demarque = "DeclarationDemarque";
}
