using GestionStock.Caisse.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Caisse.Infrastructure;

/// <summary>Dernier numéro de ticket attribué sur un poste (RG34 : numérotation continue par poste).</summary>
internal sealed class CompteurCaisse
{
    public string NumeroCaisse { get; set; } = "";

    public long DernierNumero { get; set; }
}

/// <summary>Persistance du module Caisse, dans le schéma PostgreSQL <c>caisse</c>.</summary>
internal sealed class CaisseDbContext(DbContextOptions<CaisseDbContext> options) : DbContext(options)
{
    public const string Schema = "caisse";

    public DbSet<SessionCaisse> Sessions => Set<SessionCaisse>();

    public DbSet<TicketCaisse> Tickets => Set<TicketCaisse>();

    public DbSet<LigneTicket> Lignes => Set<LigneTicket>();

    public DbSet<RemboursementCaisse> Remboursements => Set<RemboursementCaisse>();

    public DbSet<CompteurCaisse> Compteurs => Set<CompteurCaisse>();

    public DbSet<AnomalieCaisse> Anomalies => Set<AnomalieCaisse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SessionCaisse>(session =>
        {
            session.ToTable("session_caisse");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.NumeroCaisse).HasMaxLength(30);
            session.Property(s => s.Caissier).HasMaxLength(256);
            session.Property(s => s.ValideePar).HasMaxLength(256);
            session.Property(s => s.Statut).HasConversion<string>().HasMaxLength(20);
            session.Property(s => s.FondCaisse).HasPrecision(19, 4);
            session.Property(s => s.EspecesTheoriques).HasPrecision(19, 4);
            session.Property(s => s.EspecesComptees).HasPrecision(19, 4);
            session.Property(s => s.Ecart).HasPrecision(19, 4);

            // Une seule session ouverte par poste, et par caissier.
            session.HasIndex(s => s.NumeroCaisse).IsUnique().HasFilter("statut = 'Ouverte'").HasDatabaseName("ix_session_caisse_poste_ouvert");
            session.HasIndex(s => s.Caissier).IsUnique().HasFilter("statut = 'Ouverte'").HasDatabaseName("ix_session_caisse_caissier_ouvert");
            session.HasIndex(s => s.OuvertureUtc);
        });

        modelBuilder.Entity<TicketCaisse>(ticket =>
        {
            ticket.ToTable("ticket_caisse");
            ticket.HasKey(t => t.Id);
            ticket.Property(t => t.Id).ValueGeneratedNever();
            ticket.HasIndex(t => t.IdIdempotence).IsUnique();
            ticket.Property(t => t.NumeroCaisse).HasMaxLength(30);
            ticket.Property(t => t.NumeroComplet).HasMaxLength(40);
            ticket.HasIndex(t => new { t.NumeroCaisse, t.Numero }).IsUnique();
            ticket.HasIndex(t => t.NumeroComplet);
            // Rapports et exports bornés par date (ventes, caisse, accueil) : sans cet index, tout l'historique est lu.
            ticket.HasIndex(t => t.DateUtc);
            ticket.Property(t => t.Caissier).HasMaxLength(256);
            ticket.Property(t => t.RemiseValideePar).HasMaxLength(256);
            ticket.Property(t => t.CreditValidePar).HasMaxLength(256);
            ticket.Property(t => t.TotalTtc).HasPrecision(19, 4);
            ticket.Property(t => t.TotalHt).HasPrecision(19, 4);
            ticket.Property(t => t.TotalTva).HasPrecision(19, 4);
            ticket.Property(t => t.TauxTva).HasPrecision(5, 2);
            ticket.Property(t => t.Rendu).HasPrecision(19, 4);
            ticket.Ignore(t => t.MontantCredit);
            ticket.Ignore(t => t.EspecesNettes);
            ticket.HasOne<SessionCaisse>().WithMany().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Restrict);
            ticket.HasOne<TicketCaisse>().WithMany().HasForeignKey(t => t.TicketOrigineId).OnDelete(DeleteBehavior.Restrict);
            ticket.HasMany(t => t.Lignes).WithOne().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Restrict);
            ticket.HasMany(t => t.Paiements).WithOne().HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.Restrict);
            ticket.Navigation(t => t.Lignes).AutoInclude();
            ticket.Navigation(t => t.Paiements).AutoInclude();
        });

        modelBuilder.Entity<LigneTicket>(ligne =>
        {
            ligne.ToTable("ligne_ticket");
            ligne.HasKey(l => l.Id);
            ligne.Property(l => l.Id).ValueGeneratedNever();
            ligne.Property(l => l.Libelle).HasMaxLength(200);
            ligne.Property(l => l.LibelleConditionnement).HasMaxLength(50);
            ligne.Property(l => l.CodeScanne).HasMaxLength(50);
            ligne.Property(l => l.Quantite).HasPrecision(18, 4);
            ligne.Property(l => l.QuantiteUniteStock).HasPrecision(18, 4);
            ligne.Property(l => l.PrixUnitaire).HasPrecision(19, 4);
            ligne.Property(l => l.Remise).HasPrecision(19, 4);
            ligne.Property(l => l.TauxTva).HasPrecision(5, 2);
            ligne.Property(l => l.MontantTtc).HasPrecision(19, 4);
            ligne.Ignore(l => l.QuantiteStock);
            ligne.HasIndex(l => l.LigneOrigineId);
        });

        modelBuilder.Entity<Paiement>(paiement =>
        {
            paiement.ToTable("paiement");
            paiement.HasKey(p => p.Id);
            paiement.Property(p => p.Id).ValueGeneratedNever();
            paiement.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20);
            paiement.Property(p => p.Montant).HasPrecision(19, 4);
            paiement.Property(p => p.ReferenceTransaction).HasMaxLength(100);
        });

        modelBuilder.Entity<RemboursementCaisse>(remboursement =>
        {
            remboursement.ToTable("remboursement_credit");
            remboursement.HasKey(r => r.Id);
            remboursement.Property(r => r.Id).ValueGeneratedNever();
            remboursement.HasIndex(r => r.IdIdempotence).IsUnique();
            remboursement.HasIndex(r => r.DateUtc);
            remboursement.Property(r => r.Mode).HasConversion<string>().HasMaxLength(20);
            remboursement.Property(r => r.Montant).HasPrecision(19, 4);
            remboursement.Property(r => r.ReferenceTransaction).HasMaxLength(100);
            remboursement.Property(r => r.Caissier).HasMaxLength(256);
            remboursement.HasOne<SessionCaisse>().WithMany().HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AnomalieCaisse>(anomalie =>
        {
            anomalie.ToTable("anomalie_caisse");
            anomalie.HasKey(a => a.Id);
            anomalie.Property(a => a.Id).ValueGeneratedNever();
            anomalie.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
            anomalie.Property(a => a.NumeroCaisse).HasMaxLength(30);
            anomalie.Property(a => a.Caissier).HasMaxLength(256);
            anomalie.Property(a => a.NumeroDocument).HasMaxLength(50);
            anomalie.Property(a => a.Detail).HasMaxLength(AnomalieCaisse.LongueurMaxDetail);
            anomalie.Property(a => a.TraiteePar).HasMaxLength(256);
            anomalie.Property(a => a.Commentaire).HasMaxLength(AnomalieCaisse.LongueurMaxCommentaire);
            anomalie.HasOne<SessionCaisse>().WithMany().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Restrict);
            anomalie.HasIndex(a => a.Traitee);
            anomalie.HasIndex(a => a.DocumentId);
        });

        modelBuilder.Entity<CompteurCaisse>(compteur =>
        {
            compteur.ToTable("compteur_caisse");
            compteur.HasKey(c => c.NumeroCaisse);
            compteur.Property(c => c.NumeroCaisse).HasMaxLength(30);
        });
    }
}
