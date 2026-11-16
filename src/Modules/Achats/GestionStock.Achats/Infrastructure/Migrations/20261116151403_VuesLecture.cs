using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Achats.Infrastructure.Migrations
{
    /// <summary>
    /// Vues de lecture du module, contrat de lecture du module Rapports (section 7 : CQRS côté lecture). Toute
    /// évolution des tables doit garder ces vues compatibles.
    /// </summary>
    public partial class VuesLecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW achats.v_receptions AS
                SELECT r.id AS reception_id, r.numero, r.fournisseur_id, f.nom AS fournisseur, r.numero_bon_livraison, r.statut,
                       r.date_validation_utc,
                       COALESCE((SELECT sum(l.quantite * l.cout_unitaire) FROM achats.ligne_reception l WHERE l.reception_id = r.id), 0) AS valeur_marchandise,
                       COALESCE((SELECT sum(fr.montant) FROM achats.frais_approche fr WHERE fr.reception_id = r.id), 0) AS total_frais,
                       COALESCE((SELECT sum(p.montant) FROM achats.paiement_fournisseur p WHERE p.reception_id = r.id), 0) AS montant_paye
                FROM achats.reception r
                JOIN achats.fournisseur f ON f.id = r.fournisseur_id;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW achats.v_frais AS
                SELECT fr.id AS frais_id, fr.reception_id, r.numero, fr.type, fr.libelle, fr.montant, fr.cle, fr.apres_validation,
                       fr.saisi_par, fr.saisi_le_utc,
                       COALESCE((SELECT sum(a.montant_non_incorpore) FROM achats.allocation_frais a WHERE a.frais_approche_id = fr.id), 0) AS montant_non_incorpore
                FROM achats.frais_approche fr
                JOIN achats.reception r ON r.id = fr.reception_id;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW achats.v_paiements_fournisseurs AS
                SELECT p.id AS paiement_id, p.reception_id, r.numero, p.fournisseur_id, f.nom AS fournisseur, p.date_utc, p.montant,
                       p.mode, p.reference, p.paiement_annule_id, p.motif, p.saisi_par
                FROM achats.paiement_fournisseur p
                JOIN achats.reception r ON r.id = p.reception_id
                JOIN achats.fournisseur f ON f.id = p.fournisseur_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS achats.v_paiements_fournisseurs;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS achats.v_frais;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS achats.v_receptions;");
        }
    }
}
