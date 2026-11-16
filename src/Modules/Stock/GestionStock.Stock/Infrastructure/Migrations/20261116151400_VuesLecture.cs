using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Stock.Infrastructure.Migrations
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
                CREATE VIEW stock.v_mouvements AS
                SELECT m.id AS mouvement_id, m.date_utc, m.type, m.article_id, m.site_id, m.quantite, m.cout_unitaire,
                       CASE WHEN m.emplacement_source_id IS NULL AND m.emplacement_destination_id IS NOT NULL THEN m.quantite
                            WHEN m.emplacement_source_id IS NOT NULL AND m.emplacement_destination_id IS NULL THEN -m.quantite
                            ELSE 0 END AS variation,
                       m.type_document, m.document_id, m.utilisateur
                FROM stock.mouvement_stock m;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW stock.v_soldes AS
                SELECT s.article_id, s.site_id, s.quantite_physique, s.cump, round(s.quantite_physique * s.cump, 4) AS valeur
                FROM stock.stock_site s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS stock.v_soldes;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS stock.v_mouvements;");
        }
    }
}
