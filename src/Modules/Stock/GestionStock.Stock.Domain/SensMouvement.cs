namespace GestionStock.Stock.Domain;

/// <summary>
/// Sens d'un mouvement, déduit de ses emplacements (section 4.6) : une entrée n'a qu'une destination,
/// une sortie qu'une source, un mouvement interne les deux.
/// </summary>
public enum SensMouvement
{
    Entree = 1,
    Sortie = 2,
    Interne = 3,
}
