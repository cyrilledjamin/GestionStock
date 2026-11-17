using GestionStock.Credit.Contracts;
using GestionStock.Credit.Domain;
using GestionStock.Credit.Infrastructure;
using GestionStock.Identite.Contracts;
using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Credit.Application;

internal sealed record ClientDto(
    Guid Id,
    string Nom,
    string Telephone,
    decimal PlafondCredit,
    int DelaiPaiementJours,
    decimal SoldeDu,
    decimal MontantEnRetard,
    bool Actif);

internal sealed record VenteOuverte(Guid OperationId, Guid TicketId, string? NumeroTicket, DateTime DateUtc, DateTime EcheanceUtc, decimal Montant, decimal ResteDu, bool EnRetard);

internal sealed record OperationDto(Guid Id, DateTime DateUtc, TypeOperationCredit Type, decimal Montant, Guid DocumentId, string? NumeroTicket, string Utilisateur, string? ValideePar);

internal sealed record FicheClient(ClientDto Client, IReadOnlyList<VenteOuverte> VentesOuvertes, IReadOnlyList<OperationDto> Operations, IReadOnlyDictionary<string, string> Personnel);

internal sealed record SaisieClient(string Nom, string Telephone, int? DelaiPaiementJours, bool Actif = true);

internal sealed record SaisiePlafond(decimal PlafondCredit);

/// <summary>Clients et crédit (UC26, UC27, RG39 à RG43).</summary>
internal sealed class ServiceCredit(CreditDbContext contexte, IUniteDeTravail uniteDeTravail, IHorloge horloge, IAnnuairePersonnel annuaire) : IServiceCredit
{
    // --- Contrat utilisé par la Caisse ---------------------------------------------------------------------------

