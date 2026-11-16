namespace GestionStock.Identite.Contracts;

/// <summary>
/// Validation d'une opération par un responsable présent au poste (remise hors plafond, dépassement de plafond de
/// crédit : RG40, décision 31) : il saisit ses propres identifiants sur le poste du caissier. Les échecs comptent pour
/// le verrouillage de son compte, comme à la connexion.
/// </summary>
public interface IValidationResponsable
{
    /// <summary>
    /// Vérifie les identifiants et que le compte détient la permission ; renvoie l'identifiant du responsable, à
    /// tracer avec l'opération, ou null si la validation est refusée (identifiants faux, compte verrouillé, droit absent).
    /// </summary>
    Task<string?> VerifierAsync(string identifiant, string motDePasse, string permission, CancellationToken annulation = default);
}

/// <summary>Identifiants d'un responsable joints à une demande qui exige sa validation.</summary>
public sealed record ValidationResponsable(string Identifiant, string MotDePasse);
