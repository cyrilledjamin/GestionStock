using RolesV1 = GestionStock.Identite.Contracts.Roles;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Identite.Infrastructure;

/// <summary>Persistance du module Identite, dans le schéma PostgreSQL <c>identite</c>.</summary>
internal sealed class IdentiteDbContext(DbContextOptions<IdentiteDbContext> options)
    : IdentityDbContext<Utilisateur, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identite";

    /// <summary>Identifiants fixes des rôles, créés par la migration (jamais modifiés).</summary>
    public static IReadOnlyDictionary<string, Guid> IdentifiantsRoles { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [RolesV1.Administrateur] = new("0193a5c0-0000-7000-8000-000000000001"),
        [RolesV1.Gerant] = new("0193a5c0-0000-7000-8000-000000000002"),
        [RolesV1.Comptable] = new("0193a5c0-0000-7000-8000-000000000003"),
        [RolesV1.Caissier] = new("0193a5c0-0000-7000-8000-000000000004"),
        [RolesV1.Magasinier] = new("0193a5c0-0000-7000-8000-000000000005"),
    };

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<Utilisateur>().ToTable("utilisateur");
        builder.Entity<IdentityRole<Guid>>().ToTable("role");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("utilisateur_role");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("utilisateur_revendication");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("utilisateur_connexion_externe");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("utilisateur_jeton");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_revendication");

        builder.Entity<IdentityRole<Guid>>().HasData(RolesV1.Tous.Select(nom => new IdentityRole<Guid>
        {
            Id = IdentifiantsRoles[nom],
            Name = nom,
            NormalizedName = nom.ToUpperInvariant(),
            ConcurrencyStamp = IdentifiantsRoles[nom].ToString(),
        }));
    }
}