    public async Task<ClientCredit?> ObtenirClientAsync(Guid clientId, CancellationToken annulation = default) =>
        await contexte.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ClientCredit(c.Id, c.Nom, c.Telephone, c.PlafondCredit, c.SoldeDu, c.Actif))
            .SingleOrDefaultAsync(annulation);

    public Task EnregistrerVenteAsync(VenteACredit vente, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(vente);
        return uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var client = await VerrouillerClient(vente.ClientId, jeton);
                client.Devoir(vente.Montant, valideParResponsable: vente.ValideePar is not null || vente.HorsLigne);
                contexte.Operations.Add(OperationCredit.Vente(client, vente.Montant, vente.TicketId, vente.NumeroTicket, vente.Utilisateur, vente.ValideePar, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
            },
            annulation);
    }

    public Task EnregistrerRemboursementAsync(RemboursementCredit remboursement, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(remboursement);
        return uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var client = await VerrouillerClient(remboursement.ClientId, jeton);
                client.Diminuer(remboursement.Montant);
                var operation = OperationCredit.Diminution(TypeOperationCredit.Remboursement, client.Id, remboursement.Montant, remboursement.DocumentId, remboursement.Utilisateur, horloge.MaintenantUtc);
                contexte.Operations.Add(operation);

                // RG42 : imputation sur les ventes les plus anciennes.
                var reste = remboursement.Montant;
                foreach (var vente in await VentesOuvertes(client.Id, jeton))
                {
                    if (reste == 0m)
                    {
                        break;
                    }

                    var part = Math.Min(reste, vente.ResteDu);
                    contexte.Imputations.Add(new Imputation(operation.Id, vente.OperationId, part));
                    reste -= part;
                }

                await contexte.SaveChangesAsync(jeton);
            },
            annulation);
    }

    public Task<decimal> AnnulerVenteAsync(AnnulationCredit annulation, CancellationToken jeton = default)
    {
        ArgumentNullException.ThrowIfNull(annulation);
        return uniteDeTravail.ExecuterAsync(
            async j =>
            {
                var client = await VerrouillerClient(annulation.ClientId, j);
                var vente = (await VentesOuvertes(client.Id, j)).FirstOrDefault(v => v.TicketId == annulation.TicketVenteId);
                var montant = Math.Min(annulation.Montant, vente?.ResteDu ?? 0m);
                if (montant <= 0m)
                {
                    return 0m;
                }

                client.Diminuer(montant);
                var operation = OperationCredit.Diminution(TypeOperationCredit.Annulation, client.Id, montant, annulation.TicketAvoirId, annulation.Utilisateur, horloge.MaintenantUtc);
                contexte.Operations.Add(operation);
                contexte.Imputations.Add(new Imputation(operation.Id, vente!.OperationId, montant));
                await contexte.SaveChangesAsync(j);
                return montant;
            },
            jeton);
    }

    public async Task<decimal> ResteDuSurTicketAsync(Guid ticketId, CancellationToken annulation = default)
    {
        var vente = await contexte.Operations.AsNoTracking()
            .Where(o => o.Type == TypeOperationCredit.VenteCredit && o.DocumentId == ticketId)
            .Select(o => new { o.Id, o.Montant })
            .SingleOrDefaultAsync(annulation);
        if (vente is null)
        {
            return 0m;
        }

        var impute = await contexte.Imputations.Where(i => i.VenteId == vente.Id).SumAsync(i => (decimal?)i.Montant, annulation) ?? 0m;
        return vente.Montant - impute;
    }

    // --- Gestion des clients (back-office) -----------------------------------------------------------------------

    public async Task<IReadOnlyList<ClientDto>> ListerAsync(string? recherche, bool seulementEnRetard, CancellationToken annulation, int? taille = null)
    {
        var requete = contexte.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(recherche))
        {
            var motif = $"%{recherche.Trim()}%";
            requete = requete.Where(c => EF.Functions.ILike(c.Nom, motif) || EF.Functions.ILike(c.Telephone, motif));
        }

        var triee = requete.OrderBy(c => c.Nom);
        var clients = await (taille is { } maximum ? triee.Take(Math.Clamp(maximum, 1, 1000)) : triee).ToListAsync(annulation);
        var retards = await MontantsEnRetard(annulation);
        return [.. clients
            .Select(c => VersDto(c, retards.GetValueOrDefault(c.Id)))
            .Where(c => !seulementEnRetard || c.MontantEnRetard > 0m)];
    }

    public async Task<FicheClient> ObtenirFicheAsync(Guid id, CancellationToken annulation)
    {
        var client = await contexte.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, annulation)
            ?? throw new ExceptionCredit(CodesErreurCredit.ClientIntrouvable, "Client introuvable.");
        var ventes = await VentesOuvertes(id, annulation);
        var operations = await contexte.Operations.AsNoTracking()
            .Where(o => o.ClientId == id)
            .OrderByDescending(o => o.DateUtc)
            .Select(o => new OperationDto(o.Id, o.DateUtc, o.Type, o.Montant, o.DocumentId, o.NumeroTicket, o.Utilisateur, o.ValideePar))
            .ToListAsync(annulation);
        var personnel = await annuaire.DesignationsAsync(operations.SelectMany(o => new[] { o.Utilisateur, o.ValideePar }), annulation);
        return new FicheClient(VersDto(client, ventes.Where(v => v.EnRetard).Sum(v => v.ResteDu)), ventes, operations, personnel);
    }

    public async Task<ClientDto> CreerAsync(SaisieClient saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var client = Client.Creer(saisie.Nom, saisie.Telephone, saisie.DelaiPaiementJours);
        contexte.Clients.Add(client);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(client, 0m);
    }

    public async Task<ClientDto> ModifierAsync(Guid id, SaisieClient saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var client = await Trouver(id, annulation);
        client.Modifier(saisie.Nom, saisie.Telephone, saisie.DelaiPaiementJours ?? client.DelaiPaiementJours, saisie.Actif);
        await contexte.SaveChangesAsync(annulation);
        return (await ObtenirFicheAsync(id, annulation)).Client;
    }

    public async Task<ClientDto> FixerPlafondAsync(Guid id, SaisiePlafond saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var client = await Trouver(id, annulation);
        client.FixerPlafond(saisie.PlafondCredit);
        await contexte.SaveChangesAsync(annulation);
        return (await ObtenirFicheAsync(id, annulation)).Client;
    }

    // --- Lectures internes ---------------------------------------------------------------------------------------

    /// <summary>Ventes à crédit non soldées d'un client, de la plus ancienne à la plus récente.</summary>
    private async Task<List<VenteOuverte>> VentesOuvertes(Guid clientId, CancellationToken annulation)
    {
        // Les ventes déjà soldées (en base) ne sont pas relues : seul ce qui reste dû compte.
        var ventes = await contexte.Operations.AsNoTracking()
            .Where(o => o.ClientId == clientId && o.Type == TypeOperationCredit.VenteCredit
                && o.Montant > (contexte.Imputations.Where(i => i.VenteId == o.Id).Sum(i => (decimal?)i.Montant) ?? 0m))
            .OrderBy(o => o.DateUtc).ThenBy(o => o.Id)
            .ToListAsync(annulation);
        var ids = ventes.Select(v => v.Id).ToList();
        var imputes = await contexte.Imputations.AsNoTracking()
            .Where(i => ids.Contains(i.VenteId))
            .GroupBy(i => i.VenteId)
            .Select(g => new { VenteId = g.Key, Montant = g.Sum(i => i.Montant) })
            .ToDictionaryAsync(g => g.VenteId, g => g.Montant, annulation);

        // Imputations ajoutées mais pas encore enregistrées (celles déjà enregistrées sont relues ci-dessus).
        foreach (var locale in contexte.ChangeTracker.Entries<Imputation>().Where(e => e.State == EntityState.Added))
        {
            imputes[locale.Entity.VenteId] = imputes.GetValueOrDefault(locale.Entity.VenteId) + locale.Entity.Montant;
        }

        var maintenant = horloge.MaintenantUtc;
        return [.. ventes
            .Select(v => new VenteOuverte(v.Id, v.DocumentId, v.NumeroTicket, v.DateUtc, v.EcheanceUtc!.Value, v.Montant, v.Montant - imputes.GetValueOrDefault(v.Id), v.EcheanceUtc < maintenant))
            .Where(v => v.ResteDu > 0m)];
    }

    /// <summary>Montant en retard par client (RG43) : reste dû des ventes dont l'échéance est passée.</summary>
    private async Task<Dictionary<Guid, decimal>> MontantsEnRetard(CancellationToken annulation)
    {
        var maintenant = horloge.MaintenantUtc;
        // Calculé par la base : seules les sommes par client reviennent.
        return await contexte.Operations.AsNoTracking()
            .Where(o => o.Type == TypeOperationCredit.VenteCredit && o.EcheanceUtc < maintenant)
            .Select(o => new
            {
                o.ClientId,
                Reste = o.Montant - (contexte.Imputations.Where(i => i.VenteId == o.Id).Sum(i => (decimal?)i.Montant) ?? 0m),
            })
            .Where(e => e.Reste > 0m)
            .GroupBy(e => e.ClientId)
            .Select(g => new { ClientId = g.Key, Montant = g.Sum(e => e.Reste) })
            .ToDictionaryAsync(g => g.ClientId, g => g.Montant, annulation);
    }

    private async Task<Client> VerrouillerClient(Guid id, CancellationToken annulation)
    {
        // Deux ventes à crédit simultanées du même client s'enchaînent : le plafond est contrôlé sur le solde à jour.
        var trouve = await contexte.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM credit.client WHERE id = {id} FOR UPDATE").ToListAsync(annulation);
        if (trouve.Count == 0)
        {
            throw new ExceptionCredit(CodesErreurCredit.ClientIntrouvable, "Client introuvable.");
        }

        return await contexte.Clients.SingleAsync(c => c.Id == id, annulation);
    }

    private async Task<Client> Trouver(Guid id, CancellationToken annulation) =>
        await contexte.Clients.SingleOrDefaultAsync(c => c.Id == id, annulation)
        ?? throw new ExceptionCredit(CodesErreurCredit.ClientIntrouvable, "Client introuvable.");

    private static ClientDto VersDto(Client c, decimal enRetard) =>
        new(c.Id, c.Nom, c.Telephone, c.PlafondCredit, c.DelaiPaiementJours, c.SoldeDu, enRetard, c.Actif);
}
