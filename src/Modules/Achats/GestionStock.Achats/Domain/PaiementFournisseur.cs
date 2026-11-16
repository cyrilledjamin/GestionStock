namespace GestionStock.Achats.Domain;

/// <summary>Modes de paiement d'un fournisseur (non listés par le dossier : choix validé pour la V1).</summary>
internal enum ModePaiementFournisseur
{
    Especes = 1,
    MobileMoney = 2,
    Virement = 3,
    Cheque = 4,
}

/// <summary>Statut de la dette d'une réception validée, déduit de ses paiements.</summary>
internal enum StatutDette
{
    NonPayee = 1,
    PartiellementPayee = 2,
    Payee = 3,
}

/// <summary>
/// Paiement d'une réception au fournisseur (décision 27 : montant dû par réception, paiements partiels). Jamais modifié
/// ni supprimé : une erreur se corrige par une annulation, paiement de montant opposé qui le référence et porte le motif.
/// </summary>
internal sealed class PaiementFournisseur
{
    public const int LongueurMaxReference = 50;
    public const int LongueurMaxMotif = 200;

    private PaiementFournisseur()
    {
    }

    public Guid Id { get; private set; }

    public Guid ReceptionId { get; private set; }

    public Guid FournisseurId { get; private set; }

    public DateTime DateUtc { get; private set; }

    /// <summary>Positif pour un paiement, négatif pour une annulation.</summary>
    public decimal Montant { get; private set; }

    public ModePaiementFournisseur Mode { get; private set; }

    /// <summary>Référence de la transaction, du virement ou numéro du chèque ; facultative en espèces.</summary>
    public string? Reference { get; private set; }

    /// <summary>Annulation : identifiant du paiement annulé.</summary>
    public Guid? PaiementAnnuleId { get; private set; }

    /// <summary>Annulation : motif obligatoire, journalisé avec l'utilisateur et l'horodatage.</summary>
    public string? Motif { get; private set; }

    public string SaisiPar { get; private set; } = "";

    public static PaiementFournisseur Payer(
        Reception reception,
        decimal resteDu,
        decimal montant,
        ModePaiementFournisseur mode,
        string? reference,
        string utilisateur,
        DateTime maintenantUtc)
    {
        ArgumentNullException.ThrowIfNull(reception);
        if (reception.Statut != StatutReception.Validee)
        {
            throw new ExceptionAchats(CodesErreurAchats.PaiementSurBrouillon);
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ExceptionAchats(CodesErreurAchats.ModePaiementInvalide);
        }

        if (montant <= 0m || decimal.Round(montant, Reception.DecimalesCout) != montant)
        {
            throw new ExceptionAchats(CodesErreurAchats.MontantPaiementInvalide);
        }

        if (montant > resteDu)
        {
            throw new ExceptionAchats(CodesErreurAchats.MontantSuperieurAuReste, decimal.Round(resteDu, 0, MidpointRounding.AwayFromZero).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        }

        var texte = reference?.Trim();
        if (string.IsNullOrEmpty(texte) && mode != ModePaiementFournisseur.Especes)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReferenceObligatoire);
        }

        if (texte?.Length > LongueurMaxReference)
        {
            throw new ExceptionAchats(CodesErreurAchats.ReferenceTropLongue);
        }

        return new PaiementFournisseur
        {
            Id = Guid.CreateVersion7(),
            ReceptionId = reception.Id,
            FournisseurId = reception.FournisseurId,
            DateUtc = maintenantUtc,
            Montant = montant,
            Mode = mode,
            Reference = string.IsNullOrEmpty(texte) ? null : texte,
            SaisiPar = utilisateur,
        };
    }

    /// <summary>Annule ce paiement par un paiement opposé ; un paiement ne s'annule qu'une fois.</summary>
    public PaiementFournisseur Annuler(bool dejaAnnule, string? motif, string utilisateur, DateTime maintenantUtc)
    {
        if (PaiementAnnuleId is not null || dejaAnnule)
        {
            throw new ExceptionAchats(CodesErreurAchats.PaiementDejaAnnule);
        }

        var texte = motif?.Trim();
        if (string.IsNullOrEmpty(texte))
        {
            throw new ExceptionAchats(CodesErreurAchats.MotifAnnulationObligatoire);
        }

        if (texte.Length > LongueurMaxMotif)
        {
            throw new ExceptionAchats(CodesErreurAchats.MotifAnnulationTropLong);
        }

        return new PaiementFournisseur
        {
            Id = Guid.CreateVersion7(),
            ReceptionId = ReceptionId,
            FournisseurId = FournisseurId,
            DateUtc = maintenantUtc,
            Montant = -Montant,
            Mode = Mode,
            Reference = Reference,
            PaiementAnnuleId = Id,
            Motif = texte,
            SaisiPar = utilisateur,
        };
    }

    /// <summary>Statut d'une dette d'après le montant dû et le total payé.</summary>
    public static StatutDette Statut(decimal montantDu, decimal paye) =>
        paye >= montantDu ? StatutDette.Payee : paye <= 0m ? StatutDette.NonPayee : StatutDette.PartiellementPayee;
}
