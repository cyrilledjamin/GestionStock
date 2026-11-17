using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(GestionStock.E2E.Infrastructure.Navigateur))]

namespace GestionStock.E2E.Infrastructure;

/// <summary>Un Chromium partagé par tous les parcours ; chaque parcours a son propre contexte (cookies, stockage).</summary>
public sealed class Navigateur : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Chromium { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!Environnement.Actif)
        {
            return;
        }

        _playwright = await Playwright.CreateAsync();
        Chromium = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = !Environnement.Visible,
            // Le service worker (caisse hors ligne) exige une origine sûre : l'adresse testée peut être en HTTP hors
            // de localhost (conteneur de test, réseau de la boutique).
            Args = [$"--unsafely-treat-insecure-origin-as-secure={Environnement.Url}"],
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Chromium is not null)
        {
            await Chromium.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}
