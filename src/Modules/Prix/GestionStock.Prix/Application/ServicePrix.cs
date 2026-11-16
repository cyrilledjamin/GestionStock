using GestionStock.Prix.Contracts;
using GestionStock.Prix.Domain;
using GestionStock.Prix.Infrastructure;
using GestionStock.Referentiel.Contracts;
using GestionStock.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionStock.Prix.Application;

/// <summary>Paramètres fiscaux de l'entreprise (variables <c>Entreprise__AssujettiTva</c>, <c>Entreprise__TauxTva</c>).</summary>
internal sealed class OptionsEntreprise
{
    public const string Section = "Entreprise";

    /// <summary>Assujettie à la TVA (RG37) ; non par défaut, régime courant des petites boutiques.</summary>
    public bool AssujettiTva { get; set; }

    /// <summary>Taux légal de TVA en pourcentage : 19,25 % au Cameroun (17,5 % + centimes additionnels communaux).</summary>
    public decimal TauxTva { get; set; } = 19.25m;
}

/// <summary>Prix d'un conditionnement pour l'écran de gestion : prix en vigueur et prochain prix programmé.</summary>
internal sealed record PrixConditionnement(
    Guid ConditionnementId,
    string Libelle,
    decimal QuantiteUniteStock,
    bool Actif,
    decimal? PrixTtc,
    DateOnly? DebutValidite,
    decimal? ProchainPrixTtc,
    DateOnly? ProchaineValidite);

internal sealed record HistoriquePrix(Guid Id, decimal PrixTtc, DateOnly DebutValidite, string SaisiPar, DateTime DateSaisieUtc);

internal sealed record NouveauPrix(decimal PrixTtc, DateOnly? DebutValidite);

/// <summary>Prix d'un conditionnement pour le poste : en vigueur à la date donnée, et le prochain prix programmé.</summary>
internal sealed record PrixGrille(Guid ConditionnementId, decimal PrixTtc, DateOnly DebutValidite, decimal? ProchainPrixTtc, DateOnly? ProchaineValidite);

/// <summary>Grille de prix de la caisse (7.8) : gardée sur le poste, elle sert à vendre hors ligne au prix du jour.</summary>
internal sealed record GrillePrix(DateOnly Date, bool Assujetti, decimal TauxTva, IReadOnlyList<PrixGrille> Prix);

