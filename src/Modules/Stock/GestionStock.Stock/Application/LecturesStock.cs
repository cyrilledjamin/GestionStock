using GestionStock.Referentiel.Contracts;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Application;

/// <summary>Ligne du journal des mouvements, telle qu'affichée (UC14).</summary>
/// <param name="Variation">Effet sur la quantité du site : positif en entrée, négatif en sortie, nul en interne.</param>
internal sealed record MouvementAffiche(
    long Id,
    DateTime DateUtc,
    TypeMouvementStock Type,
    decimal Quantite,
    decimal Variation,
    decimal CoutUnitaire,
    string TypeDocument,
    Guid DocumentId,
    string Utilisateur,
    Guid? EmplacementSourceId,
    Guid? EmplacementDestinationId);

/// <summary>Consultation des disponibilités et des mouvements (UC14), sur le site de la boutique par défaut.</summary>
internal sealed class LecturesStock(StockDbContext contexte, IConsultationReferentiel referentiel)
{
    /// <summary>Nombre maximal de mouvements renvoyés par défaut (ENF10 : échanges paginés).</summary>
    public const int LimiteParDefaut = 100;

    public const int LimiteMax = 1000;

    public async Task<IReadOnlyList<SoldeStockSite>> ListerSoldesAsync(Guid? siteId, CancellationToken annulation)
    {
        var site = siteId ?? (await referentiel.ObtenirSiteBoutiqueAsync(annulation)).SiteId;
        var soldes = await contexte.StocksSites.AsNoTracking()
            .Where(s => s.SiteId == site)
            .ToListAsync(annulation);
        return [.. soldes.Select(s => s.VersContrat())];
    }

    /// <summary>Mouvements d'un article sur un site, du plus récent au plus ancien.</summary>
    public async Task<IReadOnlyList<MouvementAffiche>> ListerMouvementsAsync(Guid articleId, Guid? siteId, int? limite, CancellationToken annulation)
    {
        var site = siteId ?? (await referentiel.ObtenirSiteBoutiqueAsync(annulation)).SiteId;
        var nombre = Math.Clamp(limite ?? LimiteParDefaut, 1, LimiteMax);
        var mouvements = await contexte.Mouvements.AsNoTracking()
            .Where(m => m.ArticleId == articleId && m.SiteId == site)
            .OrderByDescending(m => m.Id)
            .Take(nombre)
            .ToListAsync(annulation);

        return [.. mouvements.Select(m => new MouvementAffiche(
            m.Id,
            m.DateUtc,
            m.Type.VersContrat(),
            m.Quantite,
            m.VariationSite,
            m.CoutUnitaire,
            m.TypeDocument,
            m.DocumentId,
            m.Utilisateur,
            m.EmplacementSourceId,
            m.EmplacementDestinationId))];
    }
}
