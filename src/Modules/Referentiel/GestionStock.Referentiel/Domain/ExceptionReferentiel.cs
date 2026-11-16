namespace GestionStock.Referentiel.Domain;

/// <summary>Règle de saisie du référentiel violée ; le code est la clé des ressources fr / en.</summary>
internal sealed class ExceptionReferentiel(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Codes d'erreur du module Referentiel.</summary>
internal static class CodesErreurReferentiel
{
    public const string CodeObligatoire = "Referentiel.CodeObligatoire";
    public const string CodeTropLong = "Referentiel.CodeTropLong";
    public const string CodeDejaUtilise = "Referentiel.CodeDejaUtilise";
    public const string LibelleObligatoire = "Referentiel.LibelleObligatoire";
    public const string LibelleTropLong = "Referentiel.LibelleTropLong";
    public const string UniteObligatoire = "Referentiel.UniteObligatoire";
    public const string UniteTropLongue = "Referentiel.UniteTropLongue";
    public const string PrecisionInvalide = "Referentiel.PrecisionInvalide";
    public const string ArticleIntrouvable = "Referentiel.ArticleIntrouvable";
}
