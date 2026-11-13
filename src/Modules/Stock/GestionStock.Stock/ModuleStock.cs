using GestionStock.Infrastructure.Persistance;
using GestionStock.Stock.Application;
using GestionStock.Stock.Contracts;
using GestionStock.Stock.Endpoints;
using GestionStock.Stock.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Stock;

/// <summary>Point d'entrée du module Stock : seul type public de l'implémentation.</summary>
public static class ModuleStock
{
    /// <summary>Enregistre le module : DbContext du schéma <c>stock</c> et services du contrat.</summary>
    public static IServiceCollection AddStockModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<StockDbContext>(StockDbContext.Schema);
        services.AddScoped<IServiceMouvementsStock, ServiceMouvementsStock>();
        services.AddScoped<IConsultationStock, ConsultationStock>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsStock.Declarer(routes);
        return routes;
    }
}
