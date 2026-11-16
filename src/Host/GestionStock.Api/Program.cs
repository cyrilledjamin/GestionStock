using GestionStock.Achats;
using GestionStock.Caisse;
using GestionStock.Credit;
using GestionStock.Identite;
using GestionStock.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Inventaire;
using GestionStock.Prix;
using GestionStock.Referentiel;
using GestionStock.Reprise;
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
builder.Services.AddPrixModule(builder.Configuration);
builder.Services.AddStockModule(builder.Configuration);
builder.Services.AddAchatsModule(builder.Configuration);
builder.Services.AddCreditModule(builder.Configuration);
builder.Services.AddCaisseModule(builder.Configuration);
builder.Services.AddInventaireModule(builder.Configuration);
builder.Services.AddRepriseModule(builder.Configuration);

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

// Client Blazor WebAssembly : fichiers statiques anonymes (la connexion se fait dans le client), et toute
// adresse qui n'est ni une route d'API ni un fichier renvoie vers l'application.
app.MapStaticAssets().AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();
app.MapFallback("/api/{*chemin}", () => Results.NotFound());

// Modules
app.MapIdentiteEndpoints();
app.MapReferentielEndpoints();
app.MapPrixEndpoints();
app.MapStockEndpoints();
app.MapAchatsEndpoints();
app.MapCreditEndpoints();
app.MapCaisseEndpoints();
app.MapInventaireEndpoints();
app.MapRepriseEndpoints();

await app.RunAsync();

/// <summary>Rendu visible pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
