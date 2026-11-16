namespace GestionStock.Credit.Contracts;

/// <summary>
/// Crédit client vu par la Caisse (RG39 à RG42). Chaque opération participe à l'unité de travail ouverte par l'appelant :
/// un ticket à crédit et sa dette sont écrits ensemble, ou pas du tout.
/// </summary>
public interface IServiceCredit
{
    /// <summary>Client et sa situation ; null s'il n'existe pas.</summary>
    Task<ClientCredit?> ObtenirClientAsync(Guid clientId, CancellationToken annulation = default);

    /// <summary>
    /// Enregistre la part à crédit d'un ticket (RG39, RG41). Au-delà du plafond, la vente est refusée sauf si
    /// <see cref="VenteACredit.ValideePar"/> indique le responsable qui l'a validée (RG40).
    /// </summary>
    /// <exception cref="ExceptionCredit">Client inconnu ou inactif, montant invalide, plafond dépassé sans validation.</exception>
    Task EnregistrerVenteAsync(VenteACredit vente, CancellationToken annulation = default);

    /// <summary>Remboursement, imputé sur les ventes les plus anciennes (RG42) ; il ne peut dépasser la dette.</summary>
    /// <exception cref="ExceptionCredit">Client inconnu, montant invalide ou supérieur à la dette.</exception>
    Task EnregistrerRemboursementAsync(RemboursementCredit remboursement, CancellationToken annulation = default);

    /// <summary>
    /// Annule tout ou partie de la dette d'un ticket à crédit (avoir), dans la limite de ce qui reste dû sur ce ticket ;
    /// renvoie le montant effectivement annulé.
    /// </summary>
    Task<decimal> AnnulerVenteAsync(AnnulationCredit annulation, CancellationToken jeton = default);

    /// <summary>Reste dû sur la vente à crédit d'un ticket (0 si le ticket n'en a pas ou est soldé).</summary>
    Task<decimal> ResteDuSurTicketAsync(Guid ticketId, CancellationToken annulation = default);
}

/// <summary>Situation d'un client (données personnelles limitées au nom et au téléphone, loi n° 2024/017).</summary>
public sealed record ClientCredit(Guid Id, string Nom, string Telephone, decimal PlafondCredit, decimal SoldeDu, bool Actif)
{
    /// <summary>Montant encore autorisé avant d'atteindre le plafond.</summary>
    public decimal CreditDisponible => Math.Max(0m, PlafondCredit - SoldeDu);
}

public sealed record VenteACredit(Guid ClientId, Guid TicketId, string NumeroTicket, decimal Montant, string Utilisateur, string? ValideePar);

public sealed record RemboursementCredit(Guid ClientId, decimal Montant, Guid DocumentId, string Utilisateur);

public sealed record AnnulationCredit(Guid ClientId, Guid TicketVenteId, Guid TicketAvoirId, decimal Montant, string Utilisateur);

/// <summary>Règle du crédit violée ; le <see cref="Code"/> (ex. « Credit.PlafondDepasse ») est la clé de traduction.</summary>
public sealed class ExceptionCredit : Exception
{
    public ExceptionCredit(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ExceptionCredit()
        : this("Credit.Inconnu", "Erreur du module Credit.")
    {
    }

    public ExceptionCredit(string message)
        : this("Credit.Inconnu", message)
    {
    }

    public ExceptionCredit(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "Credit.Inconnu";
    }

    public string Code { get; }
}

/// <summary>Codes d'erreur du crédit, partagés avec la Caisse qui les traduit.</summary>
public static class CodesErreurCredit
{
    public const string Inconnu = "Credit.Inconnu";
    public const string ClientIntrouvable = "Credit.ClientIntrouvable";
    public const string ClientInactif = "Credit.ClientInactif";
    public const string NomObligatoire = "Credit.NomObligatoire";
    public const string TelephoneObligatoire = "Credit.TelephoneObligatoire";
    public const string TexteTropLong = "Credit.TexteTropLong";
    public const string MontantInvalide = "Credit.MontantInvalide";
    public const string PlafondInvalide = "Credit.PlafondInvalide";
    public const string DelaiInvalide = "Credit.DelaiInvalide";
    public const string PlafondDepasse = "Credit.PlafondDepasse";
    public const string RemboursementSuperieurDette = "Credit.RemboursementSuperieurDette";
}
