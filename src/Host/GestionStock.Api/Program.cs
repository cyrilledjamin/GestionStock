using GestionStock.Identite;
using GestionStock.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Referentiel;
using GestionStock.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

// Point d'entrée de l'hôte : il ne fait qu'assembler les modules.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLocalization();

// Sécurité : tout endpoint est refusé par défaut. Chaque endpoint métier déclare sa permission ;
// seuls la connexion et les endpoints techniques sont explicitement anonymes.
// L'authentification (comptes locaux, jetons porteurs) et les permissions viennent du module Identite.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Modules
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentiteModule(builder.Configuration);
builder.Services.AddReferentielModule(builder.Configuration);
builder.Services.AddStockModule(builder.Configuration);

var app = builder.Build();

// Migrations au démarrage, seulement si le paramètre l'active (Development, docker-compose).
if (app.Configuration.GetValue<bool>(ExtensionsMigrations.CleAppliquerAuDemarrage))
{
    await app.Services.AppliquerMigrationsAsync();
}

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
app.MapIdentiteEndpoints();
app.MapReferentielEndpoints();
app.MapStockEndpoints();

await app.RunAsync();

/// <summary>Rendu visible pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
