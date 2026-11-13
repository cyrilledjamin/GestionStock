namespace GestionStock.Stock.Contracts;

/// <summary>Lecture des soldes de stock par les autres modules.</summary>
public interface IConsultationStock
{
    /// <summary>Solde d'un article sur un site ; null si aucun mouvement n'a encore eu lieu.</summary>
    Task<SoldeStockSite?> ObtenirSoldeSiteAsync(Guid articleId, Guid siteId, CancellationToken annulation = default);
}

/// <summary>Solde article × site.</summary>
public sealed record SoldeStockSite(
    Guid ArticleId,
    Guid SiteId,
    decimal QuantitePhysique,
    decimal QuantiteReservee,
    decimal QuantiteBloquee,
    decimal QuantiteDisponible,
    decimal Cump);
