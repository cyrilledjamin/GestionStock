using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>Application des migrations de tous les modules enregistrés.</summary>
public static class ExtensionsMigrations
{
    /// <summary>
    /// Paramètre qui active l'application des migrations au démarrage
    /// (variable d'environnement <c>BaseDeDonnees__AppliquerMigrationsAuDemarrage</c>). Désactivé par défaut.
    /// </summary>
    public const string CleAppliquerAuDemarrage = "BaseDeDonnees:AppliquerMigrationsAuDemarrage";

    /// <summary>
    /// Applique les migrations en attente de chaque module (un schéma et un historique par module),
    /// dans l'ordre d'enregistrement des modules.
    /// </summary>
    public static async Task AppliquerMigrationsAsync(this IServiceProvider services, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var portee = services.CreateAsyncScope();
        var journal = portee.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExtensionsMigrations));

        foreach (var module in portee.ServiceProvider.GetServices<IContexteModule>())
        {
            var nom = module.Contexte.GetType().Name;
            var enAttente = (await module.Contexte.Database.GetPendingMigrationsAsync(annulation)).ToList();
            if (enAttente.Count == 0)
            {
                journal.LogInformation("Migrations {Contexte} : base à jour.", nom);
                continue;
            }

            journal.LogInformation("Migrations {Contexte} : application de {Migrations}.", nom, string.Join(", ", enAttente));
            await module.Contexte.Database.MigrateAsync(annulation);
        }
    }
}
