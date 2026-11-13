using GestionStock.Stock.Contracts;

namespace GestionStock.Stock.IntegrationTests;

/// <summary>Un article sur un site, avec ses emplacements : identifiants neufs pour chaque test.</summary>
internal sealed class Scenario
{
    public Guid Article { get; } = Guid.NewGuid();

    public Guid Site { get; } = Guid.NewGuid();

    public EmplacementMouvement Reception { get; } = new(Guid.NewGuid(), TypeEmplacementStock.Reception);

    public EmplacementMouvement Stockage { get; } = new(Guid.NewGuid(), TypeEmplacementStock.Stockage);

    public EmplacementMouvement Vente { get; } = new(Guid.NewGuid(), TypeEmplacementStock.Vente);

    public DemandeMouvement Entree(decimal quantite, decimal cout, EmplacementMouvement? destination = null, Guid? idIdempotence = null) =>
        Demande(TypeMouvementStock.EntreeReception, null, destination ?? Reception, quantite, cout, "Reception", "magasinier", idIdempotence);

    public DemandeMouvement VenteCaisse(decimal quantite) =>
        Demande(TypeMouvementStock.VenteCaisse, Vente, null, quantite, 0m, "TicketCaisse", "caissier");

    public DemandeMouvement Expedition(decimal quantite, EmplacementMouvement source) =>
        Demande(TypeMouvementStock.SortieExpedition, source, null, quantite, 0m, "BonLivraison", "magasinier");

    public DemandeMouvement Reassort(decimal quantite) =>
        Demande(TypeMouvementStock.Reassort, Stockage, Vente, quantite, 0m, "Reassort", "magasinier");

    private DemandeMouvement Demande(
        TypeMouvementStock type,
        EmplacementMouvement? source,
        EmplacementMouvement? destination,
        decimal quantite,
        decimal cout,
        string typeDocument,
        string utilisateur,
        Guid? idIdempotence = null) =>
        new(type, Article, Site, source, destination, quantite, cout, typeDocument, Guid.NewGuid(), utilisateur, idIdempotence ?? Guid.NewGuid());
}
