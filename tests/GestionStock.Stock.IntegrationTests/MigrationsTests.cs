using GestionStock.Stock.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>La migration Initial crée le schéma attendu, avec ses contraintes.</summary>
public sealed class MigrationsTests(BasePostgres basePostgres)
{
    [Fact]
    public async Task Aucune_migration_n_est_en_attente()
    {
        basePostgres.ExigerDocker();

        var enAttente = await basePostgres.DansPorteeAsync(services =>
            services.GetRequiredService<StockDbContext>().Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));

        Assert.Empty(enAttente);
    }

    [Fact]
    public async Task Le_schema_stock_contient_ses_tables_ses_vues_de_lecture_et_son_historique_de_migrations()
    {
        basePostgres.ExigerDocker();
        var source = basePostgres.Services.GetRequiredService<NpgsqlDataSource>();

        await using var commande = source.CreateCommand(
            "SELECT table_name || ':' || table_type FROM information_schema.tables WHERE table_schema = 'stock' ORDER BY table_name");
        await using var lecteur = await commande.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await lecteur.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(lecteur.GetString(0));
        }

        string[] attendues =
        [
            "__ef_migrations_history:BASE TABLE",
            "mouvement_stock:BASE TABLE",
            "stock_emplacement:BASE TABLE",
            "stock_site:BASE TABLE",
            "v_mouvements:VIEW",
            "v_soldes:VIEW",
        ];
        Assert.Equal(attendues, tables);
    }

    [Fact]
    public async Task La_base_refuse_deux_mouvements_avec_le_meme_identifiant_d_idempotence()
    {
        basePostgres.ExigerDocker();
        var source = basePostgres.Services.GetRequiredService<NpgsqlDataSource>();
        var idIdempotence = Guid.NewGuid();
        const string insertion = """
            INSERT INTO stock.mouvement_stock (date_utc, type, article_id, site_id, quantite, cout_unitaire,
                type_document, document_id, utilisateur, id_idempotence)
            VALUES (now(), 'EntreeReception', gen_random_uuid(), gen_random_uuid(), 1, 0, 'Test', gen_random_uuid(), 'test', $1)
            """;

        async Task Inserer()
        {
            await using var commande = source.CreateCommand(insertion);
            commande.Parameters.AddWithValue(idIdempotence);
            await commande.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await Inserer();
        var erreur = await Assert.ThrowsAsync<PostgresException>(Inserer);

        Assert.Equal(PostgresErrorCodes.UniqueViolation, erreur.SqlState);
    }
}
