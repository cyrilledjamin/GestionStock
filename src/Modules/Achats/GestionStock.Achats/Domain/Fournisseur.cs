namespace GestionStock.Achats.Domain;

/// <summary>Fournisseur. Jamais supprimé : désactivé.</summary>
internal sealed class Fournisseur
{
    public const int LongueurMaxNom = 200;
    public const int LongueurMaxTelephone = 30;

    private Fournisseur()
    {
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = "";

    public string? Telephone { get; private set; }

    /// <summary>
    /// Tolérance de surlivraison en pourcentage, 0 par défaut (RG12, décision 6). Sans commande fournisseur (V1),
    /// elle n'est pas encore appliquée.
    /// </summary>
    public decimal ToleranceSurlivraisonPct { get; private set; }

    public bool Actif { get; private set; } = true;

    public static Fournisseur Creer(string nom, string? telephone)
    {
        var fournisseur = new Fournisseur { Id = Guid.CreateVersion7() };
        fournisseur.Modifier(nom, telephone, actif: true);
        return fournisseur;
    }

    public void Modifier(string nom, string? telephone, bool actif)
    {
        var nomNettoye = nom?.Trim() ?? "";
        if (nomNettoye.Length == 0)
        {
            throw new ExceptionAchats(CodesErreurAchats.NomObligatoire);
        }

        if (nomNettoye.Length > LongueurMaxNom)
        {
            throw new ExceptionAchats(CodesErreurAchats.NomTropLong);
        }

        var telephoneNettoye = telephone?.Trim();
        if (telephoneNettoye?.Length > LongueurMaxTelephone)
        {
            throw new ExceptionAchats(CodesErreurAchats.TelephoneTropLong);
        }

        Nom = nomNettoye;
        Telephone = string.IsNullOrEmpty(telephoneNettoye) ? null : telephoneNettoye;
        Actif = actif;
    }
}
