using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Identite.Application;

/// <summary>Compte tel que le voit l'Administrateur.</summary>
internal sealed record CompteUtilisateur(Guid Id, string Identifiant, IReadOnlyList<string> Roles);

/// <summary>Création et consultation des comptes (réservées à l'Administrateur).</summary>
internal sealed class ServiceUtilisateurs(UserManager<Utilisateur> utilisateurs, IdentiteDbContext contexte, IUniteDeTravail uniteDeTravail)
{
    public async Task<IReadOnlyList<CompteUtilisateur>> ListerAsync(CancellationToken annulation)
    {
        var roles = await contexte.UserRoles
            .Join(contexte.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(annulation);

        var comptes = await contexte.Users
            .OrderBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(annulation);

        return [.. comptes.Select(u => new CompteUtilisateur(
            u.Id,
            u.UserName!,
            [.. roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).Order(StringComparer.Ordinal)]))];
    }

    /// <summary>Crée un compte avec un rôle, dans une seule transaction.</summary>
    /// <exception cref="ExceptionIdentite">Identifiant vide ou pris, rôle inconnu, mot de passe refusé.</exception>
    public Task<CompteUtilisateur> CreerAsync(string identifiant, string motDePasse, string role, CancellationToken annulation)
    {
        identifiant = identifiant?.Trim() ?? "";
        if (identifiant.Length == 0)
        {
            throw new ExceptionIdentite(CodesErreurIdentite.IdentifiantObligatoire);
        }

        if (!Roles.Tous.Contains(role, StringComparer.Ordinal))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.RoleInconnu);
        }

        return uniteDeTravail.ExecuterAsync(
            async _ =>
            {
                if (await utilisateurs.FindByNameAsync(identifiant) is not null)
                {
                    throw new ExceptionIdentite(CodesErreurIdentite.IdentifiantDejaUtilise);
                }

                var utilisateur = new Utilisateur { UserName = identifiant };
                var creation = await utilisateurs.CreateAsync(utilisateur, motDePasse ?? "");
                if (!creation.Succeeded)
                {
                    var code = creation.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
                        ? CodesErreurIdentite.MotDePasseRefuse
                        : CodesErreurIdentite.IdentifiantDejaUtilise;
                    throw new ExceptionIdentite(code, string.Join(" ", creation.Errors.Select(e => e.Code)));
                }

                var ajoutRole = await utilisateurs.AddToRoleAsync(utilisateur, role);
                if (!ajoutRole.Succeeded)
                {
                    throw new InvalidOperationException($"Rôle {role} non attribué : {string.Join(", ", ajoutRole.Errors.Select(e => e.Code))}");
                }

                return new CompteUtilisateur(utilisateur.Id, identifiant, [role]);
            },
            annulation);
    }
}
