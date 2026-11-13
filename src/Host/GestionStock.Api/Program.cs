// Point d'entrée de l'hôte : il ne fait qu'assembler les modules.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();

/// <summary>Rendu visible pour les tests d'intégration (WebApplicationFactory).</summary>
public partial class Program;
