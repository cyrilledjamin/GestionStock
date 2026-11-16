namespace GestionStock.Caisse.Domain;

/// <summary>Cycle de vie d'une session de caisse (5.4). EnClotureAttente arrivera avec la caisse hors ligne.</summary>
internal enum StatutSession
{
    Ouverte = 1,
    Comptee = 2,
    Validee = 3,
}

/// <summary>
/// Session de caisse (UC24, RG36) : ouverte avec un fond de caisse sur un poste, par un caissier ; clôturée par le
/// comptage des espèces, l'écart avec le théorique est enregistré, puis validé par un responsable.
/// </summary>
internal sealed class SessionCaisse
{
    private SessionCaisse()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Poste de caisse (ex. CAISSE-1) : les tickets sont numérotés en continu par poste (RG34).</summary>
    public string NumeroCaisse { get; private set; } = "";

    public string Caissier { get; private set; } = "";

    public DateTime OuvertureUtc { get; private set; }

    public decimal FondCaisse { get; private set; }

    public StatutSession Statut { get; private set; }

    public DateTime? ClotureUtc { get; private set; }

    public decimal? EspecesTheoriques { get; private set; }

    public decimal? EspecesComptees { get; private set; }

    /// <summary>Espèces comptées moins espèces théoriques : négatif s'il manque de l'argent.</summary>
    public decimal? Ecart { get; private set; }

    public string? ValideePar { get; private set; }

    public DateTime? ValidationUtc { get; private set; }

    public static SessionCaisse Ouvrir(string numeroCaisse, string caissier, decimal fondCaisse, DateTime maintenantUtc)
    {
        if (fondCaisse < 0m || decimal.Round(fondCaisse, 0) != fondCaisse)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.FondInvalide);
        }

        return new SessionCaisse
        {
            Id = Guid.CreateVersion7(),
            NumeroCaisse = numeroCaisse,
            Caissier = caissier,
            FondCaisse = fondCaisse,
            OuvertureUtc = maintenantUtc,
            Statut = StatutSession.Ouverte,
        };
    }

    /// <summary>Clôture par comptage des espèces : l'écart est calculé et enregistré (RG36).</summary>
    public void Cloturer(decimal especesComptees, decimal especesTheoriques, DateTime maintenantUtc)
    {
        if (Statut != StatutSession.Ouverte)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.AucuneSessionOuverte);
        }

        if (especesComptees < 0m || decimal.Round(especesComptees, 0) != especesComptees)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.ComptageInvalide);
        }

        EspecesComptees = especesComptees;
        EspecesTheoriques = especesTheoriques;
        Ecart = especesComptees - especesTheoriques;
        ClotureUtc = maintenantUtc;
        Statut = StatutSession.Comptee;
    }

    /// <summary>Validation de l'écart par un responsable (5.4, 8.3).</summary>
    public void Valider(string responsable, DateTime maintenantUtc)
    {
        if (Statut != StatutSession.Comptee)
        {
            throw new ExceptionCaisse(CodesErreurCaisse.SessionNonComptee);
        }

        ValideePar = responsable;
        ValidationUtc = maintenantUtc;
        Statut = StatutSession.Validee;
    }
}
