using System.Globalization;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire.Contracts;
using GestionStock.Prix.Contracts;
using GestionStock.Referentiel.Contracts;
using GestionStock.Reprise.Domain;
using GestionStock.Reprise.Infrastructure;

namespace GestionStock.Reprise.Application;

/// <summary>Erreur affichée : ligne du fichier, colonne (nom traduit), message traduit, précision.</summary>
internal sealed record ErreurAffichee(int Ligne, string Code, string Message, string? Colonne, string? Detail);

/// <summary>Résultat d'un contrôle ou d'un import : ce que contient le fichier, ses erreurs, l'inventaire d'ouverture créé.</summary>
internal sealed record RapportReprise(
    bool Importe,
    int Lignes,
    int Articles,
    int Conditionnements,
    int CodesBarres,
    int Prix,
    IReadOnlyList<ErreurAffichee> Erreurs,
    Guid? InventaireOuvertureId,
    string? InventaireOuvertureNumero);

/// <summary>
/// Reprise des données (décision 29) : modèle Excel, contrôle ligne par ligne, puis import tout ou rien des articles,
/// conditionnements, codes-barres et prix, avec l'inventaire d'ouverture valorisé au coût d'achat du fichier.
/// </summary>
internal sealed class ServiceReprise(
    IConsultationReferentiel consultation,
    IServiceReferentiel referentiel,
    IServicePrix prix,
    IServiceInventaire inventaire,
    IUniteDeTravail uniteDeTravail)
{
    public static byte[] Modele(bool exemple, CultureInfo culture) =>
        ClasseurReprise.Ecrire(exemple ? ExempleReprise.Lignes() : [], culture);

    /// <summary>Contrôle sans rien écrire.</summary>
    public async Task<RapportReprise> AnalyserAsync(Stream fichier, CancellationToken annulation)
    {
        var (lignes, articles, erreurs) = await Analyser(fichier, annulation);
        return Rapport(lignes, articles, erreurs, importe: false, ouverture: null);
    }

    /// <summary>Import tout ou rien : rien n'est écrit si une seule ligne est en erreur.</summary>
    public async Task<RapportReprise> ImporterAsync(Stream fichier, CancellationToken annulation)
    {
        var (lignes, articles, erreurs) = await Analyser(fichier, annulation);
        if (erreurs.Count > 0)
        {
            return Rapport(lignes, articles, erreurs, importe: false, ouverture: null);
        }

        try
        {
            var ouverture = await uniteDeTravail.ExecuterAsync(jeton => Ecrire(articles, jeton), annulation);
            return Rapport(lignes, articles, [], importe: true, ouverture);
        }
        catch (ErreurReferentiel erreur)
        {
            return Rapport(lignes, articles, [new ErreurReprise(0, CodesErreurReprise.EcritureRefusee, Detail: erreur.Code)], importe: false, ouverture: null);
        }
        catch (ErreurPrix erreur)
        {
            return Rapport(lignes, articles, [new ErreurReprise(0, CodesErreurReprise.EcritureRefusee, Detail: erreur.Code)], importe: false, ouverture: null);
        }
    }

    private async Task<InventaireCree> Ecrire(IReadOnlyList<ArticleAImporter> articles, CancellationToken annulation)
    {
        var ouverture = new List<LigneOuverture>();
        foreach (var article in articles)
        {
            var cree = await referentiel.CreerArticleAsync(
                new NouvelArticleReferentiel(article.Code, article.Libelle, article.LibelleTraduit, article.UniteStock, article.Decimales),
                annulation);
            foreach (var conditionnement in article.Conditionnements)
            {
                var id = conditionnement.EstUnite
                    ? cree.ConditionnementUniteId
                    : await referentiel.AjouterConditionnementAsync(cree.ArticleId, conditionnement.Libelle, conditionnement.Niveau, conditionnement.Contenu, annulation);
                if (conditionnement.CodeBarres is { } code)
                {
                    await referentiel.AjouterCodeAsync(id, code, annulation);
                }

                if (conditionnement.PrixTtc is { } prixTtc)
                {
                    await prix.FixerPrixAsync(id, prixTtc, annulation);
                }
            }

            ouverture.Add(new LigneOuverture(cree.ArticleId, article.CoutAchat));
        }

        return await inventaire.CreerOuvertureAsync(ouverture, annulation);
    }

    private async Task<(int Lignes, IReadOnlyList<ArticleAImporter> Articles, List<ErreurReprise> Erreurs)> Analyser(Stream fichier, CancellationToken annulation)
    {
        IReadOnlyList<LigneFichier> lignes;
        try
        {
            lignes = ClasseurReprise.Lire(fichier);
        }
        catch (InvalidDataException)
        {
            return (0, [], [new ErreurReprise(0, CodesErreurReprise.FichierIllisible)]);
        }

        var (articles, erreursFichier) = AnalyseReprise.Analyser(lignes);
        var erreurs = erreursFichier.ToList();

        // Contrôles qui demandent la base : codes article et codes-barres déjà utilisés.
        var codesArticles = lignes.Select(l => l[0]).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existants = await consultation.CodesArticlesExistantsAsync(codesArticles, annulation);
        erreurs.AddRange(lignes
            .Where(l => existants.Contains(l[0]))
            .GroupBy(l => l[0].ToUpperInvariant())
            .Select(g => new ErreurReprise(g.First().Numero, CodesErreurReprise.CodeArticleExistant, "CodeArticle", g.First()[0])));

        var codesBarres = lignes.Select(l => l[8]).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var attribues = await consultation.CodesBarresAttribuesAsync(codesBarres, annulation);
        erreurs.AddRange(lignes
            .Where(l => attribues.Contains(l[8]))
            .Select(l => new ErreurReprise(l.Numero, CodesErreurReprise.CodeBarresAttribue, "CodeBarres", l[8])));

        return (lignes.Count, erreurs.Count == 0 ? articles : [], erreurs);
    }

    private static RapportReprise Rapport(int lignes, IReadOnlyList<ArticleAImporter> articles, IReadOnlyList<ErreurReprise> erreurs, bool importe, InventaireCree? ouverture)
    {
        var culture = CultureInfo.CurrentUICulture;
        var conditionnements = articles.SelectMany(a => a.Conditionnements).ToList();
        return new RapportReprise(
            importe,
            lignes,
            articles.Count,
            conditionnements.Count,
            conditionnements.Count(c => c.CodeBarres is not null),
            conditionnements.Count(c => c.PrixTtc is not null),
            [.. erreurs.OrderBy(e => e.Ligne).Select(e => new ErreurAffichee(
                e.Ligne,
                e.Code,
                ClasseurReprise.Textes.GetString(e.Code, culture) ?? e.Code,
                e.Colonne is null ? null : ClasseurReprise.Textes.GetString($"Reprise.Colonne.{e.Colonne}", culture) ?? e.Colonne,
                e.Detail))],
            ouverture?.Id,
            ouverture?.Numero);
    }
}
