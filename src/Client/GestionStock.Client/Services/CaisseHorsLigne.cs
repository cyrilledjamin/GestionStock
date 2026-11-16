using System.Text.Json;

namespace GestionStock.Client.Services;

/// <summary>
/// Caisse coupée du serveur (7.8, ENF08) : session et numérotation du poste, tickets de la session, et file des
/// opérations faites hors ligne, gardés dans le stockage du poste. La file se rejoue dans l'ordre dès que le serveur
/// répond ; une opération n'en sort qu'une fois acceptée, et seulement sous le compte de son caissier.
/// </summary>
public sealed class CaisseHorsLigne : IDisposable
{
    private const string CleSession = "session";
    private readonly Api _api;
    private readonly StockageLocal _stockage;
    private readonly EtatConnexion _etat;
    private readonly Session _session;
    private readonly SemaphoreSlim _synchronisation = new(1, 1);
    private List<OperationEnAttente> _file = [];
    private bool _charge;

    public CaisseHorsLigne(Api api, StockageLocal stockage, EtatConnexion etat, Session session)
    {
        _api = api;
        _stockage = stockage;
        _etat = etat;
        _session = session;
        _etat.Changement += Relancer;
        _session.Changement += Relancer;
    }

    /// <summary>File d'attente, dans l'ordre d'envoi.</summary>
    public IReadOnlyList<OperationEnAttente> EnAttente => _file;

    /// <summary>Caissier dont les opérations attendent qu'il se reconnecte sur ce poste (null s'il n'y en a pas).</summary>
    public string? BloqueePar { get; private set; }

    /// <summary>File modifiée ou synchronisée (bandeau, caisse).</summary>
    public event Action? Changement;

    public async Task ChargerAsync()
    {
        if (_charge)
        {
            return;
        }

        _file = await _stockage.ListerAsync<OperationEnAttente>(StockageLocal.File);
        _charge = true;
        Changement?.Invoke();
    }

    // --- Session et numérotation du poste ------------------------------------------------------------------------

