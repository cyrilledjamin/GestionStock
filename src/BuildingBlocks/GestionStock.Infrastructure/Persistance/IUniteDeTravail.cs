namespace GestionStock.Infrastructure.Persistance;

/// <summary>
/// Transaction unique couvrant tous les modules de la portée courante.
/// À utiliser dès qu'une opération écrit dans plusieurs schémas (ex. ticket de caisse + mouvements de stock),
/// ou qu'un mouvement et ses soldes doivent être écrits ensemble (RG01).
/// </summary>
public interface IUniteDeTravail
{
    /// <summary>Indique si une transaction est en cours dans la portée.</summary>
    bool EstActive { get; }

    /// <summary>
    /// Exécute l'opération dans une transaction validée à la fin, annulée en cas d'exception.
    /// Un appel imbriqué participe à la transaction englobante.
    /// Après un échec, les DbContext de la portée ne doivent plus être réutilisés.
    /// </summary>
    Task<TResultat> ExecuterAsync<TResultat>(Func<CancellationToken, Task<TResultat>> operation, CancellationToken annulation = default);

    /// <inheritdoc cref="ExecuterAsync{TResultat}"/>
    Task ExecuterAsync(Func<CancellationToken, Task> operation, CancellationToken annulation = default);
}
