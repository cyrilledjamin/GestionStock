using GestionStock.Prix.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Prix.Infrastructure;

/// <summary>Persistance du module Prix, dans le schéma PostgreSQL <c>prix</c>.</summary>
internal sealed class PrixDbContext(DbContextOptions<PrixDbContext> options) : DbContext(options)
{
    public const string Schema = "prix";

    public DbSet<PrixVente> Prix => Set<PrixVente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PrixVente>(prix =>
        {
            prix.ToTable("prix_vente");
            prix.HasKey(p => p.Id);
            prix.Property(p => p.Id).ValueGeneratedNever();
            prix.Property(p => p.PrixTtc).HasPrecision(19, 4);
            prix.Property(p => p.SaisiPar).HasMaxLength(256);

            // Recherche du prix applicable : conditionnement, puis date de début décroissante.
            prix.HasIndex(p => new { p.ConditionnementId, p.DebutValidite, p.DateSaisieUtc });
            prix.ToTable(t => t.HasCheckConstraint("ck_prix_vente_prix_ttc", "prix_ttc > 0"));
        });
    }
}
