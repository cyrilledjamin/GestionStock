using GestionStock.Inventaire.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Inventaire.Infrastructure;

/// <summary>Persistance du module Inventaire, dans le schéma PostgreSQL <c>inventaire</c>.</summary>
internal sealed class InventaireDbContext(DbContextOptions<InventaireDbContext> options) : DbContext(options)
{
    public const string Schema = "inventaire";
    public const string SequenceInventaire = "numero_inventaire";
    public const string SequenceDemarque = "numero_demarque";

    public DbSet<Domain.Inventaire> Inventaires => Set<Domain.Inventaire>();

    public DbSet<LigneInventaire> Lignes => Set<LigneInventaire>();

    public DbSet<DeclarationDemarque> Demarques => Set<DeclarationDemarque>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(SequenceInventaire);
        modelBuilder.HasSequence<long>(SequenceDemarque);

        modelBuilder.Entity<Domain.Inventaire>(inventaire =>
        {
            inventaire.ToTable("inventaire");
            inventaire.HasKey(i => i.Id);
            inventaire.Property(i => i.Id).ValueGeneratedNever();
            inventaire.Property(i => i.Numero).HasMaxLength(20);
            inventaire.HasIndex(i => i.Numero).IsUnique();
            inventaire.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
            inventaire.Property(i => i.Statut).HasConversion<string>().HasMaxLength(20);
            inventaire.Property(i => i.CreePar).HasMaxLength(256);
            inventaire.Property(i => i.TerminePar).HasMaxLength(256);
            inventaire.HasMany(i => i.Lignes).WithOne().HasForeignKey(l => l.InventaireId).OnDelete(DeleteBehavior.Restrict);
            inventaire.Navigation(i => i.Lignes).AutoInclude();
        });

        modelBuilder.Entity<LigneInventaire>(ligne =>
        {
            ligne.ToTable("ligne_inventaire");
            ligne.HasKey(l => l.Id);
            ligne.Property(l => l.Id).ValueGeneratedNever();
            ligne.HasIndex(l => new { l.InventaireId, l.ArticleId }).IsUnique();
            ligne.Property(l => l.CodeArticle).HasMaxLength(50);
            ligne.Property(l => l.Libelle).HasMaxLength(200);
            ligne.Property(l => l.UniteStock).HasMaxLength(20);
            ligne.Property(l => l.CoutOuverture).HasPrecision(19, 4);
            ligne.Property(l => l.QuantiteComptee).HasPrecision(18, 4);
            ligne.Property(l => l.QuantiteTheorique).HasPrecision(18, 4);
            ligne.Property(l => l.ComptePar).HasMaxLength(256);
            ligne.Property(l => l.MotifEcart).HasConversion<string>().HasMaxLength(20);
            ligne.Ignore(l => l.Ecart);
        });

        modelBuilder.Entity<DeclarationDemarque>(demarque =>
        {
            demarque.ToTable("declaration_demarque");
            demarque.HasKey(d => d.Id);
            demarque.Property(d => d.Id).ValueGeneratedNever();
            demarque.Property(d => d.Numero).HasMaxLength(20);
            demarque.HasIndex(d => d.Numero).IsUnique();
            demarque.Property(d => d.CodeArticle).HasMaxLength(50);
            demarque.Property(d => d.Libelle).HasMaxLength(200);
            demarque.Property(d => d.UniteStock).HasMaxLength(20);
            demarque.Property(d => d.Quantite).HasPrecision(18, 4);
            demarque.Property(d => d.Motif).HasConversion<string>().HasMaxLength(20);
            demarque.Property(d => d.Statut).HasConversion<string>().HasMaxLength(20);
            demarque.Property(d => d.Commentaire).HasMaxLength(DeclarationDemarque.LongueurMaxCommentaire);
            demarque.Property(d => d.MotifRefus).HasMaxLength(DeclarationDemarque.LongueurMaxCommentaire);
            demarque.Property(d => d.DeclareePar).HasMaxLength(256);
            demarque.Property(d => d.TraiteePar).HasMaxLength(256);
            demarque.HasIndex(d => d.Statut);
        });
    }
}
