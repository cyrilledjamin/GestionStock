using Microsoft.Extensions.Options;

namespace GestionStock.Infrastructure.Stockage;

/// <summary>Stockage sur le disque du serveur (volume Docker en production).</summary>
internal sealed class StockageFichiersLocal(IOptions<OptionsStockage> options) : IStockageFichiers
{
    private const int TailleTampon = 81920;

    private readonly string _racine = Path.GetFullPath(options.Value.RepertoireRacine);

    public async Task EnregistrerAsync(string cheminRelatif, Stream contenu, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(contenu);
        var chemin = Resoudre(cheminRelatif);
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);

        // CreateNew : refuse d'écraser un fichier existant.
        await using var fichier = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None, TailleTampon, useAsync: true);
        await contenu.CopyToAsync(fichier, annulation);
    }

    public Task<Stream> OuvrirAsync(string cheminRelatif, CancellationToken annulation = default)
    {
        var chemin = Resoudre(cheminRelatif);
        if (!File.Exists(chemin))
        {
            throw new FileNotFoundException("Fichier introuvable dans le stockage.", cheminRelatif);
        }

        Stream flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read, TailleTampon, useAsync: true);
        return Task.FromResult(flux);
    }

    public Task<bool> ExisteAsync(string cheminRelatif, CancellationToken annulation = default) =>
        Task.FromResult(File.Exists(Resoudre(cheminRelatif)));

    /// <summary>Convertit un chemin relatif en chemin absolu, en interdisant toute sortie de la racine.</summary>
    private string Resoudre(string cheminRelatif)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cheminRelatif);
        if (Path.IsPathRooted(cheminRelatif))
        {
            throw new ArgumentException("Le chemin doit être relatif à la racine du stockage.", nameof(cheminRelatif));
        }

        var chemin = Path.GetFullPath(Path.Combine(_racine, cheminRelatif));
        var racineAvecSeparateur = Path.EndsInDirectorySeparator(_racine) ? _racine : _racine + Path.DirectorySeparatorChar;
        if (!chemin.StartsWith(racineAvecSeparateur, StringComparison.Ordinal))
        {
            throw new ArgumentException("Le chemin sort de la racine du stockage.", nameof(cheminRelatif));
        }

        return chemin;
    }
}
