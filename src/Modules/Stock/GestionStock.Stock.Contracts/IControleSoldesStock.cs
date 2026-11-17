namespace GestionStock.Stock.Contracts;

/// <summary>
/// Contrôle de cohérence des soldes de stock (ENF04, 4.6) : chaque solde est comparé au rejeu de son journal. Le
/// journal fait foi ; une reconstruction remet les soldes à la valeur rejouée sans écrire aucun mouvement (RG01).
/// Appelé par le module Exploitation.
/// </summary>
public interface IControleSoldesStock
{
    /// <summary>Compare tous les soldes (article × site, article × emplacement × lot) à leur journal.</summary>
    Task<ControleStock> ControlerAsync(CancellationToken annulation = default);

    /// <summary>
    /// Reconstruit depuis le journal tous les soldes d'un article (sites et emplacements), sous verrou ; renvoie les
    /// écarts corrigés (vide si les soldes étaient justes).
    /// </summary>
    Task<IReadOnlyList<EcartStock>> ReconstruireAsync(Guid articleId, CancellationToken annulation = default);
}

/// <summary>Résultat d'un contrôle : nombre de soldes comparés et écarts trouvés.</summary>
public sealed record ControleStock(int SoldesControles, IReadOnlyList<EcartStock> Ecarts);

/// <summary>Projection contrôlée.</summary>
public enum SoldeStockControle
{
    /// <summary>Solde article × site (physique, réservé, bloqué, CUMP).</summary>
    Site = 1,

    /// <summary>Solde article × emplacement × lot (physique).</summary>
    Emplacement = 2,
}

/// <summary>Valeur du solde comparée à celle du journal.</summary>
public enum GrandeurStock
{
    QuantitePhysique = 1,
    QuantiteReservee = 2,
    QuantiteBloquee = 3,
    Cump = 4,
}

/// <summary>
/// Solde différent de son journal. <see cref="SiteId"/> est celui des mouvements de l'emplacement ; il est vide pour une
/// ligne d'emplacement qu'aucun mouvement ne concerne.
/// </summary>
public sealed record EcartStock(
    SoldeStockControle Solde,
    GrandeurStock Grandeur,
    Guid ArticleId,
    Guid? SiteId,
    Guid? EmplacementId,
    Guid? LotId,
    decimal Journal,
    decimal Enregistre);
