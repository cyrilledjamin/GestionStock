using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Referentiel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VenteRapide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "vente_rapide",
                schema: "referentiel",
                table: "conditionnement",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vente_rapide",
                schema: "referentiel",
                table: "conditionnement");
        }
    }
}
