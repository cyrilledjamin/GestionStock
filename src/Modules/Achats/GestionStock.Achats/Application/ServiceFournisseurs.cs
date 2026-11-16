using GestionStock.Achats.Domain;
using GestionStock.Achats.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Achats.Application;

internal sealed record FournisseurDto(Guid Id, string Nom, string? Telephone, bool Actif);

internal sealed record SaisieFournisseur(string Nom, string? Telephone, bool Actif = true);

/// <summary>Gestion des fournisseurs.</summary>
internal sealed class ServiceFournisseurs(AchatsDbContext contexte)
{
    public async Task<IReadOnlyList<FournisseurDto>> ListerAsync(bool inclureInactifs, CancellationToken annulation) =>
        await contexte.Fournisseurs.AsNoTracking()
            .Where(f => inclureInactifs || f.Actif)
            .OrderBy(f => f.Nom)
            .Select(f => new FournisseurDto(f.Id, f.Nom, f.Telephone, f.Actif))
            .ToListAsync(annulation);

    public async Task<FournisseurDto> CreerAsync(SaisieFournisseur saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var fournisseur = Fournisseur.Creer(saisie.Nom, saisie.Telephone);
        contexte.Fournisseurs.Add(fournisseur);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(fournisseur);
    }

    public async Task<FournisseurDto> ModifierAsync(Guid id, SaisieFournisseur saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var fournisseur = await contexte.Fournisseurs.SingleOrDefaultAsync(f => f.Id == id, annulation)
            ?? throw new ExceptionAchats(CodesErreurAchats.FournisseurIntrouvable);
        fournisseur.Modifier(saisie.Nom, saisie.Telephone, saisie.Actif);
        await contexte.SaveChangesAsync(annulation);
        return VersDto(fournisseur);
    }

    private static FournisseurDto VersDto(Fournisseur f) => new(f.Id, f.Nom, f.Telephone, f.Actif);
}
