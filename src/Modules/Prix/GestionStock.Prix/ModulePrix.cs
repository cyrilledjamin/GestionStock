using GestionStock.Infrastructure.Persistance;
using GestionStock.Prix.Application;
using GestionStock.Prix.Contracts;
using GestionStock.Prix.Endpoints;
using GestionStock.Prix.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Prix;

/// <summary>Point d'entrée du module Prix : seul type public de l'implémentation.</summary>
public static class ModulePrix
{
    /// <summary>Enregistre le module : DbContext du schéma <c>prix</c>, paramètres fiscaux, services du contrat.</summary>
    public static IServiceCollection AddPrixModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<PrixDbContext>(PrixDbContext.Schema);
        services.AddOptions<OptionsEntreprise>()
            .Bind(configuration.GetSection(OptionsEntreprise.Section))
            .Validate(o => o.TauxTva is >= 0m and < 100m, "Entreprise:TauxTva doit être compris entre 0 et 100.")
            .ValidateOnStart();
        services.AddScoped<ServicePrix>();
        services.AddScoped<IConsultationPrix>(fournisseur => fournisseur.GetRequiredService<ServicePrix>());
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapPrixEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsPrix.Declarer(routes);
        return routes;
    }
}
