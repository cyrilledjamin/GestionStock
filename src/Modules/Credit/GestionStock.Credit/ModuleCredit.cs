using GestionStock.Credit.Application;
using GestionStock.Credit.Contracts;
using GestionStock.Credit.Endpoints;
using GestionStock.Credit.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Credit;

/// <summary>Point d'entrée du module Credit : seul type public de l'implémentation.</summary>
public static class ModuleCredit
{
    /// <summary>Enregistre le module : DbContext du schéma <c>credit</c> et services du contrat.</summary>
    public static IServiceCollection AddCreditModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<CreditDbContext>(CreditDbContext.Schema);
        services.AddScoped<ServiceCredit>();
        services.AddScoped<IServiceCredit>(fournisseur => fournisseur.GetRequiredService<ServiceCredit>());
        services.AddScoped<IControleSoldesCredit, ControleSoldesCredit>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapCreditEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsCredit.Declarer(routes);
        return routes;
    }
}
