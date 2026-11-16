using GestionStock.Identite.Application;
using GestionStock.Identite.Contracts;
using GestionStock.Identite.Endpoints;
using GestionStock.Identite.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Identite;

/// <summary>Point d'entrée du module Identite : seul type public de l'implémentation.</summary>
public static class ModuleIdentite
{
    /// <summary>
    /// Enregistre les comptes locaux (ASP.NET Core Identity, schéma <c>identite</c>), l'authentification par jeton
    /// porteur, une politique d'autorisation par permission et l'utilisateur courant.
    /// </summary>
    public static IServiceCollection AddIdentiteModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AjouterContexteModule<IdentiteDbContext>(IdentiteDbContext.Schema);

        services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme);
        services.AddIdentityCore<Utilisateur>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = false;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentiteDbContext>()
            .AddClaimsPrincipalFactory<FabriquePrincipal>()
            .AddSignInManager();

        var autorisations = services.AddAuthorizationBuilder();
        foreach (var (permission, roles) in MatricePermissions.RolesParPermission)
        {
            // Tant que son mot de passe est provisoire, un utilisateur n'a aucune permission (il doit d'abord le changer).
            autorisations.AddPolicy(permission, politique => politique
                .RequireAuthenticatedUser()
                .RequireRole(roles)
                .RequireAssertion(contexte => !contexte.User.HasClaim(c => c.Type == RevendicationsIdentite.MotDePasseProvisoire)));
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IUtilisateurCourant, UtilisateurCourant>();
        services.AddScoped<ServiceConnexion>();
        services.AddScoped<ServiceUtilisateurs>();
        services.AddScoped<IValidationResponsable, ServiceValidationResponsable>();

        services.AddOptions<OptionsAdministrateurInitial>().Bind(configuration.GetSection(OptionsAdministrateurInitial.Section));
        services.AddHostedService<InitialisationAdministrateur>();
        return services;
    }

    /// <summary>Déclare les endpoints HTTP du module.</summary>
    public static IEndpointRouteBuilder MapIdentiteEndpoints(this IEndpointRouteBuilder routes)
    {
        EndpointsIdentite.Declarer(routes);
        return routes;
    }
}
