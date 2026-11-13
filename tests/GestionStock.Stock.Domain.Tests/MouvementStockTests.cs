using GestionStock.Stock.Domain;
using static GestionStock.Stock.Domain.Tests.Fabrique;

namespace GestionStock.Stock.Domain.Tests;

public sealed class MouvementStockTests
{
    private static readonly Guid AutreEmplacement = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Une_entree_valide_porte_toutes_ses_donnees()
    {
        var idIdempotence = Guid.NewGuid();

        var mouvement = Mouvement(destination: Reception, quantite: 12.5m, cout: 1500m, idIdempotence: idIdempotence);

        Assert.Equal(SensMouvement.Entree, mouvement.Sens);
        Assert.Equal(12.5m, mouvement.VariationSite);
        Assert.Equal(1500m, mouvement.CoutUnitaire);
        Assert.Equal(idIdempotence, mouvement.IdIdempotence);
        Assert.Equal(Date, mouvement.DateUtc);
        Assert.Equal("Reception", mouvement.TypeDocument);
    }

    [Fact]
    public void Une_sortie_a_une_variation_negative_et_un_interne_une_variation_nulle()
    {
        var sortie = VenteCaisse(3m);
        var interne = Mouvement(TypeMouvement.Rangement, source: Reception, destination: AutreEmplacement);

        Assert.Equal(SensMouvement.Sortie, sortie.Sens);
        Assert.Equal(-3m, sortie.VariationSite);
        Assert.Equal(SensMouvement.Interne, interne.Sens);
        Assert.Equal(0m, interne.VariationSite);
    }

    [Theory]
    [InlineData(TypeMouvement.EntreeReception, false, true)]
    [InlineData(TypeMouvement.TransfertEntree, false, true)]
    [InlineData(TypeMouvement.AvoirCaisse, false, true)]
    [InlineData(TypeMouvement.RetourClient, false, true)]
    [InlineData(TypeMouvement.SortieExpedition, true, false)]
    [InlineData(TypeMouvement.TransfertSortie, true, false)]
    [InlineData(TypeMouvement.VenteCaisse, true, false)]
    [InlineData(TypeMouvement.RetourFournisseur, true, false)]
    [InlineData(TypeMouvement.Rebut, true, false)]
    [InlineData(TypeMouvement.Rangement, true, true)]
    [InlineData(TypeMouvement.Reassort, true, true)]
    [InlineData(TypeMouvement.AjustementInventaire, false, true)]
    [InlineData(TypeMouvement.AjustementInventaire, true, false)]
    [InlineData(TypeMouvement.Demarque, true, false)]
    [InlineData(TypeMouvement.Demarque, true, true)]
    public void Les_sens_autorises_sont_acceptes(TypeMouvement type, bool avecSource, bool avecDestination)
    {
        var mouvement = Mouvement(type, avecSource ? Reception : null, avecDestination ? AutreEmplacement : null);

        Assert.Equal(type, mouvement.Type);
    }

    [Theory]
    [InlineData(TypeMouvement.EntreeReception, true, false)]
    [InlineData(TypeMouvement.EntreeReception, true, true)]
    [InlineData(TypeMouvement.VenteCaisse, false, true)]
    [InlineData(TypeMouvement.VenteCaisse, true, true)]
    [InlineData(TypeMouvement.Rangement, false, true)]
    [InlineData(TypeMouvement.Reassort, true, false)]
    [InlineData(TypeMouvement.AjustementInventaire, true, true)]
    [InlineData(TypeMouvement.Demarque, false, true)]
    public void Les_sens_incompatibles_avec_le_type_sont_refuses(TypeMouvement type, bool avecSource, bool avecDestination)
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(
            () => Mouvement(type, avecSource ? Reception : null, avecDestination ? AutreEmplacement : null));

        Assert.Equal(CodesErreurStock.SensIncompatible, erreur.Code);
    }

    [Fact]
    public void Un_mouvement_sans_source_ni_destination_est_refuse()
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(source: null, destination: null));

        Assert.Equal(CodesErreurStock.SensIncompatible, erreur.Code);
    }

    [Fact]
    public void La_source_et_la_destination_doivent_differer()
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(
            () => Mouvement(TypeMouvement.Rangement, source: Reception, destination: Reception));

        Assert.Equal(CodesErreurStock.SourceEgaleDestination, erreur.Code);
    }

    [Theory]
    [InlineData(TypeMouvement.Blocage)]
    [InlineData(TypeMouvement.Deblocage)]
    [InlineData(TypeMouvement.Revalorisation)]
    public void Les_types_dont_l_effet_n_est_pas_defini_sont_refuses(TypeMouvement type)
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(type, destination: Reception));

        Assert.Equal(CodesErreurStock.TypeNonPrisEnCharge, erreur.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void La_quantite_doit_etre_strictement_positive(decimal quantite)
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, quantite: quantite));

        Assert.Equal(CodesErreurStock.QuantiteNonPositive, erreur.Code);
    }

    [Fact]
    public void Une_quantite_a_plus_de_quatre_decimales_est_refusee()
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, quantite: 1.00001m));

        Assert.Equal(CodesErreurStock.PrecisionQuantite, erreur.Code);
    }

    [Fact]
    public void Un_cout_negatif_est_refuse()
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, cout: -1m));

        Assert.Equal(CodesErreurStock.CoutNegatif, erreur.Code);
    }

    [Fact]
    public void Une_date_non_UTC_est_refusee()
    {
        var dateLocale = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Local);

        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, date: dateLocale));

        Assert.Equal(CodesErreurStock.DateNonUtc, erreur.Code);
    }

    [Fact]
    public void L_identifiant_d_idempotence_est_obligatoire()
    {
        var erreur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, idIdempotence: Guid.Empty));

        Assert.Equal(CodesErreurStock.IdempotenceObligatoire, erreur.Code);
    }

    [Fact]
    public void Le_document_d_origine_et_l_utilisateur_sont_obligatoires()
    {
        var sansDocument = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, typeDocument: " "));
        var sansUtilisateur = Assert.Throws<ExceptionDomaineStock>(() => Mouvement(destination: Reception, utilisateur: ""));

        Assert.Equal(CodesErreurStock.DocumentObligatoire, sansDocument.Code);
        Assert.Equal(CodesErreurStock.UtilisateurObligatoire, sansUtilisateur.Code);
    }

    [Fact]
    public void Un_mouvement_n_expose_aucun_moyen_d_etre_modifie()
    {
        // RG01 : aucune propriété publique modifiable.
        var proprietesModifiables = typeof(MouvementStock).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        Assert.Empty(proprietesModifiables);
    }
}
