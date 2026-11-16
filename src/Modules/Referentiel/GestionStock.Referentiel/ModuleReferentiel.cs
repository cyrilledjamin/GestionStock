using GestionStock.Infrastructure.Persistance;
using GestionStock.Referentiel.Application;
using GestionStock.Referentiel.Contracts;
using GestionStock.Referentiel.Endpoints;
using GestionStock.Referentiel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Referentiel;

/// <summary>Point d'entrée du module Referentiel : seul type public de l'implémentation.</summary>
public static class ModuleReferentiel
{
    /// <summary>Enregistre le module : DbContext du schéma <c>referentiel</c> et services du contrat.</summary>
    public static IServiceCollection AddReferentielModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<ReferentielDbContext>(ReferentielDbContext.Schema);
        services.AddScoped<ServiceArticles>();
        services.AddScoped<IConsultationReferentiel>(fournisseur => fournisseur.GetRequiredService<ServiceArticles>());
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapReferentielEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsReferentiel.Declarer(routes);
        return routes;
    }
}
