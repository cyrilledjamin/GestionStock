using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Credit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "credit");

            migrationBuilder.CreateTable(
                name: "client",
                schema: "credit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    plafond_credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    delai_paiement_jours = table.Column<int>(type: "integer", nullable: false),
                    dlc_residuelle_min_jours = table.Column<int>(type: "integer", nullable: false),
                    solde_du = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    actif = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client", x => x.id);
                    table.CheckConstraint("ck_client_solde_du", "solde_du >= 0");
                });

            migrationBuilder.CreateTable(
                name: "operation_credit",
                schema: "credit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_ticket = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    echeance_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    utilisateur = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    validee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_credit", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_credit_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "credit",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "imputation",
                schema: "credit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imputation", x => x.id);
                    table.ForeignKey(
                        name: "fk_imputation_operation_credit_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "credit",
                        principalTable: "operation_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_imputation_operation_credit_vente_id",
                        column: x => x.vente_id,
                        principalSchema: "credit",
                        principalTable: "operation_credit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_imputation_operation_id",
                schema: "credit",
                table: "imputation",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_imputation_vente_id",
                schema: "credit",
                table: "imputation",
                column: "vente_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_credit_client_id_date_utc",
                schema: "credit",
                table: "operation_credit",
                columns: new[] { "client_id", "date_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_operation_credit_document_id",
                schema: "credit",
                table: "operation_credit",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "imputation",
                schema: "credit");

            migrationBuilder.DropTable(
                name: "operation_credit",
                schema: "credit");

            migrationBuilder.DropTable(
                name: "client",
                schema: "credit");
        }
    }
}
