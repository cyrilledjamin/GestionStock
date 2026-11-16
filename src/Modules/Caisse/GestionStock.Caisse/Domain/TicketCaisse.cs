namespace GestionStock.Caisse.Domain;

/// <summary>Modes de paiement (RG35, section 4.5).</summary>
internal enum ModePaiement
{
    Especes = 1,
    MobileMoney = 2,
    Carte = 3,
    Credit = 4,
}

/// <summary>Ligne préparée par l'application : article, conditionnement, prix lu dans le module Prix.</summary>
internal sealed record LignePreparee(
    Guid ConditionnementId,
    Guid ArticleId,
    string Libelle,
    string LibelleConditionnement,
    string? CodeScanne,
    decimal Quantite,
    decimal QuantiteUniteStock,
    decimal PrixUnitaire,
    decimal Remise,
    Guid? LigneOrigineId = null);

/// <summary>Paiement saisi : pour les espèces, le montant remis par le client (le rendu en est déduit).</summary>
internal sealed record PaiementSaisi(ModePaiement Mode, decimal Montant, string? Reference);

/// <summary>
/// Ticket de caisse (UC17) ou avoir (UC18). Numéroté en continu par poste, jamais modifié ni supprimé : une erreur se
/// corrige par un avoir (RG34). Les montants sont en XAF entiers (pas de subdivision en usage, 1.5) : chaque ligne
/// est arrondie à l'unité, remises et paiements sont entiers.
/// </summary>
internal sealed class TicketCaisse
{
    private TicketCaisse()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Identifiant généré par le poste : un renvoi après coupure n'est intégré qu'une fois.</summary>
    public Guid IdIdempotence { get; private set; }

    public Guid SessionId { get; private set; }

    public string NumeroCaisse { get; private set; } = "";

    /// <summary>Rang dans la numérotation continue du poste (RG34).</summary>
    public long Numero { get; private set; }

    /// <summary>Numéro imprimé, ex. CAISSE-1-000042.</summary>
    public string NumeroComplet { get; private set; } = "";

    public DateTime DateUtc { get; private set; }

    public string Caissier { get; private set; } = "";

    public bool EstAvoir { get; private set; }

    /// <summary>Ticket corrigé par cet avoir.</summary>
    public Guid? TicketOrigineId { get; private set; }

    public Guid? ClientId { get; private set; }

    public decimal TotalTtc { get; private set; }

    public decimal TotalHt { get; private set; }

    public decimal TotalTva { get; private set; }

    public decimal TauxTva { get; private set; }

    /// <summary>Monnaie rendue sur les espèces.</summary>
    public decimal Rendu { get; private set; }

    /// <summary>Responsable ayant validé les remises (décision 31), tracé.</summary>
    public string? RemiseValideePar { get; private set; }

    /// <summary>Responsable ayant validé un dépassement de plafond de crédit (RG40), tracé.</summary>
    public string? CreditValidePar { get; private set; }

    public List<LigneTicket> Lignes { get; private set; } = [];

    public List<Paiement> Paiements { get; private set; } = [];

    public decimal MontantCredit => Paiements.Where(p => p.Mode == ModePaiement.Credit).Sum(p => p.Montant);

    /// <summary>Espèces restées dans le tiroir : remises moins rendu pour une vente, sorties pour un avoir.</summary>
    public decimal EspecesNettes
    {
        get
        {
            var especes = Paiements.Where(p => p.Mode == ModePaiement.Especes).Sum(p => p.Montant) - Rendu;
            return EstAvoir ? -especes : especes;
        }
    }

