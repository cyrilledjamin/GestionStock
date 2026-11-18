namespace GestionStock.Client.Services;

/// <summary>
/// Menu en tiroir (écran étroit, et la caisse sur toutes les largeurs) : ouvert ou fermé. Partagé entre le cadre, qui
/// l'affiche, et la caisse, dont la barre du haut porte son propre bouton « Menu » (maquette A).
/// </summary>
public sealed class EtatMenu
{
    public bool Ouvert { get; private set; }

    /// <summary>Le menu vient d'être ouvert par un bouton : le cadre y porte le focus.</summary>
    public bool FocusAPorter { get; set; }

    public event Action? Changement;

    public void Basculer()
    {
        Ouvert = !Ouvert;
        FocusAPorter = Ouvert;
        Changement?.Invoke();
    }

    public void Fermer()
    {
        if (Ouvert)
        {
            Ouvert = false;
            Changement?.Invoke();
        }
    }
}
