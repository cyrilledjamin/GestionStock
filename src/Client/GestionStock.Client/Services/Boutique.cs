using System.Net.Http.Json;

namespace GestionStock.Client.Services;

/// <summary>
/// Identité de la boutique (configuration <c>ENTREPRISE_NOM</c>, <c>ENTREPRISE_LOGO</c>), lue une fois au démarrage,
/// sans connexion : elle s'affiche aussi sur l'écran de connexion. Sans réseau, le nom du logiciel reste affiché.
/// </summary>
public sealed class Boutique(HttpClient http)
{
    public const string Logiciel = "GestionStock";

    public string Nom { get; private set; } = Logiciel;

    /// <summary>Un logo est configuré : l'image est servie par <see cref="AdresseLogo"/>.</summary>
    public bool AUnLogo { get; private set; }

    public static string AdresseLogo => "api/caisse/boutique/logo";

    /// <summary>Le nom de la boutique diffère de celui du logiciel : on mentionne alors « GestionStock » en petit.</summary>
    public bool EstPersonnalisee => !string.Equals(Nom, Logiciel, StringComparison.Ordinal);

    public async Task ChargerAsync()
    {
        try
        {
            if (await http.GetFromJsonAsync<IdentiteBoutique>("api/caisse/boutique") is { } identite && !string.IsNullOrWhiteSpace(identite.Nom))
            {
                Nom = identite.Nom;
                AUnLogo = identite.Logo;
            }
        }
        catch (HttpRequestException)
        {
            // Hors ligne au démarrage : nom par défaut.
        }
    }

    private sealed record IdentiteBoutique(string Nom, bool Logo);
}
