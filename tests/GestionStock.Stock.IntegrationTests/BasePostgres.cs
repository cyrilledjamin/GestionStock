using DotNet.Testcontainers.Builders;
using GestionStock.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.Infrastructure.Stockage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(GestionStock.Stock.IntegrationTests.BasePostgres))]

namespace GestionStock.Stock.IntegrationTests;

/// <summary>
/// PostgreSQL réelle dans un conteneur, partagée par tous les tests de l'assemblage, migrée comme en
/// production (<see cref="ExtensionsMigrations.AppliquerMigrationsAsync"/>). Les services sont ceux
/// de l'hôte : Infrastructure et module Stock. Chaque test isole ses données par des identifiants neufs.
/// Sans Docker, les tests sont ignorés sur un poste de développement et échouent en intégration continue
/// (variable d'environnement <c>CI=true</c>).
/// </summary>
public sealed class BasePostgres : IAsyncLifetime
{
    /// <summary>Même image que deploy/docker-compose.yml.</summary>
    public const string Image = "postgres:17-alpine";

    private PostgreSqlContainer? _conteneur;
    private ServiceProvider? _services;
    private string? _dockerIndisponible;

    private static bool EstIntegrationContinue =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Base non démarrée : appeler ExigerDocker() en début de test.");

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

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ExtensionsInfrastructure.NomChaineConnexion}"] = _conteneur.GetConnectionString(),
                [$"{OptionsStockage.Section}:{nameof(OptionsStockage.RepertoireRacine)}"] =
                    Path.Combine(Path.GetTempPath(), "gestionstock-tests", Guid.NewGuid().ToString("N")),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddStockModule(configuration);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await _services.AppliquerMigrationsAsync();
    }

    /// <summary>Ignore le test si Docker est absent du poste de développement.</summary>
    public void ExigerDocker()
    {
        if (_dockerIndisponible is not null)
        {
            Assert.Skip($"Docker indisponible, test d'intégration ignoré : {_dockerIndisponible}");
        }
    }

    /// <summary>Exécute une action dans une portée neuve, comme une requête HTTP.</summary>
    public async Task<TResultat> DansPorteeAsync<TResultat>(Func<IServiceProvider, Task<TResultat>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var portee = Services.CreateAsyncScope();
        return await action(portee.ServiceProvider);
    }

    /// <inheritdoc cref="DansPorteeAsync{TResultat}" />
    public Task DansPorteeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return DansPorteeAsync(async services =>
        {
            await action(services);
            return true;
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_conteneur is not null)
        {
            await _conteneur.DisposeAsync();
        }
    }
}
