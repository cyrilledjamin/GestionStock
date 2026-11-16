namespace GestionStock.Stock.Domain;

/// <summary>
/// Sens d'un mouvement, déduit de ses emplacements (section 4.6) : une entrée n'a qu'une destination,
/// une sortie qu'une source, un mouvement interne les deux ; une revalorisation (RG30) n'en a aucun.
/// </summary>
public enum SensMouvement
{
    Entree = 1,
    Sortie = 2,
    Interne = 3,

    /// <summary>Revalorisation : la valeur du stock change, pas sa quantité ni son emplacement.</summary>
    Valorisation = 4,
}
