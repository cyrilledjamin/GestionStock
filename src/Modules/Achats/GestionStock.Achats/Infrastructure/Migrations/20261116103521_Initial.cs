using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Achats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "achats");

            migrationBuilder.CreateSequence(
                name: "numero_reception",
                schema: "achats");

            migrationBuilder.CreateTable(
                name: "fournisseur",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tolerance_surlivraison_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fournisseur", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reception",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fournisseur_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_bon_livraison = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date_creation_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cree_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    date_validation_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    validee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reception", x => x.id);
                    table.ForeignKey(
                        name: "fk_reception_fournisseur_fournisseur_id",
                        column: x => x.fournisseur_id,
                        principalSchema: "achats",
                        principalTable: "fournisseur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ligne_reception",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordre = table.Column<int>(type: "integer", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantite = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cout_unitaire = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_reception", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_reception_receptions_reception_id",
                        column: x => x.reception_id,
                        principalSchema: "achats",
                        principalTable: "reception",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ligne_reception_reception_id_ordre",
                schema: "achats",
                table: "ligne_reception",
                columns: new[] { "reception_id", "ordre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reception_fournisseur_id",
                schema: "achats",
                table: "reception",
                column: "fournisseur_id");

            migrationBuilder.CreateIndex(
                name: "ix_reception_numero",
                schema: "achats",
                table: "reception",
                column: "numero",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ligne_reception",
                schema: "achats");

            migrationBuilder.DropTable(
                name: "reception",
                schema: "achats");

            migrationBuilder.DropTable(
                name: "fournisseur",
                schema: "achats");

            migrationBuilder.DropSequence(
                name: "numero_reception",
                schema: "achats");
        }
    }
}
