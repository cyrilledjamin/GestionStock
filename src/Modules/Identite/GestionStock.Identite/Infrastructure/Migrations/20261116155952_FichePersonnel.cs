using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Identite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FichePersonnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "numero_matricule",
                schema: "identite");

            migrationBuilder.AddColumn<bool>(
                name: "actif",
                schema: "identite",
                table: "utilisateur",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "langue",
                schema: "identite",
                table: "utilisateur",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "fr");

            migrationBuilder.AddColumn<bool>(
                name: "mot_de_passe_provisoire",
                schema: "identite",
                table: "utilisateur",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "nom",
                schema: "identite",
                table: "utilisateur",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "prenom",
                schema: "identite",
                table: "utilisateur",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "journal_compte",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    utilisateur_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    auteur = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_compte", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_compte_utilisateur_utilisateur_id",
                        column: x => x.utilisateur_id,
                        principalSchema: "identite",
                        principalTable: "utilisateur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_compte_utilisateur_id_date_utc",
                schema: "identite",
                table: "journal_compte",
                columns: new[] { "utilisateur_id", "date_utc" });

            // Comptes existants : leur identifiant devient leur matricule ; la fiche est à compléter par l'Administrateur.
            migrationBuilder.Sql("UPDATE identite.utilisateur SET nom = user_name WHERE nom = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_compte",
                schema: "identite");

            migrationBuilder.DropColumn(
                name: "actif",
                schema: "identite",
                table: "utilisateur");

            migrationBuilder.DropColumn(
                name: "langue",
                schema: "identite",
                table: "utilisateur");

            migrationBuilder.DropColumn(
                name: "mot_de_passe_provisoire",
                schema: "identite",
                table: "utilisateur");

            migrationBuilder.DropColumn(
                name: "nom",
                schema: "identite",
                table: "utilisateur");

            migrationBuilder.DropColumn(
                name: "prenom",
                schema: "identite",
                table: "utilisateur");

            migrationBuilder.DropSequence(
                name: "numero_matricule",
                schema: "identite");
        }
    }
}
