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

    public static ResourceManager Ressources { get; } =
        new("GestionStock.Identite.Ressources.ErreursIdentite", typeof(CodesErreurIdentite).Assembly);
}

/// <summary>Règle du module Identite violée ; le code est traduit par les endpoints.</summary>
internal sealed class ExceptionIdentite(string code, string? detail = null) : Exception(code)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}
