using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace GestionStock.Identite.Application;

/// <summary>
/// Annuaire du personnel pour les autres modules : même désignation que la vue <c>identite.v_utilisateurs</c> des
/// rapports. Les désignations lues sont gardées le temps de la requête (une liste de tickets d'un même caissier).
/// </summary>
internal sealed class ServiceAnnuairePersonnel(UserManager<Utilisateur> utilisateurs) : IAnnuairePersonnel
{
    private readonly Dictionary<string, string> _lues = new(StringComparer.Ordinal);

    public async Task<string> DesignationAsync(string matricule, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(matricule);
        if (_lues.TryGetValue(matricule, out var connue))
        {
            return connue;
        }

        var compte = await utilisateurs.FindByNameAsync(matricule);
        var designation = compte is null || (compte.Prenom.Length == 0 && compte.Nom == matricule)
            ? matricule
            : $"{$"{compte.Prenom} {compte.Nom}".Trim()} ({matricule})";
        _lues[matricule] = designation;
        return designation;
    }
}
