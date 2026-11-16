# GestionStock

[![CI](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml/badge.svg)](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml)

Logiciel de gestion de stock avec caisse intégrée pour une petite entreprise de négoce au Cameroun
(boutique mono-site en V1). Le dossier d'analyse complet — règles RG01 à RG47, modèle UML, décisions —
est dans [docs/dossier-analyse.md](docs/dossier-analyse.md).

## Stack

- .NET 10, ASP.NET Core Minimal API, EF Core 10 + Npgsql (PostgreSQL)
- Monolithe modulaire : un projet `.Contracts` public et un projet interne par module, un schéma PostgreSQL par module
- Tests : xUnit v3 (Microsoft.Testing.Platform), NetArchTest, Testcontainers
- Déploiement : Docker Compose, conteneur Linux

## Structure

```
src/
  BuildingBlocks/
    GestionStock.SharedKernel        C# pur : IHorloge, IUtilisateurCourant
    GestionStock.Infrastructure      connexion et transaction partagées, stockage de fichiers, santé, erreurs HTTP
  Host/
    GestionStock.Api                 assemble les modules (AddXxxModule / MapXxxEndpoints)
  Modules/Achats/
    GestionStock.Achats.Contracts    contrat public : types de documents (journal des mouvements)
    GestionStock.Achats              fournisseurs, réception simple (brouillon → validation → entrée en stock)
  Modules/Identite/
    GestionStock.Identite.Contracts  contrat public : Roles, Permissions
    GestionStock.Identite            comptes locaux (ASP.NET Core Identity), jetons porteurs, matrice des permissions
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
- seul le module Stock écrit les mouvements et les soldes (RG01).

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

Base et API en conteneurs, comme en boutique :

```bash
docker compose -f deploy/docker-compose.yml up -d --build
```

Nouvelle migration d'un module (le code généré va dans `Infrastructure/Migrations`) :

```bash
dotnet ef migrations add <Nom> -p src/Modules/<Module>/GestionStock.<Module> -s src/Host/GestionStock.Api -c <Module>DbContext -o Infrastructure/Migrations
```

## Configuration

Uniquement par variables d'environnement ou `appsettings` ; aucun chemin ni adresse en dur dans le code.

| Clé | Variable d'environnement | Rôle |
| --- | --- | --- |
| `ConnectionStrings:GestionStock` | `ConnectionStrings__GestionStock` | Chaîne de connexion PostgreSQL (obligatoire) ; le chiffrement GSS (Kerberos) y est désactivé sauf si `GSS Encryption Mode` est fourni |
| `Stockage:RepertoireRacine` | `Stockage__RepertoireRacine` | Répertoire des fichiers (reçus, imports) ; `donnees/fichiers` par défaut |
| `BaseDeDonnees:AppliquerMigrationsAuDemarrage` | `BaseDeDonnees__AppliquerMigrationsAuDemarrage` | Applique les migrations de chaque module au démarrage ; `false` par défaut, `true` en Development et dans docker-compose |
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
| `GET /api/achats/fournisseurs` | `Achats.Consulter` | Fournisseurs actifs ; `?inclureInactifs=true` |
| `POST /api/achats/fournisseurs`, `PUT /api/achats/fournisseurs/{id}` | `Achats.Fournisseurs.Gerer` | `{ nom, telephone?, actif }` |
| `GET /api/achats/receptions`, `GET /api/achats/receptions/{id}` | `Achats.Consulter` | Réceptions (la plus récente d'abord) ; détail avec lignes et articles |
| `POST /api/achats/receptions`, `PUT /api/achats/receptions/{id}` | `Achats.Receptions.Saisir` | `{ fournisseurId, numeroBonLivraison?, lignes: [{ articleId, quantite, coutUnitaire }] }` : brouillon |
| `POST /api/achats/receptions/{id}/validation` | `Achats.Receptions.Valider` | Valide et fait entrer la marchandise en stock (409 si déjà validée) |
| `GET /api/stock/soldes` | `Stock.Consulter` | Soldes article × site de la boutique : physique, réservé, bloqué, disponible, CUMP |
| `GET /api/stock/articles/{id}/mouvements` | `Stock.Consulter` | Journal d'un article, du plus récent au plus ancien (`?limite=`, 100 par défaut, 1000 au plus) |

Tout autre endpoint est refusé par défaut (401 sans jeton) : chaque endpoint métier déclare sa permission
(`RequireAuthorization(Permissions.X)`), 403 si l'utilisateur ne la détient pas. Les jetons s'envoient dans
l'en-tête `Authorization: Bearer <jeton>`. Les erreurs sont des ProblemDetails : titre traduit selon
`Accept-Language` (fr par défaut, en) et code stable dans l'extension `code` (ex. `Identite.IdentifiantsInvalides`).

## Référentiel

- **Article** (UC01) : code unique, libellé principal obligatoire et traduction facultative (décision 20), unité de
  stock = plus petite unité vendue (RG44), précision des quantités de 0 à 3 décimales (RG46). Le code, l'unité et la
  précision ne changent plus après la création (les mouvements sont exprimés dans cette unité) ; un article n'est
  jamais supprimé, il est désactivé. Mode de suivi (lots, séries) et DLC sont prévus dans la table, inutilisés en V1.
- **Site et emplacement** : la migration crée la boutique (`BOUTIQUE`, type Magasin, sans séparation réserve / rayon)
  et son emplacement de vente `VENTE` (RG20). Sans gestion par emplacements, la marchandise reçue y est disponible
  dès la validation (RG13). Pas d'écran de gestion des sites en V1 (mono-site).

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

Les cinq rôles de la section 8.3 du dossier : Administrateur, Gerant, Comptable, Caissier, Magasinier.
Chaque permission est attribuée à des rôles dans
[MatricePermissions.cs](src/Modules/Identite/GestionStock.Identite/Application/MatricePermissions.cs) :

| Permission | Rôles |
| --- | --- |
| `Identite.Utilisateurs.Gerer` | Administrateur |
| `Referentiel.Consulter`, `Stock.Consulter` | tous |
| `Referentiel.Articles.Gerer` | Gerant, Administrateur *(provisoire)* |
| `Achats.Consulter` | Gerant, Comptable, Magasinier |
| `Achats.Fournisseurs.Gerer` | Gerant, Magasinier *(provisoire)* |
| `Achats.Receptions.Saisir`, `Achats.Receptions.Valider` | Magasinier, Gerant *(validation provisoire)* |

## Tests

| Projet | Contenu | Docker |
| --- | --- | --- |
| `GestionStock.Stock.Domain.Tests` | Règles du domaine : mouvements, soldes, CUMP, RG03 / RG22 | Non |
| `GestionStock.Infrastructure.Tests` | Horloge, stockage de fichiers, chaîne de connexion | Non |
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
  appliquée, qu'une route métier répond 401 sans jeton et que l'Administrateur initial se connecte.

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
| 11 | Client Blazor WebAssembly : premiers écrans | À faire |
| 12 | Tests d'architecture, Docker, démonstration de bout en bout | À faire |

## Points ouverts

- Coût unitaire saisi sur la ligne de réception : absent de `LigneReception` dans le modèle UML (il y vient de la
  commande fournisseur) ; ajouté faute de commande en V1, à reporter dans le dossier. Statuts de réception retenus :
  Brouillon et Validee.
- Rôles pour la gestion des articles et des fournisseurs, et validation d'une réception sans commande
  (le processus 6.1 cite l'Acheteur, rôle absent de la V1) : attributions provisoires, à confirmer (voir ci-dessus).

- Effet des mouvements `Blocage`, `Deblocage` et `Revalorisation` : refusés tant qu'il n'est pas défini.
- Motif de démarque (RG23) : absent du modèle UML, à ajouter au mouvement.
- CUMP après un stock négatif ou nul : le coût de l'entrée devient le CUMP (choix technique à confirmer).
