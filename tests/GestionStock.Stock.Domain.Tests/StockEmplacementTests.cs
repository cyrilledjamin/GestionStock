using GestionStock.Stock.Domain;
using static GestionStock.Stock.Domain.Tests.Fabrique;

namespace GestionStock.Stock.Domain.Tests;

public sealed class StockEmplacementTests
{
    [Fact]
    public void Une_entree_augmente_l_emplacement_de_destination()
    {
        var reception = StockEmplacement.Ouvrir(Article, Reception);

        reception.Appliquer(Entree(10m, 1000m), TypeEmplacement.Reception);

        Assert.Equal(10m, reception.QuantitePhysique);
    }

    [Fact]
    public void Un_mouvement_interne_deplace_la_quantite_de_la_source_vers_la_destination()
    {
        var reception = StockEmplacement.Ouvrir(Article, Reception);
        var vente = StockEmplacement.Ouvrir(Article, Vente);
        reception.Appliquer(Entree(10m, 1000m), TypeEmplacement.Reception);
        var rangement = Mouvement(TypeMouvement.Rangement, source: Reception, destination: Vente, quantite: 6m);

        reception.Appliquer(rangement, TypeEmplacement.Reception);
        vente.Appliquer(rangement, TypeEmplacement.Vente);

        Assert.Equal(4m, reception.QuantitePhysique);
        Assert.Equal(6m, vente.QuantitePhysique);
    }

    [Theory]
    [InlineData(TypeEmplacement.Reception)]
    [InlineData(TypeEmplacement.Stockage)]
    [InlineData(TypeEmplacement.Reserve)]
    [InlineData(TypeEmplacement.Rayon)]
    public void Le_stock_ne_peut_pas_devenir_negatif_hors_emplacement_de_vente(TypeEmplacement type)
    {
        // RG03
        var emplacement = StockEmplacement.Ouvrir(Article, Reception);
        emplacement.Appliquer(Entree(2m, 1000m), type);
        var sortie = Mouvement(TypeMouvement.SortieExpedition, source: Reception, quantite: 3m);

        var erreur = Assert.Throws<ExceptionDomaineStock>(() => emplacement.Appliquer(sortie, type));

        Assert.Equal(CodesErreurStock.StockInsuffisant, erreur.Code);
        Assert.Equal(2m, emplacement.QuantitePhysique);
    }

    [Fact]
    public void Une_vente_en_caisse_n_est_jamais_refusee_pour_stock_insuffisant()
    {
        // RG22
        var vente = StockEmplacement.Ouvrir(Article, Vente);

        vente.Appliquer(VenteCaisse(5m), TypeEmplacement.Vente);

        Assert.Equal(-5m, vente.QuantitePhysique);
    }

    [Fact]
    public void Un_mouvement_d_un_autre_emplacement_est_refuse()
    {
        var vente = StockEmplacement.Ouvrir(Article, Vente);

        var erreur = Assert.Throws<ExceptionDomaineStock>(() => vente.Appliquer(Entree(1m, 10m, Reception), TypeEmplacement.Vente));

        Assert.Equal(CodesErreurStock.MouvementNonConcerne, erreur.Code);
    }

    [Fact]
    public void Le_recalcul_rejoue_le_journal_sans_rejuger_les_negatifs()
    {
        MouvementStock[] journal =
        [
            VenteCaisse(3m),
            Mouvement(TypeMouvement.Rangement, source: Reception, destination: Vente, quantite: 10m),
            VenteCaisse(2m),
        ];

        var vente = StockEmplacement.Recalculer(Article, Vente, null, journal);

        Assert.Equal(5m, vente.QuantitePhysique);
    }
}
