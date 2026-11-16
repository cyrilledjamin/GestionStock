# Tranche 7 : caisse hors ligne (plan, étapes 42 à 47)

## Contexte

**Exigences du dossier :**
- **ENF08, RG31, décisions 24 et 35 :** un poste coupé du serveur pendant 8 heures continue de vendre, puis synchronise
  sans doublon.
- **Section 7.8 :** vente, avoir et remboursement de crédit hors ligne, stockés dans le navigateur (IndexedDB) avec un
  identifiant d'idempotence, et envoyés au retour du réseau. Un conflit au rejeu est enregistré et devient une
  **anomalie** : il n'est jamais rejeté. Le plafond de crédit hors ligne s'appuie sur le dernier solde connu.
- **RG34 :** numérotation continue par caisse.
- **RG36 :** clôture impossible tant que des ventes ne sont pas synchronisées.

**Aujourd'hui :** l'application ne fonctionne qu'en ligne. Le serveur est prêt pour l'idempotence (verrou sur
`IdIdempotence`), mais il numérote lui-même les tickets (`caisse.compteur_caisse`) et date chaque vente à
l'encaissement.

**Décisions de l'utilisateur (16/11/2026) :**
- **Périmètre :** caisse d'abord (vente, avoir, remboursement de crédit) ; réception et comptage hors ligne dans une
  tranche suivante.
- **Remises et dépassements de plafond :** bloqués hors ligne, car ils exigent un responsable vérifié par le serveur.
  Une vente à crédit reste possible dans la limite du dernier solde connu du client.
- **Ouverture de session :** connexion requise.
- **Redémarrage du poste pendant la coupure :** pas de reconnexion hors ligne. Les ventes en attente restent sur le
  poste et partent quand le caissier se reconnecte.

Ces quatre décisions seront ajoutées au dossier (section 8.2 bis, décisions 38 à 41).

## Principes retenus

- **Numérotation (RG34) :**
  - En ligne, le serveur numérote comme aujourd'hui, et le poste mémorise le dernier numéro reçu.
  - Hors ligne, le poste attribue lui-même le numéro suivant de son poste. Il est seul à vendre sur ce poste, puisque
    le serveur n'y admet qu'une session ouverte.
  - À la synchronisation, le serveur garde le numéro proposé et avance son compteur. Un trou dans la suite, ou un
    numéro déjà pris (cas qui ne devrait pas se produire), devient une anomalie ; dans le second cas, le ticket reçoit
    le numéro libre suivant.
- **Ce qui a eu lieu sur le terrain fait foi (7.8) :** un ticket hors ligne porte sa date, son numéro, ses prix
  unitaires et l'identifiant de sa session. Le serveur les enregistre tels quels, puis signale chaque écart en
  anomalie :
  - un prix différent de celui en vigueur à la date du ticket ;
  - un plafond de crédit dépassé ;
  - une session clôturée entre-temps (par exemple par le Gérant, décision 36).
- **Même caissier :** la file d'attente appartient au caissier qui l'a constituée. Si un autre compte se connecte sur
  le poste, l'écran lui indique que ce caissier doit se reconnecter.
- **Aucune perte :** la file est gardée dans IndexedDB, et le stockage persistant est demandé au navigateur
  (`navigator.storage.persist`). Une entrée n'est retirée de la file qu'une fois acceptée par le serveur.

## Étapes

