using GestionStock.Achats.Domain;
using GestionStock.Achats.Infrastructure;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Achats.Application;

/// <summary>Dette d'une réception validée : marchandise et frais d'approche, moins les paiements.</summary>
internal sealed record DetteReception(
    Guid ReceptionId,
    string Numero,
    Guid FournisseurId,
    string Fournisseur,
    string? NumeroBonLivraison,
    DateTime DateValidationUtc,
    decimal MontantDu,
    decimal MontantPaye,
    decimal ResteDu,
    StatutDette Statut);

/// <summary>Total dû à un fournisseur, toutes réceptions validées confondues.</summary>
internal sealed record DetteFournisseur(Guid FournisseurId, string Fournisseur, int NombreReceptions, decimal MontantDu, decimal MontantPaye, decimal ResteDu);

internal sealed record Dettes(IReadOnlyList<DetteFournisseur> Fournisseurs, IReadOnlyList<DetteReception> Receptions);

internal sealed record DetailPaiement(
    Guid Id,
    DateTime DateUtc,
    decimal Montant,
    ModePaiementFournisseur Mode,
    string? Reference,
    string SaisiPar,
    Guid? PaiementAnnuleId,
    string? Motif,
    bool EstAnnule);

/// <summary>Dette d'une réception avec l'historique de ses paiements et annulations.</summary>
internal sealed record DetailDette(DetteReception Dette, IReadOnlyList<DetailPaiement> Paiements, IReadOnlyDictionary<string, string> Personnel);

internal sealed record SaisiePaiement(decimal Montant, ModePaiementFournisseur Mode, string? Reference);

internal sealed record SaisieAnnulationPaiement(string? Motif);

