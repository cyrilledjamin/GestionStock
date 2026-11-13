using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>Vérifie que PostgreSQL répond (sonde de disponibilité <c>/health/pret</c>).</summary>
internal sealed class VerificationBaseDeDonnees(IServiceProvider fournisseur) : IHealthCheck
{
    public const string Nom = "postgresql";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceDeDonnees = (NpgsqlDataSource)fournisseur.GetService(typeof(NpgsqlDataSource))!;
            await using var commande = sourceDeDonnees.CreateCommand("SELECT 1");
            await commande.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception erreur) when (erreur is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL injoignable.", erreur);
        }
    }
}
