using Microsoft.JSInterop;

namespace GestionStock.Client.Services;

/// <summary>
/// Confirmation d'une action irréversible (validation, avoir, désactivation…) avant de l'envoyer au serveur. Un seul
/// point d'entrée : la fenêtre du navigateur aujourd'hui, le dialogue de l'application quand il existera.
/// </summary>
public sealed class Confirmation(IJSRuntime js)
{
    /// <summary>Vrai si l'utilisateur confirme <paramref name="message"/> (déjà traduit).</summary>
    public ValueTask<bool> DemanderAsync(string message) => js.InvokeAsync<bool>("confirm", message);
}
