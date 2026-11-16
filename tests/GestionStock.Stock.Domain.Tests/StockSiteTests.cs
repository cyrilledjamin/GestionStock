using GestionStock.Stock.Domain;
using static GestionStock.Stock.Domain.Tests.Fabrique;

namespace GestionStock.Stock.Domain.Tests;

public sealed class StockSiteTests
{
    [Fact]
    public void Un_solde_ouvert_est_vide()
    {
        var solde = StockSite.Ouvrir(Article, Site);

        Assert.Equal(0m, solde.QuantitePhysique);
        Assert.Equal(0m, solde.QuantiteDisponible);
        Assert.Equal(0m, solde.Cump);
    }

    [Fact]
    public void Le_CUMP_est_la_moyenne_ponderee_des_entrees()
    {
        var solde = StockSite.Ouvrir(Article, Site);

        solde.Appliquer(Entree(10m, 1000m));
        solde.Appliquer(Entree(30m, 1200m));

        // (10 × 1000 + 30 × 1200) / 40 = 1150
        Assert.Equal(40m, solde.QuantitePhysique);
        Assert.Equal(1150m, solde.Cump);
        Assert.Equal(46000m, solde.Valeur);
    }

    [Fact]
    public void Le_CUMP_est_arrondi_a_quatre_decimales()
    {
        var solde = StockSite.Ouvrir(Article, Site);

        solde.Appliquer(Entree(1m, 100m));
        solde.Appliquer(Entree(2m, 101m));

        // (100 + 202) / 3 = 100,6666…
        Assert.Equal(100.6667m, solde.Cump);
    }

    [Fact]
    public void Une_sortie_diminue_le_physique_sans_changer_le_CUMP()
    {
        var solde = StockSite.Ouvrir(Article, Site);
        solde.Appliquer(Entree(10m, 1000m));

        solde.Appliquer(VenteCaisse(4m));

        Assert.Equal(6m, solde.QuantitePhysique);
        Assert.Equal(1000m, solde.Cump);
    }

    [Fact]
    public void Un_mouvement_interne_ne_change_pas_le_solde_du_site()
    {
        var solde = StockSite.Ouvrir(Article, Site);
        solde.Appliquer(Entree(10m, 1000m));

        solde.Appliquer(Mouvement(TypeMouvement.Rangement, source: Reception, destination: Vente));

        Assert.Equal(10m, solde.QuantitePhysique);
        Assert.Equal(1000m, solde.Cump);
    }

    [Fact]
    public void Le_site_peut_devenir_negatif_par_les_ventes_en_caisse()
    {
        var solde = StockSite.Ouvrir(Article, Site);

        solde.Appliquer(VenteCaisse(2m));

        Assert.Equal(-2m, solde.QuantitePhysique);
    }

    [Fact]
    public void Apres_un_stock_negatif_le_cout_de_l_entree_devient_le_CUMP()
    {
        var solde = StockSite.Ouvrir(Article, Site);
        solde.Appliquer(VenteCaisse(2m));

        solde.Appliquer(Entree(10m, 800m));

        Assert.Equal(8m, solde.QuantitePhysique);
        Assert.Equal(800m, solde.Cump);
    }

    [Fact]
    public void Une_revalorisation_augmente_le_CUMP_sans_changer_la_quantite()
    {
        var solde = StockSite.Ouvrir(Article, Site);
        solde.Appliquer(Entree(10m, 1000m));
        solde.Appliquer(VenteCaisse(2m));

        // Frais de 1 200 XAF répartis sur les 8 unités restantes : 150 de plus par unité.
        solde.Appliquer(Revalorisation(8m, 150m));

        Assert.Equal(8m, solde.QuantitePhysique);
        Assert.Equal(1150m, solde.Cump);
        Assert.Equal(9200m, solde.Valeur);
    }

    [Fact]
    public void Une_revalorisation_partielle_se_repartit_sur_tout_le_stock_present()
    {
        var solde = StockSite.Ouvrir(Article, Site);
        solde.Appliquer(Entree(3m, 100m));

        // 100 XAF de frais sur 2 des 3 unités : la valeur passe de 300 à 400, soit 133,3333 l'unité.
        solde.Appliquer(Revalorisation(2m, 50m));

        Assert.Equal(133.3333m, solde.Cump);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 6)]
    public void Une_revalorisation_au_dela_du_stock_present_est_refusee(int stock, int quantiteRevalorisee)
    {
        var solde = StockSite.Ouvrir(Article, Site);
        if (stock > 0)
        {
            solde.Appliquer(Entree(stock, 100m));
        }

        var erreur = Assert.Throws<ExceptionDomaineStock>(() => solde.Appliquer(Revalorisation(quantiteRevalorisee, 10m)));

        Assert.Equal(CodesErreurStock.RevalorisationSansStock, erreur.Code);
        Assert.Equal(stock == 0 ? 0m : 100m, solde.Cump);
    }

    [Fact]
    public void Un_mouvement_d_un_autre_article_ou_site_est_refuse()
    {
        var solde = StockSite.Ouvrir(Article, Site);

        var autreArticle = Assert.Throws<ExceptionDomaineStock>(
            () => solde.Appliquer(Mouvement(destination: Reception, article: Guid.NewGuid())));
        var autreSite = Assert.Throws<ExceptionDomaineStock>(
            () => solde.Appliquer(Mouvement(destination: Reception, site: Guid.NewGuid())));

        Assert.Equal(CodesErreurStock.MouvementNonConcerne, autreArticle.Code);
        Assert.Equal(CodesErreurStock.MouvementNonConcerne, autreSite.Code);
    }

    [Fact]
    public void Le_solde_recalcule_depuis_le_journal_egale_le_solde_tenu_au_fil_de_l_eau()
    {
        MouvementStock[] journal =
        [
            Entree(10m, 1000m),
            VenteCaisse(3m),
            Entree(5m, 1300m),
            Mouvement(TypeMouvement.Rangement, source: Reception, destination: Vente, quantite: 4m),
            VenteCaisse(1.5m),
            Revalorisation(10m, 20m),
        ];
        var auFilDeLEau = StockSite.Ouvrir(Article, Site);
        foreach (var mouvement in journal)
        {
            auFilDeLEau.Appliquer(mouvement);
        }

        var recalcule = StockSite.Recalculer(Article, Site, journal);

        Assert.Equal(auFilDeLEau.QuantitePhysique, recalcule.QuantitePhysique);
        Assert.Equal(auFilDeLEau.Cump, recalcule.Cump);
        Assert.Equal(10.5m, recalcule.QuantitePhysique);
    }
}
