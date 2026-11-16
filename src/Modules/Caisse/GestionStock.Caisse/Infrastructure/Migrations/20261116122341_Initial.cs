using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Caisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "caisse");

            migrationBuilder.CreateTable(
                name: "compteur_caisse",
                schema: "caisse",
                columns: table => new
                {
                    numero_caisse = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dernier_numero = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compteur_caisse", x => x.numero_caisse);
                });

            migrationBuilder.CreateTable(
                name: "session_caisse",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_caisse = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caissier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ouverture_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fond_caisse = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cloture_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    especes_theoriques = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    especes_comptees = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    ecart = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    validee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    validation_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_caisse", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remboursement_credit",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_idempotence = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference_transaction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    caissier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remboursement_credit", x => x.id);
                    table.ForeignKey(
                        name: "fk_remboursement_credit_session_caisse_session_id",
                        column: x => x.session_id,
                        principalSchema: "caisse",
                        principalTable: "session_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_caisse",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_idempotence = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_caisse = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false),
                    numero_complet = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    caissier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    est_avoir = table.Column<bool>(type: "boolean", nullable: false),
                    ticket_origine_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_ttc = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_ht = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_tva = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    taux_tva = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    rendu = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remise_validee_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    credit_valide_par = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_caisse", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_caisse_session_caisse_session_id",
                        column: x => x.session_id,
                        principalSchema: "caisse",
                        principalTable: "session_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_caisse_ticket_caisse_ticket_origine_id",
                        column: x => x.ticket_origine_id,
                        principalSchema: "caisse",
                        principalTable: "ticket_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ligne_ticket",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordre = table.Column<int>(type: "integer", nullable: false),
                    conditionnement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    libelle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    libelle_conditionnement = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code_scanne = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    quantite = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantite_unite_stock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    prix_unitaire = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remise = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    taux_tva = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    montant_ttc = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    lot_presume = table.Column<bool>(type: "boolean", nullable: false),
                    ligne_origine_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ligne_ticket", x => x.id);
                    table.ForeignKey(
                        name: "fk_ligne_ticket_ticket_caisse_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "caisse",
                        principalTable: "ticket_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "paiement",
                schema: "caisse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    montant = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference_transaction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paiement", x => x.id);
                    table.ForeignKey(
                        name: "fk_paiement_ticket_caisse_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "caisse",
                        principalTable: "ticket_caisse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ligne_ticket_ligne_origine_id",
                schema: "caisse",
                table: "ligne_ticket",
                column: "ligne_origine_id");

            migrationBuilder.CreateIndex(
                name: "ix_ligne_ticket_ticket_id",
                schema: "caisse",
                table: "ligne_ticket",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_paiement_ticket_id",
                schema: "caisse",
                table: "paiement",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_remboursement_credit_id_idempotence",
                schema: "caisse",
                table: "remboursement_credit",
                column: "id_idempotence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_remboursement_credit_session_id",
                schema: "caisse",
                table: "remboursement_credit",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_caisse_caissier_ouvert",
                schema: "caisse",
                table: "session_caisse",
                column: "caissier",
                unique: true,
                filter: "statut = 'Ouverte'");

            migrationBuilder.CreateIndex(
                name: "ix_session_caisse_ouverture_utc",
                schema: "caisse",
                table: "session_caisse",
                column: "ouverture_utc");

            migrationBuilder.CreateIndex(
                name: "ix_session_caisse_poste_ouvert",
                schema: "caisse",
                table: "session_caisse",
                column: "numero_caisse",
                unique: true,
                filter: "statut = 'Ouverte'");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_id_idempotence",
                schema: "caisse",
                table: "ticket_caisse",
                column: "id_idempotence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_numero_caisse_numero",
                schema: "caisse",
                table: "ticket_caisse",
                columns: new[] { "numero_caisse", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_numero_complet",
                schema: "caisse",
                table: "ticket_caisse",
                column: "numero_complet");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_session_id",
                schema: "caisse",
                table: "ticket_caisse",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_caisse_ticket_origine_id",
                schema: "caisse",
                table: "ticket_caisse",
                column: "ticket_origine_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compteur_caisse",
                schema: "caisse");

            migrationBuilder.DropTable(
                name: "ligne_ticket",
                schema: "caisse");

            migrationBuilder.DropTable(
                name: "paiement",
                schema: "caisse");

            migrationBuilder.DropTable(
                name: "remboursement_credit",
                schema: "caisse");

            migrationBuilder.DropTable(
                name: "ticket_caisse",
                schema: "caisse");

            migrationBuilder.DropTable(
                name: "session_caisse",
                schema: "caisse");
        }
    }
}
