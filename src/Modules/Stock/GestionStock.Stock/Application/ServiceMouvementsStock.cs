using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Application;

/// <summary>
/// Écrit un mouvement et ses soldes dans une même transaction (RG01, section 4.6).
/// Les lignes de solde sont verrouillées (SELECT … FOR UPDATE) : deux ventes simultanées
/// du même article s'enchaînent au lieu d'échouer en conflit de version.
/// </summary>
internal sealed class ServiceMouvementsStock(StockDbContext contexte, IUniteDeTravail uniteDeTravail, IHorloge horloge)
    : IServiceMouvementsStock
{
    public Task<ResultatMouvement> EnregistrerAsync(DemandeMouvement demande, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(demande);
        return uniteDeTravail.ExecuterAsync(jeton => Enregistrer(demande, jeton), annulation);
    }

    private async Task<ResultatMouvement> Enregistrer(DemandeMouvement demande, CancellationToken annulation)
    {
        var existant = await contexte.Mouvements
            .Where(m => m.IdIdempotence == demande.IdIdempotence)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(annulation);
        if (existant is not null)
        {
            return new ResultatMouvement(existant.Value, DejaEnregistre: true);
        }

        try
        {
            var mouvement = MouvementStock.Creer(
                demande.Type.VersDomaine(),
                demande.ArticleId,
                demande.SiteId,
                demande.Source?.EmplacementId,
                demande.Destination?.EmplacementId,
                demande.Quantite,
                demande.CoutUnitaire,
                demande.TypeDocument,
                demande.DocumentId,
                demande.Utilisateur,
                demande.IdIdempotence,
                demande.DateUtc ?? horloge.MaintenantUtc);

            var stockSite = await VerrouillerStockSite(mouvement, annulation);
            stockSite.Appliquer(mouvement);

            foreach (var emplacement in new[] { demande.Source, demande.Destination })
            {
                if (emplacement is not null)
                {
                    var stockEmplacement = await VerrouillerStockEmplacement(mouvement, emplacement.EmplacementId, annulation);
                    stockEmplacement.Appliquer(mouvement, emplacement.Type.VersDomaine());
                }
            }

            contexte.Mouvements.Add(mouvement);
            await contexte.SaveChangesAsync(annulation);
            return new ResultatMouvement(mouvement.Id, DejaEnregistre: false);
        }
        catch (ExceptionDomaineStock erreur)
        {
            throw new ExceptionStock(erreur.Code, erreur.Message, erreur);
        }
    }

    private async Task<StockSite> VerrouillerStockSite(MouvementStock mouvement, CancellationToken annulation)
    {
        var solde = await contexte.StocksSites
            .FromSql($"SELECT * FROM stock.stock_site WHERE article_id = {mouvement.ArticleId} AND site_id = {mouvement.SiteId} FOR UPDATE")
            .SingleOrDefaultAsync(annulation);
        if (solde is null)
        {
            solde = StockSite.Ouvrir(mouvement.ArticleId, mouvement.SiteId);
            contexte.StocksSites.Add(solde);
        }

        return solde;
    }

    private async Task<StockEmplacement> VerrouillerStockEmplacement(MouvementStock mouvement, Guid emplacementId, CancellationToken annulation)
    {
        var solde = await contexte.StocksEmplacements
            .FromSql($"SELECT * FROM stock.stock_emplacement WHERE article_id = {mouvement.ArticleId} AND emplacement_id = {emplacementId} AND lot_id IS NOT DISTINCT FROM {mouvement.LotId} FOR UPDATE")
            .SingleOrDefaultAsync(annulation);
        if (solde is null)
        {
            solde = StockEmplacement.Ouvrir(mouvement.ArticleId, emplacementId, mouvement.LotId);
            contexte.StocksEmplacements.Add(solde);
        }

        return solde;
    }
}
