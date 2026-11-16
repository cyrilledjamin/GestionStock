using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire.Application;
using GestionStock.Inventaire.Contracts;
using GestionStock.Inventaire.Endpoints;
using GestionStock.Inventaire.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Inventaire;

/// <summary>Point d'entrée du module Inventaire : seul type public de l'implémentation.</summary>
public static class ModuleInventaire
{
    /// <summary>Enregistre le module : DbContext du schéma <c>inventaire</c>, inventaires et démarque.</summary>
    public static IServiceCollection AddInventaireModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<InventaireDbContext>(InventaireDbContext.Schema);
        services.AddScoped<ServiceInventaires>();
        services.AddScoped<IServiceInventaire>(fournisseur => fournisseur.GetRequiredService<ServiceInventaires>());
        services.AddScoped<ServiceDemarques>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapInventaireEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsInventaire.Declarer(routes);
        return routes;
    }
}
