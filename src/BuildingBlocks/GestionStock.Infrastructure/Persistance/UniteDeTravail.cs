using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionStock.Infrastructure.Persistance;

/// <inheritdoc />
internal sealed class UniteDeTravail(ConnexionPartagee connexionPartagee, IEnumerable<IContexteModule> contextesModules)
    : IUniteDeTravail
{
    private NpgsqlTransaction? _transaction;

    public bool EstActive => _transaction is not null;

    public Task ExecuterAsync(Func<CancellationToken, Task> operation, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuterAsync(
            async jeton =>
            {
                await operation(jeton);
                return true;
            },
            annulation);
    }

    public async Task<TResultat> ExecuterAsync<TResultat>(
        Func<CancellationToken, Task<TResultat>> operation,
        CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Appel imbriqué : on participe à la transaction déjà ouverte.
        if (_transaction is not null)
        {
            return await operation(annulation);
        }

        var connexion = connexionPartagee.Connexion;
        if (connexion.State != ConnectionState.Open)
        {
            await connexion.OpenAsync(annulation);
        }

        // La libération sans validation annule la transaction.
        await using var transaction = await connexion.BeginTransactionAsync(annulation);
        _transaction = transaction;
        var contextes = contextesModules.Select(c => c.Contexte).ToList();
        try
        {
            foreach (var contexte in contextes)
            {
                await contexte.Database.UseTransactionAsync(transaction, annulation);
            }

            var resultat = await operation(annulation);
            await transaction.CommitAsync(annulation);
            return resultat;
        }
        finally
        {
            foreach (var contexte in contextes)
            {
                await contexte.Database.UseTransactionAsync(null, CancellationToken.None);
            }

            _transaction = null;
        }
    }
}
