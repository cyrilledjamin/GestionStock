using System.Globalization;
using System.Text.Json;
using GestionStock.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GestionStock.Infrastructure.Tests;

/// <summary>
/// Coupure de la base (redémarrage de PostgreSQL) : une requête qui reprend une connexion morte du pool reçoit un 503
/// traduit, jamais une erreur 500 ; les autres erreurs restent au gestionnaire par défaut.
/// </summary>
public sealed class GestionnaireCoupureBaseTests
{
    [Theory]
    [InlineData("57P01")] // arrêt demandé par l'administrateur (redémarrage du serveur)
    [InlineData("57P03")] // le serveur démarre encore
    [InlineData("08006")] // connexion rompue
    public void Une_erreur_de_connexion_PostgreSQL_est_une_coupure(string etat)
    {
        var erreur = new PostgresException("terminating connection", "FATAL", "FATAL", etat);

        Assert.True(GestionnaireCoupureBase.EstCoupure(erreur));
        Assert.True(GestionnaireCoupureBase.EstCoupure(new InvalidOperationException("transient", erreur)));
    }

    [Fact]
    public void Une_erreur_metier_ou_une_contrainte_n_est_pas_une_coupure()
    {
        Assert.False(GestionnaireCoupureBase.EstCoupure(new PostgresException("duplicate key", "ERROR", "ERROR", "23505")));
        Assert.False(GestionnaireCoupureBase.EstCoupure(new InvalidOperationException("autre")));
    }

    [Theory]
    [InlineData("fr", "rien n'a été enregistré")]
    [InlineData("en", "nothing was saved")]
    public async Task Une_coupure_repond_503_traduit_avec_son_code_et_un_delai(string langue, string extrait)
    {
        var precedente = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(langue);
        try
        {
            var contexte = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
                Response = { Body = new MemoryStream() },
            };
            var gestionnaire = new GestionnaireCoupureBase(NullLogger<GestionnaireCoupureBase>.Instance);

            var traite = await gestionnaire.TryHandleAsync(
                contexte, new PostgresException("terminating connection", "FATAL", "FATAL", "57P01"), TestContext.Current.CancellationToken);

            Assert.True(traite);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, contexte.Response.StatusCode);
            Assert.Equal("1", contexte.Response.Headers.RetryAfter.ToString());
            contexte.Response.Body.Position = 0;
            using var probleme = await JsonDocument.ParseAsync(contexte.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(GestionnaireCoupureBase.Code, probleme.RootElement.GetProperty("code").GetString());
            Assert.Contains(extrait, probleme.RootElement.GetProperty("title").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = precedente;
        }
    }

    [Fact]
    public async Task Une_autre_erreur_est_laissee_au_gestionnaire_par_defaut()
    {
        var gestionnaire = new GestionnaireCoupureBase(NullLogger<GestionnaireCoupureBase>.Instance);

        Assert.False(await gestionnaire.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException("bogue"), TestContext.Current.CancellationToken));
    }
}
