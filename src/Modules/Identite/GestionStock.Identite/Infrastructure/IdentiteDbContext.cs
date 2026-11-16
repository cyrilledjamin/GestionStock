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

    /// <summary>Séquence des matricules générés (S000001, S000002…).</summary>
    public const string SequenceMatricule = "numero_matricule";

    public DbSet<JournalCompte> JournalComptes => Set<JournalCompte>();

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

        builder.HasSequence<long>(SequenceMatricule);
        builder.Entity<Utilisateur>(utilisateur =>
        {
            utilisateur.ToTable("utilisateur");
            utilisateur.Property(u => u.Prenom).HasMaxLength(Utilisateur.LongueurMaxNom);
            utilisateur.Property(u => u.Nom).HasMaxLength(Utilisateur.LongueurMaxNom);
            utilisateur.Property(u => u.Langue).HasMaxLength(2).HasDefaultValue(LanguesInterface.Francais);
            utilisateur.Property(u => u.Actif).HasDefaultValue(true);
            utilisateur.Ignore(u => u.NomComplet);
        });
        builder.Entity<JournalCompte>(journal =>
        {
            journal.ToTable("journal_compte");
            journal.HasKey(j => j.Id);
            journal.Property(j => j.Id).ValueGeneratedNever();
            journal.Property(j => j.Action).HasConversion<string>().HasMaxLength(30);
            journal.Property(j => j.Auteur).HasMaxLength(256);
            journal.Property(j => j.Detail).HasMaxLength(JournalCompte.LongueurMaxDetail);
            journal.HasOne<Utilisateur>().WithMany().HasForeignKey(j => j.UtilisateurId).OnDelete(DeleteBehavior.Restrict);
            journal.HasIndex(j => new { j.UtilisateurId, j.DateUtc });
        });
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
