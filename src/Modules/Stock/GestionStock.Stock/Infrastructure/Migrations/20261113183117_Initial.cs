using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GestionStock.Stock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "stock");

            migrationBuilder.CreateTable(
                name: "mouvement_stock",
                schema: "stock",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emplacement_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emplacement_destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numero_serie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantite = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cout_unitaire = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    type_document = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    utilisateur = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    id_idempotence = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mouvement_stock", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_emplacement",
                schema: "stock",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emplacement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantite_physique = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_emplacement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_site",
                schema: "stock",
                columns: table => new
                {
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantite_physique = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantite_reservee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantite_bloquee = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cump = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_site", x => new { x.article_id, x.site_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_mouvement_stock_article_id_site_id_id",
                schema: "stock",
                table: "mouvement_stock",
                columns: new[] { "article_id", "site_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_mouvement_stock_id_idempotence",
                schema: "stock",
                table: "mouvement_stock",
                column: "id_idempotence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mouvement_stock_type_document_document_id",
                schema: "stock",
                table: "mouvement_stock",
                columns: new[] { "type_document", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_emplacement_article_id_emplacement_id_lot_id",
                schema: "stock",
                table: "stock_emplacement",
                columns: new[] { "article_id", "emplacement_id", "lot_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mouvement_stock",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "stock_emplacement",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "stock_site",
                schema: "stock");
        }
    }
}
