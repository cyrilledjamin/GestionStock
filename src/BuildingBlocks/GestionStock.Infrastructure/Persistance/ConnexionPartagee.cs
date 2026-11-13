using Npgsql;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>
/// Connexion PostgreSQL unique par portée (requête HTTP ou tâche) : tous les DbContext de modules
/// l'utilisent, ce qui leur permet de partager une même transaction (vente = ticket + stock).
/// </summary>
internal sealed class ConnexionPartagee(NpgsqlDataSource sourceDeDonnees) : IAsyncDisposable
{
    private NpgsqlConnection? _connexion;

    /// <summary>Connexion de la portée, créée à la première demande (ouverte par EF ou par l'unité de travail).</summary>
    public NpgsqlConnection Connexion => _connexion ??= sourceDeDonnees.CreateConnection();

    public ValueTask DisposeAsync() => _connexion?.DisposeAsync() ?? ValueTask.CompletedTask;
}
