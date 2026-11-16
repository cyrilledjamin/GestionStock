namespace GestionStock.Referentiel.Domain;

/// <summary>Type de site (section 4.5).</summary>
internal enum TypeSite
{
    Entrepot = 1,
    Magasin = 2,
}

/// <summary>
/// Rôle d'un emplacement (section 4.5). Mêmes noms et valeurs que le module Stock (vérifié par les tests).
/// </summary>
internal enum TypeEmplacement
{
    Reception = 1,
    Stockage = 2,
    Preparation = 3,
    Expedition = 4,
    Quarantaine = 5,
    Transit = 6,
    Retour = 7,
    Vente = 8,
    Reserve = 9,
    Rayon = 10,
    Demarque = 11,
}

/// <summary>Entrepôt ou magasin (RG20). La V1 n'en a qu'un : la boutique, créée par la migration.</summary>
internal sealed class Site
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    public string Nom { get; init; } = "";

    public TypeSite Type { get; init; }

    /// <summary>Séparation réserve / rayon (décision 12), désactivée par défaut.</summary>
    public bool GestionReserveRayon { get; init; }

    public List<Emplacement> Emplacements { get; init; } = [];
}

/// <summary>Emplacement d'un site. Un magasin possède au minimum un emplacement de vente (RG20).</summary>
internal sealed class Emplacement
{
    public Guid Id { get; init; }

    public Guid SiteId { get; init; }

    public string Code { get; init; } = "";

    public TypeEmplacement Type { get; init; }

    /// <summary>Multi-articles par défaut (décision 4).</summary>
    public bool MonoArticle { get; init; }

    public bool Actif { get; init; } = true;
}
