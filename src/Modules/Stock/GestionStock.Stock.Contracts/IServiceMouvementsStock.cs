namespace GestionStock.Stock.Contracts;

/// <summary>
/// Seul point d'écriture du stock (RG01) : les autres modules (Caisse, Achats, Inventaire…)
/// demandent un mouvement, le module Stock l'écrit avec ses soldes dans la même transaction.
/// S'il est appelé dans une unité de travail ouverte, il y participe (vente = ticket + stock).
/// </summary>
public interface IServiceMouvementsStock
{
    /// <summary>
    /// Enregistre un mouvement et met à jour les soldes. Un renvoi avec le même
    /// <see cref="DemandeMouvement.IdIdempotence"/> n'écrit rien et renvoie le mouvement existant.
    /// </summary>
    /// <exception cref="ExceptionStock">Si une règle de gestion est violée.</exception>
    Task<ResultatMouvement> EnregistrerAsync(DemandeMouvement demande, CancellationToken annulation = default);
}

/// <summary>Demande de mouvement de stock.</summary>
/// <param name="Type">Nature du mouvement.</param>
/// <param name="ArticleId">Article, en unité de stock.</param>
/// <param name="SiteId">Site concerné.</param>
/// <param name="Source">Emplacement de sortie, absent pour une entrée.</param>
/// <param name="Destination">Emplacement d'entrée, absent pour une sortie.</param>
/// <param name="Quantite">Quantité strictement positive, en unité de stock (RG44).</param>
/// <param name="CoutUnitaire">Coût unitaire hors TVA récupérable, frais d'approche inclus (RG30).</param>
/// <param name="TypeDocument">Type du document d'origine (ex. « TicketCaisse »).</param>
/// <param name="DocumentId">Identifiant du document d'origine.</param>
/// <param name="Utilisateur">Auteur de l'opération (audit).</param>
/// <param name="IdIdempotence">Identifiant généré par le client, unique par mouvement.</param>
/// <param name="DateUtc">Date de l'opération sur le terrain (hors ligne) ; à défaut, l'heure du serveur.</param>
public sealed record DemandeMouvement(
    TypeMouvementStock Type,
    Guid ArticleId,
    Guid SiteId,
    EmplacementMouvement? Source,
    EmplacementMouvement? Destination,
    decimal Quantite,
    decimal CoutUnitaire,
    string TypeDocument,
    Guid DocumentId,
    string Utilisateur,
    Guid IdIdempotence,
    DateTime? DateUtc = null);

/// <summary>Emplacement touché par un mouvement, avec son type (fourni par le Référentiel).</summary>
public sealed record EmplacementMouvement(Guid EmplacementId, TypeEmplacementStock Type);

/// <summary>Résultat de l'enregistrement.</summary>
/// <param name="MouvementId">Identifiant du mouvement dans le journal.</param>
/// <param name="DejaEnregistre">Vrai si la demande avait déjà été intégrée (renvoi après coupure).</param>
public sealed record ResultatMouvement(long MouvementId, bool DejaEnregistre);
