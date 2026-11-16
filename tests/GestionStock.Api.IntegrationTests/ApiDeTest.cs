using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(GestionStock.Api.IntegrationTests.ApiDeTest))]

namespace GestionStock.Api.IntegrationTests;

/// <summary>
/// L'API complète (hôte <c>Program</c>), migrée au démarrage sur une PostgreSQL réelle dans un conteneur,
/// avec un Administrateur initial. Partagée par tous les tests : chacun crée ses propres comptes et données.
/// Sans Docker, les tests sont ignorés sur un poste de développement et échouent en intégration continue.
/// </summary>
public sealed class ApiDeTest : IAsyncLifetime
{
    public const string Image = "postgres:17-alpine";
    public const string Administrateur = "admin";
    public const string MotDePasseAdministrateur = "Admin123!";

    /// <summary>Mot de passe conforme à la politique, pour les comptes créés par les tests.</summary>
    public const string MotDePasseValide = "Secret123!";

    private PostgreSqlContainer? _conteneur;
    private WebApplicationFactory<Program>? _fabrique;
    private string? _dockerIndisponible;

    private static bool EstIntegrationContinue =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public async ValueTask InitializeAsync()
    {
        try
        {
            _conteneur = new PostgreSqlBuilder(Image).Build();
            await _conteneur.StartAsync();
        }
        catch (DockerUnavailableException erreur) when (!EstIntegrationContinue)
        {
            _dockerIndisponible = erreur.Message;
            return;
        }

        var chaine = _conteneur.GetConnectionString();
        var repertoire = Path.Combine(Path.GetTempPath(), "gestionstock-tests", Guid.NewGuid().ToString("N"));
        _fabrique = new WebApplicationFactory<Program>().WithWebHostBuilder(hote => hote
            .UseEnvironment("Test")
            .UseSetting("ConnectionStrings:GestionStock", chaine)
            .UseSetting("BaseDeDonnees:AppliquerMigrationsAuDemarrage", "true")
            .UseSetting("Stockage:RepertoireRacine", repertoire)
            .UseSetting("Identite:AdministrateurInitial:Identifiant", Administrateur)
            .UseSetting("Identite:AdministrateurInitial:MotDePasse", MotDePasseAdministrateur));

        // Démarre l'hôte (migrations, Administrateur initial) avant le premier test.
        _ = _fabrique.Server;
    }

    /// <summary>Ignore le test si Docker est absent du poste de développement.</summary>
    public void ExigerDocker()
    {
        if (_dockerIndisponible is not null)
        {
            Assert.Skip($"Docker indisponible, test d'intégration ignoré : {_dockerIndisponible}");
        }
    }

    /// <summary>Client HTTP anonyme.</summary>
    public HttpClient Client()
    {
        ExigerDocker();
        return _fabrique!.CreateClient();
    }

    /// <summary>Client HTTP connecté avec ce compte.</summary>
    public async Task<HttpClient> ClientConnecteAsync(string identifiant, string motDePasse)
    {
        var client = Client();
        var jeton = await ConnecterAsync(client, identifiant, motDePasse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton.AccessToken);
        return client;
    }

    /// <summary>Client connecté en Administrateur initial.</summary>
    public Task<HttpClient> ClientAdministrateurAsync() => ClientConnecteAsync(Administrateur, MotDePasseAdministrateur);

    /// <summary>Crée un compte neuf avec ce rôle (par l'Administrateur) et renvoie un client connecté avec lui.</summary>
    public async Task<HttpClient> ClientAvecRoleAsync(string role)
    {
        var identifiant = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}"[..20];
        using var administrateur = await ClientAdministrateurAsync();
        var reponse = await administrateur.PostAsJsonAsync(
            "/api/identite/utilisateurs",
            new { identifiant, motDePasse = MotDePasseValide, role },
            TestContext.Current.CancellationToken);
        reponse.EnsureSuccessStatusCode();
        return await ClientConnecteAsync(identifiant, MotDePasseValide);
    }

    public static async Task<JetonAcces> ConnecterAsync(HttpClient client, string identifiant, string motDePasse)
    {
        ArgumentNullException.ThrowIfNull(client);
        var reponse = await client.PostAsJsonAsync(
            "/api/identite/connexion",
            new { identifiant, motDePasse },
            TestContext.Current.CancellationToken);
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<JetonAcces>(TestContext.Current.CancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        if (_fabrique is not null)
        {
            await _fabrique.DisposeAsync();
        }

        if (_conteneur is not null)
        {
            await _conteneur.DisposeAsync();
        }
    }
}

/// <summary>Réponse de connexion (format du gestionnaire de jetons porteurs d'ASP.NET Core).</summary>
public sealed record JetonAcces(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
