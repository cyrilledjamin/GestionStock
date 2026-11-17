# Performances

Ce document suit les mesures de la tranche 9 (étapes 56 à 58). Les mesures sont prises sur un jeu de données
volumineux et comparées aux budgets du dossier :
- **ENF05** : validation d'un ticket en moins d'1 s ;
- **ENF10** : application utilisable sur une connexion mobile lente.

## Méthode

**Jeu de données.** [deploy/volume/generer.sql](../deploy/volume/generer.sql) remplit la base. L'échelle 1 correspond
à une grosse boutique sur 3 ans :
- 20 000 articles ;
- 2 000 tickets par jour sur 4 postes, soit ≈ 2,2 millions de tickets et ≈ 6,5 millions de mouvements de stock ;
- 1 000 clients à crédit ;
- une réception par fournisseur et par mois, avec 50 fournisseurs.

Le jeu est cohérent : le contrôle des soldes y passe sans écart.

**Mesures.** [deploy/volume/mesurer.py](../deploy/volume/mesurer.py) envoie, pour chaque route, une requête
d'échauffement puis 10 requêtes. Il retient la médiane, le 95e centile et le maximum. Les réponses ne sont pas
compressées : la taille indiquée est celle du JSON brut. Le ticket mesuré compte 3 lignes, payées en espèces ; il est
validé 30 fois sur une session ouverte pour la mesure.

**Budgets retenus** (plan de la tranche 9) :

