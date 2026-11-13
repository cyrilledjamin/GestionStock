namespace GestionStock.Stock.Domain;

/// <summary>Nature d'un mouvement de stock (dossier d'analyse, section 4.5).</summary>
public enum TypeMouvement
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
