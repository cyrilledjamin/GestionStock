using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace GestionStock.E2E.Infrastructure;

/// <summary>Compte du personnel prêt à l'emploi (mot de passe définitif, sans changement demandé).</summary>
public sealed record Compte(Guid Id, string Matricule, string MotDePasse, string NomComplet);

/// <summary>Article vendable : son conditionnement « unité », prix et stock en place.</summary>
public sealed record ArticleVendable(Guid ArticleId, Guid ConditionnementId, string Code, string Libelle);

/// <summary>
/// Préparation des données d'un parcours par l'API, comme le ferait un autre utilisateur : le parcours ne teste dans le
/// navigateur que ce qui est son sujet. Tout passe par les routes publiques et leurs permissions.
/// </summary>
public sealed class Donnees
{
    public const string MotDePasse = "Parcours123!";

    private static readonly HttpClient _http = new() { BaseAddress = new Uri(Environnement.Url + "/"), Timeout = TimeSpan.FromSeconds(60) };
    private static readonly SemaphoreSlim _verrou = new(1, 1);
    private static string? _jetonAdministrateur;
    private static Compte? _preparateur;

    public static async Task<string> ConnexionAsync(string identifiant, string motDePasse)
    {
        using var reponse = await _http.PostAsJsonAsync("api/identite/connexion", new { identifiant, motDePasse });
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();
    }

    /// <summary>Crée un compte (rôles, langue), puis change son mot de passe provisoire comme le ferait l'employé.</summary>
    public async Task<Compte> CompteAsync(string prenom, string nom, string[] roles, string langue = "fr")
    {
        var fiche = await EnvoyerAsync(await JetonAdministrateurAsync(), HttpMethod.Post, "api/identite/utilisateurs",
            new { prenom, nom, roles, langue, motDePasse = "Provis0ire!" });
        var compte = fiche!["compte"]!;
        var matricule = compte["matricule"]!.GetValue<string>();
        var provisoire = await ConnexionAsync(matricule, "Provis0ire!");
        await EnvoyerAsync(provisoire, HttpMethod.Post, "api/identite/moi/mot-de-passe", new { actuel = "Provis0ire!", nouveau = MotDePasse });
        return new Compte(compte["id"]!.GetValue<Guid>(), matricule, MotDePasse, $"{prenom} {nom}");
    }

    /// <summary>
    /// Article vendu à l'unité, avec un prix et du stock (réception validée) : préparé par un compte qui cumule
    /// Gérant, Magasinier et Comptable.
    /// </summary>
    public async Task<ArticleVendable> ArticleEnStockAsync(string code, string libelle, decimal prixTtc, decimal quantite, decimal coutUnitaire)
    {
        var jeton = await JetonPreparateurAsync();
        var article = await EnvoyerAsync(jeton, HttpMethod.Post, "api/referentiel/articles",
            new { code, libelle, uniteStock = "pièce", precisionQuantite = 0 });
        var articleId = article!["id"]!.GetValue<Guid>();
        var conditionnements = await EnvoyerAsync(jeton, HttpMethod.Get, $"api/referentiel/articles/{articleId}/conditionnements", null);
        var conditionnementId = conditionnements!.AsArray()[0]!["id"]!.GetValue<Guid>();
        await EnvoyerAsync(jeton, HttpMethod.Post, $"api/prix/conditionnements/{conditionnementId}", new { prixTtc });

        if (quantite > 0)
        {
            var fournisseur = await EnvoyerAsync(jeton, HttpMethod.Post, "api/achats/fournisseurs", new { nom = $"Fournisseur {code}", actif = true });
            var reception = await EnvoyerAsync(jeton, HttpMethod.Post, "api/achats/receptions", new
            {
                fournisseurId = fournisseur!["id"]!.GetValue<Guid>(),
                lignes = new[] { new { articleId, quantite, coutUnitaire } },
            });
            await EnvoyerAsync(jeton, HttpMethod.Post, $"api/achats/receptions/{reception!["id"]!.GetValue<Guid>()}/validation", null);
        }

        return new ArticleVendable(articleId, conditionnementId, code, libelle);
    }

    /// <summary>Client à crédit avec un plafond.</summary>
    public async Task<Guid> ClientAsync(string nom, string telephone, decimal plafond)
    {
        var jeton = await JetonPreparateurAsync();
        var client = await EnvoyerAsync(jeton, HttpMethod.Post, "api/credit/clients", new { nom, telephone, actif = true });
        var id = client!["id"]!.GetValue<Guid>();
        await EnvoyerAsync(jeton, HttpMethod.Put, $"api/credit/clients/{id}/plafond", new { plafondCredit = plafond });
        return id;
    }

