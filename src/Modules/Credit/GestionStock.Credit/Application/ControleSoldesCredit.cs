using System.Data;
using GestionStock.Credit.Contracts;
using GestionStock.Credit.Domain;
using GestionStock.Credit.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Credit.Application;

/// <summary>
/// Contrôle de cohérence de la dette des clients (ENF04) : dette = Σ ventes à crédit − Σ (remboursements + annulations).
/// </summary>
internal sealed class ControleSoldesCredit(CreditDbContext contexte, IUniteDeTravail uniteDeTravail) : IControleSoldesCredit
{
    public async Task<ControleCredit> ControlerAsync(CancellationToken annulation = default)
    {
        // Instantané cohérent : dettes et journal lus dans une même transaction en lecture répétable.
        await using var transaction = await contexte.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, annulation);
        var clients = await contexte.Clients.AsNoTracking()
            .Select(c => new { c.Id, c.Nom, c.SoldeDu })
            .ToListAsync(annulation);
        var journal = await SoldesDuJournal(null, annulation);

        var ecarts = clients
            .Select(c => new EcartCredit(c.Id, c.Nom, journal.GetValueOrDefault(c.Id), c.SoldeDu))
            .Where(e => e.Journal != e.Enregistre)
            .ToList();
        return new ControleCredit(clients.Count, ecarts);
    }

    public Task<EcartCredit?> ReconstruireAsync(Guid clientId, CancellationToken annulation = default) =>
        uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                // Même verrou qu'une vente à crédit ou un remboursement : aucune opération ne s'intercale.
                var client = await contexte.Clients
                    .FromSql($"SELECT * FROM credit.client WHERE id = {clientId} FOR UPDATE")
                    .SingleOrDefaultAsync(jeton);
                if (client is null)
                {
                    return null;
                }

                var journal = (await SoldesDuJournal(clientId, jeton)).GetValueOrDefault(clientId);
                if (journal == client.SoldeDu)
                {
                    return null;
                }

                var ecart = new EcartCredit(client.Id, client.Nom, journal, client.SoldeDu);
                client.Reconstruire(journal);
                await contexte.SaveChangesAsync(jeton);
                return ecart;
            },
            annulation);

    /// <summary>Dette de chaque client d'après son journal (ou d'un seul client).</summary>
    private async Task<Dictionary<Guid, decimal>> SoldesDuJournal(Guid? clientId, CancellationToken annulation)
    {
        var operations = contexte.Operations.AsNoTracking();
        if (clientId is { } id)
        {
            operations = operations.Where(o => o.ClientId == id);
        }

        return await operations
            .GroupBy(o => o.ClientId)
            .Select(g => new
            {
                ClientId = g.Key,
                Solde = g.Sum(o => o.Type == TypeOperationCredit.VenteCredit ? o.Montant : -o.Montant),
            })
            .ToDictionaryAsync(s => s.ClientId, s => s.Solde, annulation);
    }
}
