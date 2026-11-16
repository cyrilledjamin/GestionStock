using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Referentiel.Infrastructure.Migrations
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
                CREATE VIEW referentiel.v_articles AS
                SELECT a.id AS article_id, a.code, a.libelle, a.unite_stock, a.precision_quantite, a.actif
                FROM referentiel.article a;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS referentiel.v_articles;");
        }
    }
}
