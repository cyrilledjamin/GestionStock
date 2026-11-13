using System.Text;
using GestionStock.Infrastructure.Stockage;
using Microsoft.Extensions.Options;

namespace GestionStock.Infrastructure.Tests;

public sealed class StockageFichiersLocalTests : IDisposable
{
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "gestionstock-tests", Guid.NewGuid().ToString("N"));
    private readonly StockageFichiersLocal _stockage;

    public StockageFichiersLocalTests()
    {
        _stockage = new StockageFichiersLocal(Options.Create(new OptionsStockage { RepertoireRacine = _racine }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_racine))
        {
            Directory.Delete(_racine, recursive: true);
        }
    }

    [Fact]
    public async Task Enregistrer_puis_ouvrir_restitue_le_contenu()
    {
        var annulation = TestContext.Current.CancellationToken;
        await _stockage.EnregistrerAsync("recus/2026/ticket-1.pdf", Flux("contenu"), annulation);

        Assert.True(await _stockage.ExisteAsync("recus/2026/ticket-1.pdf", annulation));
        await using var flux = await _stockage.OuvrirAsync("recus/2026/ticket-1.pdf", annulation);
        using var lecteur = new StreamReader(flux);
        Assert.Equal("contenu", await lecteur.ReadToEndAsync(annulation));
    }

    [Fact]
    public async Task Un_fichier_existant_ne_peut_pas_etre_ecrase()
    {
        var annulation = TestContext.Current.CancellationToken;
        await _stockage.EnregistrerAsync("recu.pdf", Flux("original"), annulation);

        await Assert.ThrowsAsync<IOException>(() => _stockage.EnregistrerAsync("recu.pdf", Flux("autre"), annulation));
    }

    [Fact]
    public async Task Ouvrir_un_fichier_absent_leve_FileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _stockage.OuvrirAsync("absent.pdf", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../hors-racine.pdf")]
    [InlineData("recus/../../hors-racine.pdf")]
    public async Task Un_chemin_qui_sort_de_la_racine_est_refuse(string chemin)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _stockage.EnregistrerAsync(chemin, Flux("x"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Un_chemin_absolu_est_refuse()
    {
        var absolu = Path.Combine(Path.GetTempPath(), "absolu.pdf");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _stockage.ExisteAsync(absolu, TestContext.Current.CancellationToken));
    }

    private static MemoryStream Flux(string texte) => new(Encoding.UTF8.GetBytes(texte));
}
