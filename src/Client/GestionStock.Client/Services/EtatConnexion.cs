using Microsoft.JSInterop;

namespace GestionStock.Client.Services;

/// <summary>
/// Le serveur est-il joignable ? Hors ligne dès qu'un appel échoue faute de réseau ou que le navigateur perd la
/// connexion ; une sonde (<c>/health</c>) toutes les 10 secondes détecte le retour du serveur, et
/// <see cref="Changement"/> déclenche alors la synchronisation des opérations en attente (7.8).
/// </summary>
public sealed class EtatConnexion(IJSRuntime js, HttpClient http) : IDisposable
{
    private static readonly TimeSpan IntervalleSonde = TimeSpan.FromSeconds(10);
    private DotNetObjectReference<EtatConnexion>? _moi;
    private CancellationTokenSource? _sonde;

    public bool EnLigne { get; private set; } = true;

    /// <summary>Passage en ligne ou hors ligne.</summary>
    public event Action? Changement;

    public async Task DemarrerAsync()
    {
        _moi = DotNetObjectReference.Create(this);
        if (!await js.InvokeAsync<bool>("stockageLocal.ecouterConnexion", _moi))
        {
            SignalerEchec();
        }
    }

    /// <summary>Événement du navigateur : hors ligne aussitôt ; en ligne seulement si le serveur répond.</summary>
    [JSInvokable]
    public async Task ConnexionChangee(bool enLigne)
    {
        if (!enLigne)
        {
            SignalerEchec();
        }
        else if (await SonderAsync())
        {
            SignalerReussite();
        }
    }

    /// <summary>Un appel n'a pas pu joindre le serveur.</summary>
    public void SignalerEchec()
    {
        if (!EnLigne)
        {
            return;
        }

        EnLigne = false;
        Changement?.Invoke();
        _ = SonderJusquauRetourAsync();
    }

    /// <summary>Le serveur a répondu (quelle que soit la réponse).</summary>
    public void SignalerReussite()
    {
        _sonde?.Cancel();
        if (EnLigne)
        {
            return;
        }

        EnLigne = true;
        Changement?.Invoke();
    }

    private async Task SonderJusquauRetourAsync()
    {
        _sonde?.Cancel();
        var annulation = new CancellationTokenSource();
        _sonde = annulation;
        using var minuterie = new PeriodicTimer(IntervalleSonde);
        try
        {
            while (await minuterie.WaitForNextTickAsync(annulation.Token))
            {
                if (await SonderAsync())
                {
                    SignalerReussite();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Retour en ligne constaté ailleurs.
        }
    }

    private async Task<bool> SonderAsync()
    {
        try
        {
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var reponse = await http.GetAsync("health", delai.Token);
            return reponse.IsSuccessStatusCode;
        }
        catch (Exception erreur) when (erreur is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _sonde?.Cancel();
        _moi?.Dispose();
    }
}
