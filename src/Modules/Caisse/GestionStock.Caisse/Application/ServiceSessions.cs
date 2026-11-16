using GestionStock.Caisse.Domain;
using GestionStock.Caisse.Infrastructure;
using GestionStock.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestionStock.Caisse.Application;

/// <summary>Postes de caisse de la boutique (variable <c>Caisse__Postes__0</c>…), CAISSE-1 par défaut.</summary>
internal sealed class OptionsCaisse
{
    public const string Section = "Caisse";

    public List<string> Postes { get; set; } = [];

    public IReadOnlyList<string> PostesEffectifs => Postes.Count > 0 ? Postes : ["CAISSE-1"];
}

/// <summary>Totaux d'une session par mode de paiement.</summary>
internal sealed record TotauxSession(
    int NombreVentes,
    int NombreAvoirs,
    decimal Ventes,
    decimal Avoirs,
    decimal Especes,
    decimal MobileMoney,
    decimal Carte,
    decimal Credit,
    decimal RemboursementsCredit,
    decimal EspecesTheoriques);

internal sealed record SessionDto(
    Guid Id,
    string NumeroCaisse,
    string Caissier,
    DateTime OuvertureUtc,
    decimal FondCaisse,
    StatutSession Statut,
    DateTime? ClotureUtc,
    string? ClotureePar,
    decimal? EspecesComptees,
    decimal? Ecart,
    string? ValideePar,
    TotauxSession Totaux);

internal sealed record OuvertureSession(string NumeroCaisse, decimal FondCaisse);

/// <summary>
/// Clôture par comptage. <paramref name="DernierNumero"/> : dernier ticket numéroté par le poste ; tant que le serveur
/// ne l'a pas reçu, des ventes hors ligne manquent et la clôture attend (RG36).
/// </summary>
internal sealed record ClotureSession(decimal EspecesComptees, long? DernierNumero = null);

/// <summary>Poste occupé par une session ouverte : qui la tient et depuis quand (le caissier bloqué sait à qui s'adresser).</summary>
internal sealed record OccupationPoste(string NumeroCaisse, Guid SessionId, string Caissier, DateTime OuvertureUtc);

