using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionStock.Identite.Infrastructure.Migrations
{
    /// <summary>Vue de lecture du personnel pour le module Rapports : matricule, nom et désignation, sans autre donnée.</summary>
    public partial class VuesLecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Désignation : « Prénom Nom (matricule) », ou le matricule seul tant que la fiche n'est pas complétée.
            migrationBuilder.Sql("""
                CREATE VIEW identite.v_utilisateurs AS
                SELECT u.user_name AS matricule, u.prenom, u.nom,
                       CASE WHEN u.prenom = '' AND u.nom = u.user_name THEN u.user_name
                            ELSE trim(u.prenom || ' ' || u.nom) || ' (' || u.user_name || ')' END AS designation,
                       u.actif
                FROM identite.utilisateur u;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS identite.v_utilisateurs;");
        }
    }
}
