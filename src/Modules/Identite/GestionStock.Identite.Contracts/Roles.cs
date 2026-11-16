namespace GestionStock.Identite.Contracts;

/// <summary>Les cinq rôles de la V1 (dossier d'analyse, décision 30 et section 8.3).</summary>
public static class Roles
{
    public const string Administrateur = "Administrateur";
    public const string Gerant = "Gerant";
    public const string Comptable = "Comptable";
    public const string Caissier = "Caissier";
    public const string Magasinier = "Magasinier";

    /// <summary>Tous les rôles, dans l'ordre de la section 8.3.</summary>
    public static IReadOnlyList<string> Tous { get; } = [Administrateur, Gerant, Comptable, Caissier, Magasinier];
}
