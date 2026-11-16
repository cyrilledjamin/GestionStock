using GestionStock.Infrastructure.Persistance;
using GestionStock.Infrastructure.Stockage;
using GestionStock.Infrastructure.Temps;
using GestionStock.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace GestionStock.Infrastructure;

/// <summary>Enregistrement des services transverses, appelé une seule fois par l'hôte.</summary>
public static class ExtensionsInfrastructure
{
    /// <summary>Nom de la chaîne de connexion (variable d'environnement <c>ConnectionStrings__GestionStock</c>).</summary>
    public const string NomChaineConnexion = "GestionStock";

    /// <summary>Étiquette des vérifications de disponibilité (dépendances externes : base de données).</summary>
    public const string EtiquettePret = "pret";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Temps
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IHorloge, HorlogeSysteme>();

        // Persistance : la chaîne est lue à la première utilisation, pas à l'enregistrement.
        services.TryAddSingleton(_ =>
        {
            var chaine = configuration.GetConnectionString(NomChaineConnexion);
            if (string.IsNullOrWhiteSpace(chaine))
            {
                throw new InvalidOperationException(
                    $"Chaîne de connexion « {NomChaineConnexion} » absente : définir ConnectionStrings__{NomChaineConnexion}.");
            }

            return NpgsqlDataSource.Create(ChaineConnexion.Preparer(chaine));
        });
        services.TryAddScoped<ConnexionPartagee>();
        services.TryAddScoped<IUniteDeTravail, UniteDeTravail>();
        services.AddHealthChecks().AddCheck<VerificationBaseDeDonnees>(VerificationBaseDeDonnees.Nom, tags: [EtiquettePret]);

        // Stockage de fichiers
        services.AddOptions<OptionsStockage>()
            .Bind(configuration.GetSection(OptionsStockage.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.RepertoireRacine), "Stockage:RepertoireRacine est obligatoire.")
            .ValidateOnStart();
        services.TryAddSingleton<IStockageFichiers, StockageFichiersLocal>();

        return services;
    }
}
