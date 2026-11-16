using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Referentiel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConditionnementsCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "code_interne",
                schema: "referentiel");

            migrationBuilder.CreateTable(
                name: "conditionnement",
                schema: "referentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    libelle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niveau = table.Column<int>(type: "integer", nullable: false),
                    quantite_unite_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conditionnement", x => x.id);
                    table.CheckConstraint("ck_conditionnement_quantite_unite_stock", "quantite_unite_stock > 0");
                    table.ForeignKey(
                        name: "fk_conditionnement_article_article_id",
                        column: x => x.article_id,
                        principalSchema: "referentiel",
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "code_article",
                schema: "referentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conditionnement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valeur = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    principal = table.Column<bool>(type: "boolean", nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_code_article", x => x.id);
                    table.ForeignKey(
                        name: "fk_code_article_conditionnement_conditionnement_id",
                        column: x => x.conditionnement_id,
                        principalSchema: "referentiel",
                        principalTable: "conditionnement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_code_article_conditionnement_id",
                schema: "referentiel",
                table: "code_article",
                column: "conditionnement_id");

            migrationBuilder.CreateIndex(
                name: "ix_code_article_valeur",
                schema: "referentiel",
                table: "code_article",
                column: "valeur",
                unique: true,
                filter: "actif");

            migrationBuilder.CreateIndex(
                name: "ix_conditionnement_article_id",
                schema: "referentiel",
                table: "conditionnement",
                column: "article_id");

            // Reprise : chaque article existant reçoit son conditionnement « Unité » de facteur 1 (RG44).
            migrationBuilder.Sql(
                """
                INSERT INTO referentiel.conditionnement (id, article_id, libelle, niveau, quantite_unite_stock, actif)
                SELECT gen_random_uuid(), a.id, a.unite_stock, 1, 1, TRUE
                FROM referentiel.article a
                WHERE NOT EXISTS (SELECT 1 FROM referentiel.conditionnement c WHERE c.article_id = a.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "code_article",
                schema: "referentiel");

            migrationBuilder.DropTable(
                name: "conditionnement",
                schema: "referentiel");

            migrationBuilder.DropSequence(
                name: "code_interne",
                schema: "referentiel");
        }
    }
}
