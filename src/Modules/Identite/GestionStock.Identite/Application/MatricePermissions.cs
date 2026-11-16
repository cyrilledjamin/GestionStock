using GestionStock.Identite.Contracts;

namespace GestionStock.Identite.Application;

/// <summary>
/// Rôles détenant chaque permission, d'après la section 8.3 du dossier d'analyse.
/// Les lignes marquées « provisoire » ne sont pas tranchées par le dossier : à confirmer (README, points ouverts).
/// </summary>
internal static class MatricePermissions
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RolesParPermission { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // 8.3 : l'Administrateur gère les utilisateurs et les rôles.
            [Permissions.UtilisateursGerer] = [Roles.Administrateur],

            // UC14 : consultation ouverte à tous.
            [Permissions.ReferentielConsulter] = Roles.Tous,
            [Permissions.StockConsulter] = Roles.Tous,
            [Permissions.PrixConsulter] = Roles.Tous,

            // 8.3 : le Gérant gère les prix ; le Comptable ne modifie pas les prix.
            [Permissions.PrixGerer] = [Roles.Gerant],

            // 8.3 : le Gérant valide les remises hors plafond ; RG40 : dépassement de crédit validé par un utilisateur
            // autorisé (provisoire : le Gérant).
            [Permissions.DepassementsValider] = [Roles.Gerant],

            // 8.3 : le Gérant gère les clients, le Comptable les plafonds de crédit ; le Caissier vend à crédit et
            // encaisse les remboursements, il consulte donc la dette des clients.
            [Permissions.CreditConsulter] = [Roles.Gerant, Roles.Comptable, Roles.Caissier],
            [Permissions.ClientsGerer] = [Roles.Gerant],
            [Permissions.PlafondsGerer] = [Roles.Comptable],

            // 8.3 : le Caissier vend, fait les avoirs et encaisse les remboursements sur sa propre caisse ;
            // l'Administrateur ne fait aucune opération de vente ; le Gérant valide les écarts de caisse, le Comptable
            // valide les clôtures.
            [Permissions.CaisseVendre] = [Roles.Caissier],
            [Permissions.TicketsConsulter] = [Roles.Caissier, Roles.Gerant, Roles.Comptable],
            [Permissions.SessionsConsulter] = [Roles.Gerant, Roles.Comptable],
            [Permissions.SessionsValider] = [Roles.Gerant, Roles.Comptable],

            // Session restée ouverte qui bloque un poste : le Gérant la clôture à la place de son titulaire.
            [Permissions.SessionsCloturer] = [Roles.Gerant],

            // 8.3 : le Magasinier fait l'inventaire et déclare la démarque ; le Comptable valide les écarts
            // d'inventaire ; la démarque est validée par le Comptable ou le Gérant.
            [Permissions.InventaireConsulter] = [Roles.Gerant, Roles.Comptable, Roles.Magasinier],
            [Permissions.InventaireCompter] = [Roles.Magasinier],
            [Permissions.InventaireValider] = [Roles.Comptable],
            [Permissions.DemarqueDeclarer] = [Roles.Magasinier],
            [Permissions.DemarqueValider] = [Roles.Comptable, Roles.Gerant],

            // Provisoire : la reprise des données n'est attribuée à aucun rôle par la section 8.3 ; elle crée des articles
            // (Gérant, Administrateur) et des prix (Gérant).
            [Permissions.RepriseImporter] = [Roles.Administrateur, Roles.Gerant],

            // Provisoire : la gestion des articles n'est attribuée à aucun rôle par la section 8.3.
            [Permissions.ArticlesGerer] = [Roles.Gerant, Roles.Administrateur],

            // 8.3 : le Comptable lit tout ; le Magasinier réceptionne ; le Gérant valide.
            [Permissions.AchatsConsulter] = [Roles.Gerant, Roles.Comptable, Roles.Magasinier],

            // Provisoire : la création des fournisseurs n'est attribuée à aucun rôle par la section 8.3.
            [Permissions.FournisseursGerer] = [Roles.Gerant, Roles.Magasinier],

            // 8.3 : le Magasinier réceptionne. Provisoire : validation d'une réception sans commande (6.1 cite
            // l'Acheteur, rôle absent de la V1) par le Magasinier ou le Gérant.
            [Permissions.ReceptionsSaisir] = [Roles.Magasinier, Roles.Gerant],
            [Permissions.ReceptionsValider] = [Roles.Magasinier, Roles.Gerant],

            // 8.3 : le Comptable saisit les frais d'approche. Provisoire : le Gérant aussi.
            [Permissions.FraisSaisir] = [Roles.Comptable, Roles.Gerant],

            // 8.3 : le Comptable enregistre les paiements fournisseurs.
            [Permissions.PaiementsFournisseursSaisir] = [Roles.Comptable],

            // 8.3 : le Gérant voit tous les rapports ; le Comptable lit tout.
            [Permissions.RapportsConsulter] = [Roles.Gerant, Roles.Comptable],

            // 8.3 : le Comptable produit les exports. Provisoire : le Gérant aussi (« tous les rapports »).
            [Permissions.ExportsProduire] = [Roles.Comptable, Roles.Gerant],
        };

    /// <summary>Permissions détenues par un utilisateur ayant ces rôles.</summary>
    public static IEnumerable<string> PermissionsDes(IEnumerable<string> roles)
    {
        var ensemble = roles.ToHashSet(StringComparer.Ordinal);
        return RolesParPermission
            .Where(p => p.Value.Any(ensemble.Contains))
            .Select(p => p.Key)
            .Order(StringComparer.Ordinal);
    }
}
