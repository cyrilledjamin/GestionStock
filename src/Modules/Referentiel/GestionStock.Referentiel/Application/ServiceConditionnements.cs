using GestionStock.Referentiel.Domain;
using GestionStock.Referentiel.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TypeCode = GestionStock.Referentiel.Domain.TypeCode;

namespace GestionStock.Referentiel.Application;

internal sealed record CodeDto(Guid Id, string Valeur, TypeCode Type, bool Principal, bool Actif);

internal sealed record ConditionnementDto(
    Guid Id,
    Guid ArticleId,
    string Libelle,
    NiveauConditionnement Niveau,
    decimal QuantiteUniteStock,
    bool Actif,
    bool VenteRapide,
    IReadOnlyList<CodeDto> Codes);

internal sealed record NouveauConditionnement(string Libelle, NiveauConditionnement Niveau, decimal QuantiteUniteStock);

internal sealed record ModificationConditionnement(string Libelle, bool Actif);

internal sealed record NouveauCode(string Valeur);

internal sealed record ChoixVenteRapide(bool VenteRapide);

/// <summary>Conditionnement vendable avec ses codes actifs : de quoi identifier un scan sur le poste hors ligne.</summary>
internal sealed record VendableCatalogue(
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int PrecisionQuantite,
    Guid ConditionnementId,
    string LibelleConditionnement,
    decimal QuantiteUniteStock,
    bool EstUnite,
    bool VenteRapide,
    IReadOnlyList<string> Codes);

/// <summary>Catalogue de la caisse (7.8) : tout ce qui se vend aujourd'hui, à garder sur le poste pour vendre hors ligne.</summary>
internal sealed record CatalogueCaisse(DateTime GenereLeUtc, IReadOnlyList<VendableCatalogue> Vendables);

/// <summary>Article vendable tel qu'identifié par un scan ou une recherche (UC22) : article + conditionnement.</summary>
internal sealed record ArticleVendable(
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int PrecisionQuantite,
    Guid ConditionnementId,
    string LibelleConditionnement,
    decimal QuantiteUniteStock,
    string? CodeScanne);

/// <summary>Conditionnements, codes (RG18, RG26, RG33) et identification des articles par scan ou recherche (UC22).</summary>
internal sealed class ServiceConditionnements(ReferentielDbContext contexte)
{
    private const string ViolationUnicite = "23505";
    private const int LimiteRecherche = 30;

    public async Task<IReadOnlyList<ConditionnementDto>> ListerAsync(Guid articleId, CancellationToken annulation)
    {
        await TrouverArticle(articleId, annulation);
        var conditionnements = await contexte.Conditionnements.AsNoTracking()
            .Where(c => c.ArticleId == articleId)
            .OrderBy(c => c.QuantiteUniteStock)
            .ToListAsync(annulation);
        var ids = conditionnements.Select(c => c.Id).ToList();
        var codes = await contexte.Codes.AsNoTracking()
            .Where(c => ids.Contains(c.ConditionnementId))
            .OrderByDescending(c => c.Actif).ThenByDescending(c => c.Principal).ThenBy(c => c.Valeur)
            .ToListAsync(annulation);

        return [.. conditionnements.Select(c => VersDto(c, codes.Where(code => code.ConditionnementId == c.Id)))];
    }

    public async Task<ConditionnementDto> CreerAsync(Guid articleId, NouveauConditionnement saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var article = await TrouverArticle(articleId, annulation);
        var conditionnement = Conditionnement.Creer(article, saisie.Libelle, saisie.Niveau, saisie.QuantiteUniteStock);
        contexte.Conditionnements.Add(conditionnement);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(conditionnement, []);
    }

    public async Task<ConditionnementDto> ModifierAsync(Guid id, ModificationConditionnement saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var conditionnement = await TrouverConditionnement(id, annulation);
        conditionnement.Modifier(saisie.Libelle, saisie.Actif);
        await contexte.SaveChangesAsync(annulation);
        return await Relire(conditionnement, annulation);
    }

    /// <summary>Ajoute ou retire un conditionnement de la grille d'articles rapides de la caisse (choix du Gérant).</summary>
    public async Task<ConditionnementDto> ChoisirVenteRapideAsync(Guid id, ChoixVenteRapide saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var conditionnement = await TrouverConditionnement(id, annulation);
        conditionnement.ChoisirVenteRapide(saisie.VenteRapide);
        await contexte.SaveChangesAsync(annulation);
        return await Relire(conditionnement, annulation);
    }

