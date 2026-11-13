using GestionStock.Stock.Contracts;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Application;

/// <inheritdoc />
internal sealed class ConsultationStock(StockDbContext contexte) : IConsultationStock
{
    public async Task<SoldeStockSite?> ObtenirSoldeSiteAsync(Guid articleId, Guid siteId, CancellationToken annulation = default)
    {
        var solde = await contexte.StocksSites
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ArticleId == articleId && s.SiteId == siteId, annulation);
        return solde?.VersContrat();
    }
}
