using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Vente en cours au comptoir : panier, billets rapides, compte juste, pas de saisie.</summary>
public sealed class VenteEnCoursTests
{
    private static readonly ArticleVendable Lait = Vendable("LAIT-397", "boîte", precision: 0, contenu: 1m);
    private static readonly ArticleVendable CartonLait = Vendable("LAIT-397", "carton", precision: 0, contenu: 48m);
    private static readonly ArticleVendable Riz = Vendable("RIZ-PARF", "kg", precision: 3, contenu: 1m);

    [Fact]
    public void Un_article_deja_au_panier_augmente_d_une_unite_au_lieu_d_ajouter_une_ligne()
    {
        var vente = new VenteEnCours();

        vente.Ajouter(Lait, 450m);
        vente.Ajouter(CartonLait, 20_000m);
        vente.Ajouter(Lait, 450m);

        Assert.Equal(2, vente.Lignes.Count);
        Assert.Equal(2m, vente.Ligne(Lait.ConditionnementId)!.Quantite);
        Assert.Equal(20_900m, vente.Total);
    }

    [Fact]
    public void Le_montant_d_une_ligne_est_arrondi_au_franc_comme_au_serveur_puis_diminue_de_la_remise()
    {
        var vente = new VenteEnCours();
        var riz = vente.Ajouter(Riz, 525m);

        riz.Quantite = 1.5m; // 787,5 XAF arrondis à 788
        riz.Remise = 38m;

        Assert.Equal(750m, riz.Montant);
        Assert.Equal(750m, vente.Total);
    }

    [Fact]
    public void Les_billets_rapides_s_additionnent_et_donnent_la_monnaie_a_rendre()
    {
        var vente = new VenteEnCours();
        vente.Ajouter(Lait, 450m).Quantite = 14; // 6 300 XAF

        vente.AjouterEspeces(5_000m);
        vente.AjouterEspeces(2_000m);

        var especes = Assert.Single(vente.Paiements);
        Assert.Equal((ModesPaiement.Especes, 7_000m), (especes.Mode, especes.Montant));
        Assert.Equal(-700m, vente.Reste);
    }

    [Fact]
    public void Le_compte_juste_couvre_en_especes_ce_que_les_autres_modes_ne_paient_pas()
    {
        var vente = new VenteEnCours();
        vente.Ajouter(CartonLait, 20_000m);
        vente.Ajouter(Lait, 450m).Quantite = 2;
        vente.AjouterPaiement(ModesPaiement.MobileMoney);
        vente.Paiements.Single().Montant = 5_900m;
        vente.AjouterEspeces(20_000m);

        vente.CompteJuste();

        Assert.Equal(15_000m, vente.Paiements.Single(p => p.Mode == ModesPaiement.Especes).Montant);
        Assert.Equal(0m, vente.Reste);
    }

    [Fact]
    public void Un_mode_deja_choisi_recoit_le_reste_au_lieu_d_une_seconde_ligne()
    {
        var vente = new VenteEnCours();
        vente.Ajouter(Lait, 450m).Quantite = 4;
        vente.AjouterPaiement(ModesPaiement.Especes);
        vente.Paiements.Single().Montant = 1_000m;

        vente.AjouterPaiement(ModesPaiement.Especes);
        vente.AjouterPaiement(ModesPaiement.Carte);

        Assert.Equal(1_800m, Assert.Single(vente.Paiements).Montant);
    }

    [Fact]
    public void La_quantite_ne_descend_pas_sous_la_plus_petite_quantite_vendable()
    {
        var vente = new VenteEnCours();
        var lait = vente.Ajouter(Lait, 450m);
        var riz = vente.Ajouter(Riz, 525m);
        riz.Quantite = 1.25m;

        VenteEnCours.Decrementer(lait);
        VenteEnCours.Decrementer(riz);
        VenteEnCours.Decrementer(riz);

        Assert.Equal(1m, lait.Quantite);
        Assert.Equal(0.25m, riz.Quantite);
    }

    [Fact]
    public void Le_poids_se_saisit_au_millieme_et_le_reste_a_l_unite()
    {
        Assert.Equal("0.001", VenteEnCours.PasSaisie(Riz));
        Assert.Equal("1", VenteEnCours.PasSaisie(Lait));
        Assert.Equal("1", VenteEnCours.PasSaisie(CartonLait));
    }

    [Fact]
    public void Vider_la_vente_retire_lignes_et_paiements()
    {
        var vente = new VenteEnCours();
        vente.Ajouter(Lait, 450m);
        vente.AjouterEspeces(500m);

        vente.Vider();

        Assert.True(vente.EstVide);
        Assert.Empty(vente.Paiements);
        Assert.Equal(0m, vente.Reste);
    }

    private static ArticleVendable Vendable(string code, string conditionnement, int precision, decimal contenu) =>
        new(Guid.NewGuid(), code, code, null, conditionnement, precision, Guid.NewGuid(), conditionnement, contenu, null);
}
