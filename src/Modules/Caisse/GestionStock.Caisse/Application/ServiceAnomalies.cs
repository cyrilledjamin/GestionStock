using GestionStock.Caisse.Domain;
using GestionStock.Caisse.Infrastructure;
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
    string? Commentaire);

internal sealed record TraitementAnomalie(string? Commentaire);

/// <summary>Anomalies de synchronisation (7.8) : liste pour la supervision, traitement par le Gérant.</summary>
internal sealed class ServiceAnomalies(CaisseDbContext contexte, IUtilisateurCourant utilisateur, IHorloge horloge)
{
    public async Task<IReadOnlyList<AnomalieDto>> ListerAsync(bool? traitees, CancellationToken annulation) =>
        await contexte.Anomalies.AsNoTracking()
            .Where(a => traitees == null || a.Traitee == traitees)
            .OrderByDescending(a => a.DateUtc)
            .Take(500)
            .Select(a => new AnomalieDto(a.Id, a.Type, a.SessionId, a.NumeroCaisse, a.Caissier, a.DocumentId, a.NumeroDocument, a.Detail, a.DateUtc, a.Traitee, a.TraiteePar, a.TraitementUtc, a.Commentaire))
            .ToListAsync(annulation);

    public async Task<AnomalieDto> TraiterAsync(Guid id, TraitementAnomalie saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var anomalie = await contexte.Anomalies.SingleOrDefaultAsync(a => a.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.AnomalieIntrouvable);
        anomalie.Traiter(utilisateur.Identifiant, saisie.Commentaire, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        var a = anomalie;
        return new AnomalieDto(a.Id, a.Type, a.SessionId, a.NumeroCaisse, a.Caissier, a.DocumentId, a.NumeroDocument, a.Detail, a.DateUtc, a.Traitee, a.TraiteePar, a.TraitementUtc, a.Commentaire);
    }
}