| Étape | Contenu | Modules touchés |
| --- | --- | --- |
| 42 | **Données de la caisse pour le poste** : `GET /api/referentiel/catalogue-caisse` (vendables actifs avec leurs codes actifs et articles rapides) ; `GET /api/prix/grille` (prix applicable aujourd'hui et prochain prix daté de chaque conditionnement) ; compression des réponses dans l'hôte (ENF10). Tests d'intégration. | Referentiel, Prix, Api |
| 43 | **Tickets hors ligne côté serveur** : bloc `HorsLigne` facultatif sur la vente, l'avoir et le remboursement (session, numéro, date, prix unitaires) ; numéro imposé avec reprise du compteur ; prix du terrain ; avoir sur un ticket désigné par son identifiant d'idempotence ; dépassement de plafond accepté et signalé (`IServiceCredit` reçoit une vente « hors ligne » forcée) ; entité `AnomalieCaisse` (migration, vue `caisse.v_anomalies`) ; clôture refusée (`Caisse.VentesNonSynchronisees`) si le dernier numéro annoncé par le poste n'est pas encore reçu. Tests d'intégration : rejeu sans doublon, ordre, trou, prix différent, plafond, session clôturée entre-temps, clôture gardée. | Caisse, Credit |
| 44 | **Client installable et cache du poste** : PWA (manifeste, service worker qui met l'application en cache, icônes PNG) ; module JS IndexedDB (`stockage-local.js`) et service .NET `StockageLocal` ; service `EtatConnexion` (événements online / offline, sonde `/health`, erreurs réseau de `Api`) ; `CacheCaisse` (catalogue, grille, clients à crédit avec solde, tickets de la session, dernier numéro, date de dernière mise à jour), rafraîchi à l'ouverture de la caisse puis périodiquement ; résolution locale d'un code scanné (même règle que `ServiceConditionnements.ResoudreAsync` : code actif, UPC-A lu en EAN-13 avec un zéro de tête, puis code article). | Client |
| 45 | **Caisse hors ligne** : sans serveur, l'encaissement numérote et date le ticket sur le poste, l'ajoute à la file et imprime le reçu localement (même `RecuTicket`) ; avoir sur un ticket de la session et remboursement de crédit en file ; remise et dépassement de plafond refusés avec un message ; plafond contrôlé sur le dernier solde connu moins le crédit déjà en file ; synchronisation dans l'ordre dès le retour du réseau (puis renouvellement du jeton), sans jamais perdre une entrée ; bandeau « Hors ligne : N ventes en attente, données du HH:mm » ; clôture bloquée tant que la file n'est pas vide (« clôture en attente de synchronisation »). Logique pure (file, numérotation, plafond, résolution de code) dans `Services/` et testée dans `GestionStock.Client.Tests`. | Client |
| 46 | **Anomalies à traiter** : liste `GET /api/caisse/anomalies` et traitement par le Gérant (commentaire, auteur, horodatage, ENF03) ; section du rapport « Caisse et anomalies » ; indicateur « Anomalies à traiter » sur l'accueil (tableau de bord) ; écran de traitement. | Caisse, Rapports, Client |
| 47 | **Vérification** : coupure simulée en arrêtant le conteneur de l'API, ventes, avoir, remboursement, rechargement de la page, retour du réseau, synchronisation sans doublon, clôture ; CI ; README ; démonstration ; décisions 38 à 41 dans le dossier. | — |

Chaque étape garde `dotnet build` (Release, sans avertissement) et `dotnet test` au vert, et fait l'objet d'un commit.

## Fichiers principaux

- **Caisse :**
  - `Application/ServiceTickets.cs` : `Ecrire`, `NumeroSuivant`, `PreparerVente`, `VendreAsync`, `FaireAvoirAsync` ;
  - `Application/ServiceSessions.cs` : `CloturerAsync` ;
  - `Domain/TicketCaisse.cs` (date et numéro fournis) ;
  - nouveaux `Domain/AnomalieCaisse.cs` et `Endpoints` ;
  - migrations `TicketsHorsLigne` et `Anomalies`.
- **Credit :** `IServiceCredit.EnregistrerVenteAsync` (variante hors ligne sans refus de plafond).
- **Referentiel / Prix :** `ServiceConditionnements` (catalogue) ; endpoints Prix (grille).
- **Rapports :** `ServiceRapports.CaisseAsync` et `TableauDeBordAsync` (anomalies).
- **Client :**
  - nouveaux fichiers dans `wwwroot` : `manifest.webmanifest`, `service-worker*.js`, icônes PNG ;
  - nouveau `wwwroot/js/stockage-local.js` ;
  - nouveaux services `Services/` : `StockageLocal`, `EtatConnexion`, `CacheCaisse`, `FileHorsLigne`,
    `Synchroniseur` ;
  - fichiers modifiés : `Services/Api.cs` (erreur réseau distinguée), `Pages/Caisse.razor`,
    `Layout/MainLayout.razor` (bandeau), `GestionStock.Client.csproj` (`ServiceWorkerAssetsManifest`).
- **Tests :** `CaisseTests.cs`, nouveaux tests `HorsLigneTests.cs`, `GestionStock.Client.Tests`.
- **Documentation :** `docs/dossier-analyse.md` (décisions 38 à 41), `README.md`, `docs/plan-tranche-7-hors-ligne.md`.

## Vérification

1. Après chaque étape : build Release et `dotnet test` dans le conteneur SDK 10 ; ajout des tests d'intégration
   hors ligne.
2. Dans le navigateur, à l'étape 47 :
   - caissier en session ;
   - arrêt du conteneur `api` : l'application reste chargée (service worker) et le bandeau passe en « Hors ligne » ;
   - vente au scan (résolution locale), vente avec rendu, avoir, remboursement de crédit, reçu imprimé ;
   - remise et dépassement de plafond refusés ;
   - rechargement de la page : la file est conservée ;
   - clôture refusée ;
   - redémarrage de l'API : la synchronisation se lance seule ;
   - contrôles : tickets numérotés sans trou, aucun doublon (même renvoyé deux fois), stock et crédit à jour,
     anomalies attendues ;
   - clôture acceptée ensuite.
3. CI : les tests d'intégration hors ligne tournent avec les autres ; contrôle des endpoints `catalogue-caisse` et
   `grille`.
