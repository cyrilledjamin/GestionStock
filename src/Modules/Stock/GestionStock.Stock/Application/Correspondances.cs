using GestionStock.Stock.Contracts;
using GestionStock.Stock.Domain;

namespace GestionStock.Stock.Application;

/// <summary>Passage entre les types du contrat public et ceux du domaine (mêmes valeurs).</summary>
internal static class Correspondances
{
    public static TypeMouvement VersDomaine(this TypeMouvementStock type) => (TypeMouvement)(int)type;

    public static TypeEmplacement VersDomaine(this TypeEmplacementStock type) => (TypeEmplacement)(int)type;

    public static TypeMouvementStock VersContrat(this TypeMouvement type) => (TypeMouvementStock)(int)type;

    public static SoldeStockSite VersContrat(this StockSite solde) => new(
        solde.ArticleId,
        solde.SiteId,
        solde.QuantitePhysique,
        solde.QuantiteReservee,
        solde.QuantiteBloquee,
        solde.QuantiteDisponible,
        solde.Cump);
}
