namespace GestionStock.Achats.Contracts;

/// <summary>
/// Types de documents du module Achats, tels qu'ils apparaissent dans le journal des mouvements de stock
/// (<c>TypeDocument</c> + <c>DocumentId</c>, décision 28).
/// </summary>
/// <remarks>
/// Valeurs <c>static readonly</c> et non <c>const</c> : une constante publique est recopiée dans les assemblages
/// qui l'utilisent, qui ne référenceraient alors plus ce contrat (règle vérifiée par les tests d'architecture).
/// </remarks>
public static class TypesDocumentsAchats
{
    /// <summary>Réception fournisseur : <c>DocumentId</c> est l'identifiant de la réception.</summary>
    public static readonly string Reception = "Reception";
}
