using GestionStock.Infrastructure.Stockage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Infrastructure.Tests;

/// <summary>
/// Les clés qui protègent les jetons de connexion sont conservées dans le répertoire de données :
/// un redémarrage du serveur ne déconnecte personne.
/// </summary>
public sealed class ProtectionDonneesTests : IDisposable
{
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "gestionstock-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_racine))
        {
            Directory.Delete(_racine, recursive: true);
        }
    }

    [Fact]
    public void Un_jeton_protege_avant_un_redemarrage_reste_lisible_apres()
    {
        string protege;
        using (var avant = Services())
        {
            protege = avant.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("session");
        }

        using var apres = Services();
        var lu = apres.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protege);

        Assert.Equal("session", lu);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_racine, ExtensionsInfrastructure.RepertoireClesProtection), "key-*.xml"));
    }

    private ServiceProvider Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{OptionsStockage.Section}:{nameof(OptionsStockage.RepertoireRacine)}"] = _racine,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
