namespace GestionStock.Client.Services;

/// <summary>
/// Données de la caisse gardées sur le poste (7.8) : catalogue avec codes, grille de prix et clients à crédit (dernier
/// solde connu). Rafraîchies à l'ouverture de la caisse puis régulièrement tant que le serveur répond ; relues du
/// stockage du poste quand il ne répond plus. Chaque donnée garde sa version (ENF10) : le serveur ne renvoie que ce qui
/// a changé (304 sinon), et les quatre demandes partent ensemble.
/// </summary>
public sealed class CacheCaisse(Api api, StockageLocal stockage)
{
    private const string CleCatalogue = "catalogue";
    private const string CleGrille = "grille";
    private const string CleClients = "clients";
    private const string CleDate = "cache-date";
    private const string CleEntete = "entete";
    private const string SuffixeVersion = "-version";

    /// <summary>Âge au-delà duquel les prix gardés ne servent plus en ligne : le serveur est interrogé.</summary>
    public static readonly TimeSpan DureeFraicheur = TimeSpan.FromMinutes(10);

    private CatalogueCaisse? _catalogue;
    private GrillePrix? _grille;
    private bool _relu;

    /// <summary>Catalogue prêt à l'emploi, ou null tant que le poste n'a jamais rien reçu.</summary>
    public CatalogueLocal? Catalogue { get; private set; }

    /// <summary>Clients à crédit avec leur dernier solde connu (contrôle du plafond hors ligne, 7.8).</summary>
    public IReadOnlyList<ClientCredit> Clients { get; private set; } = [];

    /// <summary>En-tête du reçu (identité de l'entreprise, régime de TVA), pour imprimer un reçu hors ligne.</summary>
    public EnteteRecu? Entete { get; private set; }

    /// <summary>Date de la dernière mise à jour réussie, affichée hors ligne (« données du … »).</summary>
    public DateTime? MiseAJourUtc { get; private set; }

    /// <summary>
    /// Vrai si le catalogue et les prix ont été vérifiés auprès du serveur depuis moins de <see cref="DureeFraicheur"/> :
    /// la caisse lit alors scan et prix sur le poste, sans aller-retour (le serveur relit le prix à l'encaissement).
    /// </summary>
    public bool EstFrais => Catalogue is not null && MiseAJourUtc is { } le && DateTime.UtcNow - le < DureeFraicheur;

    /// <summary>Relit ce que le poste a gardé (au démarrage, ou quand le serveur ne répond pas).</summary>
    public async Task ChargerAsync()
    {
        if (_relu)
        {
            return;
        }

        _relu = true;
        _catalogue = await stockage.LireAsync<CatalogueCaisse>(StockageLocal.Cache, CleCatalogue);
        _grille = await stockage.LireAsync<GrillePrix>(StockageLocal.Cache, CleGrille);
        if (_catalogue is not null && _grille is not null)
        {
            Catalogue = new CatalogueLocal(_catalogue, _grille);
            Clients = await stockage.LireAsync<List<ClientCredit>>(StockageLocal.Cache, CleClients) ?? [];
            Entete = await stockage.LireAsync<EnteteRecu>(StockageLocal.Cache, CleEntete);
            MiseAJourUtc = await stockage.LireAsync<DateTime?>(StockageLocal.Cache, CleDate);
        }
    }

    /// <summary>
    /// Demande au serveur ce qui a changé et le garde ; faux si le serveur ne répond pas (rien n'est perdu). Une donnée
    /// inchangée (304) n'est ni retéléchargée ni réécrite.
    /// </summary>
    public async Task<bool> RafraichirAsync()
    {
        await ChargerAsync();
        var versions = await Task.WhenAll(
            VersionAsync(CleCatalogue, _catalogue),
            VersionAsync(CleGrille, _grille),
            VersionAsync(CleClients, Catalogue is null ? null : Clients));
        var catalogueTache = api.GetVersionneAsync<CatalogueCaisse>("api/referentiel/catalogue-caisse", versions[0]);
        var grilleTache = api.GetVersionneAsync<GrillePrix>("api/prix/grille", versions[1]);
        var clientsTache = api.GetVersionneAsync<List<ClientCredit>>("api/credit/clients", versions[2]);
        var enteteTache = api.GetAsync<EnteteRecu>("api/caisse/entete");
        await Task.WhenAll(catalogueTache, grilleTache, clientsTache, enteteTache);

        var catalogue = catalogueTache.Result.Valeur;
        var grille = grilleTache.Result.Valeur;
        if (catalogue is null || grille is null)
        {
            return false;
        }

        if (!catalogue.NonModifie && catalogue.Valeur is not null)
        {
            _catalogue = catalogue.Valeur;
            await GarderAsync(CleCatalogue, _catalogue, catalogue.Version);
        }

        if (!grille.NonModifie && grille.Valeur is not null)
        {
            _grille = grille.Valeur;
            await GarderAsync(CleGrille, _grille, grille.Version);
        }

        if (clientsTache.Result.Valeur is { NonModifie: false, Valeur: { } clients } lecture)
        {
            Clients = clients;
            await GarderAsync(CleClients, Clients, lecture.Version);
        }

        if (_catalogue is null || _grille is null)
        {
            return false;
        }

        if (!catalogue.NonModifie || !grille.NonModifie || Catalogue is null)
        {
            Catalogue = new CatalogueLocal(_catalogue, _grille);
        }

        Entete = enteteTache.Result.Valeur ?? Entete;
        MiseAJourUtc = DateTime.UtcNow;
        await stockage.EcrireAsync(StockageLocal.Cache, CleDate, MiseAJourUtc);
        if (Entete is not null)
        {
            await stockage.EcrireAsync(StockageLocal.Cache, CleEntete, Entete);
        }

        return true;
    }

    /// <summary>Version gardée d'une donnée, seulement si la donnée elle-même est sur le poste.</summary>
    private async Task<string?> VersionAsync(string cle, object? donnee) =>
        donnee is null ? null : await stockage.LireAsync<string>(StockageLocal.Cache, cle + SuffixeVersion);

    private async Task GarderAsync<T>(string cle, T donnee, string? version)
    {
        await stockage.EcrireAsync(StockageLocal.Cache, cle, donnee);
        if (version is not null)
        {
            await stockage.EcrireAsync(StockageLocal.Cache, cle + SuffixeVersion, version);
        }
    }
}
