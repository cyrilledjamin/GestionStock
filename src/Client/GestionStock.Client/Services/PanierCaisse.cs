using System.Text.Json;

namespace GestionStock.Client.Services;

/// <summary>
/// Vente en cours de la caisse, gardée hors de la page : un changement d'écran (même par erreur) ne perd ni le panier,
/// ni les paiements saisis, ni le client choisi, ni la clé d'idempotence (une vente envoyée puis reprise reste unique).
/// Elle est aussi gardée sur le poste (stockage local), pour survivre à un rechargement de la page. Elle n'appartient
/// qu'à une session : une autre session (autre caissier, nouvelle ouverture) commence une vente vide.
/// </summary>
public sealed class PanierCaisse(StockageLocal stockage)
{
    /// <summary>Clé de la vente en cours dans le magasin « cache » du poste.</summary>
    public const string Cle = "vente-en-cours";

    private Guid? _sessionId;
    private string? _derniereEcriture;

    public VenteEnCours Vente { get; private set; } = new();

    public ClientCredit? Client { get; set; }

    public Guid IdIdempotence { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// À l'ouverture de la caisse : la vente de cette session si elle est déjà en mémoire (retour depuis un autre écran),
    /// sinon celle gardée sur le poste pour cette session (rechargement), sinon une vente vide.
    /// </summary>
    public async Task ReprendreAsync(Guid sessionId)
    {
        if (_sessionId == sessionId)
        {
            return;
        }

        _sessionId = sessionId;
        var gardee = await stockage.LireAsync<PanierGarde>(StockageLocal.Cache, Cle);
        if (gardee is not null && gardee.SessionId == sessionId)
        {
            Vente = VenteEnCours.Reprendre(gardee.Vente);
            Client = gardee.Client;
            IdIdempotence = gardee.IdIdempotence;
            _derniereEcriture = JsonSerializer.Serialize(gardee);
        }
        else
        {
            Nouvelle();
        }
    }

    /// <summary>Garde l'état courant sur le poste, seulement s'il a changé depuis la dernière écriture.</summary>
    public async Task GarderAsync()
    {
        if (_sessionId is not { } sessionId)
        {
            return;
        }

        var gardee = new PanierGarde(sessionId, IdIdempotence, Client, Vente.Garder());
        var json = JsonSerializer.Serialize(gardee);
        if (json == _derniereEcriture)
        {
            return;
        }

        _derniereEcriture = json;
        if (Vente.EstVide && Client is null)
        {
            await stockage.SupprimerAsync(StockageLocal.Cache, Cle);
        }
        else
        {
            await stockage.EcrireAsync(StockageLocal.Cache, Cle, gardee);
        }
    }

    /// <summary>Vente vide, nouvelle clé d'idempotence (encaissement réussi, vente annulée).</summary>
    public void Nouvelle()
    {
        Vente = new VenteEnCours();
        Client = null;
        IdIdempotence = Guid.NewGuid();
    }

    /// <summary>Vente terminée (encaissée, ou session clôturée) : rien ne reste en mémoire ni sur le poste.</summary>
    public async Task TerminerAsync(bool sessionFermee = false)
    {
        Nouvelle();
        _derniereEcriture = null;
        if (sessionFermee)
        {
            _sessionId = null;
        }

        await stockage.SupprimerAsync(StockageLocal.Cache, Cle);
    }

}

/// <summary>Vente en cours gardée sur le poste, rattachée à sa session de caisse.</summary>
public sealed record PanierGarde(Guid SessionId, Guid IdIdempotence, ClientCredit? Client, VenteGardee Vente);
