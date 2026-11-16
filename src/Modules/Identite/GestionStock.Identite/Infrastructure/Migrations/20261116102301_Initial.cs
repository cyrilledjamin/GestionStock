using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GestionStock.Identite.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identite");

            migrationBuilder.CreateTable(
                name: "role",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "utilisateur",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilisateur", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_revendication",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_revendication", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_revendication_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identite",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utilisateur_connexion_externe",
                schema: "identite",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilisateur_connexion_externe", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_utilisateur_connexion_externe_utilisateur_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "utilisateur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utilisateur_jeton",
                schema: "identite",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilisateur_jeton", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_utilisateur_jeton_utilisateur_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "utilisateur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utilisateur_revendication",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilisateur_revendication", x => x.id);
                    table.ForeignKey(
                        name: "fk_utilisateur_revendication_utilisateur_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "utilisateur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "utilisateur_role",
                schema: "identite",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_utilisateur_role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_utilisateur_role_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identite",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_utilisateur_role_utilisateur_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "utilisateur",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identite",
                table: "role",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[,]
                {
                    { new Guid("0193a5c0-0000-7000-8000-000000000001"), "0193a5c0-0000-7000-8000-000000000001", "Administrateur", "ADMINISTRATEUR" },
                    { new Guid("0193a5c0-0000-7000-8000-000000000002"), "0193a5c0-0000-7000-8000-000000000002", "Gerant", "GERANT" },
                    { new Guid("0193a5c0-0000-7000-8000-000000000003"), "0193a5c0-0000-7000-8000-000000000003", "Comptable", "COMPTABLE" },
                    { new Guid("0193a5c0-0000-7000-8000-000000000004"), "0193a5c0-0000-7000-8000-000000000004", "Caissier", "CAISSIER" },
                    { new Guid("0193a5c0-0000-7000-8000-000000000005"), "0193a5c0-0000-7000-8000-000000000005", "Magasinier", "MAGASINIER" }
                });

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identite",
                table: "role",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_revendication_role_id",
                schema: "identite",
                table: "role_revendication",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identite",
                table: "utilisateur",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identite",
                table: "utilisateur",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_utilisateur_connexion_externe_user_id",
                schema: "identite",
                table: "utilisateur_connexion_externe",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_utilisateur_revendication_user_id",
                schema: "identite",
                table: "utilisateur_revendication",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_utilisateur_role_role_id",
                schema: "identite",
                table: "utilisateur_role",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_revendication",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "utilisateur_connexion_externe",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "utilisateur_jeton",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "utilisateur_revendication",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "utilisateur_role",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "role",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "utilisateur",
                schema: "identite");
        }
    }
}
