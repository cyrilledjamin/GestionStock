using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Catalogue gardé sur le poste : mêmes règles que le serveur pour un scan, une recherche et le prix du jour.</summary>
public sealed class CatalogueLocalTests
{
    private static readonly Guid Lait = Guid.NewGuid();
    private static readonly Guid BoiteLait = Guid.NewGuid();
    private static readonly Guid CartonLait = Guid.NewGuid();
    private static readonly Guid Riz = Guid.NewGuid();
    private static readonly DateOnly Aujourdhui = new(2026, 11, 17);

    private static readonly CatalogueLocal Catalogue = new(
        new CatalogueCaisse(DateTime.UtcNow,
        [
            new(Lait, "LAIT-397", "Lait concentré sucré 397 g", "Sweetened condensed milk", "boîte", 0, BoiteLait, "boîte", 1m, true, false, ["6170000000011"]),
            new(Lait, "LAIT-397", "Lait concentré sucré 397 g", "Sweetened condensed milk", "boîte", 0, CartonLait, "Carton de 48", 48m, false, false, ["6170000000004", "012345678905"]),
            new(Guid.NewGuid(), "RIZ-PARF", "Riz parfumé", null, "kg", 3, Riz, "kg", 1m, true, true, []),
        ]),
        new GrillePrix(Aujourdhui, false, 0m,
        [
            new(BoiteLait, 450m, Aujourdhui.AddDays(-30), 475m, Aujourdhui.AddDays(1)),
            new(CartonLait, 20_000m, Aujourdhui.AddDays(-30), null, null),
        ]));

    [Fact]
    public void Un_code_barres_donne_son_conditionnement_et_le_code_scanne()
    {
        var vendable = Catalogue.Resoudre(" 6170000000004 ");

        Assert.Equal((CartonLait, 48m, "6170000000004"), (vendable!.ConditionnementId, vendable.QuantiteUniteStock, vendable.CodeScanne));
    }

    [Fact]
    public void Un_EAN_13_a_zero_de_tete_se_lit_aussi_en_UPC_A()
    {
        Assert.Equal(CartonLait, Catalogue.Resoudre("0012345678905")!.ConditionnementId);
    }

    [Fact]
    public void Le_code_article_saisi_donne_le_conditionnement_a_l_unite_sans_tenir_compte_de_la_casse()
    {
        var vendable = Catalogue.Resoudre("riz-parf");

        Assert.Equal((Riz, null), (vendable!.ConditionnementId, vendable.CodeScanne));
        Assert.Null(Catalogue.Resoudre("INCONNU"));
    }

    [Fact]
    public void La_recherche_porte_sur_le_code_et_les_libelles_et_propose_chaque_conditionnement()
    {
        Assert.Equal([BoiteLait, CartonLait], Catalogue.Rechercher("lait").Select(v => v.ConditionnementId));
        Assert.Equal([BoiteLait, CartonLait], Catalogue.Rechercher("condensed").Select(v => v.ConditionnementId));
        Assert.Empty(Catalogue.Rechercher("  "));
    }

    [Fact]
    public void Le_prix_du_jour_tient_compte_du_prochain_prix_programme()
    {
        Assert.Equal(450m, Catalogue.Prix(BoiteLait, Aujourdhui));
        Assert.Equal(475m, Catalogue.Prix(BoiteLait, Aujourdhui.AddDays(1))); // poste resté hors ligne après minuit
        Assert.Null(Catalogue.Prix(Riz, Aujourdhui));
    }

    [Fact]
    public void Les_articles_rapides_sont_ceux_choisis_par_le_gerant()
    {
        Assert.Equal([Riz], Catalogue.Rapides.Select(v => v.ConditionnementId));
    }
}
