using GestionStock.Achats.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Achats.Infrastructure;

/// <summary>Persistance du module Achats, dans le schéma PostgreSQL <c>achats</c>.</summary>
internal sealed class AchatsDbContext(DbContextOptions<AchatsDbContext> options) : DbContext(options)
{
    public const string Schema = "achats";

    /// <summary>Séquence des numéros de réception (R-000001, R-000002…).</summary>
    public const string SequenceNumeroReception = "numero_reception";

    public DbSet<Fournisseur> Fournisseurs => Set<Fournisseur>();

    public DbSet<Reception> Receptions => Set<Reception>();

    public DbSet<PaiementFournisseur> PaiementsFournisseurs => Set<PaiementFournisseur>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(SequenceNumeroReception);

        modelBuilder.Entity<Fournisseur>(fournisseur =>
        {
            fournisseur.ToTable("fournisseur");
            fournisseur.HasKey(f => f.Id);
            fournisseur.Property(f => f.Nom).HasMaxLength(Fournisseur.LongueurMaxNom);
            fournisseur.Property(f => f.Telephone).HasMaxLength(Fournisseur.LongueurMaxTelephone);
            fournisseur.Property(f => f.ToleranceSurlivraisonPct).HasPrecision(5, 2);
        });

        modelBuilder.Entity<Reception>(reception =>
        {
            reception.ToTable("reception");
            reception.HasKey(r => r.Id);
            reception.Property(r => r.Numero).HasMaxLength(20);
            reception.HasIndex(r => r.Numero).IsUnique();
            reception.Property(r => r.NumeroBonLivraison).HasMaxLength(Reception.LongueurMaxBonLivraison);
            reception.Property(r => r.Statut).HasConversion<string>().HasMaxLength(20);
            reception.Property(r => r.CreePar).HasMaxLength(256);
            reception.Property(r => r.ValideePar).HasMaxLength(256);
            reception.HasOne<Fournisseur>().WithMany().HasForeignKey(r => r.FournisseurId).OnDelete(DeleteBehavior.Restrict);
            reception.HasIndex(r => r.FournisseurId);
            reception.HasMany(r => r.Lignes).WithOne().HasForeignKey(l => l.ReceptionId).OnDelete(DeleteBehavior.Cascade);
            reception.Navigation(r => r.Lignes).AutoInclude();
            reception.HasMany(r => r.Frais).WithOne().HasForeignKey(f => f.ReceptionId).OnDelete(DeleteBehavior.Cascade);
            reception.Navigation(r => r.Frais).AutoInclude();
            reception.Ignore(r => r.ValeurMarchandise);
            reception.Ignore(r => r.TotalFrais);
        });

        modelBuilder.Entity<FraisApproche>(frais =>
        {
            frais.ToTable("frais_approche");
            frais.HasKey(f => f.Id);
            frais.Property(f => f.Id).ValueGeneratedNever();
            frais.Property(f => f.Type).HasConversion<string>().HasMaxLength(20);
            frais.Property(f => f.Cle).HasConversion<string>().HasMaxLength(20);
            frais.Property(f => f.Libelle).HasMaxLength(FraisApproche.LongueurMaxLibelle);
            frais.Property(f => f.Montant).HasPrecision(19, 4);
            frais.Property(f => f.SaisiPar).HasMaxLength(256);
            frais.HasMany(f => f.Allocations).WithOne().HasForeignKey(a => a.FraisApprocheId).OnDelete(DeleteBehavior.Cascade);
            frais.Navigation(f => f.Allocations).AutoInclude();
            frais.Ignore(f => f.MontantNonIncorpore);
        });

        modelBuilder.Entity<PaiementFournisseur>(paiement =>
        {
            paiement.ToTable("paiement_fournisseur");
            paiement.HasKey(p => p.Id);
            paiement.Property(p => p.Id).ValueGeneratedNever();
            paiement.Property(p => p.Montant).HasPrecision(19, 4);
            paiement.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20);
            paiement.Property(p => p.Reference).HasMaxLength(PaiementFournisseur.LongueurMaxReference);
            paiement.Property(p => p.Motif).HasMaxLength(PaiementFournisseur.LongueurMaxMotif);
            paiement.Property(p => p.SaisiPar).HasMaxLength(256);
            paiement.HasOne<Reception>().WithMany().HasForeignKey(p => p.ReceptionId).OnDelete(DeleteBehavior.Restrict);
            paiement.HasOne<Fournisseur>().WithMany().HasForeignKey(p => p.FournisseurId).OnDelete(DeleteBehavior.Restrict);
            paiement.HasOne<PaiementFournisseur>().WithMany().HasForeignKey(p => p.PaiementAnnuleId).OnDelete(DeleteBehavior.Restrict);

            // Un paiement ne s'annule qu'une fois, même en cas de demandes simultanées.
            paiement.HasIndex(p => p.PaiementAnnuleId).IsUnique();
            paiement.HasIndex(p => p.ReceptionId);
        });

        modelBuilder.Entity<AllocationFrais>(allocation =>
        {
            allocation.ToTable("allocation_frais");
            allocation.HasKey(a => a.Id);
            allocation.Property(a => a.Id).ValueGeneratedNever();
            allocation.Property(a => a.Montant).HasPrecision(19, 4);
            allocation.Property(a => a.SupplementUnitaire).HasPrecision(19, 4);
            allocation.Property(a => a.MontantNonIncorpore).HasPrecision(19, 4);
            allocation.Property(a => a.QuantiteRevalorisee).HasPrecision(18, 4);
            allocation.HasOne<LigneReception>().WithMany().HasForeignKey(a => a.LigneReceptionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LigneReception>(ligne =>
        {
            ligne.ToTable("ligne_reception");
            ligne.HasKey(l => l.Id);
            ligne.Property(l => l.Id).ValueGeneratedNever();
            ligne.Property(l => l.Quantite).HasPrecision(18, 4);
            ligne.Property(l => l.CoutUnitaire).HasPrecision(19, 4);
            ligne.Property(l => l.FraisApprocheAlloues).HasPrecision(19, 4);
            ligne.Ignore(l => l.CoutEntree);
            ligne.HasIndex(l => new { l.ReceptionId, l.Ordre }).IsUnique();
        });
    }
}
