using GestionStock.Stock.Domain;

namespace GestionStock.Stock.Domain.Tests;

/// <summary>Fabrique de mouvements valides pour les tests, chaque paramètre pouvant être surchargé.</summary>
internal static class Fabrique
{
    public static readonly Guid Article = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Site = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Reception = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Vente = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly DateTime Date = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    public static MouvementStock Mouvement(
        TypeMouvement type = TypeMouvement.EntreeReception,
        Guid? source = null,
        Guid? destination = null,
        decimal quantite = 10m,
        decimal cout = 100m,
        Guid? article = null,
        Guid? site = null,
        string typeDocument = "Reception",
        string utilisateur = "magasinier",
        Guid? idIdempotence = null,
        DateTime? date = null) =>
        MouvementStock.Creer(
            type,
            article ?? Article,
            site ?? Site,
            source,
            destination,
            quantite,
            cout,
            typeDocument,
            Guid.NewGuid(),
            utilisateur,
            idIdempotence ?? Guid.NewGuid(),
            date ?? Date);

    public static MouvementStock Entree(decimal quantite, decimal cout, Guid? destination = null) =>
        Mouvement(TypeMouvement.EntreeReception, destination: destination ?? Reception, quantite: quantite, cout: cout);

    public static MouvementStock Revalorisation(decimal quantite, decimal supplementUnitaire) =>
        Mouvement(TypeMouvement.Revalorisation, quantite: quantite, cout: supplementUnitaire);

    public static MouvementStock VenteCaisse(decimal quantite, Guid? source = null) =>
        Mouvement(TypeMouvement.VenteCaisse, source: source ?? Vente, quantite: quantite, cout: 0m, typeDocument: "TicketCaisse");
}