    /// <summary>
    /// Catalogue complet de la caisse pour le poste (7.8) : conditionnements actifs d'articles actifs, avec leurs codes
    /// actifs. Le poste y résout un scan comme <see cref="ResoudreAsync"/> le fait en ligne.
    /// </summary>
    public async Task<CatalogueCaisse> CatalogueCaisseAsync(DateTime maintenantUtc, CancellationToken annulation)
    {
        var vendables = await (
                from article in contexte.Articles
                join conditionnement in contexte.Conditionnements on article.Id equals conditionnement.ArticleId
                where article.Actif && conditionnement.Actif
                orderby article.Code, conditionnement.QuantiteUniteStock
                select new { article, conditionnement })
            .AsNoTracking()
            .ToListAsync(annulation);
        var codes = (await contexte.Codes.AsNoTracking().Where(c => c.Actif).Select(c => new { c.ConditionnementId, c.Valeur }).ToListAsync(annulation))
            .ToLookup(c => c.ConditionnementId, c => c.Valeur);
        return new CatalogueCaisse(
            maintenantUtc,
            [.. vendables.Select(v => new VendableCatalogue(
                v.article.Id, v.article.Code, v.article.Libelle, v.article.LibelleTraduit, v.article.UniteStock, v.article.PrecisionQuantite,
                v.conditionnement.Id, v.conditionnement.Libelle, v.conditionnement.QuantiteUniteStock, v.conditionnement.EstUnite,
                v.conditionnement.VenteRapide, [.. codes[v.conditionnement.Id].Order(StringComparer.Ordinal)]))]);
    }

    /// <summary>Grille d'articles rapides de la caisse : conditionnements actifs et marqués, d'articles actifs.</summary>
    public async Task<IReadOnlyList<ArticleVendable>> ListerVenteRapideAsync(CancellationToken annulation)
    {
        var rapides = await (
                from article in contexte.Articles
                join conditionnement in contexte.Conditionnements on article.Id equals conditionnement.ArticleId
                where article.Actif && conditionnement.Actif && conditionnement.VenteRapide
                orderby article.Libelle, conditionnement.QuantiteUniteStock
                select new { article, conditionnement })
            .AsNoTracking()
            .ToListAsync(annulation);
        return [.. rapides.Select(r => VersVendable(r.article, r.conditionnement, null))];
    }

    /// <summary>Ajoute un GTIN (EAN-13, EAN-8, UPC-A) après contrôle de sa clé et de son unicité (RG33).</summary>
    public async Task<ConditionnementDto> AjouterCodeAsync(Guid conditionnementId, NouveauCode saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var conditionnement = await TrouverConditionnement(conditionnementId, annulation);
        var code = CodeArticle.Gtin(conditionnement.Id, saisie.Valeur, await EstPremierCode(conditionnement.Id, annulation));
        await Enregistrer(code, annulation);
        return await Relire(conditionnement, annulation);
    }

    /// <summary>Génère un code interne à imprimer en étiquette, pour un article sans GTIN fiable (RG33).</summary>
    public async Task<ConditionnementDto> GenererCodeInterneAsync(Guid conditionnementId, CancellationToken annulation)
    {
        var conditionnement = await TrouverConditionnement(conditionnementId, annulation);
        var numero = await contexte.Database
            .SqlQuery<long>($"SELECT nextval('referentiel.code_interne') AS \"Value\"")
            .SingleAsync(annulation);
        await Enregistrer(CodeArticle.Interne(conditionnement.Id, numero, await EstPremierCode(conditionnement.Id, annulation)), annulation);
        return await Relire(conditionnement, annulation);
    }

    /// <summary>Désactive un code (réattribution, code erroné) : il reste dans l'historique.</summary>
    public async Task<ConditionnementDto> DesactiverCodeAsync(Guid codeId, CancellationToken annulation)
    {
        var code = await contexte.Codes.SingleOrDefaultAsync(c => c.Id == codeId, annulation)
            ?? throw new ExceptionReferentiel(CodesErreurReferentiel.CodeIntrouvable);
        code.Desactiver();
        await contexte.SaveChangesAsync(annulation);
        return await Relire(await TrouverConditionnement(code.ConditionnementId, annulation), annulation);
    }

