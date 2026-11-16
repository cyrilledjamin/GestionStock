# GestionStock

[![CI](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml/badge.svg)](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml)

Logiciel de gestion de stock avec caisse intégrée pour une petite entreprise de négoce au Cameroun
(boutique mono-site en V1). Le dossier d'analyse complet — règles RG01 à RG47, modèle UML, décisions —
est dans [docs/dossier-analyse.md](docs/dossier-analyse.md).

## Stack

- .NET 10, ASP.NET Core Minimal API, EF Core 10 + Npgsql (PostgreSQL)
- Client Blazor WebAssembly servi par l'API (même origine), sans ressource externe
- Monolithe modulaire : un projet `.Contracts` public et un projet interne par module, un schéma PostgreSQL par module
- Tests : xUnit v3 (Microsoft.Testing.Platform), NetArchTest, Testcontainers
- Déploiement : Docker Compose, conteneur Linux

## Structure

```
src/
  Client/
    GestionStock.Client              Blazor WebAssembly (back-office), servi par l'hôte ; textes fr / en
  BuildingBlocks/
    GestionStock.SharedKernel        C# pur : IHorloge, IUtilisateurCourant
    GestionStock.Infrastructure      connexion et transaction partagées, stockage de fichiers, santé, erreurs HTTP
  Host/
    GestionStock.Api                 assemble les modules (AddXxxModule / MapXxxEndpoints)
  Modules/Achats/
    GestionStock.Achats.Contracts    contrat public : types de documents (journal des mouvements)
    GestionStock.Achats              fournisseurs, réception simple (brouillon → validation → entrée en stock)
  Modules/Caisse/
    GestionStock.Caisse.Contracts    contrat public : IGenerateurRecu et modèle de reçu (décision 17)
    GestionStock.Caisse              sessions, tickets, avoirs, paiements, remboursements, reçu PDF
  Modules/Credit/
    GestionStock.Credit.Contracts    contrat public : IServiceCredit (vente à crédit, remboursement, annulation)
    GestionStock.Credit              clients, plafonds, journal des opérations, imputations, impayés
  Modules/Identite/
    GestionStock.Identite.Contracts  contrat public : Roles, Permissions, IValidationResponsable
    GestionStock.Identite            comptes locaux (ASP.NET Core Identity), jetons porteurs, matrice des permissions
  Modules/Prix/
    GestionStock.Prix.Contracts      contrat public : IConsultationPrix (prix applicable), RegimeTva (HT / TVA)
    GestionStock.Prix                prix de vente par conditionnement, historisés
  Modules/Referentiel/
    GestionStock.Referentiel.Contracts  contrat public : IConsultationReferentiel (articles, site de la boutique)
    GestionStock.Referentiel         articles, site et emplacements (style CRUD)
  Modules/Stock/
    GestionStock.Stock.Contracts     contrat public : IServiceMouvementsStock, IConsultationStock
    GestionStock.Stock.Domain        C# pur : MouvementStock, StockSite, StockEmplacement
    GestionStock.Stock               implémentation : Application / Infrastructure / Endpoints (lecture seule)
tests/
  GestionStock.Api.IntegrationTests  API complète par HTTP sur PostgreSQL réelle (Testcontainers)
  GestionStock.ArchitectureTests     règles de dépendance et conventions
  GestionStock.Infrastructure.Tests  briques transverses
  GestionStock.Stock.Domain.Tests    règles du domaine Stock
  GestionStock.Stock.IntegrationTests  PostgreSQL réelle (Testcontainers)
deploy/
  docker-compose.yml                 PostgreSQL 17 (db) et API (api)
  Dockerfile                         image Linux de l'API
  .env.example                       variables à copier dans deploy/.env
docs/                                dossier d'analyse
```

Règles de dépendance, vérifiées par `tests/GestionStock.ArchitectureTests` :
- un module n'en référence un autre que par son projet `.Contracts` ;
- `GestionStock.SharedKernel` et les projets `.Domain` sont en C# pur, sans aucune dépendance ;
- un projet `.Contracts` ne dépend que de la bibliothèque de base et du SharedKernel ;
- `GestionStock.Infrastructure` ne dépend d'aucun module ;
- l'hôte n'accède ni aux domaines des modules ni à EF Core ;
- dans un module, seul `Module<Nom>` est public (hors migrations générées) ; `Endpoints` ne touche pas
  à la persistance, et `Application` / `Infrastructure` ne dépendent pas d'`Endpoints` ;
