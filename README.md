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
docs/                                dossier d'analyse
```

Règles de dépendance :
- un module n'en référence un autre que par son projet `.Contracts` ;
- `GestionStock.Stock.Domain` n'a aucune dépendance ;
- seul le module Stock écrit les mouvements et les soldes (RG01).

## Prérequis

- SDK .NET 10.0.403 ou supérieur (voir [global.json](global.json))
- Docker Desktop avec le moteur Linux, pour la base locale et les tests d'intégration
- Outils locaux : `dotnet tool restore` (installe `dotnet-ef` 10)

## Commandes

```bash
dotnet tool restore
dotnet build
dotnet test
dotnet run --project src/Host/GestionStock.Api
```

## Configuration

Uniquement par variables d'environnement ou `appsettings` ; aucun chemin ni adresse en dur dans le code.

| Clé | Variable d'environnement | Rôle |
| --- | --- | --- |
| `ConnectionStrings:GestionStock` | `ConnectionStrings__GestionStock` | Chaîne de connexion PostgreSQL (obligatoire) |
| `Stockage:RepertoireRacine` | `Stockage__RepertoireRacine` | Répertoire des fichiers (reçus, imports) ; `donnees/fichiers` par défaut |

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
| 5 | docker-compose PostgreSQL et migration `Initial` | À faire |
| 6 | Tests d'architecture | À faire |
| 7 | Tests d'intégration (Testcontainers) | À faire |

## Points ouverts

- Effet des mouvements `Blocage`, `Deblocage` et `Revalorisation` : refusés tant qu'il n'est pas défini.
- Motif de démarque (RG23) : absent du modèle UML, à ajouter au mouvement.
- CUMP après un stock négatif ou nul : le coût de l'entrée devient le CUMP (choix technique à confirmer).
