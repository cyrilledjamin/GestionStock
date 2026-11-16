using GestionStock.Reprise.Application;
using GestionStock.Reprise.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Reprise;

/// <summary>
/// Point d'entrée du module Reprise : seul type public de l'implémentation. Sans schéma propre : il écrit par les
/// contrats des modules Referentiel, Prix et Inventaire, dans une seule transaction.
/// </summary>
public static class ModuleReprise
{
    public static IServiceCollection AddRepriseModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<ServiceReprise>();
        return services;
    }

    public static IEndpointRouteBuilder MapRepriseEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsReprise.Declarer(routes);
        return routes;
    }
}
