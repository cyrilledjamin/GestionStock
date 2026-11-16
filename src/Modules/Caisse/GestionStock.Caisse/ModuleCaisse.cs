using GestionStock.Caisse.Application;
using GestionStock.Caisse.Contracts;
using GestionStock.Caisse.Endpoints;
using GestionStock.Caisse.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Caisse;

/// <summary>Point d'entrée du module Caisse : seul type public de l'implémentation.</summary>
public static class ModuleCaisse
{
    /// <summary>Enregistre le module : DbContext du schéma <c>caisse</c>, postes, sessions, tickets et reçus.</summary>
    public static IServiceCollection AddCaisseModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<CaisseDbContext>(CaisseDbContext.Schema);
        services.AddOptions<OptionsCaisse>().Bind(configuration.GetSection(OptionsCaisse.Section));
        services.AddOptions<OptionsEntrepriseRecu>().Bind(configuration.GetSection(OptionsEntrepriseRecu.Section));
        services.AddScoped<ServiceSessions>();
        services.AddScoped<ServiceTickets>();
        services.AddScoped<ServiceRecus>();
        services.AddSingleton<IGenerateurRecu, GenerateurRecuPdf>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapCaisseEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsCaisse.Declarer(routes);
        return routes;
    }
}