    /// <summary>Vente : contrôle des lignes et des paiements (RG35, RG39), calcul des totaux et du rendu.</summary>
    public static TicketCaisse Vente(
        Guid idIdempotence,
        SessionCaisse session,
        long numero,
        DateTime maintenantUtc,
        IReadOnlyList<LignePreparee> lignes,
        IReadOnlyList<PaiementSaisi> paiements,
        Guid? clientId,
        decimal tauxTva,
        string? remiseValideePar,
        string? creditValidePar)
    {
        var ticket = Creer(idIdempotence, session, numero, maintenantUtc, estAvoir: false, ticketOrigineId: null, clientId, lignes, tauxTva);
        if (ticket.Lignes.Any(l => l.Remise > 0m) && remiseValideePar is null)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.RemiseNonValidee);
        }

        ticket.RemiseValideePar = ticket.Lignes.Any(l => l.Remise > 0m) ? remiseValideePar : null;
        ticket.AjouterPaiements(paiements, autoriserCredit: true);
        ticket.CreditValidePar = ticket.MontantCredit > 0m ? creditValidePar : null;
        return ticket;
    }

    /// <summary>Avoir : lignes reprises du ticket d'origine, remboursement (annulation de dette, espèces, Mobile Money, carte).</summary>
    public static TicketCaisse Avoir(
        Guid idIdempotence,
        SessionCaisse session,
        long numero,
        DateTime maintenantUtc,
        TicketCaisse origine,
        IReadOnlyList<LignePreparee> lignes,
        decimal annulationCredit,
        PaiementSaisi? remboursement)
    {
        ArgumentNullException.ThrowIfNull(origine);
        if (origine.EstAvoir)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.AvoirSurAvoir);
        }

        var ticket = Creer(idIdempotence, session, numero, maintenantUtc, estAvoir: true, origine.Id, origine.ClientId, lignes, origine.TauxTva);
        var paiements = new List<PaiementSaisi>();
        if (annulationCredit > 0m)
        {
            paiements.Add(new PaiementSaisi(ModePaiement.Credit, annulationCredit, null));
        }

        var reste = ticket.TotalTtc - annulationCredit;
        if (reste > 0m)
        {
            var mode = remboursement?.Mode ?? ModePaiement.Especes;
            if (mode == ModePaiement.Credit)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ModeInvalide);
            }

            paiements.Add(new PaiementSaisi(mode, reste, remboursement?.Reference));
        }

        ticket.AjouterPaiements(paiements, autoriserCredit: true);
        return ticket;
    }

    private static TicketCaisse Creer(
        Guid idIdempotence,
        SessionCaisse session,
        long numero,
        DateTime maintenantUtc,
        bool estAvoir,
        Guid? ticketOrigineId,
        Guid? clientId,
        IReadOnlyList<LignePreparee> lignes,
        decimal tauxTva)
    {
        if (idIdempotence == Guid.Empty)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.IdempotenceObligatoire);
        }

        if (lignes.Count == 0)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.TicketSansLigne);
        }

        var ticket = new TicketCaisse
        {
            Id = Guid.CreateVersion7(),
            IdIdempotence = idIdempotence,
            SessionId = session.Id,
            NumeroCaisse = session.NumeroCaisse,
            Numero = numero,
            NumeroComplet = $"{session.NumeroCaisse}-{numero:D6}",
            DateUtc = maintenantUtc,
            Caissier = session.Caissier,
            EstAvoir = estAvoir,
            TicketOrigineId = ticketOrigineId,
            ClientId = clientId,
            TauxTva = tauxTva,
        };
        ticket.Lignes.AddRange(lignes.Select((l, index) => LigneTicket.Creer(ticket.Id, index + 1, l, tauxTva)));
        ticket.TotalTtc = ticket.Lignes.Sum(l => l.MontantTtc);
        ticket.TotalHt = decimal.Round(ticket.TotalTtc / (1m + (tauxTva / 100m)), 4, MidpointRounding.AwayFromZero);
        ticket.TotalTva = ticket.TotalTtc - ticket.TotalHt;
        return ticket;
    }

    private void AjouterPaiements(IReadOnlyList<PaiementSaisi> paiements, bool autoriserCredit)
    {
        if (paiements.Count == 0 && TotalTtc > 0m)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.PaiementManquant);
        }

        foreach (var saisi in paiements)
        {
            if (!Enum.IsDefined(saisi.Mode) || (saisi.Mode == ModePaiement.Credit && !autoriserCredit))
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ModeInvalide);
            }

            if (saisi.Montant <= 0m || decimal.Round(saisi.Montant, 0) != saisi.Montant)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.MontantNonEntier);
            }

            // RG35 : la référence de transaction est obligatoire en Mobile Money.
            if (saisi.Mode == ModePaiement.MobileMoney && string.IsNullOrWhiteSpace(saisi.Reference))
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ReferenceObligatoire);
            }

            // RG39 : le crédit exige un client identifié.
            if (saisi.Mode == ModePaiement.Credit && ClientId is null)
            {
                throw new ExceptionCaisse(CodesErreurCaisse.ClientObligatoire);
            }

            Paiements.Add(new Paiement(Id, saisi.Mode, saisi.Montant, saisi.Reference?.Trim()));
        }

        var total = Paiements.Sum(p => p.Montant);
        if (total < TotalTtc)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.PaiementInsuffisant, $"{TotalTtc - total}");
        }

        // Seules les espèces se rendent : carte, Mobile Money et crédit ne dépassent pas le total.
        Rendu = total - TotalTtc;
        if (Rendu > Paiements.Where(p => p.Mode == ModePaiement.Especes).Sum(p => p.Montant))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.RenduSansEspeces);
        }
    }
}

/// <summary>Ligne de ticket : quantité dans le conditionnement vendu, prix et remise figés au moment de la vente.</summary>
internal sealed class LigneTicket
{
    private LigneTicket()
    {
    }

    public Guid Id { get; private set; }

    public Guid TicketId { get; private set; }

    public int Ordre { get; private set; }

    public Guid ConditionnementId { get; private set; }

    public Guid ArticleId { get; private set; }

    public string Libelle { get; private set; } = "";

    public string LibelleConditionnement { get; private set; } = "";

    public string? CodeScanne { get; private set; }