    /// <summary>Réception validée d'un article (marchandise en stock, dette fournisseur créée) ; renvoie son numéro.</summary>
    public async Task<string> ReceptionValideeAsync(Guid articleId, decimal quantite, decimal coutUnitaire)
    {
        var jeton = await JetonPreparateurAsync();
        var fournisseur = await EnvoyerAsync(jeton, HttpMethod.Post, "api/achats/fournisseurs", new { nom = $"Grossiste {Guid.NewGuid():N}"[..20], actif = true });
        var reception = await EnvoyerAsync(jeton, HttpMethod.Post, "api/achats/receptions", new
        {
            fournisseurId = fournisseur!["id"]!.GetValue<Guid>(),
            lignes = new[] { new { articleId, quantite, coutUnitaire } },
        });
        var id = reception!["id"]!.GetValue<Guid>();
        await EnvoyerAsync(jeton, HttpMethod.Post, $"api/achats/receptions/{id}/validation", null);
        return reception["numero"]!.GetValue<string>();
    }

    /// <summary>Inventaire partiel en cours, l'article compté ; renvoie son numéro.</summary>
    public async Task<string> InventaireCompteAsync(Guid articleId, decimal quantite)
    {
        var jeton = await JetonPreparateurAsync();
        var inventaire = await EnvoyerAsync(jeton, HttpMethod.Post, "api/inventaires", new { type = "Partiel", articles = new[] { articleId } });
        var fiche = inventaire!["inventaire"] ?? inventaire;
        await EnvoyerAsync(jeton, HttpMethod.Post, $"api/inventaires/{fiche["id"]!.GetValue<Guid>()}/comptages", new { articleId, quantite, motifEcart = "Aucun" });
        return fiche["numero"]!.GetValue<string>();
    }

    /// <summary>Démarque déclarée par le magasinier, en attente de validation ; renvoie son numéro.</summary>
    public async Task<string> DemarqueDeclareeAsync(Guid articleId, decimal quantite)
    {
        var demarque = await EnvoyerAsync(await JetonPreparateurAsync(), HttpMethod.Post, "api/demarques",
            new { articleId, quantite, motif = "Casse", commentaire = "Parcours" });
        return demarque!["numero"]!.GetValue<string>();
    }

    /// <summary>Session ouverte par un caissier sur le premier poste, puis laissée ouverte (décision 36).</summary>
    public async Task SessionOuverteAsync(Compte caissier, decimal fond)
    {
        await LibererPostesAsync();
        var jeton = await ConnexionAsync(caissier.Matricule, caissier.MotDePasse);
        var postes = await EnvoyerAsync(jeton, HttpMethod.Get, "api/caisse/postes", null);
        await EnvoyerAsync(jeton, HttpMethod.Post, "api/caisse/sessions", new { numeroCaisse = postes!.AsArray()[0]!.GetValue<string>(), fondCaisse = fond });
    }

    /// <summary>
    /// Libère les postes de caisse : une session restée ouverte (parcours interrompu) est clôturée par le Gérant à la
    /// place de son titulaire (décision 36), comme en boutique.
    /// </summary>
    public async Task LibererPostesAsync()
    {
        var jeton = await JetonPreparateurAsync();
        var ouvertes = await EnvoyerAsync(jeton, HttpMethod.Get, "api/caisse/sessions?statut=Ouverte", null);
        foreach (var session in ouvertes!.AsArray())
        {
            await EnvoyerAsync(jeton, HttpMethod.Post, $"api/caisse/sessions/{session!["id"]!.GetValue<Guid>()}/cloture", new { especesComptees = 0 });
        }
    }

    /// <summary>Appel authentifié ; le contenu JSON de la réponse (null si vide). Échoue sur un code d'erreur.</summary>
    public static async Task<JsonNode?> EnvoyerAsync(string jeton, HttpMethod methode, string route, object? corps)
    {
        using var requete = new HttpRequestMessage(methode, route);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        if (corps is not null)
        {
            requete.Content = JsonContent.Create(corps);
        }

        using var reponse = await _http.SendAsync(requete);
        var texte = await reponse.Content.ReadAsStringAsync();
        if (!reponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{methode} {route} : {(int)reponse.StatusCode} {texte}");
        }

        return texte.Length == 0 ? null : JsonNode.Parse(texte);
    }

    public static async Task<string> JetonAdministrateurAsync()
    {
        await _verrou.WaitAsync();
        try
        {
            return _jetonAdministrateur ??= await ConnexionAsync(Environnement.Administrateur, Environnement.MotDePasseAdministrateur);
        }
        finally
        {
            _verrou.Release();
        }
    }

    private async Task<string> JetonPreparateurAsync()
    {
        if (_preparateur is null)
        {
            var compte = await CompteAsync("Preparation", "Donnees", ["Gerant", "Magasinier", "Comptable"]);
            await _verrou.WaitAsync();
            try
            {
                _preparateur ??= compte;
            }
            finally
            {
                _verrou.Release();
            }
        }

        return await ConnexionAsync(_preparateur.Matricule, _preparateur.MotDePasse);
    }
}
