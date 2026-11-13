using GestionStock.Stock.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Stock.Infrastructure;

/// <summary>Persistance du module Stock, dans le schéma PostgreSQL <c>stock</c>.</summary>
internal sealed class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public const string Schema = "stock";

    public DbSet<MouvementStock> Mouvements => Set<MouvementStock>();

    public DbSet<StockSite> StocksSites => Set<StockSite>();

    public DbSet<StockEmplacement> StocksEmplacements => Set<StockEmplacement>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(GardeJournalImmuable.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockDbContext).Assembly);
    }
}
