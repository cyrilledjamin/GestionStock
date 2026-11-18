using System.Globalization;

namespace GestionStock.Client.Services;

/// <summary>
/// Vente en cours au comptoir : panier, paiements et montants affichés. Les calculs suivent ceux du serveur, qui relit
/// prix et montants à l'encaissement (RG37) ; rien ici ne décide d'une règle de gestion.
/// </summary>
public sealed class VenteEnCours
{
    /// <summary>Coupures du franc CFA proposées en billets rapides (pièce de 500 et billets).</summary>
    public static IReadOnlyList<decimal> Billets { get; } = [500m, 1_000m, 2_000m, 5_000m, 10_000m];

    private readonly List<LignePanier> _lignes = [];
    private readonly List<PaiementSaisi> _paiements = [];

    public IReadOnlyList<LignePanier> Lignes => _lignes;

    public IReadOnlyList<PaiementSaisi> Paiements => _paiements;

    public bool EstVide => _lignes.Count == 0;

    public decimal Total => _lignes.Sum(l => l.Montant);

    /// <summary>Positif : reste à payer ; négatif : monnaie à rendre.</summary>
    public decimal Reste => Total - _paiements.Sum(p => p.Montant);

    /// <summary>Ligne du conditionnement, s'il est déjà dans le panier.</summary>
    public LignePanier? Ligne(Guid conditionnementId) =>
        _lignes.FirstOrDefault(l => l.Vendable.ConditionnementId == conditionnementId);

    /// <summary>Ajoute une unité de vente : la ligne existante augmente d'un pas, sinon une ligne est créée au prix lu.</summary>
    public LignePanier Ajouter(ArticleVendable vendable, decimal prix)
    {
        ArgumentNullException.ThrowIfNull(vendable);
        if (Ligne(vendable.ConditionnementId) is { } existante)
        {
            Incrementer(existante);
            return existante;
        }

        var ligne = new LignePanier { Vendable = vendable, Prix = prix, Quantite = 1 };
        _lignes.Add(ligne);
        return ligne;
    }

    public static void Incrementer(LignePanier ligne)
    {
        ArgumentNullException.ThrowIfNull(ligne);
        ligne.Quantite += 1;
    }

    /// <summary>Retire une unité sans descendre sous la plus petite quantité vendable (le bouton « retirer » supprime la ligne).</summary>
    public static void Decrementer(LignePanier ligne)
    {
        ArgumentNullException.ThrowIfNull(ligne);
        if (ligne.Quantite - 1 >= Pas(ligne.Vendable))
        {
            ligne.Quantite -= 1;
        }
    }

    public void Retirer(LignePanier ligne) => _lignes.Remove(ligne);

    /// <summary>
    /// Un seul paiement par mode : un nouveau clic sur un mode déjà choisi lui ajoute le reste à payer au lieu
    /// de créer une ligne en double ; rien n'est ajouté quand tout est déjà payé.
    /// </summary>
    public void AjouterPaiement(string mode)
    {
        var reste = Math.Max(0, Reste);
        if (Paiement(mode) is { } existant)
        {
            existant.Montant += reste;
        }
        else if (reste > 0)
        {
            _paiements.Add(new PaiementSaisi { Mode = mode, Montant = reste });
        }
    }

    public void RetirerPaiement(PaiementSaisi paiement) => _paiements.Remove(paiement);

    /// <summary>Billet rapide : la somme reçue s'ajoute aux espèces (5 000 puis 2 000 font 7 000 reçus).</summary>
    public void AjouterEspeces(decimal montant)
    {
        if (montant <= 0)
        {
            return;
        }

        if (Paiement(ModesPaiement.Especes) is { } especes)
        {
            especes.Montant += montant;
        }
        else
        {
            _paiements.Add(new PaiementSaisi { Mode = ModesPaiement.Especes, Montant = montant });
        }
    }

    /// <summary>Compte juste : les espèces couvrent exactement ce que les autres modes ne paient pas.</summary>
    public void CompteJuste()
    {
        var autres = _paiements.Where(p => p.Mode != ModesPaiement.Especes).Sum(p => p.Montant);
        var du = Math.Max(0, Total - autres);
        if (Paiement(ModesPaiement.Especes) is { } especes)
        {
            especes.Montant = du;
        }
        else if (du > 0)
        {
            _paiements.Add(new PaiementSaisi { Mode = ModesPaiement.Especes, Montant = du });
        }
    }

    /// <summary>Lignes et paiements tels que saisis, pour les garder sur le poste (changement d'écran, rechargement).</summary>
    public VenteGardee Garder() => new(
        [.. _lignes.Select(l => new LigneGardee(l.Vendable, l.Quantite, l.Prix, l.Remise))],
        [.. _paiements.Select(p => new PaiementGarde(p.Mode, p.Montant, p.Reference))]);

    /// <summary>Vente reprise telle qu'elle avait été gardée (mêmes lignes, quantités, prix, remises et paiements).</summary>
    public static VenteEnCours Reprendre(VenteGardee gardee)
    {
        ArgumentNullException.ThrowIfNull(gardee);
        var vente = new VenteEnCours();
        vente._lignes.AddRange(gardee.Lignes.Select(l => new LignePanier { Vendable = l.Vendable, Quantite = l.Quantite, Prix = l.Prix, Remise = l.Remise }));
        vente._paiements.AddRange(gardee.Paiements.Select(p => new PaiementSaisi { Mode = p.Mode, Montant = p.Montant, Reference = p.Reference }));
        return vente;
    }

    public void Vider()
    {
        _lignes.Clear();
        _paiements.Clear();
    }

    /// <summary>Plus petite quantité saisissable : décimale pour un article au poids vendu à l'unité de stock (RG46), sinon 1.</summary>
    public static decimal Pas(ArticleVendable vendable)
    {
        ArgumentNullException.ThrowIfNull(vendable);
        return vendable.QuantiteUniteStock == 1m && vendable.PrecisionQuantite > 0
            ? 1m / (decimal)Math.Pow(10, vendable.PrecisionQuantite)
            : 1m;
    }

    /// <summary>Pas au format attendu par l'attribut <c>step</c> d'un champ numérique.</summary>
    public static string PasSaisie(ArticleVendable vendable) => Pas(vendable).ToString(CultureInfo.InvariantCulture);

    private PaiementSaisi? Paiement(string mode) => _paiements.FirstOrDefault(p => p.Mode == mode);
}

public sealed class LignePanier
{
    public required ArticleVendable Vendable { get; init; }

    public decimal Quantite { get; set; }

    public decimal Prix { get; init; }

    public decimal Remise { get; set; }

    /// <summary>Même calcul que le serveur : quantité × prix arrondi à l'unité, moins la remise.</summary>
    public decimal Montant => decimal.Round(Quantite * Prix, 0, MidpointRounding.AwayFromZero) - Remise;
}

public sealed class PaiementSaisi
{
    public required string Mode { get; init; }

    public decimal Montant { get; set; }

    public string? Reference { get; set; }
}

/// <summary>Vente en cours telle qu'elle est gardée sur le poste (lignes et paiements saisis), sans calcul.</summary>
public sealed record VenteGardee(List<LigneGardee> Lignes, List<PaiementGarde> Paiements);

public sealed record LigneGardee(ArticleVendable Vendable, decimal Quantite, decimal Prix, decimal Remise);

public sealed record PaiementGarde(string Mode, decimal Montant, string? Reference);
