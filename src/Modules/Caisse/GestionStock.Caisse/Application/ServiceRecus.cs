using GestionStock.Caisse.Contracts;
using GestionStock.Caisse.Infrastructure;
using GestionStock.Credit.Contracts;
using GestionStock.Identite.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionStock.Caisse.Application;

/// <summary>Identité publique de la boutique : nom et présence d'un logo.</summary>
internal sealed record IdentiteBoutique(string Nom, bool Logo);

/// <summary>Identité de l'entreprise imprimée sur les reçus (section <c>Entreprise</c> de la configuration).</summary>
internal sealed class OptionsEntrepriseRecu
{
    public const string Section = "Entreprise";

    public string Nom { get; set; } = "GestionStock";

    public string? Adresse { get; set; }

    public string? Telephone { get; set; }

    /// <summary>Numéro d'identifiant unique du contribuable, s'il doit figurer sur le reçu.</summary>
    public string? NumeroContribuable { get; set; }

    /// <summary>
    /// Logo facultatif : chemin d'une image PNG, JPEG ou WebP dans le stockage de fichiers (ex. <c>identite/logo.png</c>).
    /// Le SVG est refusé : servi depuis le site même, il pourrait exécuter du script.
    /// </summary>
    public string? Logo { get; set; }

    /// <summary>Type MIME du logo d'après son extension, ou null si l'extension n'est pas une image acceptée.</summary>
    public string? TypeLogo => Path.GetExtension(Logo ?? "").ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => null,
    };
}

/// <summary>Assemble le reçu d'un ticket et le fait générer par <see cref="IGenerateurRecu"/> (décision 17).</summary>
internal sealed class ServiceRecus(
    CaisseDbContext contexte,
    ServiceTickets tickets,
    IServiceCredit credit,
    IGenerateurRecu generateur,
    IAnnuairePersonnel annuaire,
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
            await annuaire.DesignationAsync(ticket.Caissier, annulation),
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
