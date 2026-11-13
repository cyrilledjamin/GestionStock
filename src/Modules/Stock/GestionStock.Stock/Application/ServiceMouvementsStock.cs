using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Application;

/// <summary>
/// Écrit un mouvement et ses soldes dans une même transaction (RG01, section 4.6).
/// Les lignes de solde sont créées si besoin (INSERT … ON CONFLICT DO NOTHING) puis verrouillées
/// (SELECT … FOR UPDATE) : deux mouvements simultanés du même article s'enchaînent au lieu d'échouer,
/// y compris le tout premier mouvement d'un article sur un site.
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

            // Le verrou du solde site passe avant le contrôle d'idempotence : un renvoi simultané
            // de la même demande attend la fin de la première, puis voit son mouvement validé.
            var stockSite = await VerrouillerStockSite(mouvement, annulation);

            var existant = await contexte.Mouvements
                .Where(m => m.IdIdempotence == demande.IdIdempotence)
                .Select(m => (long?)m.Id)
                .FirstOrDefaultAsync(annulation);
            if (existant is not null)
            {
                return new ResultatMouvement(existant.Value, DejaEnregistre: true);
            }

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

    /// <summary>Crée au besoin le solde site (valeurs de <see cref="StockSite.Ouvrir"/> : tout à zéro), puis le verrouille.</summary>
    private async Task<StockSite> VerrouillerStockSite(MouvementStock mouvement, CancellationToken annulation)
    {
        await contexte.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stock.stock_site (article_id, site_id, quantite_physique, quantite_reservee, quantite_bloquee, cump)
            VALUES ({mouvement.ArticleId}, {mouvement.SiteId}, 0, 0, 0, 0)
            ON CONFLICT DO NOTHING
            """,
            annulation);

        return await contexte.StocksSites
            .FromSql($"SELECT * FROM stock.stock_site WHERE article_id = {mouvement.ArticleId} AND site_id = {mouvement.SiteId} FOR UPDATE")
            .SingleAsync(annulation);
    }

    /// <summary>Crée au besoin le solde emplacement (quantité nulle), puis le verrouille.</summary>
    private async Task<StockEmplacement> VerrouillerStockEmplacement(MouvementStock mouvement, Guid emplacementId, CancellationToken annulation)
    {
        await contexte.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stock.stock_emplacement (id, article_id, emplacement_id, lot_id, quantite_physique)
            VALUES ({Guid.CreateVersion7()}, {mouvement.ArticleId}, {emplacementId}, {mouvement.LotId}, 0)
            ON CONFLICT DO NOTHING
            """,
            annulation);

        return await contexte.StocksEmplacements
            .FromSql($"SELECT * FROM stock.stock_emplacement WHERE article_id = {mouvement.ArticleId} AND emplacement_id = {emplacementId} AND lot_id IS NOT DISTINCT FROM {mouvement.LotId} FOR UPDATE")
            .SingleAsync(annulation);
    }
}
