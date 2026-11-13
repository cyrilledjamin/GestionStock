# GestionStock

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
    GestionStock.Infrastructure      connexion et transaction partagées, stockage de fichiers, santé
  Host/
    GestionStock.Api                 assemble les modules (AddXxxModule / MapXxxEndpoints)
  Modules/Stock/
    GestionStock.Stock.Contracts     contrat public : IServiceMouvementsStock, IConsultationStock
    GestionStock.Stock.Domain        C# pur : MouvementStock, StockSite, StockEmplacement
    GestionStock.Stock               implémentation : Application / Infrastructure / Endpoints
tests/
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
dotnet ef migrations add <Nom> -p src/Modules/<Module>/GestionStock.<Module> -s src/Host/GestionStock.Api -o Infrastructure/Migrations
```

## Configuration

Uniquement par variables d'environnement ou `appsettings` ; aucun chemin ni adresse en dur dans le code.

| Clé | Variable d'environnement | Rôle |
| --- | --- | --- |
| `ConnectionStrings:GestionStock` | `ConnectionStrings__GestionStock` | Chaîne de connexion PostgreSQL (obligatoire) |
| `Stockage:RepertoireRacine` | `Stockage__RepertoireRacine` | Répertoire des fichiers (reçus, imports) ; `donnees/fichiers` par défaut |
| `BaseDeDonnees:AppliquerMigrationsAuDemarrage` | `BaseDeDonnees__AppliquerMigrationsAuDemarrage` | Applique les migrations de chaque module au démarrage ; `false` par défaut, `true` en Development et dans docker-compose |

En Development, [appsettings.Development.json](src/Host/GestionStock.Api/appsettings.Development.json) pointe vers
la base de docker-compose avec le mot de passe de développement de [deploy/.env.example](deploy/.env.example).
En boutique, le mot de passe est défini uniquement dans `deploy/.env`, qui n'est jamais versionné.

Chaque module a son schéma PostgreSQL et sa propre table d'historique des migrations
(`stock.__ef_migrations_history`). La base n'écoute que sur `127.0.0.1` ; l'API écoute sur le port 8080.

## Endpoints techniques

| Route | Rôle |
| --- | --- |
| `GET /health` | Le processus répond (200) |
| `GET /health/pret` | La base de données répond (200), sinon 503 |
| `GET /openapi/v1.json` | Description OpenAPI, en Development uniquement |

Tout autre endpoint est refusé par défaut (401) : chaque endpoint métier devra déclarer sa permission.
Le schéma d'authentification est provisoire jusqu'au module Identite.

## Avancement du squelette

| Étape | Contenu | État |
| --- | --- | --- |
| 0 | Dépôt, `global.json`, `dotnet-ef` local | Fait |
| 1 | Solution, projets et références | Fait |
| 2 | BuildingBlocks : horloge, transaction partagée, stockage de fichiers | Fait |
| 3 | Domaine Stock : mouvements immuables, soldes, CUMP, RG03 / RG22 | Fait |
| 4 | Contrat et implémentation Stock (EF Core), hôte Api | Fait |
| 5 | docker-compose PostgreSQL et migration `Initial` | Fait (compose non testé : Docker absent du poste de développement) |
| 6 | Tests d'architecture | Fait |
| 7 | Tests d'intégration (Testcontainers) | À faire |

## Points ouverts

- Effet des mouvements `Blocage`, `Deblocage` et `Revalorisation` : refusés tant qu'il n'est pas défini.
- Motif de démarque (RG23) : absent du modèle UML, à ajouter au mouvement.
- CUMP après un stock négatif ou nul : le coût de l'entrée devient le CUMP (choix technique à confirmer).
