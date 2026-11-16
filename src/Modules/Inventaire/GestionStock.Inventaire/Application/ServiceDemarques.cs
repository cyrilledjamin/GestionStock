using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire.Contracts;
using GestionStock.Inventaire.Domain;
using GestionStock.Inventaire.Infrastructure;
using GestionStock.Referentiel.Contracts;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Inventaire.Application;

internal sealed record DemarqueDto(
    Guid Id,
    string Numero,
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string UniteStock,
    decimal Quantite,
    MotifDemarque Motif,
    string? Commentaire,
    StatutDemarque Statut,
    DateTime DeclareeLeUtc,
    string DeclareePar,
    DateTime? TraiteeLeUtc,
    string? TraiteePar,
    string? MotifRefus);

internal sealed record NouvelleDemarque(Guid ArticleId, decimal Quantite, MotifDemarque Motif, string? Commentaire);

internal sealed record RefusDemarque(string Motif);

/// <summary>Démarque connue (UC20, RG23) : déclaration, puis validation (mouvement Demarque) ou refus.</summary>
internal sealed class ServiceDemarques(
    InventaireDbContext contexte,
    ServiceInventaires inventaires,
    IConsultationReferentiel referentiel,
    IConsultationStock consultationStock,
    IUniteDeTravail uniteDeTravail,
    IUtilisateurCourant utilisateur,
    IHorloge horloge)
{
    public async Task<IReadOnlyList<DemarqueDto>> ListerAsync(StatutDemarque? statut, CancellationToken annulation) =>
        [.. (await contexte.Demarques.AsNoTracking()
                .Where(d => statut == null || d.Statut == statut)
                .OrderByDescending(d => d.DeclareeLeUtc)
                .Take(500)
                .ToListAsync(annulation))
            .Select(VersDto)];

    public async Task<DemarqueDto> DeclarerAsync(NouvelleDemarque saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var article = await referentiel.ObtenirArticleAsync(saisie.ArticleId, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.ArticleIntrouvable);
        var demarque = DeclarationDemarque.Declarer(
            await inventaires.Numero(InventaireDbContext.SequenceDemarque, "DEM", annulation),
            ServiceInventaires.VersAInventorier(article),
            saisie.Quantite,
            saisie.Motif,
            saisie.Commentaire,
            utilisateur.Identifiant,
            horloge.MaintenantUtc);
        contexte.Demarques.Add(demarque);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(demarque);
    }

    /// <summary>Validation : la marchandise sort de l'emplacement de vente au CUMP (mouvement Demarque), tout ou rien.</summary>
    public Task<DemarqueDto> ValiderAsync(Guid id, CancellationToken annulation) =>
        uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var demarque = await Verrouiller(id, jeton);
                demarque.Valider(utilisateur.Identifiant, horloge.MaintenantUtc);
                var site = await referentiel.ObtenirSiteBoutiqueAsync(jeton);
                var cump = (await consultationStock.ObtenirSoldeSiteAsync(demarque.ArticleId, site.SiteId, jeton))?.Cump ?? 0m;
                await inventaires.EnregistrerMouvement(
                    new DemandeMouvement(
                        TypeMouvementStock.Demarque,
                        demarque.ArticleId,
                        site.SiteId,
                        Source: new EmplacementMouvement(site.EmplacementVenteId, TypeEmplacementStock.Vente),
                        Destination: null,
                        demarque.Quantite,
                        decimal.Round(cump, 4),
                        TypesDocumentsInventaire.Demarque,
                        demarque.Id,
                        utilisateur.Identifiant,
                        IdIdempotence: demarque.Id),
                    jeton);
                await contexte.SaveChangesAsync(jeton);
                return VersDto(demarque);
            },
            annulation);

    public async Task<DemarqueDto> RefuserAsync(Guid id, RefusDemarque refus, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(refus);
        var demarque = await contexte.Demarques.SingleOrDefaultAsync(d => d.Id == id, annulation)
            ?? throw new ExceptionInventaire(CodesErreurInventaire.DemarqueIntrouvable);
        demarque.Refuser(refus.Motif, utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(demarque);
    }

    private async Task<DeclarationDemarque> Verrouiller(Guid id, CancellationToken annulation)
    {
        var trouve = await contexte.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventaire.declaration_demarque WHERE id = {id} FOR UPDATE").ToListAsync(annulation);
        if (trouve.Count == 0)
        {
            throw new ExceptionInventaire(CodesErreurInventaire.DemarqueIntrouvable);
        }

        return await contexte.Demarques.SingleAsync(d => d.Id == id, annulation);
    }

    private static DemarqueDto VersDto(DeclarationDemarque d) =>
        new(d.Id, d.Numero, d.ArticleId, d.CodeArticle, d.Libelle, d.UniteStock, d.Quantite, d.Motif, d.Commentaire, d.Statut,
            d.DeclareeLeUtc, d.DeclareePar, d.TraiteeLeUtc, d.TraiteePar, d.MotifRefus);
}
