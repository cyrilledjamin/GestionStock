using GestionStock.Referentiel.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Referentiel.Infrastructure;

/// <summary>Persistance du module Referentiel, dans le schéma PostgreSQL <c>referentiel</c>.</summary>
internal sealed class ReferentielDbContext(DbContextOptions<ReferentielDbContext> options) : DbContext(options)
{
    public const string Schema = "referentiel";

    /// <summary>Site unique de la V1 (boutique) et son emplacement de vente, créés par la migration.</summary>
    public static readonly Guid IdSiteBoutique = new("0193a5c0-0000-7000-8000-000000000101");
    public static readonly Guid IdEmplacementVente = new("0193a5c0-0000-7000-8000-000000000102");

    /// <summary>Séquence des codes internes générés (INT000001…).</summary>
    public const string SequenceCodeInterne = "code_interne";

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<Conditionnement> Conditionnements => Set<Conditionnement>();

    public DbSet<CodeArticle> Codes => Set<CodeArticle>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Emplacement> Emplacements => Set<Emplacement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(SequenceCodeInterne);

        modelBuilder.Entity<Article>(article =>
        {
            article.ToTable("article");
            article.HasKey(a => a.Id);
            article.Property(a => a.Code).HasMaxLength(Article.LongueurMaxCode);
            article.HasIndex(a => a.Code).IsUnique();
            article.Property(a => a.Libelle).HasMaxLength(Article.LongueurMaxLibelle);
            article.Property(a => a.LibelleTraduit).HasMaxLength(Article.LongueurMaxLibelle);
            article.Property(a => a.UniteStock).HasMaxLength(Article.LongueurMaxUnite);
            article.ToTable(t => t.HasCheckConstraint("ck_article_precision_quantite", "precision_quantite BETWEEN 0 AND 3"));
        });

        modelBuilder.Entity<Conditionnement>(conditionnement =>
        {
            conditionnement.ToTable("conditionnement");
            conditionnement.HasKey(c => c.Id);
            conditionnement.Property(c => c.Id).ValueGeneratedNever();
            conditionnement.Property(c => c.Libelle).HasMaxLength(Conditionnement.LongueurMaxLibelle);
            conditionnement.Property(c => c.QuantiteUniteStock).HasPrecision(18, 4);
            conditionnement.Ignore(c => c.EstUnite);
            conditionnement.HasOne<Article>().WithMany().HasForeignKey(c => c.ArticleId).OnDelete(DeleteBehavior.Restrict);
            conditionnement.HasIndex(c => c.ArticleId);
            conditionnement.ToTable(t => t.HasCheckConstraint("ck_conditionnement_quantite_unite_stock", "quantite_unite_stock > 0"));
        });

        modelBuilder.Entity<CodeArticle>(code =>
        {
            code.ToTable("code_article");
            code.HasKey(c => c.Id);
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.Valeur).HasMaxLength(CodeArticle.LongueurMaxValeur);
            code.HasOne<Conditionnement>().WithMany().HasForeignKey(c => c.ConditionnementId).OnDelete(DeleteBehavior.Restrict);
            code.HasIndex(c => c.ConditionnementId);

            // Un code actif désigne un seul conditionnement (RG26, 4.6) ; les codes désactivés restent pour l'historique.
            code.HasIndex(c => c.Valeur).IsUnique().HasFilter("actif");
        });

        modelBuilder.Entity<Site>(site =>
        {
            site.ToTable("site");
            site.HasKey(s => s.Id);
            site.Property(s => s.Code).HasMaxLength(20);
            site.HasIndex(s => s.Code).IsUnique();
            site.Property(s => s.Nom).HasMaxLength(100);
            site.HasMany(s => s.Emplacements).WithOne().HasForeignKey(e => e.SiteId);
            site.HasData(new Site
            {
                Id = IdSiteBoutique,
                Code = "BOUTIQUE",
                Nom = "Boutique",
                Type = TypeSite.Magasin,
                GestionReserveRayon = false,
            });
        });

        modelBuilder.Entity<Emplacement>(emplacement =>
        {
            emplacement.ToTable("emplacement");
            emplacement.HasKey(e => e.Id);
            emplacement.Property(e => e.Code).HasMaxLength(20);
            emplacement.HasIndex(e => new { e.SiteId, e.Code }).IsUnique();
            emplacement.HasData(new Emplacement
            {
                Id = IdEmplacementVente,
                SiteId = IdSiteBoutique,
                Code = "VENTE",
                Type = TypeEmplacement.Vente,
                MonoArticle = false,
                Actif = true,
            });
        });
    }
}