- seul le module Stock écrit les mouvements et les soldes (RG01) ;
- le client Blazor ne référence aucun projet serveur ; ses permissions et rôles sont ceux de l'API, et chaque texte
  de l'interface existe en français et en anglais (vérifié sur ses sources).

Les assemblages sont découverts à partir de l'hôte et classés selon leur nom : un nouveau module
branché sur l'hôte est couvert sans modifier les tests.

## Prérequis

- SDK .NET 10.0.403 ou supérieur (voir [global.json](global.json))
- Docker Desktop avec le moteur Linux, pour la base locale et les tests d'intégration
- Outils locaux : `dotnet tool restore` (installe `dotnet-ef` 10)

## Commandes

Première installation :

```bash
dotnet tool restore
cp deploy/.env.example deploy/.env
```

Base locale, puis API (en Development, les migrations s'appliquent au démarrage) :

```bash
docker compose -f deploy/docker-compose.yml up -d db
dotnet run --project src/Host/GestionStock.Api
```

Build et tests :

```bash
dotnet build
dotnet test
```

Base et API en conteneurs, comme en boutique (application sur http://localhost:8080) :

```bash
docker compose -f deploy/docker-compose.yml up -d --build
```

Sans SDK .NET 10 sur le poste, build et tests peuvent tourner dans le conteneur du SDK, sur une copie du dépôt
(le socket Docker permet aux tests d'intégration de lancer leur PostgreSQL) :

```bash
docker run --rm -v "$PWD:/src:ro" -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal mcr.microsoft.com/dotnet/sdk:10.0 bash -c "cp -r /src /work && cd /work && dotnet test"
```

Si le port 5432 du poste est déjà pris (PostgreSQL installé sous Windows, par exemple), choisir un autre
`POSTGRES_PORT` dans `deploy/.env`.

Nouvelle migration d'un module (le code généré va dans `Infrastructure/Migrations`) :

```bash
dotnet ef migrations add <Nom> -p src/Modules/<Module>/GestionStock.<Module> -s src/Host/GestionStock.Api -c <Module>DbContext -o Infrastructure/Migrations
```

## Configuration

Uniquement par variables d'environnement ou `appsettings` ; aucun chemin ni adresse en dur dans le code.

| Clé | Variable d'environnement | Rôle |
| --- | --- | --- |
| `ConnectionStrings:GestionStock` | `ConnectionStrings__GestionStock` | Chaîne de connexion PostgreSQL (obligatoire) ; le chiffrement GSS (Kerberos) y est désactivé sauf si `GSS Encryption Mode` est fourni |
| `Stockage:RepertoireRacine` | `Stockage__RepertoireRacine` | Répertoire des fichiers (reçus, imports) ; `donnees/fichiers` par défaut. Contient aussi `cles-protection/`, les clés qui signent les jetons de connexion : conservées, elles évitent qu'un redémarrage déconnecte tout le monde (à inclure dans les sauvegardes) |
| `BaseDeDonnees:AppliquerMigrationsAuDemarrage` | `BaseDeDonnees__AppliquerMigrationsAuDemarrage` | Applique les migrations de chaque module au démarrage ; `false` par défaut, `true` en Development et dans docker-compose |
| `Entreprise:Nom`, `:Adresse`, `:Telephone`, `:NumeroContribuable` | `Entreprise__Nom`… | Identité imprimée en tête des reçus |
| `Caisse:Postes` | `Caisse__Postes__0`, `Caisse__Postes__1`… | Postes de caisse de la boutique ; `CAISSE-1` par défaut |
| `Entreprise:AssujettiTva` | `Entreprise__AssujettiTva` | Entreprise assujettie à la TVA (RG37) : reçu HT / TVA / TTC ; `false` par défaut, prix nets (`ENTREPRISE_ASSUJETTIE_TVA` dans `deploy/.env`) |
| `Entreprise:TauxTva` | `Entreprise__TauxTva` | Taux légal en %, 19,25 par défaut |
| `Identite:AdministrateurInitial:Identifiant` | `Identite__AdministrateurInitial__Identifiant` | Compte Administrateur créé au démarrage si la base ne contient aucun compte (`ADMIN_IDENTIFIANT` dans `deploy/.env`) |
| `Identite:AdministrateurInitial:MotDePasse` | `Identite__AdministrateurInitial__MotDePasse` | Son mot de passe (`ADMIN_MOT_DE_PASSE`) ; à changer en boutique |

En Development, [appsettings.Development.json](src/Host/GestionStock.Api/appsettings.Development.json) pointe vers
la base de docker-compose avec le mot de passe de développement de [deploy/.env.example](deploy/.env.example).
En boutique, le mot de passe est défini uniquement dans `deploy/.env`, qui n'est jamais versionné.

Chaque module a son schéma PostgreSQL et sa propre table d'historique des migrations
(`stock.__ef_migrations_history`). La base n'écoute que sur `127.0.0.1` ; l'API écoute sur le port 8080.
Si le port 5432 est déjà pris (PostgreSQL installé sur le poste), choisir un autre `POSTGRES_PORT` dans
`deploy/.env` et, pour `dotnet run`, le reporter dans `ConnectionStrings__GestionStock`.

## Endpoints

| Route | Accès | Rôle |
| --- | --- | --- |
| `GET /health` | anonyme | Le processus répond (200) |
| `GET /health/pret` | anonyme | La base de données répond (200), sinon 503 |
| `GET /openapi/v1.json` | anonyme | Description OpenAPI, en Development uniquement |
| `POST /api/identite/connexion` | anonyme | `{ identifiant, motDePasse }` → jeton d'accès (1 h) et jeton de renouvellement |
| `POST /api/identite/rafraichir` | anonyme | `{ jetonRenouvellement }` → nouveau jeton d'accès |
| `GET /api/identite/moi` | connecté | Identifiant, rôles et permissions de l'utilisateur |
| `GET /api/identite/utilisateurs` | `Identite.Utilisateurs.Gerer` | Liste des comptes et de leurs rôles |
| `POST /api/identite/utilisateurs` | `Identite.Utilisateurs.Gerer` | `{ identifiant, motDePasse, role }` → crée un compte |
| `GET /api/referentiel/articles` | `Referentiel.Consulter` | Articles actifs ; `?recherche=` (code ou libellé, sans casse), `?inclureInactifs=true` |
| `GET /api/referentiel/articles/{id}` | `Referentiel.Consulter` | Un article (404 s'il n'existe pas) |
| `POST /api/referentiel/articles` | `Referentiel.Articles.Gerer` | `{ code, libelle, libelleTraduit?, uniteStock, precisionQuantite }` → crée un article (409 si le code existe) |
| `PUT /api/referentiel/articles/{id}` | `Referentiel.Articles.Gerer` | `{ libelle, libelleTraduit?, actif }` : le code, l'unité et la précision sont fixés à la création |
| `GET /api/referentiel/site` | `Referentiel.Consulter` | Site de la boutique et son emplacement de vente |
| `GET /api/referentiel/articles/{id}/conditionnements` | `Referentiel.Consulter` | Conditionnements de l'article et leurs codes |
| `POST /api/referentiel/articles/{id}/conditionnements` | `Referentiel.Articles.Gerer` | `{ libelle, niveau, quantiteUniteStock }` (niveau : Unite, Pack, Carton, Sac, Palette) |
| `PUT /api/referentiel/conditionnements/{id}` | `Referentiel.Articles.Gerer` | `{ libelle, actif }` : le facteur est fixé à la création |
| `POST /api/referentiel/conditionnements/{id}/codes` | `Referentiel.Articles.Gerer` | `{ valeur }` : EAN-13, EAN-8 ou UPC-A, clé contrôlée (409 si déjà attribué) |
| `POST /api/referentiel/conditionnements/{id}/codes/interne` | `Referentiel.Articles.Gerer` | Génère un code interne `INT000001` à imprimer en étiquette |
| `POST /api/referentiel/codes/{id}/desactivation` | `Referentiel.Articles.Gerer` | Désactive un code (il reste dans l'historique) |
| `GET /api/referentiel/scan/{valeur}` | `Referentiel.Consulter` | Article + conditionnement d'un code scanné ou du code article (404 si inconnu) |
| `GET /api/referentiel/vendables?recherche=` | `Referentiel.Consulter` | Articles actifs (code ou libellé) avec chacun de leurs conditionnements actifs |
| `GET /api/prix/regime` | `Prix.Consulter` | Régime de TVA : `{ assujetti, tauxPourcent, tauxApplique }` |
| `GET /api/prix/articles/{id}` | `Prix.Consulter` | Conditionnements de l'article avec prix en vigueur et prochain prix programmé |
| `GET /api/prix/conditionnements/{id}` | `Prix.Consulter` | Prix applicable aujourd'hui (204 s'il n'y en a pas) |
| `GET /api/prix/conditionnements/{id}/historique` | `Prix.Consulter` | Tous les prix du conditionnement, du plus récent au plus ancien |
| `POST /api/prix/conditionnements/{id}` | `Prix.Gerer` | `{ prixTtc, debutValidite? }` : nouveau prix, dès aujourd'hui ou à une date future |
| `GET /api/achats/fournisseurs` | `Achats.Consulter` | Fournisseurs actifs ; `?inclureInactifs=true` |
| `POST /api/achats/fournisseurs`, `PUT /api/achats/fournisseurs/{id}` | `Achats.Fournisseurs.Gerer` | `{ nom, telephone?, actif }` |
| `GET /api/achats/receptions`, `GET /api/achats/receptions/{id}` | `Achats.Consulter` | Réceptions (la plus récente d'abord) ; détail avec lignes et articles |
| `POST /api/achats/receptions`, `PUT /api/achats/receptions/{id}` | `Achats.Receptions.Saisir` | `{ fournisseurId, numeroBonLivraison?, lignes: [{ articleId, quantite, coutUnitaire }] }` : brouillon |
| `POST /api/achats/receptions/{id}/validation` | `Achats.Receptions.Valider` | Valide et fait entrer la marchandise en stock (409 si déjà validée) |
| `GET /api/credit/clients?recherche=` | `Credit.Consulter` | Clients (nom ou téléphone) avec solde dû et montant en retard |
| `GET /api/credit/clients/{id}` | `Credit.Consulter` | Fiche : client, ventes non soldées (reste dû, échéance), journal des opérations |
| `GET /api/credit/impayes` | `Credit.Consulter` | Clients ayant une dette échue (RG43) |
| `POST /api/credit/clients`, `PUT /api/credit/clients/{id}` | `Credit.Clients.Gerer` | `{ nom, telephone, delaiPaiementJours?, actif }` |
| `PUT /api/credit/clients/{id}/plafond` | `Credit.Plafonds.Gerer` | `{ plafondCredit }` |
| `GET /api/caisse/postes` | `Caisse.Vendre` | Postes de caisse configurés |
| `POST /api/caisse/sessions` | `Caisse.Vendre` | `{ numeroCaisse, fondCaisse }` : ouvre la session du caissier connecté |
| `GET /api/caisse/sessions/courante` | `Caisse.Vendre` | Session ouverte du caissier avec ses totaux (204 s'il n'en a pas) |
| `POST /api/caisse/sessions/courante/cloture` | `Caisse.Vendre` | `{ especesComptees }` : clôture, écart calculé |
| `GET /api/caisse/sessions`, `GET /api/caisse/sessions/{id}` | `Caisse.Sessions.Consulter` | Sessions (`?statut=Ouverte|Comptee|Validee`) et leurs totaux |
| `POST /api/caisse/sessions/{id}/validation` | `Caisse.Sessions.Valider` | Valide une session clôturée et son écart |
| `POST /api/caisse/tickets` | `Caisse.Vendre` | Vente : `{ idIdempotence, clientId?, lignes: [{ conditionnementId, quantite, remise?, codeScanne? }], paiements: [{ mode, montant, reference? }], validation? }` ; 201, ou 200 si déjà reçue |
| `POST /api/caisse/avoirs` | `Caisse.Vendre` | `{ idIdempotence, ticketOrigineId, lignes: [{ ligneOrigineId, quantite }], modeRemboursement?, reference? }` |
| `POST /api/caisse/remboursements` | `Caisse.Vendre` | Remboursement de crédit : `{ idIdempotence, clientId, mode, montant, reference? }` |
| `GET /api/caisse/tickets/{id}`, `/tickets/numero/{numero}`, `/sessions/{id}/tickets` | `Caisse.Tickets.Consulter` | Tickets (quantités déjà rendues par ligne) |
| `GET /api/caisse/tickets/{id}/recu.pdf` | `Caisse.Tickets.Consulter` | Reçu PDF 80 mm dans la langue de la requête |
| `GET /api/stock/soldes` | `Stock.Consulter` | Soldes article × site de la boutique : physique, réservé, bloqué, disponible, CUMP |
| `GET /api/stock/articles/{id}/mouvements` | `Stock.Consulter` | Journal d'un article, du plus récent au plus ancien (`?limite=`, 100 par défaut, 1000 au plus) |

Tout autre endpoint est refusé par défaut (401 sans jeton) : chaque endpoint métier déclare sa permission
(`RequireAuthorization(Permissions.X)`), 403 si l'utilisateur ne la détient pas. Les jetons s'envoient dans
l'en-tête `Authorization: Bearer <jeton>`. Les erreurs sont des ProblemDetails : titre traduit selon
`Accept-Language` (fr par défaut, en) et code stable dans l'extension `code` (ex. `Identite.IdentifiantsInvalides`).

## Écrans (client Blazor)

Ouvrir http://localhost:8080 (docker-compose) puis se connecter. Le menu dépend des permissions de l'utilisateur.

| Écran | Contenu |
| --- | --- |
| Connexion | Identifiant et mot de passe ; choix de la langue (français / anglais), mémorisé sur le poste |
| Accueil | Raccourcis selon le rôle |
| Stock | Soldes de la boutique : physique, disponible, CUMP, valeur ; total valorisé |
| Journal d'un article | Soldes et mouvements (date à l'heure de Douala, variation, coût, document d'origine, utilisateur) |
| Réceptions | Liste ; saisie en brouillon (lignes, valeur calculée), enregistrement, validation |
| Fournisseurs | Liste, création, modification, désactivation |
| Articles | Recherche, création, modification, désactivation |
| Fiche article | Conditionnements, codes-barres (saisis, générés, désactivés) et prix (en vigueur, programmé, nouveau) |
| Caisse | Ouverture de session (poste, fond) ; scan à la douchette (le curseur revient sur le scan après chaque ajout) ou recherche ; panier ; client et crédit ; paiements combinés avec rendu ; validation d'un responsable si remise ou plafond dépassé ; reçu 80 mm imprimable et PDF ; remboursement de crédit ; clôture avec écart |
| Tickets de la session, ticket | Recherche par numéro, réimpression, avoir (quantités à rendre, mode de remboursement) |
| Sessions de caisse | Supervision (Gérant, Comptable) : ventes nettes, espèces théoriques et comptées, écart, validation |
| Clients, fiche client | Clients à crédit, impayés, création (Gérant), plafond (Comptable), ventes non soldées, journal |
| Utilisateurs | Création des comptes avec leur rôle (Administrateur) |

Le reçu s'imprime par le navigateur du poste (feuille de style d'impression au format 80 mm, seul le reçu est
imprimé) ou se télécharge en PDF pour l'envoyer par WhatsApp. Le panier affiche les prix lus dans l'application ;
le serveur les relit à l'encaissement.

Les jetons de connexion restent dans le `sessionStorage` de l'onglet : ils disparaissent à sa fermeture (poste partagé).
Montants arrondis à l'unité en XAF, quantités selon la précision de l'article, libellé traduit quand l'interface est
en anglais. Hors ligne (PWA, IndexedDB) : tranche ultérieure, avec la caisse.

## Référentiel

- **Article** (UC01) : code unique, libellé principal obligatoire et traduction facultative (décision 20), unité de
  stock = plus petite unité vendue (RG44), précision des quantités de 0 à 3 décimales (RG46). Le code, l'unité et la
  précision ne changent plus après la création (les mouvements sont exprimés dans cette unité) ; un article n'est
  jamais supprimé, il est désactivé. Mode de suivi (lots, séries) et DLC sont prévus dans la table, inutilisés en V1.
- **Conditionnements** (RG18, RG44) : chaque article a d'office un conditionnement « Unité » de facteur 1 ; on y
  ajoute packs, cartons, sacs… avec leur nombre d'unités de stock, entier si l'article se vend à la pièce. Le facteur
  ne change plus après la création ; un conditionnement est désactivé, jamais supprimé (sauf l'unité, toujours active).
- **Codes** (RG26, RG33) : plusieurs codes par conditionnement ; EAN-13, EAN-8 et UPC-A avec contrôle de la clé
  modulo 10 ; un code actif ne désigne qu'un conditionnement (index unique filtré) ; un code réattribué est désactivé.
  Un article sans GTIN fiable reçoit un code interne `INT` + 6 chiffres, imprimé en Code 128.
- **Scan** (UC22, V1) : code actif d'un conditionnement (un UPC-A lu en EAN-13 avec un zéro de tête est reconnu),
  sinon code de l'article (conditionnement à l'unité). GS1-128, DataMatrix et poids variable : plus tard (RG24, RG27).
- **Site et emplacement** : la migration crée la boutique (`BOUTIQUE`, type Magasin, sans séparation réserve / rayon)
  et son emplacement de vente `VENTE` (RG20). Sans gestion par emplacements, la marchandise reçue y est disponible
  dès la validation (RG13). Pas d'écran de gestion des sites en V1 (mono-site).

## Prix de vente

- Prix **TTC par conditionnement** (RG45) : le carton peut coûter moins que la somme de ses unités. L'application est
  la seule référence des prix (RG37).
- Un changement de prix crée un **nouveau prix daté**, jamais une modification : l'historique est conservé. Le prix
  applicable un jour donné (date de Douala) est celui de date de début la plus récente sans dépasser ce jour ; le même
  jour, le dernier saisi l'emporte. Un prix peut être programmé à une date future, jamais dans le passé.
- **TVA** (RG37) : paramètre `Entreprise:AssujettiTva`. Assujettie, l'entreprise affiche HT, TVA 19,25 % et TTC ;
  sinon, prix nets et taux appliqué nul.

## Caisse

- **Session** (UC24, RG36) : le Caissier l'ouvre sur un poste avec un fond de caisse ; une seule session ouverte par
  poste et par caissier. Clôture par comptage des espèces : espèces théoriques = fond + espèces des ventes (moins le
  rendu) − espèces rendues sur les avoirs + remboursements de crédit en espèces ; l'écart est enregistré, puis validé
  par le Gérant ou le Comptable (8.3).
- **Ticket** (UC17) : le poste envoie les conditionnements et quantités ; **le prix vient du module Prix** (RG37).
  Validation en **une seule transaction** : ticket, un mouvement `VenteCaisse` par ligne depuis l'emplacement de vente
  (quantité × facteur du conditionnement, au CUMP courant), part à crédit. **Jamais refusé pour stock insuffisant**
  (RG22). Numérotation **continue par poste** (`CAISSE-1-000042`, RG34) : le compteur est verrouillé et annulé avec la
  transaction, donc sans trou même en cas de refus ou de ventes simultanées. Un ticket n'est jamais modifié.
- **Idempotence** : `idIdempotence` est généré par le poste ; un renvoi après coupure renvoie le ticket déjà enregistré
  (verrou transactionnel sur l'identifiant), et les mouvements de stock en dérivent leur propre identifiant.
- **Paiements** (RG35) : espèces, Mobile Money (référence obligatoire), carte, crédit (client obligatoire, RG39),
  combinables ; seules les espèces dépassent le total (monnaie rendue). Montants en **francs CFA entiers** (pas de
  subdivision en usage, 1.5) : chaque ligne est arrondie à l'unité, remises et paiements sont entiers.
- **Remise** : plafonds par rôle non définis, donc toute remise exige la validation d'un responsable (décision 31) ;
  **dépassement de plafond de crédit** : idem (RG40). Le responsable saisit ses identifiants sur le poste ; son
  identifiant est tracé sur le ticket.
- **Avoir** (UC18, RG34) : nouveau ticket numéroté lié au ticket d'origine, quantités bornées par ce qui reste à rendre,
  remise d'origine rendue au prorata ; la marchandise revient sur l'emplacement de vente (`AvoirCaisse`, au CUMP
  courant) ; la dette éventuelle du ticket est annulée d'abord, le reste est rendu en espèces (par défaut), Mobile Money
  ou carte.
- **Remboursement de crédit** (RG42) : encaissé dans la session, en espèces ou Mobile Money.
- **Reçu** (décision 26) : PDF 80 mm généré derrière `IGenerateurRecu` (décision 17), dans la langue de l'utilisateur,
  heure de Douala, HT / TVA / TTC si l'entreprise est assujettie, « prix nets » sinon. Police Courier (l'une des 14
  polices standard des lecteurs PDF, à chasse fixe comme un ticket thermique) : aucune police à embarquer, aucune
  dépendance. L'impression se fait sur le poste client.

## Crédit client

- **Client** (UC26) : nom et téléphone obligatoires (RG39), seules données personnelles conservées (loi n° 2024/017) ;
  plafond fixé par le Comptable, 0 tant qu'il n'est pas défini (décision 31) ; délai de paiement (30 jours par défaut).
- **Vente à crédit** (RG40, RG41) : la marchandise sort du stock ; au-delà du plafond, la vente exige la validation d'un
  responsable, tracée sur l'opération. Le client est verrouillé pendant l'opération : deux ventes simultanées ne
  dépassent pas ensemble le plafond.
- **Remboursement** (RG42) : imputé sur les ventes les plus anciennes ; jamais au-delà de la dette.
- **Avoir** sur un ticket à crédit : annule la dette de ce ticket, dans la limite de ce qui en reste dû.
- **Impayés** (RG43) : ventes non soldées dont l'échéance (date de vente + délai) est passée.
- Journal immuable des opérations (VenteCredit, Remboursement, Annulation) et de leurs imputations ; le solde dû du
  client est une projection mise à jour dans la même transaction.

## Réception simple (V1)

Sans commande fournisseur, lots ni frais d'approche (prévus plus tard) :

1. le Magasinier ou le Gérant saisit la réception en **brouillon** : fournisseur actif, numéro de bon de livraison
   facultatif, lignes article × quantité (en unité de stock, selon la précision de l'article) × coût unitaire ;
   un numéro `R-000001` est attribué ;
2. le brouillon reste modifiable (ses lignes sont remplacées) ;
3. la **validation** contrôle à nouveau chaque ligne, puis écrit dans **une seule transaction** le statut de la
   réception et un mouvement `EntreeReception` par ligne vers l'emplacement de vente (RG13), avec le CUMP recalculé
   par le module Stock (RG17). Si une ligne est refusée, rien n'est écrit. Une réception validée ne se modifie plus ;
   deux validations simultanées n'écrivent qu'une fois (verrou de la réception, idempotence des mouvements).

## Comptes, rôles et permissions

Comptes locaux nominatifs (ENF06), stockés dans le schéma `identite`. Mot de passe : 8 caractères au moins,
avec majuscule, minuscule, chiffre et caractère spécial ; compte verrouillé 5 minutes après 5 échecs.
Au premier démarrage, l'Administrateur initial est créé depuis la configuration ; il crée ensuite les autres comptes.

**Validation par un responsable** (`IValidationResponsable`) : une remise ou un dépassement de plafond de crédit
(RG40, décision 31) est validé par un responsable présent, qui saisit ses propres identifiants sur le poste du
caissier. Ses échecs comptent pour le verrouillage de son compte ; son identifiant est tracé avec l'opération.

Les cinq rôles de la section 8.3 du dossier : Administrateur, Gerant, Comptable, Caissier, Magasinier.
Chaque permission est attribuée à des rôles dans
[MatricePermissions.cs](src/Modules/Identite/GestionStock.Identite/Application/MatricePermissions.cs) :

| Permission | Rôles |
| --- | --- |
| `Identite.Utilisateurs.Gerer` | Administrateur |
| `Referentiel.Consulter`, `Stock.Consulter`, `Prix.Consulter` | tous |
| `Prix.Gerer` | Gerant |
| `Caisse.Depassements.Valider` | Gerant *(provisoire pour le crédit)* |
| `Credit.Consulter` | Gerant, Comptable, Caissier |
| `Credit.Clients.Gerer` | Gerant |
| `Credit.Plafonds.Gerer` | Comptable |
| `Caisse.Vendre` | Caissier |
| `Caisse.Tickets.Consulter` | Caissier, Gerant, Comptable |
| `Caisse.Sessions.Consulter`, `Caisse.Sessions.Valider` | Gerant, Comptable |
| `Referentiel.Articles.Gerer` | Gerant, Administrateur *(provisoire)* |
| `Achats.Consulter` | Gerant, Comptable, Magasinier |
| `Achats.Fournisseurs.Gerer` | Gerant, Magasinier *(provisoire)* |
| `Achats.Receptions.Saisir`, `Achats.Receptions.Valider` | Magasinier, Gerant *(validation provisoire)* |

## Tests

| Projet | Contenu | Docker |
| --- | --- | --- |
| `GestionStock.Stock.Domain.Tests` | Règles du domaine : mouvements, soldes, CUMP, RG03 / RG22 | Non |
| `GestionStock.Infrastructure.Tests` | Horloge, stockage de fichiers, chaîne de connexion, clés de protection conservées | Non |
| `GestionStock.Api.IntegrationTests` | API complète par HTTP : connexion, jetons, 401 / 403, comptes, articles, fournisseurs, réceptions (CUMP, transaction, validation simultanée), soldes et journal | Oui |
| `GestionStock.ArchitectureTests` | Dépendances entre projets, visibilité, couches, traductions | Non |
| `GestionStock.Stock.IntegrationTests` | PostgreSQL 17 réelle : migration, transaction partagée, idempotence, journal immuable, mouvements simultanés | Oui |

Les tests d'intégration démarrent un conteneur `postgres:17-alpine` (Testcontainers), appliquent les
migrations comme au démarrage de l'API, puis passent par le contrat public du module. Sans Docker, ils sont
**ignorés** sur un poste de développement (la raison est affichée) et **échouent** en intégration continue
(variable `CI=true`, définie par GitHub Actions).

## Intégration continue

[.github/workflows/ci.yml](.github/workflows/ci.yml) s'exécute à chaque push sur `master` et à chaque pull request,
sur une machine Linux de GitHub qui dispose de Docker :

- **Build et tests** : build en Release sans avertissement, puis tous les tests, y compris ceux sur PostgreSQL réelle ;
- **Image et docker-compose** : construit l'image de l'API, démarre `deploy/docker-compose.yml` avec les valeurs de
  `deploy/.env.example`, puis vérifie que `/health/pret` répond, que la migration `Initial` de chaque module est
  appliquée, qu'une route métier répond 401 sans jeton, que l'Administrateur initial se connecte et que le client
  Blazor est servi.

## Avancement du squelette

| Étape | Contenu | État |
| --- | --- | --- |
| 0 | Dépôt, `global.json`, `dotnet-ef` local | Fait |
| 1 | Solution, projets et références | Fait |
| 2 | BuildingBlocks : horloge, transaction partagée, stockage de fichiers | Fait |
| 3 | Domaine Stock : mouvements immuables, soldes, CUMP, RG03 / RG22 | Fait |
| 4 | Contrat et implémentation Stock (EF Core), hôte Api | Fait |
| 5 | docker-compose PostgreSQL et migration `Initial` | Fait (vérifié en CI) |
| 6 | Tests d'architecture | Fait |
| 7 | Tests d'intégration (Testcontainers) | Fait (exécutés en CI) |
| 8 | Module Identite : comptes locaux, rôles, permissions, jetons porteurs | Fait |
| 9 | Module Referentiel : articles, site et emplacement de vente | Fait |
| 10 | Module Achats minimal : fournisseurs, réception simple ; lectures du stock | Fait |
| 11 | Client Blazor WebAssembly : premiers écrans | Fait |
| 12 | Tests d'architecture du client, clés de protection conservées, démonstration de bout en bout | Fait |
| 13 | Referentiel : conditionnements, codes-barres, scan | Fait |
| 14 | Module Prix et paramètre TVA | Fait |
| 15 | Identite : validation par un responsable | Fait |
| 16 | Module Credit : clients, plafond, remboursements | Fait |
| 17 | Module Caisse : sessions, tickets, avoirs, remises, reçu PDF | Fait |
| 18 | Client : écran de caisse et écrans de gestion | Fait |
| 19 | Architecture, CI, README, démonstration de la caisse | À faire |

## Démonstration

Scénario vérifié dans le navigateur sur http://localhost:8080, base vierge :

1. connexion de l'Administrateur initial (`admin`, mot de passe de `deploy/.env`) ;
2. **Utilisateurs** : création d'un compte `magasinier` (Magasinier) et d'un compte `gerant` (Gérant) ;
3. **Articles** : création de `LAIT-397` « Lait concentré sucré 397 g », unité « boîte », 0 décimale ;
4. connexion du Magasinier ; **Fournisseurs** : création d'un grossiste ;
5. **Réceptions** : `R-000001`, 10 boîtes à 500 XAF, enregistrée puis validée ; `R-000002`, 10 boîtes à 700 XAF ;
6. **Stock** : 20 boîtes, CUMP 600 XAF, valeur 12 000 XAF ; le journal de l'article montre les deux entrées,
   avec leur réception et le Magasinier ;
7. passage en anglais : textes, rôles, format des montants et libellé traduit de l'article ;
8. redémarrage de l'API : la session reste ouverte.

## Points ouverts

- Validation par un responsable au poste du caissier (ses identifiants), et attribution au Gérant de la validation
  d'un dépassement de crédit (RG40 cite « un utilisateur autorisé ») : choix provisoires.
- Avoir valorisé au CUMP courant à son retour en stock (le dossier ne précise pas la valorisation d'un retour).
- Montants de caisse en francs CFA entiers et arrondi de chaque ligne à l'unité (déduits de 1.5 et RG32).
- Délai de paiement par défaut (30 jours) : non fixé par le dossier, choix provisoire.
- Format des codes internes (`INT` + 6 chiffres) : non fixé par le dossier, choix provisoire.
- Coût unitaire saisi sur la ligne de réception : absent de `LigneReception` dans le modèle UML (il y vient de la
  commande fournisseur) ; ajouté faute de commande en V1, à reporter dans le dossier. Statuts de réception retenus :
  Brouillon et Validee.
- Rôles pour la gestion des articles et des fournisseurs, et validation d'une réception sans commande
  (le processus 6.1 cite l'Acheteur, rôle absent de la V1) : attributions provisoires, à confirmer (voir ci-dessus).

- Effet des mouvements `Blocage`, `Deblocage` et `Revalorisation` : refusés tant qu'il n'est pas défini.
- Motif de démarque (RG23) : absent du modèle UML, à ajouter au mouvement.
- CUMP après un stock négatif ou nul : le coût de l'entrée devient le CUMP (choix technique à confirmer).
