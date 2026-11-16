namespace GestionStock.Credit.Domain;

/// <summary>Nature d'une opération de crédit (section 4.5).</summary>
internal enum TypeOperationCredit
{
    VenteCredit = 1,
    Remboursement = 2,
    Annulation = 3,
}

/// <summary>
/// Opération de crédit immuable (journal du client). Une vente à crédit porte son échéance (RG43) ; ce qui en reste dû
/// se calcule à partir des imputations.
/// </summary>
internal sealed class OperationCredit
{
    private OperationCredit()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClientId { get; private set; }

    public DateTime DateUtc { get; private set; }

    public TypeOperationCredit Type { get; private set; }

    public decimal Montant { get; private set; }

    /// <summary>Ticket de vente (VenteCredit), ticket d'avoir (Annulation) ou ticket d'encaissement (Remboursement).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Numéro lisible du ticket de vente, pour la fiche client.</summary>
    public string? NumeroTicket { get; private set; }

    /// <summary>Date à partir de laquelle une vente à crédit non soldée est en retard (RG43).</summary>
    public DateTime? EcheanceUtc { get; private set; }

    public string Utilisateur { get; private set; } = "";

    /// <summary>Responsable ayant validé un dépassement de plafond (RG40).</summary>
    public string? ValideePar { get; private set; }

    public static OperationCredit Vente(Client client, decimal montant, Guid ticketId, string numeroTicket, string utilisateur, string? valideePar, DateTime maintenantUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ClientId = client.Id,
            DateUtc = maintenantUtc,
            Type = TypeOperationCredit.VenteCredit,
            Montant = montant,
            DocumentId = ticketId,
            NumeroTicket = numeroTicket,
            EcheanceUtc = maintenantUtc.AddDays(client.DelaiPaiementJours),
            Utilisateur = utilisateur,
            ValideePar = valideePar,
        };

    public static OperationCredit Diminution(TypeOperationCredit type, Guid clientId, decimal montant, Guid documentId, string utilisateur, DateTime maintenantUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            DateUtc = maintenantUtc,
            Type = type,
            Montant = montant,
            DocumentId = documentId,
            Utilisateur = utilisateur,
        };
}

/// <summary>Part d'un remboursement ou d'une annulation affectée à une vente à crédit (RG42 : les plus anciennes d'abord).</summary>
internal sealed class Imputation
{
    private Imputation()
    {
    }

    public Imputation(Guid operationId, Guid venteId, decimal montant)
    {
        Id = Guid.CreateVersion7();
        OperationId = operationId;
        VenteId = venteId;
        Montant = montant;
    }

    public Guid Id { get; private set; }

    public Guid OperationId { get; private set; }

    public Guid VenteId { get; private set; }

    public decimal Montant { get; private set; }
}
