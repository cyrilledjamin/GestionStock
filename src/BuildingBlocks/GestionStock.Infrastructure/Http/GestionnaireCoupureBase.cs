using System.Resources;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GestionStock.Infrastructure.Http;

/// <summary>
/// Coupure de la base (redémarrage de PostgreSQL, du conteneur ou du PC) : les connexions gardées dans le pool sont
/// mortes, et la première requête qui en reprend une échoue. Au lieu d'une erreur 500, la requête reçoit un 503 traduit
/// (code <c>Base.Indisponible</c>, en-tête Retry-After) et le pool est vidé : la requête suivante ouvre une connexion
/// neuve. L'échec survient avant toute validation de transaction, rien n'a donc été enregistré.
/// </summary>
internal sealed partial class GestionnaireCoupureBase(ILogger<GestionnaireCoupureBase> journal) : IExceptionHandler
{
    public const string Code = "Base.Indisponible";

    private static readonly ResourceManager _ressources =
        new("GestionStock.Infrastructure.Ressources.ErreursInfrastructure", typeof(GestionnaireCoupureBase).Assembly);

    public async ValueTask<bool> TryHandleAsync(HttpContext contexte, Exception exception, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(contexte);
        if (!EstCoupure(exception))
        {
            return false;
        }

        NpgsqlConnection.ClearAllPools();
        JournaliserCoupure(journal, exception.GetBaseException().Message);
        contexte.Response.Headers.RetryAfter = "1";
        await ReponsesErreur.Probleme(StatusCodes.Status503ServiceUnavailable, Code, _ressources).ExecuteAsync(contexte);
        return true;
    }

    /// <summary>
    /// Connexion rompue ou serveur arrêté : erreur réseau Npgsql transitoire, ou code PostgreSQL de la classe 57P
    /// (arrêt de l'administrateur, du serveur) ou 08 (connexion).
    /// </summary>
    internal static bool EstCoupure(Exception exception)
    {
        for (var erreur = exception; erreur is not null; erreur = erreur.InnerException)
        {
            if (erreur is PostgresException { SqlState: var etat } && (etat.StartsWith("57P", StringComparison.Ordinal) || etat.StartsWith("08", StringComparison.Ordinal)))
            {
                return true;
            }

            if (erreur is NpgsqlException { IsTransient: true } and not PostgresException)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Base de données interrompue, connexions du pool fermées : {Cause}")]
    private static partial void JournaliserCoupure(ILogger journal, string cause);
}
