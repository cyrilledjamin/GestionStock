using GestionStock.Stock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestionStock.Stock.Infrastructure.Configurations;

internal sealed class ConfigurationMouvementStock : IEntityTypeConfiguration<MouvementStock>
{
    public void Configure(EntityTypeBuilder<MouvementStock> builder)
    {
        builder.ToTable("mouvement_stock");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityAlwaysColumn();

        builder.Property(m => m.DateUtc).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(Longueurs.Enumeration).IsRequired();
        builder.Property(m => m.Quantite).HasPrecision(18, 4);
        builder.Property(m => m.CoutUnitaire).HasPrecision(19, 4);
        builder.Property(m => m.TypeDocument).HasMaxLength(Longueurs.TypeDocument).IsRequired();
        builder.Property(m => m.Utilisateur).HasMaxLength(Longueurs.Utilisateur).IsRequired();

        builder.Ignore(m => m.Sens);
        builder.Ignore(m => m.VariationSite);

        // Un renvoi après coupure n'est intégré qu'une fois.
        builder.HasIndex(m => m.IdIdempotence).IsUnique();

        // Recalcul des soldes et historique d'un article (ENF04, UC14).
        builder.HasIndex(m => new { m.ArticleId, m.SiteId, m.Id });

        // Retrouver les mouvements d'un document d'origine (lien générique, sans clé étrangère).
        builder.HasIndex(m => new { m.TypeDocument, m.DocumentId });
    }
}
