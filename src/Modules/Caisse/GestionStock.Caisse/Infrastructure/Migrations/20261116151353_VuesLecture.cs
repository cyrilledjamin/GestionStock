using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Caisse.Infrastructure.Migrations
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
                CREATE VIEW caisse.v_tickets AS
                SELECT t.id AS ticket_id, t.numero_complet, t.date_utc, t.numero_caisse, t.session_id, t.caissier, t.est_avoir,
                       t.ticket_origine_id, t.client_id, CASE WHEN t.est_avoir THEN -1 ELSE 1 END AS signe,
                       t.total_ttc, t.total_ht, t.total_tva, t.taux_tva, t.rendu, t.remise_validee_par, t.credit_valide_par,
                       COALESCE((SELECT sum(l.remise) FROM caisse.ligne_ticket l WHERE l.ticket_id = t.id), 0) AS remise
                FROM caisse.ticket_caisse t;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW caisse.v_lignes_vendues AS
                SELECT l.id AS ligne_id, l.ticket_id, t.numero_complet, t.date_utc, t.caissier, t.est_avoir,
                       CASE WHEN t.est_avoir THEN -1 ELSE 1 END AS signe, l.article_id, l.libelle, l.conditionnement_id,
                       l.quantite, l.quantite * l.quantite_unite_stock AS quantite_stock,
                       l.montant_ttc, round(l.montant_ttc * 100 / (100 + l.taux_tva), 4) AS montant_ht, l.remise, l.taux_tva
                FROM caisse.ligne_ticket l
                JOIN caisse.ticket_caisse t ON t.id = l.ticket_id;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW caisse.v_paiements AS
                SELECT p.id AS paiement_id, p.ticket_id, t.numero_complet, t.date_utc, t.caissier, t.client_id, t.est_avoir, p.mode,
                       CASE WHEN t.est_avoir THEN -p.montant ELSE p.montant END AS montant,
                       CASE WHEN t.est_avoir THEN -p.montant
                            WHEN p.mode = 'Especes' THEN p.montant - t.rendu
                            ELSE p.montant END AS montant_encaisse,
                       p.reference_transaction AS reference
                FROM caisse.paiement p
                JOIN caisse.ticket_caisse t ON t.id = p.ticket_id;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW caisse.v_remboursements_credit AS
                SELECT r.id AS remboursement_id, r.session_id, r.client_id, r.mode, r.montant, r.reference_transaction AS reference,
                       r.date_utc, r.caissier
                FROM caisse.remboursement_credit r;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW caisse.v_sessions AS
                SELECT s.id AS session_id, s.numero_caisse, s.caissier, s.ouverture_utc, s.cloture_utc, s.statut, s.fond_caisse,
                       s.especes_theoriques, s.especes_comptees, s.ecart, s.validee_par,
                       (SELECT count(*) FROM caisse.ticket_caisse t WHERE t.session_id = s.id AND NOT t.est_avoir) AS nombre_ventes,
                       (SELECT count(*) FROM caisse.ticket_caisse t WHERE t.session_id = s.id AND t.est_avoir) AS nombre_avoirs,
                       COALESCE((SELECT sum(CASE WHEN t.est_avoir THEN -t.total_ttc ELSE t.total_ttc END)
                                 FROM caisse.ticket_caisse t WHERE t.session_id = s.id), 0) AS ventes_nettes
                FROM caisse.session_caisse s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_sessions;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_remboursements_credit;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_paiements;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_lignes_vendues;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_tickets;");
        }
    }
}
