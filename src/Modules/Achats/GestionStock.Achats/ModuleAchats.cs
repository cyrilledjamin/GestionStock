using GestionStock.Achats.Application;
using GestionStock.Achats.Endpoints;
using GestionStock.Achats.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Achats;

/// <summary>Point d'entrée du module Achats : seul type public de l'implémentation.</summary>
public static class ModuleAchats
{
    /// <summary>Enregistre le module : DbContext du schéma <c>achats</c>, fournisseurs et réceptions.</summary>
    public static IServiceCollection AddAchatsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<AchatsDbContext>(AchatsDbContext.Schema);
        services.AddScoped<ServiceFournisseurs>();
        services.AddScoped<ServiceReceptions>();
        services.AddScoped<ServiceDettes>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapAchatsEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsAchats.Declarer(routes);
        return routes;
    }
}
