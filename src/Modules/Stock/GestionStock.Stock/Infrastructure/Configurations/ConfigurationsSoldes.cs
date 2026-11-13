using GestionStock.Stock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestionStock.Stock.Infrastructure.Configurations;

internal sealed class ConfigurationStockSite : IEntityTypeConfiguration<StockSite>
{
    /// <summary>Nom de la propriété fantôme de version, adossée à la colonne système <c>xmin</c>.</summary>
    public const string Version = "Version";

    public void Configure(EntityTypeBuilder<StockSite> builder)
    {
        builder.ToTable("stock_site");
        builder.HasKey(s => new { s.ArticleId, s.SiteId });

        builder.Property(s => s.QuantitePhysique).HasPrecision(18, 4);
        builder.Property(s => s.QuantiteReservee).HasPrecision(18, 4);
        builder.Property(s => s.QuantiteBloquee).HasPrecision(18, 4);
        builder.Property(s => s.Cump).HasPrecision(19, 4);

        // Jeton de concurrence optimiste (rowversion du modèle) : xmin de PostgreSQL.
        builder.Property<uint>(Version).IsRowVersion();

        builder.Ignore(s => s.QuantiteDisponible);
        builder.Ignore(s => s.Valeur);
    }
}

internal sealed class ConfigurationStockEmplacement : IEntityTypeConfiguration<StockEmplacement>
{
    public void Configure(EntityTypeBuilder<StockEmplacement> builder)
    {
        builder.ToTable("stock_emplacement");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.QuantitePhysique).HasPrecision(18, 4);

        // Une seule ligne par article × emplacement × lot, lot absent compris.
        builder.HasIndex(s => new { s.ArticleId, s.EmplacementId, s.LotId })
            .IsUnique()
            .AreNullsDistinct(false);
    }
}

/// <summary>Longueurs maximales des colonnes texte du module.</summary>
internal static class Longueurs
{
    public const int Enumeration = 32;
    public const int TypeDocument = 64;
    public const int Utilisateur = 128;
}
