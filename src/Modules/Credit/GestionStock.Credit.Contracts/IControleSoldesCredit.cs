namespace GestionStock.Credit.Contracts;

/// <summary>
/// Contrôle de cohérence de la dette des clients (ENF04, 4.6) : <c>Client.SoldeDu</c> doit égaler la somme des ventes à
/// crédit moins les remboursements et annulations de son journal. Le journal fait foi ; une reconstruction remet la dette
/// à sa valeur sans écrire aucune opération. Appelé par le module Exploitation.
/// </summary>
public interface IControleSoldesCredit
{
    /// <summary>Compare la dette de chaque client à son journal.</summary>
    Task<ControleCredit> ControlerAsync(CancellationToken annulation = default);

    /// <summary>Reconstruit la dette d'un client depuis son journal, sous verrou ; renvoie l'écart corrigé, ou null.</summary>
    Task<EcartCredit?> ReconstruireAsync(Guid clientId, CancellationToken annulation = default);
}

/// <summary>Résultat d'un contrôle : nombre de clients comparés et écarts trouvés.</summary>
public sealed record ControleCredit(int SoldesControles, IReadOnlyList<EcartCredit> Ecarts);

/// <summary>Dette enregistrée différente de celle du journal ; le nom sert à l'affichage de l'écart.</summary>
public sealed record EcartCredit(Guid ClientId, string NomClient, decimal Journal, decimal Enregistre);
