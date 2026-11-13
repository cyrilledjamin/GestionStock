using GestionStock.Api.Securite;
using GestionStock.Infrastructure;
using GestionStock.Stock;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// Point d'entrée de l'hôte : il ne fait qu'assembler les modules.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLocalization();

// Sécurité : tout endpoint est refusé par défaut. Chaque endpoint métier déclare sa permission ;
// seuls les endpoints techniques sont explicitement anonymes. Le schéma d'authentification
// (comptes locaux / OIDC, ENF06) arrivera avec le module Identite.
builder.Services.AddAuthentication(AuthentificationProvisoire.Schema)
    .AddScheme<AuthenticationSchemeOptions, AuthentificationProvisoire>(AuthentificationProvisoire.Schema, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Modules
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddStockModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Bilinguisme (ENF09) : français par défaut, anglais disponible.
string[] langues = ["fr", "en"];
app.UseRequestLocalization(options => options
    .SetDefaultCulture(langues[0])
    .AddSupportedCultures(langues)
    .AddSupportedUICultures(langues));

app.UseAuthentication();
app.UseAuthorization();

// Endpoints techniques, anonymes par choix explicite.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/pret", new HealthCheckOptions
{
    Predicate = verification => verification.Tags.Contains(ExtensionsInfrastructure.EtiquettePret),
}).AllowAnonymous();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Modules
app.MapStockEndpoints();

await app.RunAsync();

/// <summary>Rendu visible pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
