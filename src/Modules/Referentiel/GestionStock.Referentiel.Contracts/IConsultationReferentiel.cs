namespace GestionStock.Referentiel.Contracts;

/// <summary>Lecture du référentiel (articles, site, emplacements) par les autres modules.</summary>
public interface IConsultationReferentiel
{
    /// <summary>Article, actif ou non ; null s'il n'existe pas.</summary>
    Task<ArticleReferentiel?> ObtenirArticleAsync(Guid articleId, CancellationToken annulation = default);

    /// <summary>
    /// Site de la boutique et son emplacement de vente (V1 mono-site, RG20). Sans gestion par emplacements,
    /// la marchandise reçue y est disponible dès la validation (RG13).
    /// </summary>
    Task<SiteBoutique> ObtenirSiteBoutiqueAsync(CancellationToken annulation = default);
}

/// <summary>Article tel que le voient les autres modules.</summary>
/// <param name="PrecisionQuantite">Nombre de décimales autorisées pour une quantité (RG46), de 0 à 3.</param>
public sealed record ArticleReferentiel(Guid Id, string Code, string Libelle, string UniteStock, int PrecisionQuantite, bool Actif);

/// <summary>Site de la boutique et son emplacement de vente.</summary>
public sealed record SiteBoutique(Guid SiteId, string Code, string Nom, Guid EmplacementVenteId);
