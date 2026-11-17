using GestionStock.Caisse.Domain;
using GestionStock.Caisse.Infrastructure;
using GestionStock.Identite.Contracts;
using GestionStock.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Caisse.Application;

internal sealed record AnomalieDto(
    Guid Id,
    TypeAnomalie Type,
    Guid SessionId,
    string NumeroCaisse,
    string Caissier,
    Guid DocumentId,
    string? NumeroDocument,
    string Detail,
    DateTime DateUtc,
    bool Traitee,
    string? TraiteePar,
    DateTime? TraitementUtc,
    string? Commentaire,
    string NomCaissier,
    string? NomTraiteePar);

internal sealed record TraitementAnomalie(string? Commentaire);

/// <summary>Anomalies de synchronisation (7.8) : liste pour la supervision, traitement par le Gérant.</summary>
/// <remarks>
/// Le caissier et l'auteur du traitement sont tracés par leur matricule ; l'écran les montre par leur nom (annuaire).
/// </remarks>
internal sealed class ServiceAnomalies(CaisseDbContext contexte, IUtilisateurCourant utilisateur, IHorloge horloge, IAnnuairePersonnel annuaire)
{
    public async Task<IReadOnlyList<AnomalieDto>> ListerAsync(bool? traitees, CancellationToken annulation)
    {
        var anomalies = await contexte.Anomalies.AsNoTracking()
            .Where(a => traitees == null || a.Traitee == traitees)
            .OrderByDescending(a => a.DateUtc)
            .Take(500)
            .ToListAsync(annulation);
        var resultat = new List<AnomalieDto>(anomalies.Count);
        foreach (var anomalie in anomalies)
        {
            resultat.Add(await VersDto(anomalie, annulation));
        }

        return resultat;
    }

    public async Task<AnomalieDto> TraiterAsync(Guid id, TraitementAnomalie saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var anomalie = await contexte.Anomalies.SingleOrDefaultAsync(a => a.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.AnomalieIntrouvable);
        anomalie.Traiter(utilisateur.Identifiant, saisie.Commentaire, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDto(anomalie, annulation);
    }

    private async Task<AnomalieDto> VersDto(AnomalieCaisse a, CancellationToken annulation) =>
        new(a.Id, a.Type, a.SessionId, a.NumeroCaisse, a.Caissier, a.DocumentId, a.NumeroDocument, a.Detail, a.DateUtc, a.Traitee, a.TraiteePar, a.TraitementUtc, a.Commentaire,
            await annuaire.DesignationAsync(a.Caissier, annulation),
            a.TraiteePar is null ? null : await annuaire.DesignationAsync(a.TraiteePar, annulation));
}
