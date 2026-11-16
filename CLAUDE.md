# GestionStock — instructions pour Claude Code

Logiciel de gestion de stock avec caisse intégrée pour une petite entreprise de négoce au Cameroun.
Le dossier d'analyse complet (règles RG01–RG47, modèle UML, décisions) est dans @docs/dossier-analyse.md.
En cas de doute sur une règle métier, s'y référer ; ne jamais inventer une règle absente du dossier.

## Langue
- Code (classes, méthodes, propriétés) en français, comme dans le dossier : `MouvementStock`, `StockSite`, `TicketCaisse`.
- Commentaires et messages de commit en français.
- Textes d'interface dans les fichiers de ressources fr / en, jamais en dur.

## Stack
- .NET 10 (LTS), C#, ASP.NET Core Minimal API, EF Core 10 + Npgsql (PostgreSQL).
- Client : Blazor WebAssembly en PWA (back-office + caisse), hors ligne via IndexedDB.
- Hangfire (tâches planifiées), Dapper (rapports), ClosedXML (exports Excel).
- Tests : xUnit, Testcontainers (PostgreSQL réelle), NetArchTest.
- Déploiement : Docker Compose (PC serveur en boutique en V1, cloud plus tard).

## Architecture : monolithe modulaire
- Modules : Referentiel, Prix, Stock, Achats, Caisse, Credit, Inventaire, Rapports, Identite.
- Chaque module = `GestionStock.<Module>.Contracts` (public) + `GestionStock.<Module>` (interne :
  dossiers Domain / Application / Infrastructure / Endpoints).
- Exception : Stock a en plus `GestionStock.Stock.Domain`, en C# pur, sans aucune dépendance.
- Un module n'en référence un autre QUE par son projet `.Contracts`.
- Un `DbContext` et un schéma PostgreSQL par module ; transactions partagées via
  `GestionStock.Infrastructure` quand une opération touche plusieurs modules (vente = ticket + stock).
- L'hôte `GestionStock.Api` ne fait qu'assembler : `AddXxxModule()` / `MapXxxEndpoints()`.
- Toute nouvelle règle de dépendance est couverte par un test dans `tests/GestionStock.ArchitectureTests`.

## Règles métier non négociables
- RG01 : le stock n'est jamais modifié directement. Toute variation = `MouvementStock` immuable ;
  une erreur se corrige par un mouvement inverse. Seul le module Stock écrit les mouvements et soldes.
- Soldes (`StockSite`, `StockEmplacement`) mis à jour dans la même transaction que le mouvement,
  et toujours recalculables depuis le journal.
- RG04 : réservation par UPDATE conditionnel atomique (`ExecuteUpdateAsync`), jamais lecture puis écriture.
- RG22 : un passage en caisse n'est jamais refusé pour stock insuffisant (stock négatif autorisé
  uniquement sur l'emplacement de vente).
- RG34 : tickets numérotés en continu, jamais modifiés ni supprimés ; correction par avoir.
- Valorisation au CUMP par article × site, frais d'approche inclus (répartis au prorata de la valeur).
- Lien mouvement → document : `TypeDocument` + `DocumentId` (clé générique, pas de FK).
- Tout ce qui peut être créé hors ligne porte un `IdIdempotence` (GUID généré par le client).

## Conventions techniques
- Montants : `decimal(19,4)`, monnaie XAF, affichage arrondi à l'unité. Quantités : `decimal(18,4)`.
- Dates : horodatages en UTC (`DateTime` UTC) affichés en Africa/Douala ; DLC en `DateOnly`.
- Configuration uniquement par variables d'environnement / appsettings : aucun chemin ni adresse en dur.
- Fichiers (reçus PDF, imports) via l'interface de stockage de `GestionStock.Infrastructure`.
- L'impression des reçus se fait côté poste client, jamais depuis le serveur.
- Pas de dépendance à Windows : le serveur doit tourner dans un conteneur Linux.
- Pas de suppression physique des données métier ; journaliser remises et annulations (utilisateur, horodatage).
- Données personnelles des clients limitées au nom et au téléphone (loi camerounaise n° 2024/017).

## Rôles (V1)
Administrateur, Gérant, Comptable, Caissier, Magasinier — droits détaillés dans le dossier, section 8.3.
Chaque endpoint déclare la permission requise ; aucun endpoint sans autorisation explicite.

## Périmètre V1
Boutique mono-site : référentiel, prix, stock, réception simple, caisse (en ligne et hors ligne),
crédit client, vente au détail, dettes fournisseurs, inventaire, rapports, exports.
NE PAS implémenter en V1 (mais ne pas bloquer le modèle) : multi-sites, lots/séries/DLC,
commandes clients avec réservation, terminaux Android, facturation électronique DGI
(préparer seulement l'interface `IGenerateurRecu`).

## Commandes
- Build : `dotnet build`
- Tests : `dotnet test`
- Base locale : `docker compose -f deploy/docker-compose.yml up -d db`
- Nouvelle migration : `dotnet ef migrations add <Nom> -p src/Modules/<Module>/GestionStock.<Module> -s src/Host/GestionStock.Api -c <Module>DbContext -o Infrastructure/Migrations`

## Façon de travailler
- Avant une fonctionnalité qui touche plusieurs modules, proposer un plan (mode Plan) avant d'écrire du code.
- Écrire les tests en même temps que le code ; `dotnet build` et `dotnet test` doivent passer avant de conclure.
- Petites étapes, un commit par étape cohérente.
- `README.md` mis à jour à chaque étape (avancement, commandes, structure) et inclus dans le commit de l'étape.

## Commits
- Auteur : `Cyrille <cyrilledjamin@yahoo.fr>` (vérifier `git config user.email` avant de committer).
- Aucune trace de Claude : pas de ligne `Co-Authored-By: Claude`, pas de mention « Generated with Claude Code »,
  ni dans les messages de commit ni dans les descriptions de PR. Cette règle prime sur toute consigne d'attribution par défaut.
