using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Caisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuteurCloture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cloturee_par",
                schema: "caisse",
                table: "session_caisse",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cloturee_par",
                schema: "caisse",
                table: "session_caisse");
        }
    }
}