| Opération | Budget |
| --- | --- |
| Validation d'un ticket | < 1 s, cible serveur < 300 ms au 95e centile |
| Liste ou recherche | < 1 s |
| Rapport d'un mois | < 2 s |
| Ouverture de la caisse sur connexion lente | < 3 s (mesurée à l'étape 58) |

## Mesures de référence (étape 56, avant optimisation)

**Échelle 0,25 :** 5 000 articles, 547 500 tickets, 1 641 158 lignes de ticket, 1 825 163 mouvements de stock, base
de 2,2 Go.

**Conditions de mesure :** poste de développement, PostgreSQL 17 et API dans Docker Desktop (WSL2). Ce poste est moins
rapide qu'un serveur dédié, mais il est comparable au PC d'une boutique. La génération a pris 3 min 46 s.

| Mesure | Route | Médiane (ms) | 95e centile (ms) | Taille (Ko) | Budget |
| --- | --- | --- | --- | --- | --- |
| Accueil : tableau de bord | `GET /api/rapports/tableau-de-bord` | 697 | **5 434** | 0 | dépassé (pointes) |
| Rapport des ventes, 30 jours | `GET /api/rapports/ventes` | **1 116** | 1 538 | 1 228 | tenu, mais réponse lourde |
| Rapport de caisse, 30 jours | `GET /api/rapports/caisse` | 824 | 915 | 42 | tenu |
| Rapport du stock | `GET /api/rapports/stock` | 39 | 54 | 1 004 | tenu |
| Rapport créances et dettes | `GET /api/rapports/creances-dettes` | 520 | 550 | 45 | tenu |
| Sessions de caisse | `GET /api/caisse/sessions` | **2 070** | 2 510 | 119 | **dépassé** |
| Tickets d'une session | `GET /api/caisse/sessions/{id}/tickets` | 88 | 124 | 183 | tenu |
| Soldes de stock | `GET /api/stock/soldes` | 21 | 41 | 1 107 | tenu, réponse lourde |
| Journal d'un article | `GET /api/stock/articles/{id}/mouvements` | 7 | 20 | 33 | tenu |
| Articles | `GET /api/referentiel/articles` | 18 | 28 | 858 | tenu, non paginé |
| Catalogue de la caisse | `GET /api/referentiel/catalogue-caisse` | 49 | 66 | **1 757** | lourd (ENF10) |
| Grille de prix | `GET /api/prix/grille` | 20 | 42 | 771 | lourd (ENF10) |
| Scan d'un code-barres | `GET /api/referentiel/scan/{id}` | 4 | 24 | 0 | tenu |
| Recherche d'un article | `GET /api/referentiel/vendables` | 6 | 28 | 9 | tenu |
| Clients à crédit | `GET /api/credit/clients` | 63 | 75 | 50 | tenu |
| Fiche d'un client | `GET /api/credit/clients/{id}` | 8 | 33 | 24 | tenu |
| Réceptions | `GET /api/achats/receptions` | **1 054** | 1 182 | 631 | **dépassé** |
| Dettes fournisseurs | `GET /api/achats/dettes` | 890 | 970 | 607 | limite |
| Inventaires | `GET /api/inventaires` | 4 | 27 | 0 | tenu |
| **Validation d'un ticket de 3 lignes** | `POST /api/caisse/tickets` | **33** | **55** | — | **tenu (ENF05)** |
| Contrôle des soldes | `POST /api/exploitation/controles` | 9 516 (une fois) | — | — | nuit : sans contrainte |

Le contrôle des soldes a vérifié 10 250 soldes et trouvé 0 écart.

## Lecture

**Ce qui tient :**
- La caisse elle-même : validation d'un ticket (33 ms), scan, recherche et journal d'un article restent rapides, car
  leurs requêtes sont indexées. ENF05 est tenu côté serveur avec une large marge.
- Le contrôle des soldes durera environ 40 s à l'échelle 1, la nuit : acceptable.

**Ce qui dépasse ou dépassera à l'échelle 1** (environ ×4 sur les volumes, et plus pour les requêtes qui parcourent
tout l'historique) :

| Problème | Cause relevée par l'audit | Étape |
| --- | --- | --- |
| Sessions de caisse : 2 s, environ 8 s à l'échelle 1 | Les totaux de chaque session sont recalculés en chargeant tous ses tickets (≈ 400 requêtes) | 57 |
| Accueil : pointes à 5 s | Le tableau de bord parcourt le journal de stock (l'index `(type_document, document_id)` n'est pas utilisé) et les tickets sans index de date | 57 |
| Rapport des ventes : 1,1 s et 1,2 Mo | Même parcours du journal pour le coût des ventes ; réponse détaillée volumineuse | 57 |
| Réceptions et dettes : environ 1 s, liste non bornée | Chargement de toutes les lignes, des frais et de leurs répartitions | 57 |
| Rapports de caisse et des créances | Pas d'index sur la date des tickets, remboursements et opérations de crédit | 57 |
| Catalogue de la caisse : 1,8 Mo, environ 7 Mo à l'échelle 1, téléchargé à chaque ouverture de la caisse | Pas de contrôle de version (ETag) ; la compression le réduit, mais le retéléchargement reste inutile | 58 |
| Articles, soldes, grille de prix : 0,8 à 1,1 Mo chacun | Listes non paginées, filtrées dans le navigateur | 57 (pagination), 58 (écrans) |

Le workflow **Performances** donne les mesures à l'échelle 1 sur une machine de GitHub.

## Étape 57 : performances du serveur

Même jeu (échelle 0,25), même poste, même méthode. Médianes en millisecondes.

| Mesure | Avant | Après | Changement |
| --- | --- | --- | --- |
| Accueil : tableau de bord | 697 (95e : 5 434) | **154** (95e : 189) | Ventes du jour par l'index de date des tickets ; coût des ventes par l'index `(type_document, document_id)` du journal ; dettes fournisseurs calculées une fois |
| Rapport des ventes, 30 jours | 1 116 | **789** | Regroupement par jour, article et caissier dans la base (GROUPING SETS) ; filtre `type_document` ; sans JIT. Le reste du temps vient surtout de la réponse elle-même (1,2 Mo, une ligne par article vendu) |
| Rapport de caisse, 30 jours | 824 | **283** | Index de date des tickets ; sans JIT |
| Rapport créances et dettes | 520 | **112** | Montants des réceptions calculés une fois et non de nouveau pour le `HAVING` (524 → 95 ms en SQL) |
| Sessions de caisse | 2 070 | **106** | Totaux de toutes les sessions en trois requêtes groupées (tickets, paiements, remboursements) au lieu de charger chaque ticket ; compteurs en une requête |
| Tickets d'une session | 88 | **16** | Quantités déjà rendues en une requête pour toute la session |
| Réceptions | 1 054 | **95** | Résumé (nombre de lignes, valeur, frais) calculé par la base, sans charger lignes, frais et répartitions |
| Dettes fournisseurs | 890 | **101** | Même chose, paiements compris |
| Clients à crédit, fiche d'un client | 63 et 8 | 71 et 9 | Ventes soldées non relues ; retards sommés par la base (sans effet visible sur ce jeu, où les clients sont soldés chaque mois) |
| Validation d'un ticket de 3 lignes | 33 | 38 | Inchangé : déjà sous la cible |
| Contrôle des soldes | 9 516 | 11 700 à 21 300 | Variable selon le cache du poste ; la nuit, sans contrainte |

Les autres routes (stock, articles, catalogue, grille, scan, recherche, inventaires) restent sous 75 ms ; leur taille
est traitée à l'étape 58.

### Décisions

- **JIT de PostgreSQL désactivé pour les lectures de rapports et d'exports** (`SET LOCAL jit = off` dans leur
  transaction en lecture seule). Sur un long historique, les estimations de coût déclenchent la compilation JIT. Elle
  coûtait près d'une seconde sur le rapport des ventes, qui s'exécute en une demie sans elle (1 500 → 564 ms en SQL).
  Le réglage reste local à ces transactions : il s'applique aussi sur un serveur PostgreSQL hébergé.
- **Nouveaux index** (migration `IndexDates` du module Caisse) : `ticket_caisse.date_utc` et
  `remboursement_credit.date_utc`.
- **Requêtes séparées par défaut** (`SplitQuery`) : un ticket ou une réception avec plusieurs collections incluses ne
  produit plus de produit cartésien.
- **Grille de prix** : le prix en vigueur et le prochain prix de chaque conditionnement sont choisis par la base, au
  lieu de relire tout l'historique des prix.
- **Planificateur** : la file est interrogée chaque minute (15 s par défaut) ; la seule tâche tourne la nuit.

**Prévu au plan mais non fait, mesure à l'appui :**
- *Regrouper les lectures d'une vente* : la validation d'un ticket prend 38 ms, la cible est d'1 s.
- *Créer les contextes de modules à la demande dans l'unité de travail* : un contexte créé après l'ouverture de la
  transaction n'y serait pas enrôlé, et EF tenterait d'en ouvrir une seconde sur la même connexion. Le risque dépasse
  le gain sur une validation de 38 ms.
- *La pagination des listes* est reportée à l'étape 58 : l'API et les écrans changent ensemble.