    /// <summary>Quantité de conditionnements vendus (ou rendus pour un avoir).</summary>
    public decimal Quantite { get; private set; }

    /// <summary>Unités de stock par conditionnement, figées au moment de la vente (RG18).</summary>
    public decimal QuantiteUniteStock { get; private set; }

    public decimal PrixUnitaire { get; private set; }

    public decimal Remise { get; private set; }

    public decimal TauxTva { get; private set; }

    /// <summary>Quantité × prix arrondi à l'unité (RG32), moins la remise.</summary>
    public decimal MontantTtc { get; private set; }

    /// <summary>Lot affecté par défaut faute de code 2D (RG24) : prévu par le modèle, sans lots en V1.</summary>
    public bool LotPresume { get; private set; }

    /// <summary>Ligne vendue que reprend cette ligne d'avoir.</summary>
    public Guid? LigneOrigineId { get; private set; }

    /// <summary>Quantité en unités de stock : celle des mouvements de stock.</summary>
    public decimal QuantiteStock => Quantite * QuantiteUniteStock;

    public static LigneTicket Creer(Guid ticketId, int ordre, LignePreparee ligne, decimal tauxTva)
    {
        ArgumentNullException.ThrowIfNull(ligne);
        if (ligne.Quantite <= 0m)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.QuantiteInvalide, ligne.Libelle);
        }

        var brut = decimal.Round(ligne.Quantite * ligne.PrixUnitaire, 0, MidpointRounding.AwayFromZero);
        if (ligne.Remise < 0m || ligne.Remise > brut || decimal.Round(ligne.Remise, 0) != ligne.Remise)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.RemiseInvalide, ligne.Libelle);
        }

        return new LigneTicket
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            Ordre = ordre,
            ConditionnementId = ligne.ConditionnementId,
            ArticleId = ligne.ArticleId,
            Libelle = ligne.Libelle,
            LibelleConditionnement = ligne.LibelleConditionnement,
            CodeScanne = ligne.CodeScanne,
            Quantite = ligne.Quantite,
            QuantiteUniteStock = ligne.QuantiteUniteStock,
            PrixUnitaire = ligne.PrixUnitaire,
            Remise = ligne.Remise,
            TauxTva = tauxTva,
            MontantTtc = brut - ligne.Remise,
            LigneOrigineId = ligne.LigneOrigineId,
        };
    }
}

/// <summary>Paiement d'un ticket (RG35) ; un ticket peut combiner plusieurs modes.</summary>
internal sealed class Paiement
{
    private Paiement()
    {
    }

    public Paiement(Guid ticketId, ModePaiement mode, decimal montant, string? reference)
    {
        Id = Guid.CreateVersion7();
        TicketId = ticketId;
        Mode = mode;
        Montant = montant;
        ReferenceTransaction = string.IsNullOrEmpty(reference) ? null : reference;
    }

    public Guid Id { get; private set; }

    public Guid TicketId { get; private set; }

    public ModePaiement Mode { get; private set; }

    public decimal Montant { get; private set; }

    /// <summary>Référence de la transaction Mobile Money (obligatoire) ou carte (facultative).</summary>
    public string? ReferenceTransaction { get; private set; }
}

/// <summary>Remboursement de crédit encaissé dans une session de caisse (RG42).</summary>
internal sealed class RemboursementCaisse
{
    private RemboursementCaisse()
    {
    }

    public Guid Id { get; private set; }

    public Guid IdIdempotence { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid ClientId { get; private set; }

    public ModePaiement Mode { get; private set; }

    public decimal Montant { get; private set; }

    public string? ReferenceTransaction { get; private set; }

    public DateTime DateUtc { get; private set; }

    public string Caissier { get; private set; } = "";

    public static RemboursementCaisse Encaisser(Guid idIdempotence, SessionCaisse session, Guid clientId, PaiementSaisi paiement, DateTime maintenantUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paiement);
        if (idIdempotence == Guid.Empty)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.IdempotenceObligatoire);
        }

        // RG42 : un remboursement s'encaisse en espèces ou en Mobile Money.
        if (paiement.Mode is not (ModePaiement.Especes or ModePaiement.MobileMoney))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.ModeInvalide);
        }

        if (paiement.Montant <= 0m || decimal.Round(paiement.Montant, 0) != paiement.Montant)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.MontantNonEntier);
        }

        if (paiement.Mode == ModePaiement.MobileMoney && string.IsNullOrWhiteSpace(paiement.Reference))
        {
            throw new ExceptionCaisse(CodesErreurCaisse.ReferenceObligatoire);
        }

        return new RemboursementCaisse
        {
            Id = Guid.CreateVersion7(),
            IdIdempotence = idIdempotence,
            SessionId = session.Id,
            ClientId = clientId,
            Mode = paiement.Mode,
            Montant = paiement.Montant,
            ReferenceTransaction = paiement.Reference?.Trim(),
            DateUtc = maintenantUtc,
            Caissier = session.Caissier,
        };
    }
}
