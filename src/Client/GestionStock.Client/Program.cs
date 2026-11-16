using System.Globalization;
using GestionStock.Client;
using GestionStock.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// L'API est servie par le même hôte que le client (même origine, pas de CORS).
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<Api>();
builder.Services.AddScoped<Boutique>();
builder.Services.AddScoped<EtatConnexion>();
builder.Services.AddScoped<StockageLocal>();
builder.Services.AddScoped<CacheCaisse>();
builder.Services.AddScoped<CaisseHorsLigne>();
builder.Services.AddLocalization(options => options.ResourcesPath = "Ressources");

var hote = builder.Build();

// Langue de l'interface (ENF09) : choisie par l'utilisateur, mémorisée sur le poste ; français par défaut.
var js = hote.Services.GetRequiredService<IJSRuntime>();
var langue = await js.InvokeAsync<string?>("localStorage.getItem", Langues.Cle) is "en" ? "en" : "fr";
var culture = CultureInfo.GetCultureInfo(langue == "en" ? "en-GB" : "fr-CM");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await hote.Services.GetRequiredService<EtatConnexion>().DemarrerAsync();
await hote.Services.GetRequiredService<Session>().InitialiserAsync();
await hote.Services.GetRequiredService<Boutique>().ChargerAsync();

// Opérations faites hors ligne et pas encore envoyées (7.8) : relues du poste, envoyées si le serveur répond.
var horsLigne = hote.Services.GetRequiredService<CaisseHorsLigne>();
await horsLigne.ChargerAsync();
_ = horsLigne.SynchroniserAsync();
await hote.RunAsync();
