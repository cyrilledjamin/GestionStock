using GestionStock.Referentiel.Contracts;
using GestionStock.Referentiel.Domain;
using GestionStock.Referentiel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionStock.Referentiel.Application;

/// <summary>Article affiché dans le back-office.</summary>
internal sealed record ArticleDto(
    Guid Id,
    string Code,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int PrecisionQuantite,
    bool Actif);

/// <summary>Données saisies à la création d'un article.</summary>
internal sealed record NouvelArticle(string Code, string Libelle, string? LibelleTraduit, string UniteStock, int PrecisionQuantite);

/// <summary>Données modifiables d'un article (le code, l'unité et la précision sont fixés à la création).</summary>
internal sealed record ModificationArticle(string Libelle, string? LibelleTraduit, bool Actif);

/// <summary>Gestion des articles (UC01) et lecture du référentiel par les autres modules.</summary>
internal sealed class ServiceArticles(ReferentielDbContext contexte) : IConsultationReferentiel
{
    /// <summary>Code d'erreur PostgreSQL d'une violation d'unicité.</summary>
    private const string ViolationUnicite = "23505";

    public async Task<IReadOnlyList<ArticleDto>> ListerAsync(string? recherche, bool inclureInactifs, CancellationToken annulation)
    {
        var requete = contexte.Articles.AsNoTracking();
        if (!inclureInactifs)
        {
            requete = requete.Where(a => a.Actif);
        }

        if (!string.IsNullOrWhiteSpace(recherche))
        {
            var motif = $"%{recherche.Trim()}%";
            requete = requete.Where(a =>
                EF.Functions.ILike(a.Code, motif)
                || EF.Functions.ILike(a.Libelle, motif)
                || (a.LibelleTraduit != null && EF.Functions.ILike(a.LibelleTraduit, motif)));
        }

        return await requete.OrderBy(a => a.Code).Select(a => VersDto(a)).ToListAsync(annulation);
    }

    public async Task<ArticleDto> ObtenirAsync(Guid id, CancellationToken annulation) =>
        VersDto(await Trouver(id, annulation));

    /// <exception cref="ExceptionReferentiel">Saisie invalide ou code déjà utilisé.</exception>
    public async Task<ArticleDto> CreerAsync(NouvelArticle saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var article = Article.Creer(saisie.Code, saisie.Libelle, saisie.LibelleTraduit, saisie.UniteStock, saisie.PrecisionQuantite);

        if (await contexte.Articles.AnyAsync(a => a.Code == article.Code, annulation))
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.CodeDejaUtilise);
        }

        contexte.Articles.Add(article);
        try
        {
            await contexte.SaveChangesAsync(annulation);
        }
        catch (DbUpdateException erreur) when (erreur.InnerException is PostgresException { SqlState: ViolationUnicite })
        {
            // Création simultanée du même code.
            throw new ExceptionReferentiel(CodesErreurReferentiel.CodeDejaUtilise);
        }

        return VersDto(article);
    }

    /// <exception cref="ExceptionReferentiel">Article introuvable ou saisie invalide.</exception>
    public async Task<ArticleDto> ModifierAsync(Guid id, ModificationArticle saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var article = await Trouver(id, annulation);
        article.Modifier(saisie.Libelle, saisie.LibelleTraduit, saisie.Actif);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(article);
    }

    public async Task<ArticleReferentiel?> ObtenirArticleAsync(Guid articleId, CancellationToken annulation = default) =>
        await contexte.Articles.AsNoTracking()
            .Where(a => a.Id == articleId)
            .Select(a => new ArticleReferentiel(a.Id, a.Code, a.Libelle, a.UniteStock, a.PrecisionQuantite, a.Actif))
            .SingleOrDefaultAsync(annulation);

    public async Task<SiteBoutique> ObtenirSiteBoutiqueAsync(CancellationToken annulation = default)
    {
        var site = await contexte.Sites.AsNoTracking()
            .Include(s => s.Emplacements)
            .Where(s => s.Type == TypeSite.Magasin)
            .OrderBy(s => s.Code)
            .FirstOrDefaultAsync(annulation)
            ?? throw new InvalidOperationException("Aucun site de type Magasin : la migration du référentiel n'est pas appliquée.");

        var vente = site.Emplacements.Where(e => e.Type == TypeEmplacement.Vente && e.Actif).OrderBy(e => e.Code).FirstOrDefault()
            ?? throw new InvalidOperationException($"Le magasin {site.Code} n'a pas d'emplacement de vente actif (RG20).");

        return new SiteBoutique(site.Id, site.Code, site.Nom, vente.Id);
    }

    private async Task<Article> Trouver(Guid id, CancellationToken annulation) =>
        await contexte.Articles.SingleOrDefaultAsync(a => a.Id == id, annulation)
        ?? throw new ExceptionReferentiel(CodesErreurReferentiel.ArticleIntrouvable);

    private static ArticleDto VersDto(Article a) =>
        new(a.Id, a.Code, a.Libelle, a.LibelleTraduit, a.UniteStock, a.PrecisionQuantite, a.Actif);
}
