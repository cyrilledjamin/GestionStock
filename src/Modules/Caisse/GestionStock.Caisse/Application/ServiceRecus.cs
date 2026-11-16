using GestionStock.Caisse.Contracts;
using GestionStock.Caisse.Infrastructure;
using GestionStock.Credit.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionStock.Caisse.Application;

/// <summary>Identité de l'entreprise imprimée sur les reçus (section <c>Entreprise</c> de la configuration).</summary>
internal sealed class OptionsEntrepriseRecu
{
    public const string Section = "Entreprise";

    public string Nom { get; set; } = "GestionStock";

    public string? Adresse { get; set; }

    public string? Telephone { get; set; }

    /// <summary>Numéro d'identifiant unique du contribuable, s'il doit figurer sur le reçu.</summary>
    public string? NumeroContribuable { get; set; }
}

/// <summary>Assemble le reçu d'un ticket et le fait générer par <see cref="IGenerateurRecu"/> (décision 17).</summary>
internal sealed class ServiceRecus(
    CaisseDbContext contexte,
    ServiceTickets tickets,
    IServiceCredit credit,
    IGenerateurRecu generateur,
    IOptions<OptionsEntrepriseRecu> entreprise)
{
    public async Task<(byte[] Pdf, string Numero)> GenererPdfAsync(Guid ticketId, string langue, CancellationToken annulation)
    {
        var ticket = await tickets.ChargerAsync(ticketId, annulation);
        var client = ticket.ClientId is { } id ? await credit.ObtenirClientAsync(id, annulation) : null;
        var origine = ticket.TicketOrigineId is { } origineId
            ? await contexte.Tickets.AsNoTracking().Where(t => t.Id == origineId).Select(t => t.NumeroComplet).SingleOrDefaultAsync(annulation)
            : null;
        var options = entreprise.Value;

        var recu = new Recu(
            options.Nom,
            options.Adresse,
            options.Telephone,
            options.NumeroContribuable,
            ticket.NumeroComplet,
            ticket.DateUtc,
            ticket.Caissier,
            ticket.EstAvoir,
            origine,
            client?.Nom,
            [.. ticket.Lignes.OrderBy(l => l.Ordre).Select(l => new LigneRecu(l.Libelle, l.Quantite, l.PrixUnitaire, l.Remise, l.MontantTtc))],
            ticket.TotalTtc,
            ticket.TauxTva > 0m,
            ticket.TauxTva,
            ticket.TotalHt,
            ticket.TotalTva,
            [.. ticket.Paiements.Select(p => new PaiementRecu(p.Mode.ToString(), p.Montant, p.ReferenceTransaction))],
            ticket.Rendu);
        return (generateur.GenererPdf(recu, langue), ticket.NumeroComplet);
    }
}
