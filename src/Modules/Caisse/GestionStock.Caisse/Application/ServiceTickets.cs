using System.Security.Cryptography;
using GestionStock.Caisse.Contracts;
using GestionStock.Caisse.Domain;
using GestionStock.Caisse.Infrastructure;
using GestionStock.Credit.Contracts;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Prix.Contracts;
using GestionStock.Referentiel.Contracts;
using GestionStock.SharedKernel;
using GestionStock.Stock.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Caisse.Application;

internal sealed record LigneDemandee(Guid ConditionnementId, decimal Quantite, decimal Remise = 0m, string? CodeScanne = null);

/// <summary>Ticket envoyé par le poste. <see cref="IdIdempotence"/> est généré par le poste (renvoi sans doublon).</summary>
internal sealed record DemandeVente(
    Guid IdIdempotence,
    Guid? ClientId,
    IReadOnlyList<LigneDemandee> Lignes,
    IReadOnlyList<PaiementSaisi> Paiements,
    ValidationResponsable? Validation);

internal sealed record LigneRendue(Guid LigneOrigineId, decimal Quantite);

/// <summary>Avoir sur un ticket : lignes rendues ; remboursement (hors part à crédit) en espèces par défaut.</summary>
internal sealed record DemandeAvoir(
    Guid IdIdempotence,
    Guid TicketOrigineId,
    IReadOnlyList<LigneRendue> Lignes,
    ModePaiement? ModeRemboursement,
    string? Reference);

internal sealed record DemandeRemboursement(Guid IdIdempotence, Guid ClientId, ModePaiement Mode, decimal Montant, string? Reference);

internal sealed record LigneTicketDto(
    Guid Id,
    int Ordre,
    Guid ArticleId,
    Guid ConditionnementId,
    string Libelle,
    string LibelleConditionnement,
    decimal Quantite,
    decimal QuantiteRendue,
    decimal PrixUnitaire,
    decimal Remise,
    decimal MontantTtc);

internal sealed record PaiementDto(ModePaiement Mode, decimal Montant, string? Reference);

internal sealed record TicketDto(
    Guid Id,
    Guid IdIdempotence,
    string Numero,
    DateTime DateUtc,
    string Caissier,
    bool EstAvoir,
    Guid? TicketOrigineId,
    Guid? ClientId,
    decimal TotalTtc,
    decimal TotalHt,
    decimal TotalTva,
    decimal TauxTva,
    decimal Rendu,
    string? RemiseValideePar,
    string? CreditValidePar,
    IReadOnlyList<LigneTicketDto> Lignes,
    IReadOnlyList<PaiementDto> Paiements);

internal sealed record RemboursementDto(Guid Id, Guid ClientId, ModePaiement Mode, decimal Montant, string? Reference, DateTime DateUtc, decimal SoldeDuApres);

