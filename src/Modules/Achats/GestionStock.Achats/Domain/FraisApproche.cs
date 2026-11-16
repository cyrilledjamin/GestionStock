namespace GestionStock.Achats.Domain;

/// <summary>Nature d'un frais d'approche (section 4.7).</summary>
internal enum TypeFrais
{
    Transport = 1,
    Transit = 2,
    Douane = 3,
    Autre = 4,
}

/// <summary>
/// Clé de répartition d'un frais sur les lignes (section 4.7 ; décision 18 : valeur par défaut). La clé Poids du
/// dossier n'est pas proposée : l'article n'a pas de poids en V1 (point ouvert du README).
/// </summary>
internal enum CleRepartition
{
    Valeur = 1,
    Quantite = 2,
}

/// <summary>
/// Frais d'approche d'une réception (RG30) : transport, transit, douane non récupérable. Il est réparti sur les lignes
/// au prorata de la clé choisie. Saisi avant la validation, il entre dans le coût d'entrée en stock ; saisi après, il
/// revalorise le stock encore présent (mouvement Revalorisation).
/// </summary>
internal sealed class FraisApproche
{
    public const int LongueurMaxLibelle = 100;

    private FraisApproche()
    {
    }

    public Guid Id { get; private set; }

    public Guid ReceptionId { get; private set; }

    public TypeFrais Type { get; private set; }

    public string? Libelle { get; private set; }

    /// <summary>Montant hors TVA récupérable, en XAF (RG30).</summary>
    public decimal Montant { get; private set; }

    public CleRepartition Cle { get; private set; }

    /// <summary>Saisi après la validation de la réception : il revalorise le stock au lieu d'entrer dans le coût d'entrée.</summary>
    public bool ApresValidation { get; private set; }

    public string SaisiPar { get; private set; } = "";

    public DateTime SaisiLeUtc { get; private set; }

    public List<AllocationFrais> Allocations { get; private set; } = [];

    /// <summary>Part du frais qui n'a pas pu entrer dans la valeur du stock (marchandise déjà sortie).</summary>
    public decimal MontantNonIncorpore => Allocations.Sum(a => a.MontantNonIncorpore);

    public static FraisApproche Creer(
        Guid receptionId,
        TypeFrais type,
        decimal montant,
        CleRepartition cle,
        string? libelle,
        bool apresValidation,
        string utilisateur,
        DateTime maintenantUtc)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ExceptionAchats(CodesErreurAchats.TypeFraisInvalide);
        }

        if (!Enum.IsDefined(cle))
        {
            throw new ExceptionAchats(CodesErreurAchats.CleRepartitionInvalide);
        }

        if (montant <= 0m || decimal.Round(montant, Reception.DecimalesCout) != montant)
        {
            throw new ExceptionAchats(CodesErreurAchats.MontantFraisInvalide);
        }

        var texte = libelle?.Trim();
        if (texte?.Length > LongueurMaxLibelle)
        {
            throw new ExceptionAchats(CodesErreurAchats.LibelleFraisTropLong);
        }

        return new FraisApproche
        {
            Id = Guid.CreateVersion7(),
            ReceptionId = receptionId,
            Type = type,
            Montant = montant,
            Cle = cle,
            Libelle = string.IsNullOrEmpty(texte) ? null : texte,
            ApresValidation = apresValidation,
            SaisiPar = utilisateur,
            SaisiLeUtc = maintenantUtc,
        };
    }

    /// <summary>
    /// Répartit le montant au prorata de la clé, à 4 décimales ; le reliquat d'arrondi va à la ligne de plus grand poids
    /// (la première dans l'ordre de saisie en cas d'égalité) pour que la somme des parts égale exactement le montant.
    /// </summary>
    public void Repartir(IReadOnlyList<LigneReception> toutesLignes)
    {
        ArgumentNullException.ThrowIfNull(toutesLignes);

        // L'ordre de chargement depuis la base n'est pas garanti : l'ordre de saisie rend le reliquat reproductible.
        var lignes = toutesLignes.OrderBy(l => l.Ordre).ToList();
        var poids = lignes.Select(l => Cle == CleRepartition.Valeur ? l.Quantite * l.CoutUnitaire : l.Quantite).ToList();
        var total = poids.Sum();
        if (total <= 0m)
        {
            throw new ExceptionAchats(CodesErreurAchats.RepartitionImpossible);
        }

        var parts = poids.Select(p => decimal.Round(Montant * p / total, Reception.DecimalesCout, MidpointRounding.AwayFromZero)).ToList();
        var plusLourde = poids.IndexOf(poids.Max());
        parts[plusLourde] += Montant - parts.Sum();

        Allocations.Clear();
        Allocations.AddRange(lignes.Select((ligne, index) => new AllocationFrais(Id, ligne.Id, parts[index])));
    }
}

/// <summary>
/// Part d'un frais affectée à une ligne de réception. Pour un frais tardif, son identifiant est aussi celui
/// d'idempotence du mouvement Revalorisation de la ligne.
/// </summary>
internal sealed class AllocationFrais
{
    public AllocationFrais(Guid fraisApprocheId, Guid ligneReceptionId, decimal montant)
    {
        Id = Guid.CreateVersion7();
        FraisApprocheId = fraisApprocheId;
        LigneReceptionId = ligneReceptionId;
        Montant = montant;
    }

    public Guid Id { get; private set; }

    public Guid FraisApprocheId { get; private set; }

    public Guid LigneReceptionId { get; private set; }

    public decimal Montant { get; private set; }

    /// <summary>Frais tardif : quantité de la ligne encore en stock, revalorisée.</summary>
    public decimal QuantiteRevalorisee { get; private set; }

    /// <summary>Frais tardif : supplément par unité reçue (part / quantité de la ligne).</summary>
    public decimal SupplementUnitaire { get; private set; }

    /// <summary>Frais tardif : part correspondant à la marchandise déjà sortie, hors de la valeur du stock.</summary>
    public decimal MontantNonIncorpore { get; private set; }

    /// <summary>
    /// Frais tardif : seule la marchandise encore en stock est revalorisée, au supplément par unité reçue ;
    /// le reste du montant est conservé comme non incorporé.
    /// </summary>
    public void Incorporer(decimal quantiteEnStock, decimal quantiteRecue)
    {
        SupplementUnitaire = decimal.Round(Montant / quantiteRecue, Reception.DecimalesCout, MidpointRounding.AwayFromZero);
        QuantiteRevalorisee = SupplementUnitaire > 0m ? Math.Clamp(quantiteEnStock, 0m, quantiteRecue) : 0m;
        MontantNonIncorpore = Montant - decimal.Round(QuantiteRevalorisee * SupplementUnitaire, Reception.DecimalesCout, MidpointRounding.AwayFromZero);
    }
}