/// <summary>Gestion des prix de vente (UC25) et lecture par la caisse.</summary>
internal sealed class ServicePrix(
    PrixDbContext contexte,
    IConsultationReferentiel referentiel,
    IUtilisateurCourant utilisateur,
    IHorloge horloge,
    IOptions<OptionsEntreprise> options) : IConsultationPrix, IServicePrix
{
    public RegimeTva Regime => new(options.Value.AssujettiTva, options.Value.TauxTva);

    public async Task<PrixApplicable?> ObtenirPrixAsync(Guid conditionnementId, DateOnly date, CancellationToken annulation = default) =>
        await contexte.Prix.AsNoTracking()
            .Where(p => p.ConditionnementId == conditionnementId && p.DebutValidite <= date)
            .OrderByDescending(p => p.DebutValidite).ThenByDescending(p => p.DateSaisieUtc)
            .Select(p => new PrixApplicable(p.ConditionnementId, p.PrixTtc, p.DebutValidite))
            .FirstOrDefaultAsync(annulation);

    /// <summary>Conditionnements d'un article avec leur prix en vigueur aujourd'hui et le prochain prix programmé.</summary>
    public async Task<IReadOnlyList<PrixConditionnement>> ListerParArticleAsync(Guid articleId, CancellationToken annulation)
    {
        var conditionnements = await referentiel.ListerConditionnementsAsync(articleId, annulation);
        if (conditionnements.Count == 0)
        {
            throw new ExceptionPrix(CodesErreurPrix.ArticleIntrouvable);
        }

        var aujourdhui = horloge.AujourdhuiDouala;
        var ids = conditionnements.Select(c => c.Id).ToList();
        var prix = await contexte.Prix.AsNoTracking().Where(p => ids.Contains(p.ConditionnementId)).ToListAsync(annulation);

        return [.. conditionnements.Select(c =>
        {
            var duConditionnement = prix.Where(p => p.ConditionnementId == c.Id)
                .OrderByDescending(p => p.DebutValidite).ThenByDescending(p => p.DateSaisieUtc).ToList();
            var actuel = duConditionnement.FirstOrDefault(p => p.DebutValidite <= aujourdhui);
            var prochain = duConditionnement.LastOrDefault(p => p.DebutValidite > aujourdhui);
            return new PrixConditionnement(
                c.Id, c.Libelle, c.QuantiteUniteStock, c.Actif,
                actuel?.PrixTtc, actuel?.DebutValidite, prochain?.PrixTtc, prochain?.DebutValidite);
        })];
    }

    /// <summary>
    /// Grille de tous les conditionnements qui ont un prix en vigueur à la date (jour de Douala), avec le prochain prix
    /// programmé : un poste coupé du réseau passé minuit applique le bon prix.
    /// </summary>
    public async Task<GrillePrix> GrilleAsync(DateOnly date, CancellationToken annulation)
    {
        var prix = await contexte.Prix.AsNoTracking().ToListAsync(annulation);
        var grille = prix
            .GroupBy(p => p.ConditionnementId)
            .Select(groupe =>
            {
                var ordonnes = groupe.OrderByDescending(p => p.DebutValidite).ThenByDescending(p => p.DateSaisieUtc).ToList();
                var actuel = ordonnes.FirstOrDefault(p => p.DebutValidite <= date);
                var prochain = ordonnes.LastOrDefault(p => p.DebutValidite > date);
                return actuel is null
                    ? null
                    : new PrixGrille(groupe.Key, actuel.PrixTtc, actuel.DebutValidite, prochain?.PrixTtc, prochain?.DebutValidite);
            })
            .OfType<PrixGrille>()
            .OrderBy(p => p.ConditionnementId)
            .ToList();
        return new GrillePrix(date, Regime.Assujetti, Regime.TauxApplique, grille);
    }

    public async Task<IReadOnlyList<HistoriquePrix>> HistoriqueAsync(Guid conditionnementId, CancellationToken annulation) =>
        await contexte.Prix.AsNoTracking()
            .Where(p => p.ConditionnementId == conditionnementId)
            .OrderByDescending(p => p.DebutValidite).ThenByDescending(p => p.DateSaisieUtc)
            .Select(p => new HistoriquePrix(p.Id, p.PrixTtc, p.DebutValidite, p.SaisiPar, p.DateSaisieUtc))
            .ToListAsync(annulation);

    public async Task FixerPrixAsync(Guid conditionnementId, decimal prixTtc, CancellationToken annulation = default)
    {
        try
        {
            await FixerAsync(conditionnementId, new NouveauPrix(prixTtc, null), annulation);
        }
        catch (ExceptionPrix erreur)
        {
            throw new ErreurPrix(erreur.Code, erreur.Message);
        }
    }

    /// <summary>Fixe un prix à partir d'aujourd'hui (par défaut) ou d'une date future.</summary>
    public async Task<HistoriquePrix> FixerAsync(Guid conditionnementId, NouveauPrix saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var conditionnement = await referentiel.ObtenirConditionnementAsync(conditionnementId, annulation)
            ?? throw new ExceptionPrix(CodesErreurPrix.ConditionnementIntrouvable);
        if (!conditionnement.Actif || !conditionnement.Article.Actif)
        {
            throw new ExceptionPrix(CodesErreurPrix.ConditionnementInactif);
        }

        var aujourdhui = horloge.AujourdhuiDouala;
        var prix = PrixVente.Fixer(conditionnementId, saisie.PrixTtc, saisie.DebutValidite ?? aujourdhui, aujourdhui, utilisateur.Identifiant, horloge.MaintenantUtc);
        contexte.Prix.Add(prix);
        await contexte.SaveChangesAsync(annulation);
        return new HistoriquePrix(prix.Id, prix.PrixTtc, prix.DebutValidite, prix.SaisiPar, prix.DateSaisieUtc);
    }
}
