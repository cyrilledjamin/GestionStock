namespace GestionStock.Infrastructure.Stockage;

/// <summary>Configuration du stockage de fichiers (section <c>Stockage</c>).</summary>
public sealed class OptionsStockage
{
    public const string Section = "Stockage";

    /// <summary>Répertoire racine des fichiers ; obligatoire, jamais en dur dans le code.</summary>
    public string RepertoireRacine { get; set; } = string.Empty;
}
