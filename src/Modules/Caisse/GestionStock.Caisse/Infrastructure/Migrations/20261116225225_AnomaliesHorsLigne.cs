using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Caisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnomaliesHorsLigne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anomalie_caisse",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_caisse = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caissier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_document = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    traitee = table.Column<bool>(type: "boolean", nullable: false),
                    traitee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    traitement_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    commentaire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anomalie_caisse", x => x.id);
                    table.ForeignKey(
                        name: "fk_anomalie_caisse_session_caisse_session_id",
                        column: x => x.session_id,
                        principalSchema: "caisse",
                        principalTable: "session_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anomalie_caisse_document_id",
                schema: "caisse",
                table: "anomalie_caisse",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_anomalie_caisse_session_id",
                schema: "caisse",
                table: "anomalie_caisse",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_anomalie_caisse_traitee",
                schema: "caisse",
                table: "anomalie_caisse",
                column: "traitee");

            // Vue de lecture des anomalies de synchronisation pour le module Rapports (section 7 : CQRS côté lecture).
            migrationBuilder.Sql("""
                CREATE VIEW caisse.v_anomalies AS
                SELECT a.id AS anomalie_id, a.type, a.session_id, a.numero_caisse, a.caissier, a.document_id, a.numero_document,
                       a.detail, a.date_utc, a.traitee, a.traitee_par, a.traitement_utc, a.commentaire
                FROM caisse.anomalie_caisse a;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS caisse.v_anomalies;");

            migrationBuilder.DropTable(
                name: "anomalie_caisse",
                schema: "caisse");
        }
    }
}
