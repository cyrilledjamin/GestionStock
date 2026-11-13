using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GestionStock.Api.Securite;

/// <summary>
/// Schéma d'authentification provisoire, en attendant le module Identite : il n'authentifie personne,
/// si bien que la politique par défaut répond 401 à toute requête non anonyme (au lieu d'une erreur 500).
/// </summary>
internal sealed class AuthentificationProvisoire(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory journalisation,
    UrlEncoder encodeur)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, journalisation, encodeur)
{
    public const string Schema = "Provisoire";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
