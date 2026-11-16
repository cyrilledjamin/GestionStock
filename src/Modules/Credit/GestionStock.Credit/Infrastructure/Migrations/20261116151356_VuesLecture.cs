using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Credit.Infrastructure.Migrations
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
                CREATE VIEW credit.v_clients AS
                SELECT c.id AS client_id, c.nom, c.telephone, c.plafond_credit, c.delai_paiement_jours, c.solde_du, c.actif
                FROM credit.client c;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW credit.v_operations AS
                SELECT o.id AS operation_id, o.client_id, o.date_utc, o.type, o.montant, o.document_id, o.numero_ticket,
                       o.echeance_utc, o.utilisateur
                FROM credit.operation_credit o;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW credit.v_ventes_credit AS
                SELECT o.id AS operation_id, o.client_id, o.date_utc, o.numero_ticket, o.document_id AS ticket_id, o.echeance_utc,
                       o.montant, o.montant - COALESCE((SELECT sum(i.montant) FROM credit.imputation i WHERE i.vente_id = o.id), 0) AS reste_du
                FROM credit.operation_credit o
                WHERE o.type = 'VenteCredit';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS credit.v_ventes_credit;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS credit.v_operations;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS credit.v_clients;");
        }
    }
}