/// <summary>
/// Dettes fournisseurs (décision 27) : montant dû par réception validée (marchandise et frais d'approche), paiements
/// partiels, liste et annulation journalisée.
/// </summary>
internal sealed class ServiceDettes(
    AchatsDbContext contexte,
    IUniteDeTravail uniteDeTravail,
    IUtilisateurCourant utilisateur,
    IHorloge horloge,
    IAnnuairePersonnel annuaire)
{
    public async Task<Dettes> ListerAsync(Guid? fournisseurId, bool seulementNonSoldees, CancellationToken annulation)
    {
        var receptions = await contexte.Receptions.AsNoTracking()
            .Where(r => r.Statut == StatutReception.Validee && (fournisseurId == null || r.FournisseurId == fournisseurId))
            .ToListAsync(annulation);
        var payes = await contexte.PaiementsFournisseurs.AsNoTracking()
            .GroupBy(p => p.ReceptionId)
            .Select(g => new { ReceptionId = g.Key, Paye = g.Sum(p => p.Montant) })
            .ToDictionaryAsync(p => p.ReceptionId, p => p.Paye, annulation);
        var noms = await contexte.Fournisseurs.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Nom, annulation);

        var dettes = receptions
            .Select(r => VersDette(r, noms[r.FournisseurId], payes.GetValueOrDefault(r.Id)))
            .Where(d => !seulementNonSoldees || d.ResteDu > 0m)
            .OrderBy(d => d.DateValidationUtc)
            .ToList();
        var parFournisseur = dettes
            .GroupBy(d => (d.FournisseurId, d.Fournisseur))
            .Select(g => new DetteFournisseur(g.Key.FournisseurId, g.Key.Fournisseur, g.Count(), g.Sum(d => d.MontantDu), g.Sum(d => d.MontantPaye), g.Sum(d => d.ResteDu)))
            .OrderByDescending(f => f.ResteDu)
            .ToList();
        return new Dettes(parFournisseur, dettes);
    }

    public async Task<DetailDette> ObtenirAsync(Guid receptionId, CancellationToken annulation)
    {
        var reception = await contexte.Receptions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == receptionId, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        if (reception.Statut != StatutReception.Validee)
        {
            throw new ExceptionAchats(CodesErreurAchats.PaiementSurBrouillon);
        }

        return await VersDetail(reception, annulation);
    }

    /// <summary>Paiement partiel ou total ; la réception est verrouillée pour que deux paiements ne dépassent pas le reste dû.</summary>
    public async Task<DetailDette> PayerAsync(Guid receptionId, SaisiePaiement saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var reception = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var aPayer = await Verrouiller(receptionId, jeton);
                var paye = await contexte.PaiementsFournisseurs.Where(p => p.ReceptionId == receptionId).SumAsync(p => p.Montant, jeton);
                var paiement = PaiementFournisseur.Payer(
                    aPayer,
                    MontantDu(aPayer) - paye,
                    saisie.Montant,
                    saisie.Mode,
                    saisie.Reference,
                    utilisateur.Identifiant,
                    horloge.MaintenantUtc);
                contexte.PaiementsFournisseurs.Add(paiement);
                await contexte.SaveChangesAsync(jeton);
                return aPayer;
            },
            annulation);

        return await VersDetail(reception, annulation);
    }

    public async Task<DetailDette> AnnulerAsync(Guid paiementId, SaisieAnnulationPaiement saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var reception = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var paiement = await contexte.PaiementsFournisseurs.SingleOrDefaultAsync(p => p.Id == paiementId, jeton)
                    ?? throw new ExceptionAchats(CodesErreurAchats.PaiementIntrouvable);
                var concernee = await Verrouiller(paiement.ReceptionId, jeton);
                var dejaAnnule = await contexte.PaiementsFournisseurs.AnyAsync(p => p.PaiementAnnuleId == paiementId, jeton);
                contexte.PaiementsFournisseurs.Add(paiement.Annuler(dejaAnnule, saisie.Motif, utilisateur.Identifiant, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
                return concernee;
            },
            annulation);

        return await VersDetail(reception, annulation);
    }

    /// <summary>Montant dû : marchandise et frais d'approche, y compris les frais tardifs (choix validé).</summary>
    private static decimal MontantDu(Reception reception) => reception.ValeurMarchandise + reception.TotalFrais;

    private async Task<Reception> Verrouiller(Guid id, CancellationToken annulation)
    {
        var trouvee = await contexte.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM achats.reception WHERE id = {id} FOR UPDATE")
            .ToListAsync(annulation);
        if (trouvee.Count == 0)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReceptionIntrouvable);
        }

        return await contexte.Receptions.SingleAsync(r => r.Id == id, annulation);
    }

    private static DetteReception VersDette(Reception reception, string fournisseur, decimal paye)
    {
        var du = MontantDu(reception);
        return new DetteReception(
            reception.Id,
            reception.Numero,
            reception.FournisseurId,
            fournisseur,
            reception.NumeroBonLivraison,
            reception.DateValidationUtc!.Value,
            du,
            paye,
            du - paye,
            PaiementFournisseur.Statut(du, paye));
    }

    private async Task<DetailDette> VersDetail(Reception reception, CancellationToken annulation)
    {
        var paiements = await contexte.PaiementsFournisseurs.AsNoTracking()
            .Where(p => p.ReceptionId == reception.Id)
            .OrderBy(p => p.DateUtc)
            .ToListAsync(annulation);
        var annules = paiements.Where(p => p.PaiementAnnuleId is not null).Select(p => p.PaiementAnnuleId!.Value).ToHashSet();
        var fournisseur = await contexte.Fournisseurs.AsNoTracking().Where(f => f.Id == reception.FournisseurId).Select(f => f.Nom).SingleAsync(annulation);

        return new DetailDette(
            VersDette(reception, fournisseur, paiements.Sum(p => p.Montant)),
            [.. paiements.Select(p => new DetailPaiement(p.Id, p.DateUtc, p.Montant, p.Mode, p.Reference, p.SaisiPar, p.PaiementAnnuleId, p.Motif, annules.Contains(p.Id)))],
            await annuaire.DesignationsAsync(paiements.Select(p => p.SaisiPar), annulation));
    }
}
