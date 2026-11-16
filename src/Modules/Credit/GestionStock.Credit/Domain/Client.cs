using GestionStock.Credit.Contracts;

namespace GestionStock.Credit.Domain;

/// <summary>
/// Client à crédit (UC26). Données personnelles limitées au nom et au téléphone (loi n° 2024/017), tous deux
/// obligatoires pour vendre à crédit (RG39). Le solde dû est une projection des opérations, mise à jour dans la même
/// transaction et recalculable. Jamais supprimé : désactivé.
/// </summary>
internal sealed class Client
{
    public const int LongueurMaxNom = 200;
    public const int LongueurMaxTelephone = 30;

    /// <summary>Délai de paiement par défaut, en jours (provisoire : non fixé par le dossier).</summary>
    public const int DelaiParDefautJours = 30;

    private Client()
    {
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = "";

    public string Telephone { get; private set; } = "";

    /// <summary>Plafond de crédit (RG40), fixé par le Comptable (8.3) ; 0 tant qu'il n'est pas défini (décision 31).</summary>
    public decimal PlafondCredit { get; private set; }

    /// <summary>Délai au-delà duquel une dette est en retard (RG43).</summary>
    public int DelaiPaiementJours { get; private set; }

    /// <summary>DLC résiduelle minimale exigée (RG11, décision 5) : prévue par le modèle, inutilisée en V1.</summary>
    public int DlcResiduelleMinJours { get; private set; }

    /// <summary>Somme due : ventes à crédit moins remboursements et annulations.</summary>
    public decimal SoldeDu { get; private set; }

    public bool Actif { get; private set; } = true;

    public static Client Creer(string nom, string telephone, int? delaiPaiementJours)
    {
        var client = new Client { Id = Guid.CreateVersion7() };
        client.Modifier(nom, telephone, delaiPaiementJours ?? DelaiParDefautJours, actif: true);
        return client;
    }

    public void Modifier(string nom, string telephone, int delaiPaiementJours, bool actif)
    {
        Nom = Exiger(nom, LongueurMaxNom, CodesErreurCredit.NomObligatoire);
        Telephone = Exiger(telephone, LongueurMaxTelephone, CodesErreurCredit.TelephoneObligatoire);
        if (delaiPaiementJours is < 0 or > 3650)
        {
            throw new ExceptionCredit(CodesErreurCredit.DelaiInvalide, "Délai de paiement invalide.");
        }

        DelaiPaiementJours = delaiPaiementJours;
        Actif = actif;
    }

    public void FixerPlafond(decimal plafond)
    {
        if (plafond < 0m || decimal.Round(plafond, 4) != plafond)
        {
            throw new ExceptionCredit(CodesErreurCredit.PlafondInvalide, "Plafond invalide.");
        }

        PlafondCredit = plafond;
    }

    /// <summary>
    /// Ajoute une dette. Au-delà du plafond, il faut la validation d'un responsable (RG40) ; elle est alors tracée
    /// sur l'opération.
    /// </summary>
    public void Devoir(decimal montant, bool valideParResponsable)
    {
        ExigerMontant(montant);
        if (!Actif)
        {
            throw new ExceptionCredit(CodesErreurCredit.ClientInactif, "Client désactivé.");
        }

        if (SoldeDu + montant > PlafondCredit && !valideParResponsable)
        {
            throw new ExceptionCredit(CodesErreurCredit.PlafondDepasse, $"Plafond de {PlafondCredit} dépassé.");
        }

        SoldeDu += montant;
    }

    /// <summary>Diminue la dette (remboursement ou annulation) ; jamais en dessous de zéro.</summary>
    public void Diminuer(decimal montant)
    {
        ExigerMontant(montant);
        if (montant > SoldeDu)
        {
            throw new ExceptionCredit(CodesErreurCredit.RemboursementSuperieurDette, $"La dette n'est que de {SoldeDu}.");
        }

        SoldeDu -= montant;
    }

    private static void ExigerMontant(decimal montant)
    {
        if (montant <= 0m || decimal.Round(montant, 4) != montant)
        {
            throw new ExceptionCredit(CodesErreurCredit.MontantInvalide, "Montant invalide.");
        }
    }

    private static string Exiger(string? valeur, int longueurMax, string codeVide)
    {
        var nettoyee = valeur?.Trim() ?? "";
        if (nettoyee.Length == 0)
        {
            throw new ExceptionCredit(codeVide, "Valeur obligatoire.");
        }

        return nettoyee.Length <= longueurMax ? nettoyee : throw new ExceptionCredit(CodesErreurCredit.TexteTropLong, "Valeur trop longue.");
    }
}
