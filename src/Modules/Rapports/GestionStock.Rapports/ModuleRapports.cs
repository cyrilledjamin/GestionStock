using GestionStock.Rapports.Application;
using GestionStock.Rapports.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Rapports;

/// <summary>
/// Point d'entrée du module Rapports : seul type public de l'implémentation. Sans schéma ni DbContext : il lit, en
/// lecture seule, les vues <c>v_*</c> que chaque module publie dans son schéma (section 7, CQRS côté lecture).
/// </summary>
public static class ModuleRapports
{
    public static IServiceCollection AddRapportsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<ServiceRapports>();
        return services;
    }

    public static IEndpointRouteBuilder MapRapportsEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsRapports.Declarer(routes);
        return routes;
    }
}
