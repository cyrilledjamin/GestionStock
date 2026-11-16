using System.Data.Common;
using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionStock.Identite.Application;

/// <summary>
/// Premier compte Administrateur, lu dans la configuration (variables
/// <c>Identite__AdministrateurInitial__Identifiant</c> et <c>__MotDePasse</c>).
/// </summary>
internal sealed class OptionsAdministrateurInitial
{
    public const string Section = "Identite:AdministrateurInitial";

    public string? Identifiant { get; set; }

    public string? MotDePasse { get; set; }
}

/// <summary>
/// Au démarrage, crée l'Administrateur initial si la base ne contient encore aucun compte.
/// Sans configuration, ou une fois un compte créé, ne fait rien.
/// </summary>
internal sealed partial class InitialisationAdministrateur(
    IServiceScopeFactory portees,
    IOptions<OptionsAdministrateurInitial> options,
    ILogger<InitialisationAdministrateur> journal) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.Identifiant) || string.IsNullOrWhiteSpace(configuration.MotDePasse))
        {
            JournalNonConfigure(journal);
            return;
        }

        await using var portee = portees.CreateAsyncScope();
        try
        {
            var contexte = portee.ServiceProvider.GetRequiredService<IdentiteDbContext>();
            if (await contexte.Users.AnyAsync(cancellationToken))
            {
                return;
            }
        }
        catch (DbException erreur)
        {
            // Base absente ou non migrée : l'API démarre quand même, l'erreur est visible dans les journaux.
            JournalBaseIndisponible(journal, erreur);
            return;
        }

        var service = portee.ServiceProvider.GetRequiredService<ServiceUtilisateurs>();
        await service.CreerAsync(configuration.Identifiant, configuration.MotDePasse, Roles.Administrateur, cancellationToken);
        JournalCree(journal, configuration.Identifiant);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrateur initial non configuré : aucun compte créé.")]
    private static partial void JournalNonConfigure(ILogger journal);

    [LoggerMessage(Level = LogLevel.Error, Message = "Administrateur initial : base de données indisponible.")]
    private static partial void JournalBaseIndisponible(ILogger journal, Exception erreur);

    [LoggerMessage(Level = LogLevel.Information, Message = "Administrateur initial « {Identifiant} » créé.")]
    private static partial void JournalCree(ILogger journal, string identifiant);
}