    /// <summary>
    /// Identifie un code scanné ou saisi (UC22, V1) : code actif d'un conditionnement (EAN, UPC-A lu en EAN-13 avec un
    /// zéro de tête, code interne), sinon code de l'article (conditionnement Unité). Null si rien ne correspond.
    /// </summary>
    public async Task<ArticleVendable?> ResoudreAsync(string valeur, CancellationToken annulation)
    {
        var nettoyee = valeur?.Trim() ?? "";
        if (nettoyee.Length == 0)
        {
            return null;
        }

        var candidates = new List<string> { nettoyee };
        if (nettoyee.Length == 13 && nettoyee[0] == '0')
        {
            candidates.Add(nettoyee[1..]);
        }

        var parCode = await (
                from code in contexte.Codes
                join conditionnement in contexte.Conditionnements on code.ConditionnementId equals conditionnement.Id
                join article in contexte.Articles on conditionnement.ArticleId equals article.Id
                where code.Actif && conditionnement.Actif && article.Actif && candidates.Contains(code.Valeur)
                select new { article, conditionnement, code.Valeur })
            .AsNoTracking()
            .FirstOrDefaultAsync(annulation);
        if (parCode is not null)
        {
            return VersVendable(parCode.article, parCode.conditionnement, parCode.Valeur);
        }

        var parArticle = await (
                from article in contexte.Articles
                join conditionnement in contexte.Conditionnements on article.Id equals conditionnement.ArticleId
                where article.Actif && conditionnement.Niveau == NiveauConditionnement.Unite && conditionnement.QuantiteUniteStock == 1m
                      && EF.Functions.ILike(article.Code, nettoyee)
                select new { article, conditionnement })
            .AsNoTracking()
            .FirstOrDefaultAsync(annulation);
        return parArticle is null ? null : VersVendable(parArticle.article, parArticle.conditionnement, null);
    }

    /// <summary>Articles vendables dont le code ou le libellé contient le texte, avec chacun de leurs conditionnements actifs.</summary>
    public async Task<IReadOnlyList<ArticleVendable>> RechercherAsync(string? texte, CancellationToken annulation)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return [];
        }

        var motif = $"%{texte.Trim()}%";
        var trouves = await (
                from article in contexte.Articles
                join conditionnement in contexte.Conditionnements on article.Id equals conditionnement.ArticleId
                where article.Actif && conditionnement.Actif
                      && (EF.Functions.ILike(article.Code, motif) || EF.Functions.ILike(article.Libelle, motif)
                          || (article.LibelleTraduit != null && EF.Functions.ILike(article.LibelleTraduit, motif)))
                orderby article.Code, conditionnement.QuantiteUniteStock
                select new { article, conditionnement })
            .AsNoTracking()
            .Take(LimiteRecherche)
            .ToListAsync(annulation);
        return [.. trouves.Select(t => VersVendable(t.article, t.conditionnement, null))];
    }

    private async Task<bool> EstPremierCode(Guid conditionnementId, CancellationToken annulation) =>
        !await contexte.Codes.AnyAsync(c => c.ConditionnementId == conditionnementId && c.Actif, annulation);

    private async Task Enregistrer(CodeArticle code, CancellationToken annulation)
    {
        if (await contexte.Codes.AnyAsync(c => c.Valeur == code.Valeur && c.Actif, annulation))
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.CodeDejaAttribue);
        }

        contexte.Codes.Add(code);
        try
        {
            await contexte.SaveChangesAsync(annulation);
        }
        catch (DbUpdateException erreur) when (erreur.InnerException is PostgresException { SqlState: ViolationUnicite })
        {
            throw new ExceptionReferentiel(CodesErreurReferentiel.CodeDejaAttribue);
        }
    }

    private async Task<Article> TrouverArticle(Guid id, CancellationToken annulation) =>
        await contexte.Articles.SingleOrDefaultAsync(a => a.Id == id, annulation)
        ?? throw new ExceptionReferentiel(CodesErreurReferentiel.ArticleIntrouvable);

    private async Task<Conditionnement> TrouverConditionnement(Guid id, CancellationToken annulation) =>
        await contexte.Conditionnements.SingleOrDefaultAsync(c => c.Id == id, annulation)
        ?? throw new ExceptionReferentiel(CodesErreurReferentiel.ConditionnementIntrouvable);

    private async Task<ConditionnementDto> Relire(Conditionnement conditionnement, CancellationToken annulation)
    {
        var codes = await contexte.Codes.AsNoTracking()
            .Where(c => c.ConditionnementId == conditionnement.Id)
            .OrderByDescending(c => c.Actif).ThenByDescending(c => c.Principal).ThenBy(c => c.Valeur)
            .ToListAsync(annulation);
        return VersDto(conditionnement, codes);
    }

    private static ConditionnementDto VersDto(Conditionnement c, IEnumerable<CodeArticle> codes) =>
        new(c.Id, c.ArticleId, c.Libelle, c.Niveau, c.QuantiteUniteStock, c.Actif, c.VenteRapide,
            [.. codes.Select(code => new CodeDto(code.Id, code.Valeur, code.Type, code.Principal, code.Actif))]);

    private static ArticleVendable VersVendable(Article a, Conditionnement c, string? codeScanne) =>
        new(a.Id, a.Code, a.Libelle, a.LibelleTraduit, a.UniteStock, a.PrecisionQuantite, c.Id, c.Libelle, c.QuantiteUniteStock, codeScanne);
}
