using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Achats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DettesFournisseurs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paiement_fournisseur",
                schema: "achats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fournisseur_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    paiement_annule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motif = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    saisi_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paiement_fournisseur", x => x.id);
                    table.ForeignKey(
                        name: "fk_paiement_fournisseur_fournisseur_fournisseur_id",
                        column: x => x.fournisseur_id,
                        principalSchema: "achats",
                        principalTable: "fournisseur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paiement_fournisseur_paiement_fournisseur_paiement_annule_id",
                        column: x => x.paiement_annule_id,
                        principalSchema: "achats",
                        principalTable: "paiement_fournisseur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paiement_fournisseur_reception_reception_id",
                        column: x => x.reception_id,
                        principalSchema: "achats",
                        principalTable: "reception",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paiement_fournisseur_fournisseur_id",
                schema: "achats",
                table: "paiement_fournisseur",
                column: "fournisseur_id");

            migrationBuilder.CreateIndex(
                name: "ix_paiement_fournisseur_paiement_annule_id",
                schema: "achats",
                table: "paiement_fournisseur",
                column: "paiement_annule_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paiement_fournisseur_reception_id",
                schema: "achats",
                table: "paiement_fournisseur",
                column: "reception_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paiement_fournisseur",
                schema: "achats");
        }
    }
}