/// <summary>
/// Ventes (UC17), avoirs (UC18) et remboursements de crédit (UC27) d'une session de caisse. Chaque opération est
/// écrite en une seule transaction : ticket, mouvements de stock (RG21) et opération de crédit, ou rien.
/// </summary>
internal sealed class ServiceTickets(
    CaisseDbContext contexte,
    ServiceSessions sessions,
    IConsultationReferentiel referentiel,
    IConsultationPrix prix,
    IConsultationStock consultationStock,
    IServiceMouvementsStock mouvements,
    IServiceCredit credit,
    IValidationResponsable validation,
    IUniteDeTravail uniteDeTravail,
    IHorloge horloge)
{
    /// <summary>Encaisse une vente. Un renvoi du même ticket (même identifiant d'idempotence) renvoie le ticket déjà enregistré.</summary>
    public async Task<(TicketDto Ticket, bool DejaEnregistre)> VendreAsync(DemandeVente demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        if (await TrouverParIdempotence(demande.IdIdempotence, annulation) is { } existant)
        {
            return (existant, true);
        }

        var session = await sessions.ExigerCouranteAsync(annulation);
        var regime = prix.Regime;
        var lignes = await PreparerVente(demande.Lignes ?? [], annulation);
        var paiements = demande.Paiements ?? [];
        if (demande.ClientId is { } clientId && await credit.ObtenirClientAsync(clientId, annulation) is null)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.ClientIntrouvable);
        }

        // Validation d'un responsable : remise (décision 31) ou dépassement de plafond de crédit (RG40).
        var valideur = await VerifierResponsable(demande.Validation, annulation);

        var ticket = await Ecrire(
            demande.IdIdempotence,
            session,
            async (numero, jeton) =>
            {
                var vente = TicketCaisse.Vente(demande.IdIdempotence, session, numero, horloge.MaintenantUtc, lignes, paiements, demande.ClientId, regime.TauxApplique, valideur, valideur);
                contexte.Tickets.Add(vente);
                await contexte.SaveChangesAsync(jeton);
                await EcrireMouvements(vente, TypeMouvementStock.VenteCaisse, jeton);
                if (vente.MontantCredit > 0m)
                {
                    await credit.EnregistrerVenteAsync(
                        new VenteACredit(vente.ClientId!.Value, vente.Id, vente.NumeroComplet, vente.MontantCredit, vente.Caissier, valideur),
                        jeton);
                }

                return vente;
            },
            annulation);
        return (await VersDto(ticket, annulation), false);
    }

    /// <summary>Avoir (UC18) : reprend tout ou partie des lignes d'un ticket ; la marchandise revient en stock.</summary>
    public async Task<(TicketDto Ticket, bool DejaEnregistre)> FaireAvoirAsync(DemandeAvoir demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        if (await TrouverParIdempotence(demande.IdIdempotence, annulation) is { } existant)
        {
            return (existant, true);
        }

        var session = await sessions.ExigerCouranteAsync(annulation);
        var origine = await contexte.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == demande.TicketOrigineId, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.TicketIntrouvable);
        if (origine.EstAvoir)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.AvoirSurAvoir);
        }

        var remboursement = demande.ModeRemboursement is { } mode ? new PaiementSaisi(mode, 0m, demande.Reference) : null;
        var ticket = await Ecrire(
            demande.IdIdempotence,
            session,
            async (numero, jeton) =>
            {
                // Quantités déjà rendues relues dans la transaction : deux avoirs simultanés ne rendent pas deux fois.
                var lignes = await PreparerAvoir(origine, demande.Lignes ?? [], jeton);
                var total = lignes.Sum(l => decimal.Round(l.Quantite * l.PrixUnitaire, 0, MidpointRounding.AwayFromZero) - l.Remise);
                var annulationCredit = origine.ClientId is { } client
                    ? Math.Min(total, await credit.ResteDuSurTicketAsync(origine.Id, jeton))
                    : 0m;

                var avoir = TicketCaisse.Avoir(demande.IdIdempotence, session, numero, horloge.MaintenantUtc, origine, lignes, annulationCredit, remboursement);
                contexte.Tickets.Add(avoir);
                await contexte.SaveChangesAsync(jeton);
                await EcrireMouvements(avoir, TypeMouvementStock.AvoirCaisse, jeton);
                if (annulationCredit > 0m)
                {
                    var annule = await credit.AnnulerVenteAsync(new AnnulationCredit(origine.ClientId!.Value, origine.Id, avoir.Id, annulationCredit, avoir.Caissier), jeton);
                    if (annule != annulationCredit)
                    {
                        throw new InvalidOperationException("La dette du ticket a changé pendant l'avoir.");
                    }
                }

                return avoir;
            },
            annulation);
        return (await VersDto(ticket, annulation), false);
    }

    /// <summary>Remboursement de crédit encaissé dans la session (RG42), en espèces ou Mobile Money.</summary>
    public async Task<RemboursementDto> RembourserAsync(DemandeRemboursement demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        var existant = await contexte.Remboursements.AsNoTracking().SingleOrDefaultAsync(r => r.IdIdempotence == demande.IdIdempotence, annulation);
        if (existant is null)
        {
            var session = await sessions.ExigerCouranteAsync(annulation);
            if (await credit.ObtenirClientAsync(demande.ClientId, annulation) is null)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ClientIntrouvable);
            }

            existant = await uniteDeTravail.ExecuterAsync(
                async jeton =>
                {
                    await VerrouillerIdempotence(demande.IdIdempotence, jeton);
                    var remboursement = RemboursementCaisse.Encaisser(demande.IdIdempotence, session, demande.ClientId, new PaiementSaisi(demande.Mode, demande.Montant, demande.Reference), horloge.MaintenantUtc);
                    contexte.Remboursements.Add(remboursement);
                    await contexte.SaveChangesAsync(jeton);
                    await TraduireCredit(() => credit.EnregistrerRemboursementAsync(
                        new RemboursementCredit(remboursement.ClientId, remboursement.Montant, remboursement.Id, remboursement.Caissier), jeton));
                    return remboursement;
                },
                annulation);
        }

        var client = await credit.ObtenirClientAsync(existant.ClientId, annulation);
        return new RemboursementDto(existant.Id, existant.ClientId, existant.Mode, existant.Montant, existant.ReferenceTransaction, existant.DateUtc, client?.SoldeDu ?? 0m);
    }

    public async Task<TicketDto> ObtenirAsync(Guid id, CancellationToken annulation)
    {
        var ticket = await contexte.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.TicketIntrouvable);
        return await VersDto(ticket, annulation);
    }

    /// <summary>Recherche d'un ticket par son numéro imprimé (ex. pour un avoir), sans tenir compte de la casse.</summary>
    public async Task<TicketDto> ObtenirParNumeroAsync(string numero, CancellationToken annulation)
    {
        var nettoye = numero?.Trim().ToUpperInvariant() ?? "";
        var ticket = await contexte.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.NumeroComplet == nettoye, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.TicketIntrouvable);
        return await VersDto(ticket, annulation);
    }

    public async Task<IReadOnlyList<TicketDto>> ListerSessionAsync(Guid sessionId, CancellationToken annulation)
    {
        var tickets = await contexte.Tickets.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.Numero)
            .ToListAsync(annulation);
        var resultat = new List<TicketDto>();
        foreach (var ticket in tickets)
        {
            resultat.Add(await VersDto(ticket, annulation));
        }

        return resultat;
    }

    /// <summary>Ticket complet (entité) pour l'impression du reçu.</summary>
    public async Task<TicketCaisse> ChargerAsync(Guid id, CancellationToken annulation) =>
        await contexte.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, annulation)
        ?? throw new ExceptionCaisse(CodesErreurCaisse.TicketIntrouvable);

    // --- Écriture ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ouvre la transaction, sérialise les renvois du même ticket, attribue le numéro suivant du poste (sans trou : le
    /// compteur est annulé avec la transaction, RG34) puis écrit le ticket.
    /// </summary>
    private async Task<TicketCaisse> Ecrire(Guid idIdempotence, SessionCaisse session, Func<long, CancellationToken, Task<TicketCaisse>> ecriture, CancellationToken annulation)
    {
        try
        {
            return await uniteDeTravail.ExecuterAsync(
                async jeton =>
                {
                    await VerrouillerIdempotence(idIdempotence, jeton);
                    if (await contexte.Tickets.AsNoTracking().AnyAsync(t => t.IdIdempotence == idIdempotence, jeton))
                    {
                        return await contexte.Tickets.AsNoTracking().SingleAsync(t => t.IdIdempotence == idIdempotence, jeton);
                    }

                    var numero = await NumeroSuivant(session.NumeroCaisse, jeton);
                    return await ecriture(numero, jeton);
                },
                annulation);
        }
        catch (ExceptionStock erreur)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.MouvementRefuse, erreur.Code);
        }
        catch (ExceptionCredit erreur)
        {
            throw TraduireCredit(erreur);
        }
    }

    private async Task<long> NumeroSuivant(string poste, CancellationToken annulation)
    {
        await contexte.Database.ExecuteSqlAsync(
            $"INSERT INTO caisse.compteur_caisse (numero_caisse, dernier_numero) VALUES ({poste}, 0) ON CONFLICT DO NOTHING",
            annulation);
        var numeros = await contexte.Database
            .SqlQuery<long>($"UPDATE caisse.compteur_caisse SET dernier_numero = dernier_numero + 1 WHERE numero_caisse = {poste} RETURNING dernier_numero AS \"Value\"")
            .ToListAsync(annulation);
        return numeros.Single();
    }

    /// <summary>Verrou transactionnel sur l'identifiant d'idempotence : deux envois simultanés s'enchaînent.</summary>
    private Task VerrouillerIdempotence(Guid idIdempotence, CancellationToken annulation) =>
        contexte.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({idIdempotence.ToString()}, 0))", annulation);

    /// <summary>
    /// Un mouvement par ligne depuis (vente) ou vers (avoir) l'emplacement de vente, au CUMP courant. La vente n'est
    /// jamais refusée pour stock insuffisant (RG22). Identifiant d'idempotence dérivé de celui du ticket.
    /// </summary>
    private async Task EcrireMouvements(TicketCaisse ticket, TypeMouvementStock type, CancellationToken annulation)
    {
        var site = await referentiel.ObtenirSiteBoutiqueAsync(annulation);
        var vente = new EmplacementMouvement(site.EmplacementVenteId, TypeEmplacementStock.Vente);
        foreach (var ligne in ticket.Lignes.OrderBy(l => l.Ordre))
        {
            var cump = (await consultationStock.ObtenirSoldeSiteAsync(ligne.ArticleId, site.SiteId, annulation))?.Cump ?? 0m;
            await mouvements.EnregistrerAsync(
                new DemandeMouvement(
                    type,
                    ligne.ArticleId,
                    site.SiteId,
                    Source: ticket.EstAvoir ? null : vente,
                    Destination: ticket.EstAvoir ? vente : null,
                    ligne.QuantiteStock,
                    decimal.Round(cump, 4),
                    TypesDocumentsCaisse.Ticket,
                    ticket.Id,
                    ticket.Caissier,
                    IdIdempotence: Deriver(ticket.IdIdempotence, ligne.Ordre),
                    DateUtc: ticket.DateUtc),
                annulation);
        }
    }

    // --- Préparation ---------------------------------------------------------------------------------------------

    private async Task<List<LignePreparee>> PreparerVente(IReadOnlyList<LigneDemandee> demandees, CancellationToken annulation)
    {
        if (demandees.Count == 0)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.TicketSansLigne);
        }

        var aujourdhui = horloge.AujourdhuiDouala;
        var lignes = new List<LignePreparee>();
        foreach (var demandee in demandees)
        {
            var conditionnement = await referentiel.ObtenirConditionnementAsync(demandee.ConditionnementId, annulation)
                ?? throw new ExceptionCaisse(CodesErreurCaisse.ConditionnementIntrouvable);
            var article = conditionnement.Article;
            if (!conditionnement.Actif || !article.Actif)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ArticleInactif, article.Code);
            }

            // RG37 : le prix vient de l'application, jamais du poste.
            var prixApplicable = await prix.ObtenirPrixAsync(conditionnement.Id, aujourdhui, annulation)
                ?? throw new ExceptionCaisse(CodesErreurCaisse.PrixAbsent, $"{article.Code} ({conditionnement.Libelle})");
            ExigerPrecision(demandee.Quantite, conditionnement.QuantiteUniteStock, article);

            lignes.Add(new LignePreparee(
                conditionnement.Id,
                article.Id,
                Libelle(article, conditionnement),
                conditionnement.Libelle,
                string.IsNullOrWhiteSpace(demandee.CodeScanne) ? null : demandee.CodeScanne.Trim(),
                demandee.Quantite,
                conditionnement.QuantiteUniteStock,
                prixApplicable.PrixTtc,
                demandee.Remise));
        }

        return lignes;
    }

    private async Task<List<LignePreparee>> PreparerAvoir(TicketCaisse origine, IReadOnlyList<LigneRendue> rendues, CancellationToken annulation)
    {
        if (rendues.Count == 0)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.TicketSansLigne);
        }

        var dejaRendu = await QuantitesRendues(origine, annulation);
        var lignes = new List<LignePreparee>();
        foreach (var rendue in rendues)
        {
            var ligne = origine.Lignes.SingleOrDefault(l => l.Id == rendue.LigneOrigineId)
                ?? throw new ExceptionCaisse(CodesErreurCaisse.LigneOrigineIntrouvable);
            if (rendue.Quantite <= 0m)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.QuantiteInvalide, ligne.Libelle);
            }

            var disponible = ligne.Quantite - dejaRendu.GetValueOrDefault(ligne.Id) - lignes.Where(l => l.LigneOrigineId == ligne.Id).Sum(l => l.Quantite);
            if (rendue.Quantite > disponible)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.QuantiteRenduTropGrande, $"{ligne.Libelle} : {disponible:0.###}");
            }

            var article = (await referentiel.ObtenirArticleAsync(ligne.ArticleId, annulation))!;
            ExigerPrecision(rendue.Quantite, ligne.QuantiteUniteStock, article);

            // La remise d'origine est rendue au prorata des quantités.
            var remise = decimal.Round(ligne.Remise * rendue.Quantite / ligne.Quantite, 0, MidpointRounding.AwayFromZero);
            lignes.Add(new LignePreparee(ligne.ConditionnementId, ligne.ArticleId, ligne.Libelle, ligne.LibelleConditionnement, null, rendue.Quantite, ligne.QuantiteUniteStock, ligne.PrixUnitaire, remise, ligne.Id));
        }

        return lignes;
    }

    private async Task<Dictionary<Guid, decimal>> QuantitesRendues(TicketCaisse origine, CancellationToken annulation)
    {
        var ids = origine.Lignes.Select(l => l.Id).ToList();
        return await contexte.Lignes.AsNoTracking()
            .Where(l => l.LigneOrigineId != null && ids.Contains(l.LigneOrigineId.Value))
            .GroupBy(l => l.LigneOrigineId!.Value)
            .Select(g => new { Ligne = g.Key, Quantite = g.Sum(l => l.Quantite) })
            .ToDictionaryAsync(g => g.Ligne, g => g.Quantite, annulation);
    }

    /// <summary>La quantité en unités de stock respecte la précision de l'article (RG46).</summary>
    private static void ExigerPrecision(decimal quantite, decimal facteur, ArticleReferentiel article)
    {
        var quantiteStock = quantite * facteur;
        if (quantite <= 0m || decimal.Round(quantiteStock, article.PrecisionQuantite) != quantiteStock || decimal.Round(quantite, 4) != quantite)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.QuantiteInvalide, article.Code);
        }
    }

    private static string Libelle(ArticleReferentiel article, ConditionnementReferentiel conditionnement) =>
        conditionnement.QuantiteUniteStock == 1m ? article.Libelle : $"{article.Libelle} ({conditionnement.Libelle})";

    private async Task<string?> VerifierResponsable(ValidationResponsable? saisie, CancellationToken annulation)
    {
        if (saisie is null || string.IsNullOrWhiteSpace(saisie.Identifiant))
        {
            return null;
        }

        return await validation.VerifierAsync(saisie.Identifiant, saisie.MotDePasse, Permissions.DepassementsValider, annulation)
            ?? throw new ExceptionCaisse(CodesErreurCaisse.ValidationRefusee);
    }

    private async Task<TicketDto?> TrouverParIdempotence(Guid idIdempotence, CancellationToken annulation)
    {
        if (idIdempotence == Guid.Empty)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.IdempotenceObligatoire);
        }

        var ticket = await contexte.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.IdIdempotence == idIdempotence, annulation);
        return ticket is null ? null : await VersDto(ticket, annulation);
    }

    private async Task<TicketDto> VersDto(TicketCaisse t, CancellationToken annulation)
    {
        var rendues = t.EstAvoir ? [] : await QuantitesRendues(t, annulation);
        return new TicketDto(
            t.Id, t.IdIdempotence, t.NumeroComplet, t.DateUtc, t.Caissier, t.EstAvoir, t.TicketOrigineId, t.ClientId,
            t.TotalTtc, t.TotalHt, t.TotalTva, t.TauxTva, t.Rendu, t.RemiseValideePar, t.CreditValidePar,
            [.. t.Lignes.OrderBy(l => l.Ordre).Select(l => new LigneTicketDto(
                l.Id, l.Ordre, l.ArticleId, l.ConditionnementId, l.Libelle, l.LibelleConditionnement, l.Quantite,
                rendues.GetValueOrDefault(l.Id), l.PrixUnitaire, l.Remise, l.MontantTtc))],
            [.. t.Paiements.Select(p => new PaiementDto(p.Mode, p.Montant, p.ReferenceTransaction))]);
    }

    private static async Task TraduireCredit(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ExceptionCredit erreur)
        {
            throw TraduireCredit(erreur);
        }
    }

    private static ExceptionCaisse TraduireCredit(ExceptionCredit erreur) => erreur.Code switch
    {
        CodesErreurCredit.PlafondDepasse => new ExceptionCaisse(CodesErreurCaisse.PlafondDepasse),
        CodesErreurCredit.ClientIntrouvable or CodesErreurCredit.ClientInactif => new ExceptionCaisse(CodesErreurCaisse.ClientIntrouvable),
        _ => new ExceptionCaisse(CodesErreurCaisse.RemboursementInvalide, erreur.Code),
    };

    /// <summary>Identifiant stable dérivé (même ticket, même ligne → même identifiant de mouvement).</summary>
    private static Guid Deriver(Guid base_, int ordre)
    {
        Span<byte> donnees = stackalloc byte[20];
        base_.TryWriteBytes(donnees);
        BitConverter.TryWriteBytes(donnees[16..], ordre);
        Span<byte> empreinte = stackalloc byte[32];
        SHA256.HashData(donnees, empreinte);
        return new Guid(empreinte[..16]);
    }
}
