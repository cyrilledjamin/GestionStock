using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Achats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FraisApproche : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "frais_approche_alloues",
                schema: "achats",
                table: "ligne_reception",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "frais_approche",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    libelle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    cle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    apres_validation = table.Column<bool>(type: "boolean", nullable: false),
                    saisi_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    saisi_le_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_frais_approche", x => x.id);
                    table.ForeignKey(
                        name: "fk_frais_approche_receptions_reception_id",
                        column: x => x.reception_id,
                        principalSchema: "achats",
                        principalTable: "reception",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allocation_frais",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    frais_approche_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ligne_reception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quantite_revalorisee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    supplement_unitaire = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    montant_non_incorpore = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_frais", x => x.id);
                    table.ForeignKey(
                        name: "fk_allocation_frais_frais_approche_frais_approche_id",
                        column: x => x.frais_approche_id,
                        principalSchema: "achats",
                        principalTable: "frais_approche",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_allocation_frais_ligne_reception_ligne_reception_id",
                        column: x => x.ligne_reception_id,
                        principalSchema: "achats",
                        principalTable: "ligne_reception",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_frais_frais_approche_id",
                schema: "achats",
                table: "allocation_frais",
                column: "frais_approche_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_frais_ligne_reception_id",
                schema: "achats",
                table: "allocation_frais",
                column: "ligne_reception_id");

            migrationBuilder.CreateIndex(
                name: "ix_frais_approche_reception_id",
                schema: "achats",
                table: "frais_approche",
                column: "reception_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_frais",
                schema: "achats");

            migrationBuilder.DropTable(
                name: "frais_approche",
                schema: "achats");

            migrationBuilder.DropColumn(
                name: "frais_approche_alloues",
                schema: "achats",
                table: "ligne_reception");
        }
    }
}
