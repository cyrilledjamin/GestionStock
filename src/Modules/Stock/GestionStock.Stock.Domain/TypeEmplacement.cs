namespace GestionStock.Stock.Domain;

/// <summary>Rôle d'un emplacement (dossier d'analyse, section 4.5). L'emplacement lui-même relève du Référentiel.</summary>
public enum TypeEmplacement
{
    Reception = 1,
    Stockage = 2,
    Preparation = 3,
    Expedition = 4,
    Quarantaine = 5,
    Transit = 6,
    Retour = 7,
    Vente = 8,
    Reserve = 9,
    Rayon = 10,
    Demarque = 11,
}
