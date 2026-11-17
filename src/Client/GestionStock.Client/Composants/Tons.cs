namespace GestionStock.Client.Composants;

/// <summary>Sens d'un statut ou d'un chiffre : donne sa couleur (jetons --succes, --attention, --danger…).</summary>
public enum Ton
{
    Neutre,
    Succes,
    Attention,
    Danger,
    Info,
}

/// <summary>Variantes de <see cref="Bouton"/>.</summary>
public enum VarianteBouton
{
    /// <summary>Bouton ordinaire, cadre gris.</summary>
    Secondaire,

    /// <summary>Action principale de l'écran, fond accent (une seule par zone).</summary>
    Principal,

    /// <summary>Action irréversible qui retire ou annule (désactivation, refus), fond rouge.</summary>
    Danger,

    /// <summary>Action secondaire sans cadre (barre d'outils, ligne de tableau).</summary>
    Discret,

    /// <summary>Validation sur fond clair, cadre accent.</summary>
    Valider,
}

/// <summary>Forme de <see cref="Graphique"/>.</summary>
public enum TypeGraphique
{
    Barres,
    Courbe,
}

/// <summary>Correspondance entre les tons, variantes et classes de la feuille de style.</summary>
public static class Classes
{
    public static string Pastille(Ton ton) => ton switch
    {
        Ton.Succes => "pastille succes",
        Ton.Attention => "pastille attention",
        Ton.Danger => "pastille danger",
        Ton.Info => "pastille info",
        _ => "pastille",
    };

    public static string Indicateur(Ton ton) => ton switch
    {
        Ton.Succes => "indicateur succes",
        Ton.Attention => "indicateur attention",
        Ton.Danger => "indicateur alerte",
        _ => "indicateur",
    };

    public static string Bouton(VarianteBouton variante, bool iconeSeule) =>
        (variante switch
        {
            VarianteBouton.Principal => "principal",
            VarianteBouton.Danger => "danger",
            VarianteBouton.Discret => "bouton-discret",
            VarianteBouton.Valider => "valider",
            _ => "",
        } + (iconeSeule ? " icone-seule" : "")).Trim();
}
