namespace GestionStock.Client.Services;

/// <summary>
/// Données de la caisse gardées sur le poste (7.8) : catalogue avec codes, grille de prix et clients à crédit (dernier
/// solde connu). Rafraîchies à l'ouverture de la caisse puis régulièrement tant que le serveur répond ; relues du
/// stockage du poste quand il ne répond plus.
/// </summary>
public sealed class CacheCaisse(Api api, StockageLocal stockage)
{
    private const string CleCatalogue = "catalogue";
    private const string CleGrille = "grille";
    private const string CleClients = "clients";
    private const string CleDate = "cache-date";
    private const string CleEntete = "entete";

    /// <summary>Catalogue prêt à l'emploi, ou null tant que le poste n'a jamais rien reçu.</summary>
    public CatalogueLocal? Catalogue { get; private set; }

    /// <summary>Clients à crédit avec leur dernier solde connu (contrôle du plafond hors ligne, 7.8).</summary>
    public IReadOnlyList<ClientCredit> Clients { get; private set; } = [];

    /// <summary>En-tête du reçu (identité de l'entreprise, régime de TVA), pour imprimer un reçu hors ligne.</summary>
    public EnteteRecu? Entete { get; private set; }

    /// <summary>Date de la dernière mise à jour réussie, affichée hors ligne (« données du … »).</summary>
    public DateTime? MiseAJourUtc { get; private set; }

    /// <summary>Relit ce que le poste a gardé (au démarrage, ou quand le serveur ne répond pas).</summary>
    public async Task ChargerAsync()
    {
        if (Catalogue is not null)
        {
            return;
        }

        var catalogue = await stockage.LireAsync<CatalogueCaisse>(StockageLocal.Cache, CleCatalogue);
        var grille = await stockage.LireAsync<GrillePrix>(StockageLocal.Cache, CleGrille);
        if (catalogue is not null && grille is not null)
        {
            Catalogue = new CatalogueLocal(catalogue, grille);
            Clients = await stockage.LireAsync<List<ClientCredit>>(StockageLocal.Cache, CleClients) ?? [];
            Entete = await stockage.LireAsync<EnteteRecu>(StockageLocal.Cache, CleEntete);
            MiseAJourUtc = await stockage.LireAsync<DateTime?>(StockageLocal.Cache, CleDate);
        }
    }

    /// <summary>Demande au serveur des données fraîches et les garde ; faux si le serveur ne répond pas (rien n'est perdu).</summary>
    public async Task<bool> RafraichirAsync()
    {
        var catalogue = await api.GetAsync<CatalogueCaisse>("api/referentiel/catalogue-caisse");
        var grille = await api.GetAsync<GrillePrix>("api/prix/grille");
        var clients = await api.GetAsync<List<ClientCredit>>("api/credit/clients");
        var entete = await api.GetAsync<EnteteRecu>("api/caisse/entete");
        if (catalogue.Valeur is null || grille.Valeur is null)
        {
            await ChargerAsync();
            return false;
        }

        Catalogue = new CatalogueLocal(catalogue.Valeur, grille.Valeur);
        Clients = clients.Valeur ?? Clients;
        Entete = entete.Valeur ?? Entete;
        MiseAJourUtc = DateTime.UtcNow;
        await stockage.EcrireAsync(StockageLocal.Cache, CleCatalogue, catalogue.Valeur);
        await stockage.EcrireAsync(StockageLocal.Cache, CleGrille, grille.Valeur);
        await stockage.EcrireAsync(StockageLocal.Cache, CleClients, Clients);
        await stockage.EcrireAsync(StockageLocal.Cache, CleDate, MiseAJourUtc);
        if (Entete is not null)
        {
            await stockage.EcrireAsync(StockageLocal.Cache, CleEntete, Entete);
        }
        return true;
    }
}
