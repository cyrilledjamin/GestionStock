using GestionStock.Client.Services;

namespace GestionStock.Client.Tests;

/// <summary>Confirmation d'une action irréversible : la page attend la réponse donnée dans le dialogue.</summary>
public sealed class ConfirmationTests
{
    [Fact]
    public async Task La_reponse_du_dialogue_est_rendue_a_la_page()
    {
        var confirmation = new Confirmation();
        var changements = 0;
        confirmation.Changement += () => changements++;

        var reponse = confirmation.DemanderAsync("Valider ?", "Valider");
        Assert.Equal("Valider ?", confirmation.Message);
        Assert.Equal("Valider", confirmation.Action);
        Assert.False(reponse.IsCompleted);

        confirmation.Repondre(true);

        Assert.True(await reponse);
        Assert.Null(confirmation.Message);
        Assert.Equal(2, changements);
    }

    [Fact]
    public async Task Une_nouvelle_question_repond_non_a_la_precedente()
    {
        var confirmation = new Confirmation();

        var premiere = confirmation.DemanderAsync("Première ?");
        var seconde = confirmation.DemanderAsync("Seconde ?", danger: true);

        Assert.False(await premiere);
        Assert.True(confirmation.Danger);
        confirmation.Repondre(false);
        Assert.False(await seconde);
    }

    [Fact]
    public void Repondre_sans_question_ne_fait_rien()
    {
        var confirmation = new Confirmation();

        confirmation.Repondre(true);

        Assert.Null(confirmation.Message);
    }
}
