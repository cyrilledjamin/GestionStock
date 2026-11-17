namespace GestionStock.E2E.Infrastructure;

/// <summary>
/// Application testée, donnée par l'environnement : E2E_URL (ex. http://localhost:8080), E2E_ADMIN et
/// E2E_MOT_DE_PASSE (Administrateur initial, ADMIN_IDENTIFIANT et ADMIN_MOT_DE_PASSE de deploy/.env).
/// Sans E2E_URL, les parcours sont ignorés.
/// </summary>
public static class Environnement
{
    public static string Url { get; } = (Environment.GetEnvironmentVariable("E2E_URL") ?? "").TrimEnd('/');

    public static string Administrateur { get; } = Environment.GetEnvironmentVariable("E2E_ADMIN") ?? "admin";

    public static string MotDePasseAdministrateur { get; } = Environment.GetEnvironmentVariable("E2E_MOT_DE_PASSE") ?? "";

    /// <summary>Navigateur visible (débogage local) : E2E_VISIBLE=1.</summary>
    public static bool Visible { get; } = Environment.GetEnvironmentVariable("E2E_VISIBLE") == "1";

    /// <summary>Lu par xUnit (SkipUnless) : les parcours ne tournent que contre une application déployée.</summary>
    public static bool Actif => Url.Length > 0;
}