    /// <summary>Garde la session ouverte du caissier et le dernier numéro de son poste, pour continuer hors ligne.</summary>
    public async Task GarderSessionAsync(SessionCaisse session)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _stockage.EcrireAsync(StockageLocal.Cache, CleSession, session);
        await NoterNumeroAsync(session.NumeroCaisse, session.DernierNumeroPoste);
    }

    /// <summary>Session gardée sur le poste, si elle appartient au caissier connecté.</summary>
    public async Task<SessionCaisse?> SessionGardeeAsync()
    {
        var session = await _stockage.LireAsync<SessionCaisse>(StockageLocal.Cache, CleSession);
        return session is not null && session.Caissier == _session.Profil?.Identifiant ? session : null;
    }

    public async Task<long> DernierNumeroAsync(string poste) =>
        await _stockage.LireAsync<long?>(StockageLocal.Cache, CleNumero(poste)) ?? 0;

    /// <summary>Un numéro est connu pour ce poste (ticket reçu ou fait sur le poste) : la suite repart de lui (RG34).</summary>
    public async Task NoterNumeroAsync(string poste, long numero)
    {
        if (numero > await DernierNumeroAsync(poste))
        {
            await _stockage.EcrireAsync(StockageLocal.Cache, CleNumero(poste), numero);
        }
    }

    /// <summary>Numéro du prochain ticket fait hors ligne sur ce poste ; il est réservé aussitôt.</summary>
    public async Task<long> ProchainNumeroAsync(string poste)
    {
        var numero = await DernierNumeroAsync(poste) + 1;
        await _stockage.EcrireAsync(StockageLocal.Cache, CleNumero(poste), numero);
        return numero;
    }

    // --- Tickets de la session (avoir et réimpression hors ligne) --------------------------------------------------

    public async Task<List<TicketCaisse>> TicketsAsync(Guid sessionId) =>
        await _stockage.LireAsync<List<TicketCaisse>>(StockageLocal.Cache, CleTickets(sessionId)) ?? [];

    /// <summary>Tickets reçus du serveur pour la session, complétés des tickets faits sur le poste pas encore envoyés.</summary>
    public async Task GarderTicketsAsync(Guid sessionId, IReadOnlyList<TicketCaisse> duServeur)
    {
        var locaux = (await TicketsAsync(sessionId)).Where(t => t.Id == t.IdIdempotence && duServeur.All(s => s.IdIdempotence != t.IdIdempotence));
        await _stockage.EcrireAsync(StockageLocal.Cache, CleTickets(sessionId), duServeur.Concat(locaux).OrderByDescending(t => t.Numero, StringComparer.Ordinal).ToList());
    }

    /// <summary>Ajoute ou remplace (même idempotence) un ticket de la session sur le poste.</summary>
    public async Task GarderTicketAsync(Guid sessionId, TicketCaisse ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var tickets = (await TicketsAsync(sessionId)).Where(t => t.IdIdempotence != ticket.IdIdempotence).Append(ticket)
            .OrderByDescending(t => t.Numero, StringComparer.Ordinal).ToList();
        await _stockage.EcrireAsync(StockageLocal.Cache, CleTickets(sessionId), tickets);
    }

    // --- File d'attente ---------------------------------------------------------------------------------------------

    /// <summary>Met une opération en file, sur le poste, avant d'imprimer quoi que ce soit (aucune perte).</summary>
    public async Task AjouterAsync(OperationEnAttente operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ChargerAsync();
        await _stockage.EcrireAsync(StockageLocal.File, operation.Cle, operation);
        _file = [.. _file, operation];
        Changement?.Invoke();
    }

    /// <summary>Clé d'une nouvelle opération : croissante, elle fixe l'ordre d'envoi.</summary>
    public static string NouvelleCle() => DateTime.UtcNow.Ticks.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Envoie la file dans l'ordre. S'arrête si le serveur ne répond plus, ou sur l'opération d'un autre caissier ; une
    /// opération refusée reste en file avec son message (jamais perdue) et les suivantes partent quand même.
    /// </summary>
    public async Task SynchroniserAsync()
    {
        await ChargerAsync();
        if (_file.Count == 0 || !_etat.EnLigne || _session.Profil is null || !await _synchronisation.WaitAsync(0))
        {
            return;
        }

        try
        {
            BloqueePar = null;
            foreach (var operation in _file.ToList())
            {
                if (operation.Caissier != _session.Profil?.Identifiant)
                {
                    BloqueePar = operation.Caissier;
                    break;
                }

                var reponse = await _api.PostAsync<JsonElement>(operation.Url, operation.Corps);
                if (reponse.Code == Api.CodeHorsLigne)
                {
                    break;
                }

                if (reponse.Erreur is not null)
                {
                    await Remplacer(operation, operation with { Erreur = reponse.Erreur });
                    continue;
                }

                if (operation.Type is OperationEnAttente.Vente or OperationEnAttente.Avoir
                    && reponse.Valeur.Deserialize<TicketCaisse>(JsonSerializerOptions.Web) is { } ticket)
                {
                    // Le ticket du serveur (identifiant, numéro éventuellement réattribué) remplace celui du poste.
                    await GarderTicketAsync(operation.SessionId, ticket);
                    await NoterNumeroAsync(ticket.Numero[..ticket.Numero.LastIndexOf('-')], TicketLocal.Rang(ticket.Numero));
                }

                await _stockage.SupprimerAsync(StockageLocal.File, operation.Cle);
                _file = [.. _file.Where(o => o.Cle != operation.Cle)];
                Changement?.Invoke();
            }
        }
        finally
        {
            _synchronisation.Release();
            Changement?.Invoke();
        }
    }

    private async Task Remplacer(OperationEnAttente ancienne, OperationEnAttente nouvelle)
    {
        await _stockage.EcrireAsync(StockageLocal.File, nouvelle.Cle, nouvelle);
        _file = [.. _file.Select(o => o.Cle == ancienne.Cle ? nouvelle : o)];
    }

    private void Relancer()
    {
        if (_etat.EnLigne)
        {
            _ = SynchroniserAsync();
        }
    }

    private static string CleNumero(string poste) => $"numero:{poste}";

    private static string CleTickets(Guid sessionId) => $"tickets:{sessionId}";

    public void Dispose()
    {
        _etat.Changement -= Relancer;
        _session.Changement -= Relancer;
        _synchronisation.Dispose();
    }
}
