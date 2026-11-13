namespace GestionStock.Infrastructure.Stockage;

/// <summary>
/// Stockage des fichiers (reçus PDF, imports Excel). Les chemins sont relatifs à la racine configurée.
/// Pas de suppression : un fichier métier n'est jamais effacé physiquement.
/// </summary>
public interface IStockageFichiers
{
    /// <summary>Enregistre un nouveau fichier ; échoue si le chemin existe déjà (un reçu ne s'écrase pas).</summary>
    Task EnregistrerAsync(string cheminRelatif, Stream contenu, CancellationToken annulation = default);

    /// <summary>Ouvre un fichier en lecture ; lève <see cref="FileNotFoundException"/> s'il n'existe pas.</summary>
    Task<Stream> OuvrirAsync(string cheminRelatif, CancellationToken annulation = default);

    /// <summary>Indique si le fichier existe.</summary>
    Task<bool> ExisteAsync(string cheminRelatif, CancellationToken annulation = default);
}
