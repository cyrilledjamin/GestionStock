using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Caisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndexDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_date_utc",
                schema: "caisse",
                table: "ticket_caisse",
                column: "date_utc");

            migrationBuilder.CreateIndex(
                name: "ix_remboursement_credit_date_utc",
                schema: "caisse",
                table: "remboursement_credit",
                column: "date_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ticket_caisse_date_utc",
                schema: "caisse",
                table: "ticket_caisse");

            migrationBuilder.DropIndex(
                name: "ix_remboursement_credit_date_utc",
                schema: "caisse",
                table: "remboursement_credit");
        }
    }
}
