using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Referentiel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "referentiel");

            migrationBuilder.CreateTable(
                name: "article",
                schema: "referentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    libelle_traduit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unite_stock = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    precision_quantite = table.Column<int>(type: "integer", nullable: false),
                    mode_suivi = table.Column<int>(type: "integer", nullable: false),
                    gere_dlc = table.Column<bool>(type: "boolean", nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article", x => x.id);
                    table.CheckConstraint("ck_article_precision_quantite", "precision_quantite BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "site",
                schema: "referentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    gestion_reserve_rayon = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "emplacement",
                schema: "referentiel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    mono_article = table.Column<bool>(type: "boolean", nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emplacement", x => x.id);
                    table.ForeignKey(
                        name: "fk_emplacement_site_site_id",
                        column: x => x.site_id,
                        principalSchema: "referentiel",
                        principalTable: "site",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "referentiel",
                table: "site",
                columns: new[] { "id", "code", "gestion_reserve_rayon", "nom", "type" },
                values: new object[] { new Guid("0193a5c0-0000-7000-8000-000000000101"), "BOUTIQUE", false, "Boutique", 2 });

            migrationBuilder.InsertData(
                schema: "referentiel",
                table: "emplacement",
                columns: new[] { "id", "actif", "code", "mono_article", "site_id", "type" },
                values: new object[] { new Guid("0193a5c0-0000-7000-8000-000000000102"), true, "VENTE", false, new Guid("0193a5c0-0000-7000-8000-000000000101"), 8 });

            migrationBuilder.CreateIndex(
                name: "ix_article_code",
                schema: "referentiel",
                table: "article",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emplacement_site_id_code",
                schema: "referentiel",
                table: "emplacement",
                columns: new[] { "site_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_code",
                schema: "referentiel",
                table: "site",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article",
                schema: "referentiel");

            migrationBuilder.DropTable(
                name: "emplacement",
                schema: "referentiel");

            migrationBuilder.DropTable(
                name: "site",
                schema: "referentiel");
        }
    }
}
