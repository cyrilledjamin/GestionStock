namespace GestionStock.Stock.Contracts;

/// <summary>
/// Nature d'un mouvement, telle que l'exposent les autres modules.
/// Mêmes noms et valeurs que le domaine (vérifié par les tests d'architecture).
/// </summary>
public enum TypeMouvementStock
{
    EntreeReception = 1,
    SortieExpedition = 2,
    Rangement = 3,
    Reassort = 4,
    TransfertSortie = 5,
    TransfertEntree = 6,
    AjustementInventaire = 7,
    VenteCaisse = 8,
    AvoirCaisse = 9,
    RetourClient = 10,
    RetourFournisseur = 11,
    Demarque = 12,
    Rebut = 13,
    Blocage = 14,
    Deblocage = 15,
    Revalorisation = 16,
}

/// <summary>Rôle d'un emplacement. Mêmes noms et valeurs que le domaine.</summary>
public enum TypeEmplacementStock
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
