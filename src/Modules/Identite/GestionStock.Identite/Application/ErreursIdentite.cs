using System.Resources;

namespace GestionStock.Identite.Application;

/// <summary>Codes d'erreur du module Identite, clés des ressources fr / en.</summary>
internal static class CodesErreurIdentite
{
    public const string IdentifiantsInvalides = "Identite.IdentifiantsInvalides";
    public const string CompteVerrouille = "Identite.CompteVerrouille";
    public const string SessionExpiree = "Identite.SessionExpiree";
    public const string IdentifiantObligatoire = "Identite.IdentifiantObligatoire";
    public const string IdentifiantDejaUtilise = "Identite.IdentifiantDejaUtilise";
    public const string RoleInconnu = "Identite.RoleInconnu";
    public const string MotDePasseRefuse = "Identite.MotDePasseRefuse";
    public const string CompteIntrouvable = "Identite.CompteIntrouvable";
    public const string CompteDesactive = "Identite.CompteDesactive";
    public const string MatriculeInvalide = "Identite.MatriculeInvalide";
    public const string NomObligatoire = "Identite.NomObligatoire";
    public const string NomTropLong = "Identite.NomTropLong";
    public const string TelephoneInvalide = "Identite.TelephoneInvalide";
    public const string RoleObligatoire = "Identite.RoleObligatoire";
    public const string LangueInconnue = "Identite.LangueInconnue";
    public const string DernierAdministrateur = "Identite.DernierAdministrateur";
    public const string MotDePasseActuelInvalide = "Identite.MotDePasseActuelInvalide";
    public const string MotDePasseIdentique = "Identite.MotDePasseIdentique";

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Identite.Ressources.ErreursIdentite", typeof(CodesErreurIdentite).Assembly);
}

/// <summary>Règle du module Identite violée ; le code est traduit par les endpoints.</summary>
internal sealed class ExceptionIdentite(string code, string? detail = null) : Exception(code)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}
