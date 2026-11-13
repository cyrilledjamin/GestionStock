using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>Enregistrement des DbContext de modules sur la connexion partagée.</summary>
public static class ExtensionsPersistance
{
    /// <summary>Nom de la table d'historique des migrations, créée dans le schéma de chaque module.</summary>
    public const string TableHistoriqueMigrations = "__ef_migrations_history";

    /// <summary>
    /// Enregistre le DbContext d'un module : connexion partagée, historique des migrations dans
    /// le schéma du module, nommage snake_case (ex. <c>stock.stock_site.quantite_reservee</c>).
    /// Le DbContext doit déclarer <paramref name="schema"/> comme schéma par défaut.
    /// </summary>
    public static IServiceCollection AjouterContexteModule<TContexte>(this IServiceCollection services, string schema)
        where TContexte : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        services.AddDbContext<TContexte>((fournisseur, options) => options
            .UseNpgsql(
                fournisseur.GetRequiredService<ConnexionPartagee>().Connexion,
                npgsql => npgsql.MigrationsHistoryTable(TableHistoriqueMigrations, schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IContexteModule>(fournisseur => new ContexteModule(fournisseur.GetRequiredService<TContexte>()));
        return services;
    }
}
