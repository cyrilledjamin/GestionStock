using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Inventaire.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventaire");

            migrationBuilder.CreateSequence(
                name: "numero_demarque",
                schema: "inventaire");

            migrationBuilder.CreateSequence(
                name: "numero_inventaire",
                schema: "inventaire");

            migrationBuilder.CreateTable(
                name: "declaration_demarque",
                schema: "inventaire",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_article = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unite_stock = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantite = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    motif = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    commentaire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declaree_le_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    declaree_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    traitee_le_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    traitee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    motif_refus = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_demarque", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventaire",
                schema: "inventaire",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cree_le_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cree_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    termine_le_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    termine_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventaire", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ligne_inventaire",
                schema: "inventaire",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventaire_id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_article = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unite_stock = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    precision_quantite = table.Column<int>(type: "integer", nullable: false),
                    cout_ouverture = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    quantite_comptee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    quantite_theorique = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    comptage_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    compte_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    motif_ecart = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_inventaire", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_inventaire_inventaire_inventaire_id",
                        column: x => x.inventaire_id,
                        principalSchema: "inventaire",
                        principalTable: "inventaire",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_declaration_demarque_numero",
                schema: "inventaire",
                table: "declaration_demarque",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_declaration_demarque_statut",
                schema: "inventaire",
                table: "declaration_demarque",
                column: "statut");

            migrationBuilder.CreateIndex(
                name: "ix_inventaire_numero",
                schema: "inventaire",
                table: "inventaire",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ligne_inventaire_inventaire_id_article_id",
                schema: "inventaire",
                table: "ligne_inventaire",
                columns: new[] { "inventaire_id", "article_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "declaration_demarque",
                schema: "inventaire");

            migrationBuilder.DropTable(
                name: "ligne_inventaire",
                schema: "inventaire");

            migrationBuilder.DropTable(
                name: "inventaire",
                schema: "inventaire");

            migrationBuilder.DropSequence(
                name: "numero_demarque",
                schema: "inventaire");

            migrationBuilder.DropSequence(
                name: "numero_inventaire",
                schema: "inventaire");
        }
    }
}
