using GestionStock.Referentiel.Contracts;
using GestionStock.Referentiel.Domain;

namespace GestionStock.Referentiel.Application;

/// <summary>Écriture du référentiel par les autres modules : mêmes services et mêmes règles que les écrans.</summary>
internal sealed class ServiceEcritureReferentiel(ServiceArticles articles, ServiceConditionnements conditionnements) : IServiceReferentiel
{
    public Task<ArticleCree> CreerArticleAsync(NouvelArticleReferentiel article, CancellationToken annulation = default) =>
        Traduire(async () =>
        {
            ArgumentNullException.ThrowIfNull(article);
            var cree = await articles.CreerAsync(new NouvelArticle(article.Code, article.Libelle, article.LibelleTraduit, article.UniteStock, article.PrecisionQuantite), annulation);
            var unite = (await conditionnements.ListerAsync(cree.Id, annulation)).Single();
            return new ArticleCree(cree.Id, unite.Id);
        });

    public Task<Guid> AjouterConditionnementAsync(Guid articleId, string libelle, string niveau, decimal quantiteUniteStock, CancellationToken annulation = default) =>
        Traduire(async () =>
        {
            if (!Enum.TryParse<NiveauConditionnement>(niveau, ignoreCase: true, out var niveauLu) || niveauLu == NiveauConditionnement.Unite)
            {
                throw new ExceptionReferentiel(CodesErreurReferentiel.NiveauInvalide);
            }

            return (await conditionnements.CreerAsync(articleId, new NouveauConditionnement(libelle, niveauLu, quantiteUniteStock), annulation)).Id;
        });

    public Task AjouterCodeAsync(Guid conditionnementId, string valeur, CancellationToken annulation = default) =>
        Traduire(async () => (await conditionnements.AjouterCodeAsync(conditionnementId, new NouveauCode(valeur), annulation)).Id);

    private static async Task<T> Traduire<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ExceptionReferentiel erreur)
        {
            throw new ErreurReferentiel(erreur.Code, erreur.Message);
        }
    }
}
