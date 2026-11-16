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