/// <summary>Sessions de caisse (UC24, RG36) : ouverture avec fond, clôture par comptage des espèces, validation de l'écart.</summary>
internal sealed class ServiceSessions(
    CaisseDbContext contexte,
    IUtilisateurCourant utilisateur,
    IHorloge horloge,
    IOptions<OptionsCaisse> options)
{
    private const string ViolationUnicite = "23505";

    public IReadOnlyList<string> Postes => options.Value.PostesEffectifs;

    public async Task<SessionDto> OuvrirAsync(OuvertureSession saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!Postes.Contains(saisie.NumeroCaisse, StringComparer.Ordinal))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.PosteInconnu);
        }

        var caissier = utilisateur.Identifiant;
        if (await contexte.Sessions.AnyAsync(s => s.Caissier == caissier && s.Statut == StatutSession.Ouverte, annulation))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.SessionDejaOuverte);
        }

        if (await contexte.Sessions.AnyAsync(s => s.NumeroCaisse == saisie.NumeroCaisse && s.Statut == StatutSession.Ouverte, annulation))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.PosteOccupe);
        }

        var session = SessionCaisse.Ouvrir(saisie.NumeroCaisse, caissier, saisie.FondCaisse, horloge.MaintenantUtc);
        contexte.Sessions.Add(session);
        try
        {
            await contexte.SaveChangesAsync(annulation);
        }
        catch (DbUpdateException erreur) when (erreur.InnerException is PostgresException { SqlState: ViolationUnicite })
        {
            // Ouverture simultanée : l'index unique filtré tranche.
            throw new ExceptionCaisse(CodesErreurCaisse.PosteOccupe);
        }

        return await VersDto(session, annulation);
    }

    /// <summary>
    /// Session d'une opération faite hors ligne : celle du caissier connecté, même clôturée depuis (l'opération est
    /// alors enregistrée et signalée, 7.8). Les ventes d'un caissier ne s'envoient pas sous le compte d'un autre.
    /// </summary>
    public async Task<SessionCaisse> SessionDuTerrainAsync(Guid id, CancellationToken annulation)
    {
        var session = await contexte.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.SessionIntrouvable);
        return session.Caissier == utilisateur.Identifiant
            ? session
            : throw new ExceptionCaisse(CodesErreurCaisse.SessionAutreCaissier);
    }

    /// <summary>Session ouverte du caissier connecté ; null s'il n'en a pas.</summary>
    public async Task<SessionDto?> CouranteAsync(CancellationToken annulation)
    {
        var session = await TrouverCourante(annulation);
        return session is null ? null : await VersDto(session, annulation);
    }

    /// <summary>Session ouverte du caissier connecté, obligatoire pour vendre, faire un avoir ou encaisser.</summary>
    public async Task<SessionCaisse> ExigerCouranteAsync(CancellationToken annulation) =>
        await TrouverCourante(annulation) ?? throw new ExceptionCaisse(CodesErreurCaisse.AucuneSessionOuverte);

    public async Task<SessionDto> CloturerAsync(ClotureSession saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var session = await ExigerCouranteAsync(annulation);
        if (saisie.DernierNumero is { } dernierDuPoste
            && (await contexte.Compteurs.AsNoTracking().Where(c => c.NumeroCaisse == session.NumeroCaisse).Select(c => (long?)c.DernierNumero).SingleOrDefaultAsync(annulation) ?? 0) < dernierDuPoste)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.VentesNonSynchronisees);
        }

        var totaux = await Totaux(session, annulation);
        session.Cloturer(saisie.EspecesComptees, totaux.EspecesTheoriques, utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDto(session, annulation);
    }

    /// <summary>
    /// Le Gérant clôture à la place de son titulaire une session restée ouverte qui bloque un poste : même comptage et
    /// même écart, tracés à son nom ; l'écart reste à valider comme pour toute clôture.
    /// </summary>
    public async Task<SessionDto> CloturerAPlaceAsync(Guid id, ClotureSession saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var session = await contexte.Sessions.SingleOrDefaultAsync(s => s.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.SessionIntrouvable);
        var totaux = await Totaux(session, annulation);
        session.Cloturer(saisie.EspecesComptees, totaux.EspecesTheoriques, utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDto(session, annulation);
    }

    /// <summary>Postes occupés par une session ouverte, avec son titulaire et son heure d'ouverture.</summary>
    public async Task<IReadOnlyList<OccupationPoste>> OccupationAsync(CancellationToken annulation) =>
        await contexte.Sessions.AsNoTracking()
            .Where(s => s.Statut == StatutSession.Ouverte)
            .OrderBy(s => s.NumeroCaisse)
            .Select(s => new OccupationPoste(s.NumeroCaisse, s.Id, s.Caissier, s.OuvertureUtc))
            .ToListAsync(annulation);

    public async Task<SessionDto> ValiderAsync(Guid id, CancellationToken annulation)
    {
        var session = await contexte.Sessions.SingleOrDefaultAsync(s => s.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.SessionIntrouvable);
        session.Valider(utilisateur.Identifiant, horloge.MaintenantUtc);
        await contexte.SaveChangesAsync(annulation);
        return await VersDto(session, annulation);
    }

    public async Task<IReadOnlyList<SessionDto>> ListerAsync(StatutSession? statut, CancellationToken annulation)
    {
        var sessions = await contexte.Sessions.AsNoTracking()
            .Where(s => statut == null || s.Statut == statut)
            .OrderByDescending(s => s.OuvertureUtc)
            .Take(200)
            .ToListAsync(annulation);
        var resultat = new List<SessionDto>();
        foreach (var session in sessions)
        {
            resultat.Add(await VersDto(session, annulation));
        }

        return resultat;
    }

    public async Task<SessionDto> ObtenirAsync(Guid id, CancellationToken annulation)
    {
        var session = await contexte.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.SessionIntrouvable);
        return await VersDto(session, annulation);
    }

    private Task<SessionCaisse?> TrouverCourante(CancellationToken annulation)
    {
        var caissier = utilisateur.Identifiant;
        return contexte.Sessions.SingleOrDefaultAsync(s => s.Caissier == caissier && s.Statut == StatutSession.Ouverte, annulation);
    }

    /// <summary>
    /// Totaux de la session. Espèces théoriques = fond + espèces des ventes (moins le rendu) − espèces rendues sur les
    /// avoirs + remboursements de crédit en espèces.
    /// </summary>
    private async Task<TotauxSession> Totaux(SessionCaisse session, CancellationToken annulation)
    {
        var tickets = await contexte.Tickets.AsNoTracking().Where(t => t.SessionId == session.Id).ToListAsync(annulation);
        var remboursements = await contexte.Remboursements.AsNoTracking().Where(r => r.SessionId == session.Id).ToListAsync(annulation);
        var ventes = tickets.Where(t => !t.EstAvoir).ToList();
        var avoirs = tickets.Where(t => t.EstAvoir).ToList();

        decimal Encaisse(ModePaiement mode) =>
            ventes.SelectMany(t => t.Paiements).Where(p => p.Mode == mode).Sum(p => p.Montant)
            - avoirs.SelectMany(t => t.Paiements).Where(p => p.Mode == mode).Sum(p => p.Montant);

        var especes = tickets.Sum(t => t.EspecesNettes);
        var remboursementsEspeces = remboursements.Where(r => r.Mode == ModePaiement.Especes).Sum(r => r.Montant);
        return new TotauxSession(
            ventes.Count,
            avoirs.Count,
            ventes.Sum(t => t.TotalTtc),
            avoirs.Sum(t => t.TotalTtc),
            especes,
            Encaisse(ModePaiement.MobileMoney) + remboursements.Where(r => r.Mode == ModePaiement.MobileMoney).Sum(r => r.Montant),
            Encaisse(ModePaiement.Carte),
            Encaisse(ModePaiement.Credit),
            remboursements.Sum(r => r.Montant),
            session.FondCaisse + especes + remboursementsEspeces);
    }

    private async Task<SessionDto> VersDto(SessionCaisse s, CancellationToken annulation) =>
        new(s.Id, s.NumeroCaisse, s.Caissier, s.OuvertureUtc, s.FondCaisse, s.Statut, s.ClotureUtc, s.ClotureePar, s.EspecesComptees, s.Ecart, s.ValideePar,
            await Totaux(s, annulation));
}
