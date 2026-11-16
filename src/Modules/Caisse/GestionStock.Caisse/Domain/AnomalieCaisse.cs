namespace GestionStock.Caisse.Domain;

/// <summary>Écart découvert au rejeu d'une opération faite hors ligne (7.8).</summary>
internal enum TypeAnomalie
{
    /// <summary>Des numéros de ticket manquent entre le dernier reçu et celui du poste (RG34).</summary>
    NumeroManquant = 1,

    /// <summary>Le numéro du poste était déjà pris : le ticket a reçu le numéro libre suivant.</summary>
    NumeroReattribue = 2,

    /// <summary>Le prix du poste diffère de celui en vigueur à la date du ticket : le prix du poste fait foi.</summary>
    PrixDifferent = 3,

    /// <summary>La vente à crédit dépasse le plafond du client, connu seulement à la synchronisation.</summary>
    PlafondDepasse = 4,

    /// <summary>L'opération arrive pour une session déjà clôturée (par exemple par le Gérant, décision 36).</summary>
    SessionCloturee = 5,

    /// <summary>L'heure du poste était hors de la session ou dans le futur : elle a été ramenée dans ses bornes.</summary>
    DateCorrigee = 6,
}

/// <summary>
/// Anomalie de synchronisation (7.8) : le serveur enregistre l'opération qui a eu lieu sur le terrain et signale
/// l'écart, sans jamais la rejeter. Le Gérant la traite (commentaire, auteur, horodatage, ENF03) ; elle n'est jamais
/// supprimée.
/// </summary>
internal sealed class AnomalieCaisse
{
    public const int LongueurMaxDetail = 500;
    public const int LongueurMaxCommentaire = 500;

    private AnomalieCaisse()
    {
    }

    public Guid Id { get; private set; }

    public TypeAnomalie Type { get; private set; }

    public Guid SessionId { get; private set; }

    public string NumeroCaisse { get; private set; } = "";

    public string Caissier { get; private set; } = "";

    /// <summary>Ticket ou remboursement concerné.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Numéro imprimé du ticket, s'il y en a un.</summary>
    public string? NumeroDocument { get; private set; }

    /// <summary>Valeurs en cause (numéros, prix, montants), sans phrase : l'interface traduit le type.</summary>
    public string Detail { get; private set; } = "";

    public DateTime DateUtc { get; private set; }

    public bool Traitee { get; private set; }

    public string? TraiteePar { get; private set; }

    public DateTime? TraitementUtc { get; private set; }

    public string? Commentaire { get; private set; }

    public static AnomalieCaisse Signaler(TypeAnomalie type, SessionCaisse session, Guid documentId, string? numeroDocument, string detail, DateTime maintenantUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new AnomalieCaisse
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            SessionId = session.Id,
            NumeroCaisse = session.NumeroCaisse,
            Caissier = session.Caissier,
            DocumentId = documentId,
            NumeroDocument = numeroDocument,
            Detail = detail.Length > LongueurMaxDetail ? detail[..LongueurMaxDetail] : detail,
            DateUtc = maintenantUtc,
        };
    }

    /// <summary>Traitement par le Gérant : un commentaire est obligatoire, une anomalie se traite une fois.</summary>
    public void Traiter(string responsable, string? commentaire, DateTime maintenantUtc)
    {
        var nettoye = commentaire?.Trim() ?? "";
        if (nettoye.Length == 0 || nettoye.Length > LongueurMaxCommentaire)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.CommentaireObligatoire);
        }

        if (Traitee)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.AnomalieDejaTraitee);
        }

        Traitee = true;
        TraiteePar = responsable;
        TraitementUtc = maintenantUtc;
        Commentaire = nettoye;
    }
}

/// <summary>Anomalie relevée pendant la préparation d'une opération, enregistrée avec elle dans la même transaction.</summary>
internal sealed record Signalement(TypeAnomalie Type, string Detail);
