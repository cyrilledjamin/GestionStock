namespace GestionStock.Client.Services;

/// <summary>
/// Confirmation d'une action irréversible (validation, avoir, désactivation…) avant de l'envoyer au serveur. La page
/// attend la réponse ; le dialogue de l'application (<c>DialogueConfirmation</c>, dans la mise en page) l'affiche,
/// traduit, et répond « oui » (bouton de l'action) ou « non » (Annuler, Échap). Une seule question à la fois : une
/// nouvelle demande répond « non » à la précédente.
/// </summary>
public sealed class Confirmation
{
    private TaskCompletionSource<bool>? _attente;

    /// <summary>Question en cours (déjà traduite), ou null quand rien n'est demandé.</summary>
    public string? Message { get; private set; }

    /// <summary>Texte du bouton qui confirme (déjà traduit) ; « Confirmer » si null.</summary>
    public string? Action { get; private set; }

    /// <summary>Action qui retire ou annule : bouton rouge.</summary>
    public bool Danger { get; private set; }

    /// <summary>Levé quand une question apparaît ou disparaît.</summary>
    public event Action? Changement;

    /// <summary>Vrai si l'utilisateur confirme <paramref name="message"/> (déjà traduit).</summary>
    public Task<bool> DemanderAsync(string message, string? action = null, bool danger = false)
    {
        _attente?.TrySetResult(false);
        var attente = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _attente = attente;
        Message = message;
        Action = action;
        Danger = danger;
        Changement?.Invoke();
        return attente.Task;
    }

    /// <summary>Réponse de l'utilisateur à la question en cours (sans effet s'il n'y en a pas).</summary>
    public void Repondre(bool confirme)
    {
        var attente = _attente;
        if (attente is null)
        {
            return;
        }

        _attente = null;
        Message = null;
        Action = null;
        Danger = false;
        Changement?.Invoke();
        attente.TrySetResult(confirme);
    }
}
