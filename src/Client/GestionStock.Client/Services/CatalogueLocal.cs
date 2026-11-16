namespace GestionStock.Client.Services;

/// <summary>Conditionnement vendable du catalogue de la caisse, avec ses codes actifs (<c>/api/referentiel/catalogue-caisse</c>).</summary>
public sealed record VendableCatalogue(
    Guid ArticleId,
    string CodeArticle,
    string Libelle,
    string? LibelleTraduit,
    string UniteStock,
    int PrecisionQuantite,
    Guid ConditionnementId,
    string LibelleConditionnement,
    decimal QuantiteUniteStock,
    bool EstUnite,
    bool VenteRapide,
    List<string> Codes);

public sealed record CatalogueCaisse(DateTime GenereLeUtc, List<VendableCatalogue> Vendables);

/// <summary>Prix d'un conditionnement : en vigueur à la date de la grille, et prochain prix programmé (<c>/api/prix/grille</c>).</summary>
public sealed record PrixGrille(Guid ConditionnementId, decimal PrixTtc, DateOnly DebutValidite, decimal? ProchainPrixTtc, DateOnly? ProchaineValidite);

public sealed record GrillePrix(DateOnly Date, bool Assujetti, decimal TauxTva, List<PrixGrille> Prix);

/// <summary>
/// Catalogue et prix gardés sur le poste (7.8) : la caisse y identifie un scan, cherche un article et lit le prix du
/// jour sans le serveur, avec les mêmes règles que lui (UC22, RG37). Classe pure, testée sans navigateur.
/// </summary>
public sealed class CatalogueLocal
{
    private const int LimiteRecherche = 30;
    private readonly List<VendableCatalogue> _vendables;
    private readonly Dictionary<string, (VendableCatalogue Vendable, string Code)> _parCode = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, PrixGrille> _prix;

    public CatalogueLocal(CatalogueCaisse catalogue, GrillePrix grille)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(grille);
        _vendables = catalogue.Vendables;
        foreach (var vendable in _vendables)
        {
            foreach (var code in vendable.Codes)
            {
                _parCode.TryAdd(code, (vendable, code));
            }
        }

        _prix = grille.Prix.ToDictionary(p => p.ConditionnementId);
    }

    /// <summary>Articles rapides choisis par le Gérant (décision 37).</summary>
    public IEnumerable<ArticleVendable> Rapides =>
        _vendables.Where(v => v.VenteRapide).OrderBy(v => v.Libelle, StringComparer.CurrentCulture).Select(v => Vendable(v, null));

    /// <summary>
    /// Code scanné ou saisi, comme <c>ServiceConditionnements.ResoudreAsync</c> : code actif d'un conditionnement (un
    /// EAN-13 à zéro de tête est aussi cherché en UPC-A), sinon code article (conditionnement à l'unité).
    /// </summary>
    public ArticleVendable? Resoudre(string? saisie)
    {
        var nettoyee = saisie?.Trim() ?? "";
        if (nettoyee.Length == 0)
        {
            return null;
        }

        if (_parCode.TryGetValue(nettoyee, out var trouve)
            || (nettoyee.Length == 13 && nettoyee[0] == '0' && _parCode.TryGetValue(nettoyee[1..], out trouve)))
        {
            return Vendable(trouve.Vendable, trouve.Code);
        }

        var parArticle = _vendables.FirstOrDefault(v => v.EstUnite && string.Equals(v.CodeArticle, nettoyee, StringComparison.OrdinalIgnoreCase));
        return parArticle is null ? null : Vendable(parArticle, null);
    }

    /// <summary>Recherche par code ou libellé (principal ou traduit), comme <c>/api/referentiel/vendables</c>.</summary>
    public IReadOnlyList<ArticleVendable> Rechercher(string? texte)
    {
        var motif = texte?.Trim() ?? "";
        if (motif.Length == 0)
        {
            return [];
        }

        return [.. _vendables
            .Where(v => Contient(v.CodeArticle, motif) || Contient(v.Libelle, motif) || Contient(v.LibelleTraduit, motif))
            .OrderBy(v => v.CodeArticle, StringComparer.Ordinal).ThenBy(v => v.QuantiteUniteStock)
            .Take(LimiteRecherche)
            .Select(v => Vendable(v, null))];
    }

    /// <summary>Prix du conditionnement le jour donné (prix en vigueur, ou prochain prix s'il a pris effet) ; null sans prix.</summary>
    public decimal? Prix(Guid conditionnementId, DateOnly jour)
    {
        if (!_prix.TryGetValue(conditionnementId, out var prix))
        {
            return null;
        }

        return prix.ProchaineValidite is { } debut && jour >= debut && prix.ProchainPrixTtc is { } prochain
            ? prochain
            : prix.PrixTtc;
    }

    private static bool Contient(string? valeur, string motif) =>
        valeur?.Contains(motif, StringComparison.CurrentCultureIgnoreCase) == true;

    private static ArticleVendable Vendable(VendableCatalogue v, string? codeScanne) =>
        new(v.ArticleId, v.CodeArticle, v.Libelle, v.LibelleTraduit, v.UniteStock, v.PrecisionQuantite, v.ConditionnementId, v.LibelleConditionnement, v.QuantiteUniteStock, codeScanne);
}
