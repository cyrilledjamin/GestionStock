# GestionStock

[![CI](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml/badge.svg)](https://github.com/cyrilledjamin/GestionStock/actions/workflows/ci.yml)

Logiciel de gestion de stock avec caisse intégrée pour une petite entreprise de négoce au Cameroun
(boutique mono-site en V1). Le dossier d'analyse complet — règles RG01 à RG47, modèle UML, décisions —
est dans [docs/dossier-analyse.md](docs/dossier-analyse.md).

## Stack

- .NET 10, ASP.NET Core Minimal API, EF Core 10 + Npgsql (PostgreSQL)
- Client Blazor WebAssembly servi par l'API (même origine), sans ressource externe
- Monolithe modulaire : un projet `.Contracts` public et un projet interne par module, un schéma PostgreSQL par module
- Fichiers Excel : ClosedXML (reprise des données)
- Rapports : Dapper, en lecture seule sur les vues `v_*` publiées par les modules
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
    GestionStock.Credit.Contracts    contrat public : IServiceCredit (vente à crédit, remboursement, annulation),
                                     IControleSoldesCredit (dette = journal, ENF04)
    GestionStock.Credit              clients, plafonds, journal des opérations, imputations, impayés
  Modules/Exploitation/
    GestionStock.Exploitation.Contracts  contrat public (vide pour l'instant)
    GestionStock.Exploitation        contrôle nocturne des soldes (ENF04), suivi des sauvegardes (décision 32),
                                     planificateur Hangfire (schéma hangfire)
  Modules/Identite/
    GestionStock.Identite.Contracts  contrat public : Roles, Permissions, IValidationResponsable
    GestionStock.Identite            comptes locaux (ASP.NET Core Identity), jetons porteurs, matrice des permissions
  Modules/Inventaire/
    GestionStock.Inventaire.Contracts  contrat public : IServiceInventaire (inventaire d'ouverture pour la reprise)
    GestionStock.Inventaire          inventaires (ouverture, complet, partiel), écarts, démarque connue
  Modules/Prix/
    GestionStock.Prix.Contracts      contrat public : IConsultationPrix (prix applicable), RegimeTva (HT / TVA)
    GestionStock.Prix                prix de vente par conditionnement, historisés
  Modules/Referentiel/
    GestionStock.Referentiel.Contracts  contrat public : IConsultationReferentiel (articles, site de la boutique)
    GestionStock.Referentiel         articles, site et emplacements (style CRUD)
  Modules/Rapports/
    GestionStock.Rapports.Contracts  contrat public : JournauxComptables (journaux exportables)
    GestionStock.Rapports            rapports en lecture seule (Dapper) sur les vues v_* des modules, sans schéma
  Modules/Reprise/
    GestionStock.Reprise.Contracts   contrat public : colonnes du fichier de reprise
    GestionStock.Reprise             import Excel (ClosedXML) : modèle, exemple, contrôle, import tout ou rien
  Modules/Stock/
    GestionStock.Stock.Contracts     contrat public : IServiceMouvementsStock, IConsultationStock,
                                     IControleSoldesStock (soldes = rejeu du journal, ENF04)
    GestionStock.Stock.Domain        C# pur : MouvementStock, StockSite, StockEmplacement
    GestionStock.Stock               implémentation : Application / Infrastructure / Endpoints (lecture seule)
tests/
  GestionStock.Api.IntegrationTests  API complète par HTTP sur PostgreSQL réelle (Testcontainers)
  GestionStock.ArchitectureTests     règles de dépendance et conventions
  GestionStock.Client.Tests          logique du client (vente en cours), sans navigateur
  GestionStock.Infrastructure.Tests  briques transverses
  GestionStock.Stock.Domain.Tests    règles du domaine Stock
  GestionStock.Stock.IntegrationTests  PostgreSQL réelle (Testcontainers)
deploy/
  docker-compose.yml                 PostgreSQL 17 (db), API (api) et sauvegardes (sauvegarde)
  sauvegarde/                        image et scripts de sauvegarde : sauvegarder.sh, restaurer.sh, boucle.sh
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
| `Entreprise:Nom`, `:Adresse`, `:Telephone`, `:NumeroContribuable` | `Entreprise__Nom`… | Identité imprimée en tête des reçus (`ENTREPRISE_NOM`, `ENTREPRISE_ADRESSE`, `ENTREPRISE_TELEPHONE`, `ENTREPRISE_NIU` dans `deploy/.env`) |
| `Caisse:Postes` | `Caisse__Postes__0`, `Caisse__Postes__1`… | Postes de caisse de la boutique ; `CAISSE-1` par défaut (`CAISSE_POSTE` dans `deploy/.env`) ; le nom s'affiche aussi dans le menu et sur l'écran de connexion |
| `Entreprise:Logo` | `Entreprise__Logo` | Logo facultatif (`ENTREPRISE_LOGO`) : chemin d'une image PNG, JPEG ou WebP dans le stockage de fichiers, ex. `identite/logo.png`, copiée par `docker compose -f deploy/docker-compose.yml cp logo.png api:/donnees/fichiers/identite/logo.png` puis `up -d`. Affiché dans le menu, à la connexion et sur le reçu imprimé ; le SVG est refusé (il pourrait exécuter du script). Sans logo, l'icône de l'application |
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
| `POST /api/identite/connexion` | anonyme | `{ identifiant, motDePasse }` (identifiant = matricule) → jeton d'accès (1 h) et jeton de renouvellement |
| `POST /api/identite/rafraichir` | anonyme | `{ jetonRenouvellement }` → nouveau jeton d'accès |
| `GET /api/identite/moi` | connecté | Matricule, nom, langue, mot de passe provisoire ou non, rôles et permissions (aucune tant qu'il est provisoire) |
| `POST /api/identite/moi/mot-de-passe` | connecté | `{ actuel, nouveau }` : change son mot de passe (obligatoire s'il est provisoire) |
| `PUT /api/identite/moi/langue` | connecté | `{ langue: fr \| en }` |
| `GET /api/identite/utilisateurs`, `GET .../{id}` | `Identite.Utilisateurs.Gerer` | Comptes (fiche, rôles, statut) ; fiche avec le journal du compte |
| `POST /api/identite/utilisateurs` | `Identite.Utilisateurs.Gerer` | `{ matricule?, prenom, nom, telephone?, roles, langue?, motDePasse }` : mot de passe provisoire |
| `PUT /api/identite/utilisateurs/{id}` | `Identite.Utilisateurs.Gerer` | `{ prenom, nom, telephone?, roles, langue? }` (le matricule ne change pas) |
| `POST /api/identite/utilisateurs/{id}/desactivation`, `/reactivation` | `Identite.Utilisateurs.Gerer` | Départ ou retour d'un employé ; rien n'est supprimé |
| `POST /api/identite/utilisateurs/{id}/mot-de-passe` | `Identite.Utilisateurs.Gerer` | `{ motDePasse }` : mot de passe provisoire (oubli) |
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
| `PUT /api/referentiel/conditionnements/{id}/vente-rapide` | `Referentiel.Articles.Gerer` | `{ venteRapide }` : tuile à la caisse (articles sans code-barres) ; refusé sur un conditionnement désactivé, et retiré à sa désactivation |
| `GET /api/referentiel/catalogue-caisse` | `Referentiel.Consulter` | Catalogue de la caisse gardé sur le poste pour vendre hors ligne : conditionnements actifs d'articles actifs, leurs codes actifs, unité, articles rapides |
| `GET /api/referentiel/vendables/rapides` | `Referentiel.Consulter` | Articles rapides de la caisse : conditionnements actifs marqués, d'articles actifs |
| `GET /api/referentiel/vendables?recherche=` | `Referentiel.Consulter` | Articles actifs (code ou libellé) avec chacun de leurs conditionnements actifs |
| `GET /api/prix/regime` | `Prix.Consulter` | Régime de TVA : `{ assujetti, tauxPourcent, tauxApplique }` |
| `GET /api/prix/articles/{id}` | `Prix.Consulter` | Conditionnements de l'article avec prix en vigueur et prochain prix programmé |
| `GET /api/prix/grille` | `Prix.Consulter` | Grille de la caisse : prix en vigueur aujourd'hui (Douala) et prochain prix programmé de chaque conditionnement, régime de TVA |
| `GET /api/prix/conditionnements/{id}` | `Prix.Consulter` | Prix applicable aujourd'hui (204 s'il n'y en a pas) |
| `GET /api/prix/conditionnements/{id}/historique` | `Prix.Consulter` | Tous les prix du conditionnement, du plus récent au plus ancien |
| `POST /api/prix/conditionnements/{id}` | `Prix.Gerer` | `{ prixTtc, debutValidite? }` : nouveau prix, dès aujourd'hui ou à une date future |
| `GET /api/achats/fournisseurs` | `Achats.Consulter` | Fournisseurs actifs ; `?inclureInactifs=true` |
| `POST /api/achats/fournisseurs`, `PUT /api/achats/fournisseurs/{id}` | `Achats.Fournisseurs.Gerer` | `{ nom, telephone?, actif }` |
| `GET /api/achats/receptions`, `GET /api/achats/receptions/{id}` | `Achats.Consulter` | Réceptions (la plus récente d'abord) ; détail avec lignes et articles |
| `POST /api/achats/receptions`, `PUT /api/achats/receptions/{id}` | `Achats.Receptions.Saisir` | `{ fournisseurId, numeroBonLivraison?, lignes: [{ articleId, quantite, coutUnitaire }] }` : brouillon |
| `POST /api/achats/receptions/{id}/validation` | `Achats.Receptions.Valider` | Valide et fait entrer la marchandise en stock, frais d'approche compris (409 si déjà validée) |
| `POST /api/achats/receptions/{id}/frais` | `Achats.Frais.Saisir` | `{ type: Transport \| Transit \| Douane \| Autre, montant, cle?: Valeur \| Quantite, libelle? }` : avant ou après la validation |
| `DELETE /api/achats/receptions/{id}/frais/{fraisId}` | `Achats.Frais.Saisir` | Retire un frais d'un brouillon (409 après la validation) |
| `GET /api/achats/dettes?fournisseurId=&nonSoldees=` | `Achats.Consulter` | Dettes par fournisseur et par réception validée : dû, payé, reste, statut |
| `GET /api/achats/receptions/{id}/dette` | `Achats.Consulter` | Dette d'une réception et historique de ses paiements |
| `POST /api/achats/receptions/{id}/paiements` | `Achats.Paiements.Saisir` | `{ montant, mode: Especes \| MobileMoney \| Virement \| Cheque, reference? }` |
| `POST /api/achats/paiements/{id}/annulation` | `Achats.Paiements.Saisir` | `{ motif }` : paiement opposé journalisé (409 si déjà annulé) |
| `GET /api/credit/clients?recherche=` | `Credit.Consulter` | Clients (nom ou téléphone) avec solde dû et montant en retard |
| `GET /api/credit/clients/{id}` | `Credit.Consulter` | Fiche : client, ventes non soldées (reste dû, échéance), journal des opérations |
| `GET /api/credit/impayes` | `Credit.Consulter` | Clients ayant une dette échue (RG43) |
| `POST /api/credit/clients`, `PUT /api/credit/clients/{id}` | `Credit.Clients.Gerer` | `{ nom, telephone, delaiPaiementJours?, actif }` |
| `PUT /api/credit/clients/{id}/plafond` | `Credit.Plafonds.Gerer` | `{ plafondCredit }` |
| `GET /api/caisse/postes` | `Caisse.Vendre` | Postes de caisse configurés |
| `POST /api/caisse/sessions` | `Caisse.Vendre` | `{ numeroCaisse, fondCaisse }` : ouvre la session du caissier connecté |
| `GET /api/caisse/sessions/courante` | `Caisse.Vendre` | Session ouverte du caissier avec ses totaux (204 s'il n'en a pas) |
| `GET /api/caisse/boutique` | anonyme | Nom de la boutique et présence d'un logo (écran de connexion) ; rien d'autre n'est public |
| `GET /api/caisse/boutique/logo` | anonyme | Image du logo configuré (404 sans logo) |
| `GET /api/caisse/postes/occupation` | `Caisse.Vendre` | Postes tenus par une session ouverte : titulaire et heure d'ouverture (le caissier bloqué sait à qui s'adresser) |
| `POST /api/caisse/sessions/{id}/cloture` | `Caisse.Sessions.Cloturer` | `{ especesComptees }` : le Gérant clôture à la place de son titulaire une session restée ouverte (décision 36) ; auteur enregistré dans `clotureePar`, écart à valider |
| `POST /api/caisse/sessions/courante/cloture` | `Caisse.Vendre` | `{ especesComptees, dernierNumero? }` : clôture, écart calculé ; refusée (409 `Caisse.VentesNonSynchronisees`) tant que le dernier ticket numéroté par le poste n'est pas arrivé (RG36) |
| `GET /api/caisse/anomalies?traitees=` | `Caisse.Sessions.Consulter` | Anomalies de synchronisation des opérations faites hors ligne (7.8) |
| `POST /api/caisse/anomalies/{id}/traitement` | `Caisse.Anomalies.Traiter` | `{ commentaire }` : traitement par le Gérant, une fois, tracé |
| `GET /api/exploitation/etat` | `Exploitation.Consulter` | Dernier contrôle, écarts ouverts, dernières sauvegardes réussies et retards (contrôle > 26 h, locale > 2 h, hors site > 26 h) |
| `GET /api/exploitation/controles?nombre=` | `Exploitation.Consulter` | Passages du contrôle des soldes, du plus récent au plus ancien |
| `POST /api/exploitation/controles` | `Exploitation.Controles.Lancer` | Lance le contrôle tout de suite ; 409 `Exploitation.ControleEnCours` si un passage tourne déjà |
| `GET /api/exploitation/ecarts?statut=&nombre=` | `Exploitation.Consulter` | Écarts entre soldes et journaux (`Ouvert`, `Reconstruit`, `Disparu`) |
| `POST /api/exploitation/ecarts/{id}/reconstruction` | `Exploitation.Soldes.Reconstruire` | Reconstruit depuis le journal les soldes de l'article (ou la dette du client) et clôt ses écarts ; 409 si déjà résolu |
| `GET /api/exploitation/sauvegardes?type=&nombre=` | `Exploitation.Consulter` | Comptes rendus des sauvegardes (`Locale`, `HorsSite`), du plus récent au plus ancien (50 par défaut, 200 au plus) |
| `GET /api/caisse/sessions`, `GET /api/caisse/sessions/{id}` | `Caisse.Sessions.Consulter` | Sessions (`?statut=Ouverte|Comptee|Validee`) et leurs totaux |
| `POST /api/caisse/sessions/{id}/validation` | `Caisse.Sessions.Valider` | Valide une session clôturée et son écart |
| `POST /api/caisse/tickets` | `Caisse.Vendre` | Vente : `{ idIdempotence, clientId?, lignes: [{ conditionnementId, quantite, remise?, codeScanne? }], paiements: [{ mode, montant, reference? }], validation? }` ; 201, ou 200 si déjà reçue |
| `POST /api/caisse/avoirs` | `Caisse.Vendre` | `{ idIdempotence, ticketOrigineId, lignes: [{ ligneOrigineId, quantite }], modeRemboursement?, reference? }` |
| `POST /api/caisse/remboursements` | `Caisse.Vendre` | Remboursement de crédit : `{ idIdempotence, clientId, mode, montant, reference? }` |
| `GET /api/caisse/tickets/{id}`, `/tickets/numero/{numero}`, `/sessions/{id}/tickets` | `Caisse.Tickets.Consulter` | Tickets (quantités déjà rendues par ligne) |
| `GET /api/caisse/tickets/{id}/recu.pdf` | `Caisse.Tickets.Consulter` | Reçu PDF 80 mm dans la langue de la requête |
| `GET /api/inventaires?statut=`, `GET /api/inventaires/{id}` | `Inventaire.Consulter` | Inventaires ; détail avec théorique relevé, compté, écart et sa valeur |
| `POST /api/inventaires` | `Inventaire.Compter` | `{ type: Complet \| Partiel, articles? }` |
| `POST /api/inventaires/{id}/comptages` | `Inventaire.Compter` | `{ articleId, quantite, motifEcart?: Aucun \| Freinte }` (un article hors liste est ajouté) |
| `POST /api/inventaires/{id}/validation` | `Inventaire.Valider` | Écrit un mouvement par écart, tout ou rien |
| `POST /api/inventaires/{id}/annulation` | `Inventaire.Compter` | Annule un inventaire en cours |
| `GET /api/demarques?statut=` | `Inventaire.Consulter` | Déclarations de démarque |
| `POST /api/demarques` | `Inventaire.Demarque.Declarer` | `{ articleId, quantite, motif: Casse \| Vol \| Perime \| Freinte, commentaire? }` |
| `POST /api/demarques/{id}/validation`, `/refus` | `Inventaire.Demarque.Valider` | Valide (mouvement Demarque) ou refuse (`{ motif }`) |
| `GET /api/reprise/modele.xlsx` | `Reprise.Importer` | Modèle Excel dans la langue de la requête ; `?exemple=true` : rempli avec le jeu d'exemple |
| `POST /api/reprise/simulation` | `Reprise.Importer` | Corps : le classeur (`application/octet-stream`, 5 Mo au plus) ; rapport ligne par ligne, rien n'est écrit |
| `POST /api/reprise/import` | `Reprise.Importer` | Même contrôle, puis import tout ou rien et inventaire d'ouverture ; rapport |
| `GET /api/rapports/ventes?du=&au=` | `Rapports.Consulter` | Ventes et marge : totaux, par jour, par article, par caissier (avoirs déduits, coût au CUMP) |
| `GET /api/rapports/tableau-de-bord` | `Rapports.Consulter` | Accueil : ventes du jour (Douala), sessions ouvertes et à valider (toutes dates) avec leurs écarts, anomalies hors ligne à traiter, stocks négatifs et valeur du stock, créances et retards, dettes fournisseurs |
| `GET /api/rapports/stock` | `Rapports.Consulter` | Quantité, CUMP et valeur par article ; total ; stocks négatifs |
| `GET /api/rapports/creances-dettes` | `Rapports.Consulter` | Clients (dû, en retard RG43, plus ancienne échéance) et fournisseurs (dû, payé, reste) |
| `GET /api/rapports/caisse?du=&au=` | `Rapports.Consulter` | Sessions et écarts, remises, avoirs, crédits validés par un responsable, stocks négatifs, frais non incorporés, anomalies hors ligne et leur traitement |
| `GET /api/rapports/journaux` | `Rapports.Consulter` | Journaux comptables exportables |
| `GET /api/exports/{journal}?du=&au=&format=xlsx\|csv` | `Rapports.Exports.Produire` | `ventes`, `achats`, `encaissements`, `creances`, `valeur-stock` (du jour) ; Excel par défaut |
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
| Connexion | Matricule et mot de passe (bouton « Afficher » pour vérifier la saisie en clair) ; choix de la langue (français / anglais) ; à la connexion, la langue mémorisée sur le compte s'applique ; un mot de passe provisoire mène directement à « Mon compte » |
| Accueil | Tableau de bord selon les permissions : **Ma caisse** (Caissier : session ouverte, ventes, chemin vers la caisse) ; **Aujourd'hui** et **À surveiller** (Gérant, Comptable : ventes et marge du jour, caisses ouvertes, clôtures à valider et écarts, stocks négatifs, créances et retards, dettes fournisseurs) ; **À faire** (réceptions en brouillon, inventaires en cours, démarques à valider) ; chaque chiffre mène à son écran, en orange ou en rouge s'il demande une action ; puis raccourcis |
| Stock | Soldes de la boutique : physique, disponible, CUMP, valeur ; total valorisé |
| Journal d'un article | Soldes et mouvements (date à l'heure de Douala, variation, coût, document d'origine, utilisateur) |
| Réceptions | Liste ; saisie en brouillon (lignes, valeur calculée), enregistrement, validation |
| Fournisseurs | Liste, création, modification, désactivation |
| Articles | Recherche, création, modification, désactivation |
| Fiche article | Conditionnements, codes-barres (saisis, générés, désactivés), case « Vente rapide » (Gérant) et prix (en vigueur, programmé, nouveau) |
| Caisse | Ouverture de session : poste occupé signalé (titulaire, heure d'ouverture, à qui s'adresser ; un poste libre est proposé d'abord), fond saisi ou ajouté par billets ; clôture guidée : comptage par billet et pièce (ou total), sans afficher le théorique, l'écart apparaissant après la clôture ; écran du reçu : « Nouvelle vente » a le curseur (Entrée ou F2) ; plein comptoir pendant la vente (menu en tiroir) ; un seul champ : code-barres ou code article à la touche Entrée, recherche en direct dès qu'il contient une lettre (un résultat unique est ajouté) ; résultats et articles rapides en tuiles ; panier à lignes larges (− / +, remise repliée, ligne choisie pour le pavé) ; pavé numérique (quantité ou espèces reçues), affiché d'office sur écran tactile ; billets rapides (500 à 10 000 XAF, cumulés) et « Compte juste » ; raccourcis F2 (scan), F4 (compte juste), F8 (encaisser), Échap ; le curseur revient au scan après chaque bouton (douchette) ; client et crédit ; paiements combinés avec rendu (une ligne par mode, un nouveau clic sur un mode lui ajoute le reste à payer) ; validation d'un responsable si remise ou plafond dépassé ; reçu 80 mm imprimable et PDF ; remboursement de crédit ; clôture avec écart |
| Tickets de la session, ticket | Recherche par numéro, réimpression, avoir (quantités à rendre, mode de remboursement) |
| Anomalies hors ligne | Écarts relevés à la synchronisation des opérations faites hors ligne (numéros manquants ou réattribués, prix différent, plafond dépassé, session déjà clôturée, heure corrigée), chacun avec ce qu'il faut vérifier ; filtre à traiter / traitées / toutes ; le Gérant les marque traitées avec un commentaire (auteur et heure gardés) |
| Sessions de caisse | Supervision (Gérant, Comptable) : ventes nettes, espèces théoriques et comptées, écart, validation ; « Clôturer à sa place » sur une session ouverte (Gérant, comptage guidé), signalée ensuite « comptée par » |
| Clients, fiche client | Clients à crédit, impayés, création (Gérant), plafond (Comptable), ventes non soldées, journal |
| Utilisateurs | Liste (nom complet, matricule, rôles, téléphone, statut ; comptes désactivés sur demande) ; création : prénom, nom, téléphone, matricule saisi ou généré, langue, rôles à cocher, mot de passe provisoire (Administrateur) |
| Fiche d'un compte | Modification de la fiche et des rôles (le matricule ne change pas) ; mot de passe oublié (nouveau mot de passe provisoire) ; désactivation et réactivation ; journal du compte (date, opération, auteur, détail) |
| Mon compte | Fiche en lecture (nom, matricule, rôles, langue) ; changement du mot de passe avec confirmation. Tant que le mot de passe est provisoire, toute autre page renvoie ici |
| Inventaires | Liste filtrée par statut ; nouvel inventaire complet (tous les articles actifs) ou partiel (démarre vide) |
| Inventaire | Comptage à la douchette (chaque scan ajoute le contenu du conditionnement) ou saisie par ligne ; freinte sur un écart négatif ; théorique, écart et sa valeur ; filtre « non comptés » ; validation (Comptable) ; annulation |
| Démarque | Déclaration (article, quantité, motif casse / vol / périmé / freinte, commentaire) par le Magasinier ; validation ou refus motivé (Comptable, Gérant) |
| Réception (frais et dette) | Frais d'approche : ajout (type, montant, répartition, libellé), répartition par ligne et coût de revient, retrait sur un brouillon, frais tardifs avec leur part non incorporée ; dette de la réception validée : dû, payé, reste, paiements (mode, référence) et annulation motivée |
| Dettes fournisseurs | Reste dû par fournisseur et par réception, filtre « non soldées », lien vers la réception |
| Rapports | Ventes et marge (par jour, article, caissier), stock et valeur, créances et dettes, caisse et anomalies ; choix de la période |
| Exports comptables | Journal, période et format (Excel ou CSV), puis téléchargement |
| Reprise des données | Modèle Excel vide ou avec exemple ; contrôle du fichier (rapport par ligne et colonne) ; import tout ou rien ; lien vers l'inventaire d'ouverture créé |

Le reçu s'imprime par le navigateur du poste (feuille de style d'impression au format 80 mm, seul le reçu est
imprimé) ou se télécharge en PDF pour l'envoyer par WhatsApp. Le panier affiche les prix lus dans l'application ;
le serveur les relit à l'encaissement.

**Interface** ([plan de la tranche 6](docs/plan-tranche-6-design.md)) : une seule feuille de style locale,
[app.css](src/Client/GestionStock.Client/wwwroot/css/app.css), dont toutes les couleurs, tailles et espacements
viennent des jetons de `:root` (texte de 16 px, cibles tactiles de 44 px, contrastes AA, focus visible au clavier) ;
icônes SVG embarquées dans le composant `Icone` (aucune ressource externe, hors ligne). **Responsive** : poste à partir
de 1 000 px (menu latéral) ; tablette en portrait et téléphone (menu en tiroir ouvert par le bouton « Menu ») ; sous
760 px, une seule colonne ; les listes principales (stock, clients, sessions de caisse, articles, réceptions, dettes,
tickets) passent en cartes, chaque valeur gardant l'intitulé de sa colonne, les autres tableaux défilent dans leur
cadre. À la caisse, sur tablette et téléphone, une barre fixée en bas garde le total, le reste ou le rendu et le bouton
« Payer » (vers l'encaissement) puis « Encaisser » ; l'en-tête de caisse tient sur une ligne. Les tests de conventions refusent une couleur en
dur hors des jetons, un style en ligne, une icône inconnue et l'absence de la balise `viewport`.

**Faible bande passante** (ENF10) : réponses de l'API compressées (gzip ou Brotli selon le navigateur), surtout utile
pour le catalogue et la grille de prix que le poste de caisse garde pour vendre hors ligne ; pas sur HTTPS (BREACH).

**Identité et finitions** : nom de la boutique (et logo s'il est configuré) dans le menu, à la connexion et sur le
reçu ; quantités sans zéros inutiles (« 20 kg », « 2,5 kg ») ; unités courantes traduites en anglais (boîte → box,
pièce → piece, sac → bag…, liste fixe côté client ; une unité absente de la liste reste telle quelle) ; caissier
désigné sur le reçu par « Prénom Nom (matricule) », comme dans les rapports (annuaire `IAnnuairePersonnel` de
`Identite.Contracts`).

Tous les champs de mot de passe (connexion, création de compte, validation par un responsable à la caisse) sont masqués
par défaut et ont un bouton « Afficher » / « Masquer » pour vérifier la saisie. Un mot de passe enregistré ne peut pas
être réaffiché : seule son empreinte est conservée.

Les jetons de connexion restent dans le `sessionStorage` de l'onglet : ils disparaissent à sa fermeture (poste partagé).
Montants arrondis à l'unité en XAF, quantités selon la précision de l'article, libellé traduit quand l'interface est
en anglais.

**Application installable et hors ligne** (décision 24, 7.8) : manifeste et icônes ; le service worker publié
(`service-worker.published.js`) garde tous les fichiers du client sur le poste, si bien que l'application se recharge
même quand le serveur est arrêté (les appels `/api` ne passent jamais par ce cache). Le poste garde aussi, dans
IndexedDB (`js/stockage-local.js`, stockage persistant demandé au navigateur), le catalogue de la caisse avec ses codes,
la grille de prix et les clients à crédit avec leur dernier solde, rafraîchis à chaque ouverture de la caisse
(`CacheCaisse`). Un serveur injoignable fait passer le poste hors ligne (bandeau sur toutes les pages) ; une sonde
`/health` toutes les 10 secondes détecte son retour. Un rechargement de page pendant la coupure garde le caissier
connecté (profil de l'onglet) ; un onglet fermé ou un PC redémarré exige le serveur pour se reconnecter (décision 41).
Une nouvelle version s'active dès son installation et s'applique au chargement suivant de la page, jamais au milieu
d'une vente.

**Caisse hors ligne** (7.8, ENF08, décisions 38 à 41) : la session ouverte (avec le dernier numéro de son poste) et ses
tickets sont gardés sur le poste. Serveur injoignable, la caisse continue : scan, recherche et prix du jour lus sur le
poste ; l'encaissement numérote le ticket à la suite du poste (RG34), le met **en file** dans IndexedDB puis imprime le
reçu ; avoir (sur un ticket de la session sans part à crédit) et remboursement de crédit aussi. Avant la mise en file,
le poste fait les contrôles du serveur (paiement, rendu en espèces, référence Mobile Money) ; remise et validation par un
responsable sont refusées, et le crédit doit tenir dans le plafond (dernier solde connu, crédit en file compris). La
file part dans l'ordre dès que le serveur répond (`CaisseHorsLigne`), sous le compte de son caissier seulement ; une
opération refusée reste en file avec son message. Un bandeau indique le nombre d'opérations en attente. La clôture
attend que la file soit vide, et le serveur la refuse tant que le dernier numéro du poste n'est pas arrivé (RG36).
Vérifié dans Chromium : le navigateur intégré de l'outil de développement refuse les service workers.

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

### Opérations faites hors ligne (7.8, ENF08)

Une vente, un avoir ou un remboursement de crédit fait sur le poste coupé du serveur porte un bloc `horsLigne`
(`sessionId`, `numero`, `dateUtc`) et, par ligne, le `prixUnitaire` appliqué. Au rejeu, ce qui a eu lieu sur le terrain
fait foi ; un écart est enregistré comme **anomalie**, jamais comme refus :

| Anomalie | Cas | Ce que fait le serveur |
| --- | --- | --- |
| `NumeroManquant` | le poste propose un numéro au-delà du suivant | garde le numéro, avance le compteur du poste (RG34) |
| `NumeroReattribue` | le numéro est déjà pris (anormal) | attribue le numéro libre suivant |
| `PrixDifferent` | le prix du poste n'est pas celui en vigueur à la date du ticket | garde le prix du poste (celui payé) |
| `PlafondDepasse` | la vente à crédit dépasse le plafond, connu à la synchronisation | enregistre la dette |
| `SessionCloturee` | la session a été clôturée entre-temps (décision 36) | rattache l'opération à cette session |
| `DateCorrigee` | heure du poste avant l'ouverture ou dans le futur | la ramène dans ses bornes |

Le renvoi d'une opération déjà reçue (même identifiant d'idempotence) renvoie l'enregistrement existant, sans doublon.
Un avoir peut désigner un ticket fait hors ligne par son identifiant d'idempotence et ses lignes par leur rang. Les
opérations d'une session ne s'envoient que sous le compte de son caissier (403 `Caisse.SessionAutreCaissier`). Hors
ligne, remise et dépassement de plafond sont refusés par le poste : ils exigent un responsable vérifié en ligne.

## Reprise des données

Pour démarrer une boutique (décision 29) : le Gérant télécharge le **modèle Excel**, le remplit (une ligne par
conditionnement), l'envoie ; l'application le **contrôle entièrement** puis l'**importe tout ou rien**.

| Colonne | Contenu |
| --- | --- |
| Code article | Regroupe les lignes d'un même article ; ne doit pas exister dans l'application |
| Libellé, Libellé en anglais | Sur la ligne Unite |
| Unité de stock, Décimales | Sur la ligne Unite : plus petite unité vendue (RG44) ; 0 à 3 décimales (RG46) |
| Conditionnement, Niveau, Contenu | Chaque article a **une** ligne de niveau `Unite` (contenu 1) ; les autres lignes : Pack, Carton, Sac ou Palette, avec leur contenu en unités de stock |
| Code-barres | Facultatif : EAN-13, EAN-8, UPC-A, clé de contrôle, unique (RG26, RG33) |
| Prix de vente | Facultatif, TTC, par conditionnement (RG45) |
| Coût d'achat | Sur la ligne Unite, par unité de stock, hors TVA récupérable (RG30) : valorise l'inventaire d'ouverture |

- Les colonnes sont lues **par position** : l'en-tête peut être en français ou en anglais. Les nombres acceptent la
  virgule décimale et les séparateurs de milliers d'Excel ; les codes doivent rester en texte (le modèle formate ces
  colonnes en texte).
- Le **contrôle** relève toutes les erreurs, avec leur ligne et leur colonne traduites : champ manquant, nombre,
  décimales, niveau, contenu (entier pour un article à la pièce), ligne Unite absente ou en double, données d'article
  contradictoires entre ses lignes, code article déjà présent, code-barres invalide, en double ou déjà attribué, prix,
  coût.
- L'**import** écrit, dans **une seule transaction**, les articles, conditionnements, codes-barres et prix par les
  contrats des modules Referentiel et Prix (mêmes règles que les écrans), puis crée l'**inventaire d'ouverture**
  avec chaque article à compter et son coût d'achat. Une seule erreur, et rien n'est écrit.
- **Jeu d'exemple** (`?exemple=true`) : 19 articles d'une boutique de quartier (riz, sucre et farine au kg et en sac,
  huile et pétrole au litre, conserves, lait, savon, eau… à la pièce et au carton), 34 conditionnements, codes-barres
  fictifs (préfixe 617 suivi de 99), prix et coûts. Données de démonstration, pas de produits réels.

## Inventaire et démarque

- **Inventaire** (UC11) : d'ouverture (créé par la reprise des données, avec le coût d'achat de chaque article),
  complet (tous les articles actifs) ou partiel (tournant : articles choisis, ou ajoutés en les comptant).
  Statuts EnCours, Valide, Annule.
- **Comptage** par le Magasinier, en unités de stock (selon les décimales de l'article). La quantité théorique est
  **relevée au moment où la ligne est comptée** (RG16) : la boutique continue de vendre et de recevoir pendant
  l'inventaire, l'écart reste juste. Un recomptage remplace le précédent.
- **Validation** par le Comptable (8.3) ; aucun seuil d'écart n'est défini, donc tout écart est validé (RG15,
  décision 31). Refusée tant qu'un article n'est pas compté (pas de zéro implicite). Une seule transaction, un
  mouvement par écart sur l'emplacement de vente : `AjustementInventaire` en entrée (au coût d'ouverture, sinon au
  CUMP, qui ne change donc pas) ou en sortie (au CUMP) ; un manque classé **freinte** devient un mouvement `Demarque`
  (RG47).
- **Démarque connue** (UC20, RG23) : déclarée par le Magasinier (casse, vol, périmé, freinte), validée par le
  Comptable ou le Gérant (mouvement `Demarque` au CUMP) ou refusée avec un motif. Le motif est porté par la
  déclaration, que le mouvement référence (TypeDocument + DocumentId).

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

Sans commande fournisseur ni lots :

1. le Magasinier ou le Gérant saisit la réception en **brouillon** : fournisseur actif, numéro de bon de livraison
   facultatif, lignes article × quantité (en unité de stock, selon la précision de l'article) × coût unitaire ;
   un numéro `R-000001` est attribué ;
2. le brouillon reste modifiable (ses lignes sont remplacées) ;
3. la **validation** contrôle à nouveau chaque ligne, puis écrit dans **une seule transaction** le statut de la
   réception et un mouvement `EntreeReception` par ligne vers l'emplacement de vente (RG13), avec le CUMP recalculé
   par le module Stock (RG17). Si une ligne est refusée, rien n'est écrit. Une réception validée ne se modifie plus ;
   deux validations simultanées n'écrivent qu'une fois (verrou de la réception, idempotence des mouvements).

### Frais d'approche (RG30, décision 18)

Le Comptable ou le Gérant saisit les frais d'une réception : type (transport, transit, douane, autre), montant hors TVA
récupérable, clé de répartition (**valeur** par défaut, ou quantité) et libellé facultatif.

- Chaque frais est réparti sur les lignes au prorata de la clé, à 4 décimales. Le reliquat d'arrondi va à la ligne
  de plus grand poids (la première saisie en cas d'égalité) : la somme des parts égale le montant.
- **Avant la validation :** les frais suivent les lignes du brouillon (nouvelle répartition à chaque modification)
  et se retirent librement. À la validation, chaque ligne entre en stock au **coût de revient** :
  `coût unitaire + frais de la ligne / quantité`.
- **Après la validation (frais tardif) :** le frais est réparti de la même façon, puis chaque ligne revalorise la
  quantité de l'article **encore en stock** (au plus la quantité reçue), au supplément `part / quantité reçue`. La part
  de la marchandise déjà sortie est conservée comme **non incorporée** sur le frais. Tout est écrit dans une seule
  transaction ; un frais validé ne se retire plus.

### Dettes fournisseurs (décision 27)

- Une réception **validée** crée une dette : marchandise (quantité × coût unitaire) **plus ses frais d'approche**, y
  compris les frais tardifs. Un brouillon n'a pas de dette (`Achats.PaiementSurBrouillon`).
- Le Comptable enregistre des **paiements partiels** : espèces, Mobile Money, virement ou chèque, avec une référence
  obligatoire sauf en espèces. Le montant ne peut pas dépasser le reste dû ; la réception est verrouillée pendant
  l'enregistrement, deux paiements simultanés ne peuvent pas la surpayer.
- Statut déduit : non payée, partiellement payée, payée. La liste regroupe aussi les dettes par fournisseur.
- Un paiement n'est jamais modifié ni supprimé : une erreur se corrige par une **annulation** (paiement opposé, motif
  obligatoire, utilisateur et horodatage), une seule fois par paiement.

## Contrôle nocturne des soldes (ENF04)

Chaque nuit à 2 h (heure de Douala), une tâche Hangfire compare chaque solde à son journal ; si le PC serveur est
éteint à cette heure, le passage a lieu à son redémarrage. L'Administrateur peut aussi le lancer à la main.

- **Stock** : le journal de chaque article est rejoué dans l'ordre d'écriture avec les règles du domaine (CUMP compris)
  et comparé aux soldes article × site (physique, réservé, bloqué, CUMP) et article × emplacement × lot (physique).
  Un solde qu'aucun mouvement n'explique est attendu à zéro.
- **Crédit** : la dette de chaque client doit égaler ses ventes à crédit moins ses remboursements et annulations.
- Lecture dans un instantané cohérent (lecture répétable) : le contrôle tourne pendant que la caisse vend. Deux
  passages ne tournent jamais en même temps (verrou consultatif PostgreSQL).
- **Écarts** : un écart retrouvé au passage suivant reste le même (`Ouvert`) ; s'il ne se retrouve plus, il passe à
  `Disparu`. L'Administrateur peut **reconstruire** le solde depuis le journal : tous les soldes de l'article, ou la dette
  du client, sous verrou, sans écrire aucun mouvement ni opération (le journal fait foi, RG01) ; l'écart passe à
  `Reconstruit` avec son auteur et l'heure (ENF03). Rien n'est jamais supprimé.
- Une erreur technique enregistre le passage comme `Echoue` avec son message, sans toucher aux écarts ; pas de nouvel
  essai automatique (passage suivant la nuit d'après).

## Sauvegardes (décision 32)

Le service `sauvegarde` de docker-compose (image `postgres:17-alpine` + rclone + age) tourne même si l'API est arrêtée :

- **Chaque heure** (minute `SAUVEGARDE_MINUTE`, 5 par défaut, et au démarrage) : base complète (`pg_dump`, vérifiée
  par `pg_restore --list`) et volume des fichiers (reçus, clés des jetons), dans une archive datée
  `gestionstock-AAAAMMJJ-HHMM.tar` sur le disque externe (`SAUVEGARDE_REPERTOIRE`). Rotation : `horaire/` 48 h,
  `quotidien/` 30 jours, `mensuel/` 12 mois. Perte maximale en cas de panne : 1 h.
- **Chaque jour** (au premier passage qui suit 24 h sans copie réussie, ce qui rattrape un PC éteint la nuit) : la
  dernière archive, **chiffrée avec age** pour la clé publique du propriétaire (`SAUVEGARDE_CLE_PUBLIQUE`), est envoyée
  par rclone vers `SAUVEGARDE_DISTANT` (`quotidien/` 30, `mensuel/` 12). Sans clé, rien n'est envoyé en clair. La clé
  privée n'est jamais sur le serveur. Perte maximale en cas de vol ou d'incendie : 1 jour.
- **Copie complète** (choix du 17/11/2026) : elle contient les noms et téléphones des clients et du personnel ; elle
  n'est permise que vers un stockage au Cameroun, ou hors du Cameroun avec l'autorisation de transfert obtenue par la
  boutique (loi n° 2024/017).
- Chaque passage écrit son compte rendu dans `exploitation.sauvegarde`, lu par `GET /api/exploitation/etat`.

Restauration (API arrêtée) :

```bash
docker compose -f deploy/docker-compose.yml stop api
```

```bash
docker compose -f deploy/docker-compose.yml run --rm --entrypoint restaurer.sh sauvegarde --oui /sauvegardes/horaire/gestionstock-AAAAMMJJ-HHMM.tar
```

Une copie hors site se restaure de la même façon avec `--cle <clé privée>` (chemin monté dans le conteneur) ;
`--base <nom> --sans-fichiers` restaure dans une base d'essai sans toucher à la boutique.

## Rapports (décision 33)

Le module Rapports n'a ni schéma ni DbContext. Il lit, avec Dapper et dans une transaction **en lecture seule**, les
**vues `v_*`** que chaque module publie dans son schéma par une migration (`VuesLecture`) : c'est leur contrat de
lecture (section 7, CQRS côté lecture). Un test d'architecture vérifie qu'aucune requête ne lit autre chose.

| Module | Vues |
| --- | --- |
| Identite | `v_utilisateurs` (matricule, prénom, nom, désignation « Prénom Nom (matricule) », actif) |
| Referentiel | `v_articles` |
| Caisse | `v_tickets`, `v_lignes_vendues`, `v_paiements` (encaissé = espèces moins le rendu), `v_remboursements_credit`, `v_sessions` |
| Credit | `v_clients`, `v_operations`, `v_ventes_credit` (reste dû par vente) |
| Stock | `v_mouvements` (variation signée), `v_soldes` (valeur) |
| Achats | `v_receptions` (marchandise, frais, payé), `v_frais` (part non incorporée), `v_paiements_fournisseurs` |

Période en jours de Douala (`du`, `au` inclus, une année au plus ; aujourd'hui par défaut). Un avoir vient en
déduction ; le coût des ventes est celui des mouvements de stock du ticket (CUMP du moment) ; marge = CA HT − coût.
Montants calculés à 4 décimales, arrondis à l'unité à l'affichage (RG32). Le personnel (caissiers, validations,
auteurs) y est désigné par « Prénom Nom (matricule) » ; les documents gardent, eux, le matricule tracé.

### Exports comptables (UC16)

Pour le comptable externe, sans ERP (décision 7). En-têtes dans la langue de la requête, montants arrondis à l'unité
(RG32), dates à l'heure de Douala, ligne de total des montants en Excel.

| Journal | Contenu |
| --- | --- |
| `ventes` | Tickets de la période : vente ou avoir (en négatif), caissier, client, ticket d'origine, remise, HT, TVA, TTC |
| `achats` | Réceptions validées (marchandise et frais saisis avant la validation), frais tardifs, paiements et annulations |
| `encaissements` | Paiements des tickets hors crédit (espèces moins le rendu, avoirs en négatif) et remboursements de crédit |
| `creances` | Opérations de crédit (RG43) : vente à crédit (+), remboursement et annulation (−), échéance |
| `valeur-stock` | Quantité, CUMP et valeur de chaque article en stock, au jour de l'export |

Formats : **Excel** (ClosedXML : une feuille, en-têtes figés, filtre, total) ou **CSV** pour un Excel français
(séparateur `;`, UTF-8 avec BOM, fins de ligne CRLF, montants entiers, décimales à la virgule).

## Revalorisation (RG30)

Des frais d'approche connus après la réception donnent un mouvement `Revalorisation` : aucun emplacement, aucune
variation de quantité. Il porte la quantité revalorisée et le supplément par unité ; le module Stock ajoute ce montant
à la valeur du stock du site, soit `CUMP += quantité × supplément / stock physique`. La quantité revalorisée ne peut
pas dépasser le stock physique présent (`Stock.RevalorisationSansStock`). Comme tout mouvement, il est immuable et le
solde reste recalculable depuis le journal.

## Comptes, rôles et permissions

Comptes locaux nominatifs (ENF06), stockés dans le schéma `identite`. Mot de passe : 8 caractères au moins,
avec majuscule, minuscule, chiffre et caractère spécial ; compte verrouillé 5 minutes après 5 échecs.
Au premier démarrage, l'Administrateur initial est créé depuis la configuration ; il crée ensuite les autres comptes.

**Fiche du personnel** : prénom et nom (obligatoires), téléphone (facultatif), **matricule**, un ou plusieurs rôles,
langue de l'interface, statut actif ou désactivé. Rien d'autre (loi n° 2024/017).

- **Matricule = identifiant de connexion**, tracé dans tous les documents : il ne change plus après la création. Celui
  de l'entreprise est saisi tel quel (lettres, chiffres, `.`, `-`, `_`, 20 caractères au plus, unique) ; sinon il est
  généré (`S000001`, `S000002`…). Les comptes antérieurs gardent leur identifiant comme matricule. Le mot de passe
  est contrôlé avant de tirer un numéro : une création refusée pour ce motif ne fait pas sauter de matricule (une
  séquence PostgreSQL ne recule jamais ; un trou reste possible après un autre refus, il est sans conséquence).
- **Mot de passe provisoire** : à la création comme après un oubli, l'Administrateur fixe un mot de passe provisoire.
  Tant qu'il n'est pas changé, le jeton ne donne **aucune permission** (403 partout sauf `/moi` et le changement de
  mot de passe) ; l'Administrateur ne connaît jamais le mot de passe définitif. Une réinitialisation ferme les
  sessions en cours et lève un verrouillage après trop d'essais.
- **Désactivation** (départ d'un employé) : connexion (`Identite.CompteDesactive`), renouvellement de session et
  validation par un responsable refusés ; le compte et ses documents restent. Réactivation possible.
- **Rôles** : un changement de rôles ferme les sessions en cours (elles se reconnectent avec les nouveaux droits).
- Il reste toujours **un Administrateur actif** (`Identite.DernierAdministrateur`).
- **Langue** : le choix « English » / « Français » du menu est enregistré sur le compte et retrouvé à la connexion,
  sur n'importe quel poste (ENF09).
- **Journal du compte** (ENF03) : création, modification, désactivation, réactivation, réinitialisation et changement
  de mot de passe, avec l'auteur et l'horodatage, sans aucun mot de passe.

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
| `Inventaire.Consulter` | Gerant, Comptable, Magasinier |
| `Inventaire.Compter`, `Inventaire.Demarque.Declarer` | Magasinier |
| `Inventaire.Valider` | Comptable |
| `Inventaire.Demarque.Valider` | Comptable, Gerant |
| `Reprise.Importer` | Administrateur, Gerant *(provisoire)* |
| `Rapports.Consulter` | Gerant, Comptable |
| `Rapports.Exports.Produire` | Comptable, Gerant *(Gérant provisoire)* |
| `Caisse.Vendre` | Caissier |
| `Caisse.Tickets.Consulter` | Caissier, Gerant, Comptable |
| `Caisse.Sessions.Consulter`, `Caisse.Sessions.Valider` | Gerant, Comptable |
| `Caisse.Sessions.Cloturer` | Gerant *(décision 36 : session restée ouverte)* |
| `Caisse.Anomalies.Traiter` | Gerant *(anomalies de synchronisation, 7.8)* |
| `Referentiel.Articles.Gerer` | Gerant, Administrateur *(provisoire)* |
| `Achats.Consulter` | Gerant, Comptable, Magasinier |
| `Achats.Fournisseurs.Gerer` | Gerant, Magasinier *(provisoire)* |
| `Achats.Receptions.Saisir`, `Achats.Receptions.Valider` | Magasinier, Gerant *(validation provisoire)* |
| `Achats.Frais.Saisir` | Comptable, Gerant *(Gérant provisoire)* |
| `Achats.Paiements.Saisir` | Comptable |
| `Exploitation.Consulter` | Administrateur, Gerant, Comptable *(contrôle des soldes, sauvegardes)* |
| `Exploitation.Controles.Lancer`, `Exploitation.Soldes.Reconstruire` | Administrateur |

## Tests

| Projet | Contenu | Docker |
| --- | --- | --- |
| `GestionStock.Stock.Domain.Tests` | Règles du domaine : mouvements, soldes, CUMP, RG03 / RG22 | Non |
| `GestionStock.Infrastructure.Tests` | Horloge, stockage de fichiers, chaîne de connexion, clés de protection conservées | Non |
| `GestionStock.Api.IntegrationTests` | API complète par HTTP : connexion, jetons, 401 / 403, comptes, articles, fournisseurs, réceptions (CUMP, transaction, validation simultanée), soldes et journal | Oui |
| `GestionStock.ArchitectureTests` | Dépendances entre projets, visibilité, couches, traductions, conventions de style du client | Non |
| `GestionStock.Client.Tests` | Vente en cours au comptoir : panier, arrondi identique au serveur, billets rapides, compte juste, pas de saisie | Non |
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
  appliquée, que les vues de lecture des rapports existent, qu'une route métier répond 401 sans jeton, que
  l'Administrateur initial se connecte, que le cycle de vie d'un compte du personnel fonctionne
  ([.github/scripts/verifier-comptes.sh](.github/scripts/verifier-comptes.sh) : matricule généré, mot de passe
  provisoire sans aucune permission, changement par l'employé, droits de ses deux rôles, journal, désactivation qui
  refuse la connexion), que seuls le nom et la présence d'un logo de la boutique sont publics (l'en-tête des reçus
  exige une connexion), que la feuille de style à jetons et les raccourcis de la caisse sont servis, que l'application
  installable est servie (manifeste, service worker, liste des fichiers à garder) et que les données de la caisse et les
  anomalies exigent une connexion, et que le client
  Blazor est servi. Les tests du client (`GestionStock.Client.Tests`) tournent avec les autres dans le job de build.

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
| 19 | Architecture, CI, README, démonstration de la caisse | Fait |
| 20 | Module Inventaire : inventaires, écarts, démarque | Fait |
| 21 | Module Reprise : import Excel des articles, codes, prix et coûts | Fait |
| 22 | Client : écrans reprise, inventaire, démarque | Fait |
| 23 | Architecture, CI, README, démonstration de l'inventaire | Fait |
| 24 | Stock : mouvement Revalorisation (RG30) | Fait |
| 25 | Achats : frais d'approche, répartition, revalorisation des frais tardifs | Fait |
| 26 | Achats : dettes fournisseurs et paiements | Fait |
| 27 | Module Rapports : ventes et marge, stock, créances et dettes, caisse et anomalies | Fait |
| 28 | Exports comptables Excel et CSV | Fait |
| 29 | Client : écrans frais, dettes, rapports, exports | Fait |
| 30 | Architecture, CI, README, démonstration de la tranche 4 | Fait |
| 31 | Identité : fiche du personnel, matricule, plusieurs rôles, désactivation, mot de passe provisoire | Fait |
| 32 | Noms complets du personnel dans les rapports et les exports | Fait |
| 33 | Client : écrans des comptes et « Mon compte » | Fait |
| 34 | Architecture, CI, README, démonstration de la tranche 5 | Fait |
| 35 | Design : jetons, composants, icônes, menu en tiroir (responsive), accessibilité | Fait |
| 36 | Design : caisse, vente (champ unique, articles rapides, pavé numérique, raccourcis) | Fait |
| 37 | Design : caisse, ouverture, session bloquée (clôture par le Gérant), clôture guidée | Fait |
| 38 | Accueil en tableau de bord par rôle | Fait |
| 39 | Téléphone et tablette : listes en cartes, caisse sur tablette | Fait |
| 40 | Identité et finitions (nom et logo, unités traduites, quantités) | Fait |
| 41 | Vérification, CI, README, démonstration de la tranche 6 | Fait |
| 42 | Hors ligne : catalogue et grille de prix de la caisse, compression des réponses | Fait |
| 43 | Hors ligne : tickets du terrain côté serveur, anomalies, clôture gardée | Fait |
| 44 | Hors ligne : application installable (PWA), stockage du poste, état de connexion | Fait |
| 45 | Hors ligne : vente, avoir, remboursement en file, synchronisation | Fait |
| 46 | Anomalies de synchronisation à traiter | Fait |
| 47 | Vérification, CI, README, démonstration de la tranche 7 | Fait |
| 48 | Exploitation : module, suivi des sauvegardes, planificateur (Hangfire) | Fait |
| 49 | Exploitation : contrôle des soldes de stock et de crédit par contrat | Fait |
| 50 | Exploitation : contrôle nocturne, écarts, reconstruction | Fait |
| 51 | Exploitation : conteneur de sauvegarde (locale horaire, hors site chiffrée) | Fait |
| 52 | Exploitation : écrans Contrôles et Sauvegardes, accueil | À faire |
| 53 | Vérification, CI, README, démonstration de la tranche 8 | À faire |

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

### Démonstration de la caisse (tranche 2)

Vérifiée dans le navigateur sur http://localhost:8080, avec les comptes de démonstration créés par l'Administrateur
(`gerant`, `caissier`, `comptable`, mot de passe de test `Secret123!`) :

1. **Gérant**, fiche de `LAIT-397` : la boîte reçoit le code `6170000000011` et le prix 450 XAF ; un carton de 48
   boîtes est créé, avec le code `6170000000004` et le prix 20 000 XAF (RG45) ;
2. **Gérant**, Clients : création de « Mme Ngo Mballa » ; le **Comptable** fixe son plafond à 30 000 XAF ;
3. **Caissier**, Caisse : session ouverte sur CAISSE-1 avec 10 000 XAF de fond ; scan du carton puis de deux boîtes
   (le second scan incrémente la quantité) ; 20 900 XAF payés 5 900 en Mobile Money (référence) et 16 000 en
   espèces, rendu 1 000 ; ticket `CAISSE-1-000001`, reçu 80 mm et PDF ;
4. vente à crédit de 2 cartons (40 000 XAF) : refusée au-delà du plafond, puis validée par les identifiants du Gérant
   saisis sur le poste ; ticket `CAISSE-1-000002` (la tentative refusée n'a pas consommé de numéro) ;
5. avoir d'une boîte sur le ticket 1 : `CAISSE-1-000003`, 450 XAF rendus, la boîte revient en stock ;
6. clôture : espèces théoriques 24 550 XAF, comptées 24 500, écart −50 ; le **Comptable** valide l'écart ;
7. journal de l'article : ventes et avoir au CUMP de 600 XAF ; stock négatif sur l'emplacement de vente, jamais
   bloquant (RG22) ; la cliente doit 40 000 XAF.

### Démonstration de la reprise et de l'inventaire (tranche 3)

Vérifiée dans le navigateur sur http://localhost:8080, avec les comptes de démonstration (`magasinier`, `comptable`,
mot de passe de test `Secret123!`) :

1. **Administrateur**, Reprise des données : le modèle avec exemple est contrôlé (34 lignes, 19 articles,
   34 conditionnements, 34 codes-barres, 34 prix), puis importé ; l'inventaire d'ouverture `INV-000001` est créé ;
2. **Magasinier**, INV-000001 : scan de deux sacs de riz de 25 kg puis d'un kilo (51 kg), d'un carton de 50 boîtes
   de sardines puis d'une boîte (51 boîtes) ; saisie manuelle de 40 bougies ; chaque écart est valorisé au coût
   d'achat du fichier ;
3. **Comptable** : seule la validation lui est proposée ; 19 lignes comptées sur 19, écarts valorisés à 217 550 XAF ;
   l'inventaire validé fait entrer le stock (mouvements `AjustementInventaire`) ;
4. **Magasinier**, Démarque : `DEM-000001`, 3 boîtes de sardines, motif casse, commentaire « Carton tombé » ;
5. **Comptable** : validation de la démarque, sortie de 3 boîtes au CUMP (mouvement `Demarque`).

L'inventaire partiel (démarré vide, articles ajoutés au scan), la freinte, le comptage pendant les ventes (RG16) et
le refus d'une démarque sont couverts par les tests d'intégration.

### Démonstration des frais, dettes, rapports et exports (tranche 4)

Vérifiée dans le navigateur sur http://localhost:8080, sur la base de démonstration (comptes `comptable`, `caissier`,
mot de passe de test `Secret123!`) :

1. **Comptable**, Rapports : ventes et marge du jour (CA, coût des ventes au CUMP, marge par article et par
   caissier ; la marge négative du lait vendu en carton sous son coût ressort), stock et valeur (20 articles,
   un stock négatif), créances et dettes (la cliente à crédit, le grossiste), caisse et anomalies (sessions et
   écart de −50, avoir avec son ticket d'origine, vente à crédit validée par le Gérant) ;
2. **Comptable**, réception `R-000002` validée : 1 000 XAF de transport ajoutés après coup. Le lait est en stock
   négatif, donc tout le frais reste non incorporé et le CUMP ne bouge pas. La dette passe à 8 000 XAF ;
3. paiement de 5 000 XAF en espèces : dette « partiellement payée », reste 3 000 ; l'écran Dettes fournisseurs
   totalise 8 000 XAF restant dus ;
4. Exports comptables : journal des achats en CSV (réceptions, frais tardif, paiement) et journal des ventes en
   Excel, produits par l'API.

La répartition des frais avant validation, la revalorisation d'un stock encore présent, l'annulation d'un paiement et
le contenu de chaque journal sont couverts par les tests d'intégration.

### Démonstration des comptes du personnel (tranche 5)

Vérifiée dans le navigateur sur http://localhost:8080, sur la base de démonstration (Administrateur `admin`,
comptes `caissier` et `comptable`, mot de passe de test `Secret123!`) :

1. **Administrateur**, Utilisateurs : création d'« Awa Test », Caissier et Magasinier, sans matricule ni téléphone,
   avec un mot de passe provisoire ; le matricule `S000001` est généré et la fiche s'ouvre, journal compris ;
2. désactivation puis réactivation du compte : statut, message et deux lignes de journal (date, auteur) ;
3. connexion d'Awa par son matricule et le mot de passe provisoire : elle arrive sur **Mon compte** ; toute autre
   page, même ouverte par son adresse, y ramène ; une confirmation différente est refusée ; le mot de passe changé,
   le menu réunit les droits de ses deux rôles et l'accueil la salue par son prénom ;
4. passage en anglais, préférence du navigateur effacée, nouvelle connexion : l'interface revient en anglais (langue
   mémorisée sur le compte) ;
5. **Administrateur**, fiche de `caissier` : prénom « Paul », nom « Etoa » ; le matricule ne change pas ;
6. **Comptable**, Rapports, ventes par caissier : le caissier apparaît sous « Paul Etoa (caissier) ».

Le dernier Administrateur actif, la fermeture des sessions après un changement de rôles, le mot de passe oublié
remplacé par un provisoire et la validation refusée à un responsable désactivé sont couverts par les tests
d'intégration.

### Démonstration de l'interface (tranche 6)

Vérifiée dans le navigateur sur http://localhost:8080, sur la base de démonstration (comptes `gerant`, `caissier`,
`comptable`, mot de passe de test `Secret123!`) :

1. **Caissier**, poste bloqué par une session restée ouverte : l'écran dit qui la tient et depuis quand ; le
   **Gérant** la clôture à sa place depuis Sessions de caisse (comptage par coupure, « comptée par gerant ») ;
2. **Caissier**, ouverture avec 5 000 XAF de fond par le bouton « + 5 000 XAF » ; vente au **clavier seul** : trois
   codes-barres à la douchette (sardines, bougie, eau, 900 XAF), F4 (compte juste), F8 (encaisser), Entrée (vente
   suivante) ; le reçu indique « Caissier : Paul Etoa (caissier) » ;
3. vente **au toucher** avec rendu : tuile rapide « Riz parfumé » (choisie par le Gérant), billet de 1 000 XAF, rendu
   de 350 XAF affiché en grand ;
4. clôture guidée par coupure (5 000, 1 000, 500 et 50) : 6 550 XAF comptés, écart 0, sans afficher le théorique
   avant la clôture ;
5. **Gérant**, accueil : ventes et marge du jour, clôtures à valider, stock négatif, créances, dettes ;
6. téléphone (375 px) : menu en tiroir, accueil sur deux colonnes, listes en cartes, barre de total à la caisse ;
   tablette (768 px) : tableaux sur toute la largeur, caisse sur une colonne avec la barre de total ;
7. en anglais : unités traduites (box, pack, piece, bottle), quantités sans zéros inutiles (« 20 kg »).

Le logo (aucun n'est encore fourni), la clôture par le Gérant, les articles rapides, le comptage par coupure et la
mise en forme des quantités et des unités sont couverts par les tests d'intégration et les tests du client.

### Démonstration de la caisse hors ligne (tranche 7)

Vérifiée dans Chromium (le navigateur intégré de l'outil de développement refuse les service workers), sur la
base de démonstration, avec une vraie coupure : le conteneur de l'API arrêté (`docker compose -f
deploy/docker-compose.yml stop api`) puis relancé (`start api`). Compte `caissier`, mot de passe de test `Secret123!`,
poste de test `CAISSE-2` :

1. **en ligne**, ouverture de la session (5 000 XAF de fond) : la session, le dernier numéro du poste, le catalogue
   avec ses codes, la grille de prix, les clients à crédit et l'en-tête du reçu sont gardés sur le poste ;
2. **API arrêtée** : bandeau « Hors ligne » ; scan des sardines, F4, F8 : ticket `CAISSE-2-000002`, numéroté par le
   poste, reçu complet ; tuile « Riz parfumé » et billet de 1 000 XAF : ticket `CAISSE-2-000003`, 350 XAF rendus ;
3. rechargement de la page, toujours hors ligne : la caisse reprend sa session et ses deux opérations en attente ;
4. clôture refusée (« … opération(s) n'ont pas encore été envoyées ») ; avoir sur le ticket 2 depuis la liste des
   tickets du poste : `CAISSE-2-000004` ; remise refusée hors ligne ;
5. **API relancée** : la file part seule ; en base, chaque ticket une seule fois, à l'heure du terrain et avec le numéro
   du poste, compteur du poste à 4, mouvements de stock écrits, aucune anomalie (prix du jour) ;
6. clôture en ligne : 5 650 XAF comptés, écart 0 ;
7. **Gérant** : une vente « hors ligne » envoyée avec un numéro sauté apparaît sur l'accueil (« Anomalies hors ligne :
   1 ») et dans l'écran des anomalies (« Numéros manquants », détail « 5 ») ; il la marque traitée avec un commentaire.

Le prix différent, le plafond dépassé, la session clôturée entre-temps, l'heure corrigée, le remboursement hors ligne,
le refus sous le compte d'un autre caissier et la clôture gardée par le serveur sont couverts par les tests
d'intégration (`HorsLigneTests`).

## Points ouverts

- Validation par un responsable au poste du caissier (ses identifiants), et attribution au Gérant de la validation
  d'un dépassement de crédit (RG40 cite « un utilisateur autorisé ») : choix provisoires.
- Reçu PDF en Courier (police standard des lecteurs PDF, sans dépendance ni police embarquée) au lieu de
  PDFsharp-MigraDoc, qui exigerait d'embarquer une police sous Linux.
- Avoir valorisé au CUMP courant à son retour en stock (le dossier ne précise pas la valorisation d'un retour).
- Montants de caisse en francs CFA entiers et arrondi de chaque ligne à l'unité (déduits de 1.5 et RG32).
- Reprise : `Reprise.Importer` attribuée à l'Administrateur et au Gérant ; un code article déjà existant est une
  erreur (la reprise crée, elle ne met rien à jour).
- Délai de paiement par défaut (30 jours) : non fixé par le dossier, choix provisoire.
- Format des codes internes (`INT` + 6 chiffres) : non fixé par le dossier, choix provisoire.
- Coût unitaire saisi sur la ligne de réception : absent de `LigneReception` dans le modèle UML (il y vient de la
  commande fournisseur) ; ajouté faute de commande en V1, à reporter dans le dossier. Statuts de réception retenus :
  Brouillon et Validee.
- Rôles pour la gestion des articles et des fournisseurs, et validation d'une réception sans commande
  (le processus 6.1 cite l'Acheteur, rôle absent de la V1) : attributions provisoires, à confirmer (voir ci-dessus).

- Effet des mouvements `Blocage` et `Deblocage` : refusés tant qu'il n'est pas défini.
- Frais d'approche : clé de répartition Poids non proposée (pas de poids sur l'article en V1) ; saisie par le
  Comptable (8.3) et, provisoirement, par le Gérant ; un frais saisi après la validation ne se retire plus.
- Dettes fournisseurs : pas d'échéance de paiement (le dossier n'en fixe pas) ; modes de paiement espèces, Mobile
  Money, virement et chèque ; dette = marchandise et frais d'approche (choix validés).
- Exports : format CSV (`;`, UTF-8 avec BOM) ; production attribuée au Comptable et, provisoirement, au Gérant.
- Comptes : matricule généré au format `S` + 6 chiffres ; prénom et nom obligatoires, téléphone facultatif ; après une
  désactivation, un jeton d'accès déjà délivré reste valable jusqu'à son expiration (1 h au plus), son renouvellement
  est refusé.
- Revalorisation (RG30) : le supplément porte sur la quantité encore en stock ; la part des frais qui concerne de la
  marchandise déjà vendue n'entre pas dans le CUMP (conservée sur la réception, signalée dans les rapports).
- Motif de démarque (RG23) : absent du modèle UML ; porté par la déclaration de démarque que le mouvement référence.
- Statuts d'inventaire EnCours / Valide / Annule (valeurs non listées par le dossier) ; inventaire complet validable
  seulement quand tout est compté.
- CUMP après un stock négatif ou nul : le coût de l'entrée devient le CUMP (choix technique à confirmer).
- Interface (tranche 6) : comptage de clôture « à l'aveugle » (le théorique n'est pas montré au caissier ; choix de
  conception, le dossier ne le fixe pas) ; le Gérant qui clôture une session à la place de son titulaire peut aussi en
  valider l'écart (le dossier ne sépare pas ces deux gestes) ; logo absent du reçu PDF (il figure sur le reçu imprimé
  par le navigateur) ; liste fixe des unités traduites en anglais.
- Hors ligne (tranche 7, décisions 38 à 41) : réception et comptage hors ligne restent à faire ; l'avoir d'un ticket
  payé en partie à crédit exige le serveur ; pas de connexion hors ligne (un onglet fermé ou un PC redémarré attend le
  serveur, les opérations du poste sont gardées) ; les postes de caisse doivent utiliser Chrome ou Edge (le navigateur
  intégré de l'outil de développement n'accepte pas les service workers) ; une opération refusée au rejeu
  (cas qui ne devrait pas arriver, le poste faisant les contrôles du serveur) reste en file avec son message et demande
  une intervention.
