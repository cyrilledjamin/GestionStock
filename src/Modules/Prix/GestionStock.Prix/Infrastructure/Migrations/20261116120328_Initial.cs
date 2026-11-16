using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Prix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prix");

            migrationBuilder.CreateTable(
                name: "prix_vente",
                schema: "prix",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conditionnement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prix_ttc = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    debut_validite = table.Column<DateOnly>(type: "date", nullable: false),
                    saisi_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    date_saisie_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prix_vente", x => x.id);
                    table.CheckConstraint("ck_prix_vente_prix_ttc", "prix_ttc > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_prix_vente_conditionnement_id_debut_validite_date_saisie_utc",
                schema: "prix",
                table: "prix_vente",
                columns: new[] { "conditionnement_id", "debut_validite", "date_saisie_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prix_vente",
                schema: "prix");
        }
    }
}
