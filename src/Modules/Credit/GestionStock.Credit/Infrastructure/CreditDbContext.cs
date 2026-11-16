using GestionStock.Credit.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Credit.Infrastructure;

/// <summary>Persistance du module Credit, dans le schéma PostgreSQL <c>credit</c>.</summary>
internal sealed class CreditDbContext(DbContextOptions<CreditDbContext> options) : DbContext(options)
{
    public const string Schema = "credit";

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<OperationCredit> Operations => Set<OperationCredit>();

    public DbSet<Imputation> Imputations => Set<Imputation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Client>(client =>
        {
            client.ToTable("client");
            client.HasKey(c => c.Id);
            client.Property(c => c.Id).ValueGeneratedNever();
            client.Property(c => c.Nom).HasMaxLength(Client.LongueurMaxNom);
            client.Property(c => c.Telephone).HasMaxLength(Client.LongueurMaxTelephone);
            client.Property(c => c.PlafondCredit).HasPrecision(19, 4);
            client.Property(c => c.SoldeDu).HasPrecision(19, 4);
            client.ToTable(t => t.HasCheckConstraint("ck_client_solde_du", "solde_du >= 0"));
        });

        modelBuilder.Entity<OperationCredit>(operation =>
        {
            operation.ToTable("operation_credit");
            operation.HasKey(o => o.Id);
            operation.Property(o => o.Id).ValueGeneratedNever();
            operation.Property(o => o.Type).HasConversion<string>().HasMaxLength(20);
            operation.Property(o => o.Montant).HasPrecision(19, 4);
            operation.Property(o => o.NumeroTicket).HasMaxLength(30);
            operation.Property(o => o.Utilisateur).HasMaxLength(256);
            operation.Property(o => o.ValideePar).HasMaxLength(256);
            operation.HasOne<Client>().WithMany().HasForeignKey(o => o.ClientId).OnDelete(DeleteBehavior.Restrict);
            operation.HasIndex(o => new { o.ClientId, o.DateUtc });
            operation.HasIndex(o => o.DocumentId);
        });

        modelBuilder.Entity<Imputation>(imputation =>
        {
            imputation.ToTable("imputation");
            imputation.HasKey(i => i.Id);
            imputation.Property(i => i.Id).ValueGeneratedNever();
            imputation.Property(i => i.Montant).HasPrecision(19, 4);
            imputation.HasOne<OperationCredit>().WithMany().HasForeignKey(i => i.OperationId).OnDelete(DeleteBehavior.Restrict);
            imputation.HasOne<OperationCredit>().WithMany().HasForeignKey(i => i.VenteId).OnDelete(DeleteBehavior.Restrict);
            imputation.HasIndex(i => i.VenteId);
        });
    }
}
