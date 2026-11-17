namespace GestionStock.Client.Services;

/// <summary>
/// Recherche à la frappe (ENF10) : la demande ne part que 250 ms après la dernière touche, et seule la réponse de la
/// recherche la plus récente est affichée (une réponse lente d'une recherche plus ancienne est ignorée).
/// </summary>
public sealed class RechercheDifferee : IDisposable
{
    /// <summary>Pause de frappe avant d'interroger le serveur (même valeur que la recherche de la caisse).</summary>
    public static readonly TimeSpan Delai = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _attente;
    private int _numero;

    /// <summary>
    /// Attend la fin de la frappe. Renvoie le numéro de cette recherche, ou null si une frappe plus récente l'a remplacée
    /// (rien à faire).
    /// </summary>
    public async Task<int?> AttendreAsync()
    {
        if (_attente is not null)
        {
            await _attente.CancelAsync();
            _attente.Dispose();
        }

        _attente = new CancellationTokenSource();
        var numero = ++_numero;
        try
        {
            await Task.Delay(Delai, _attente.Token);
            return numero;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Vrai si aucune recherche n'a été lancée depuis celle-ci : sa réponse peut être affichée.</summary>
    public bool EstLaDerniere(int numero) => numero == _numero;

    public void Dispose()
    {
        _attente?.Cancel();
        _attente?.Dispose();
    }
}
