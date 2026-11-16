# Tranche 6 : design de l'interface (plan)

Objectif : passer d'un back-office propre à un produit qu'un caissier utilise vite et qu'un gérant a envie d'acheter,
sans toucher aux règles métier. Ordre : la caisse d'abord (l'écran le plus utilisé), puis l'accueil, le téléphone,
l'identité et les finitions.

## Contraintes conservées

- Aucune ressource externe : polices système, icônes SVG embarquées, CSS local (hors ligne, ENF08 ; bande passante
  faible, ENF10).
- Tous les textes dans `Textes.resx` / `Textes.en.resx` ; contrôlé par les tests de conventions du client.
- Aucune règle métier inventée : ce qui en demandait une a été tranché (voir « Décisions »).
- Performance de la caisse (ENF05) : rien d'ajouté sur le chemin d'encaissement côté serveur.

## Constats (relevés dans le navigateur le 16/11/2026)

| Écran | Constat |
| --- | --- |
| Caisse, vente | Bonne structure : panier à gauche, total en grand et modes de paiement à droite. Mais deux champs côte à côte (scan et recherche), résultats de recherche en liens soulignés, quantités et remises dans de petits champs de tableau, aucun raccourci clavier à part le scan, pas de saisie rapide pour les articles sans code-barres (riz au kilo, pétrole au litre), texte en 15 px. |
| Caisse, ouverture | Petit formulaire seul dans une grande page. « Une session est déjà ouverte sur ce poste » bloque le caissier sans dire qui l'a ouverte ni quoi faire. |
| Accueil | Des liens seulement ; aucun chiffre (ventes du jour, écart de caisse, stocks négatifs, impayés). |
| Téléphone | Le menu se replie en haut mais occupe la moitié de l'écran pour un gérant (12 entrées) ; les tableaux larges défilent horizontalement. |
| Identité | Ni logo ni nom de boutique ; vert sobre et cohérent, mais neutre. |
| Finitions | Unités non traduites (« boite », « pièce ») ; « 20.000 kg » en anglais se lit « vingt mille » pour un francophone ; tableaux denses ; pas d'icônes. |

## Étapes

| Étape | Contenu | Modules touchés |
| --- | --- | --- |
| 35 | **Fondations visuelles** : jetons de design (texte 16 px, échelle typographique, espacements, cibles tactiles de 44 px, contrastes AA), composants partagés (bouton, champ, carte, pastille, tableau, état vide, chargement), jeu d'icônes SVG embarqué, focus visible partout. **Responsive dès les fondations** (demande du 16/11/2026) : menu en tiroir sous 1 000 px, une colonne et tableaux défilants sous 760 px. Application à tous les écrans sans changer leur contenu. | Client |
| 36 | **Caisse, vente** : écran plein comptoir (menu replié pendant la vente) ; un seul champ qui reconnaît un code-barres ou lance la recherche ; résultats en tuiles cliquables ; lignes du panier larges avec boutons − / + ; pavé numérique à l'écran pour quantité, remise et montant ; billets rapides en espèces (1 000, 2 000, 5 000, 10 000 XAF et « compte juste ») ; total et rendu très visibles ; raccourcis clavier affichés (F2 recherche, F4 espèces, F8 encaisser, Échap annuler). Grille d'articles rapides choisis par le Gérant (décision 1). | Client, Referentiel |
| 37 | **Caisse, parcours** : ouverture avec billets rapides ; session déjà ouverte : qui, depuis quand, et clôture possible par le Gérant (décision 2) ; clôture guidée (espèces comptées par coupure ; comptage « à l'aveugle » : le théorique n'est pas affiché au caissier, l'écart apparaît après la clôture, pratique la plus sûre contre l'ajustement du comptage) ; écran de reçu avec « Nouvelle vente » au clavier. | Client, Caisse |
| 38 | **Accueil = tableau de bord par rôle** : Gérant et Comptable : ventes et marge du jour, tickets, écarts de caisse à valider, stocks négatifs, créances et dettes ; Caissier : sa session ; Magasinier : réceptions en brouillon, inventaires en cours, démarques en attente. Un endpoint de synthèse dans Rapports, sur les vues existantes. | Client, Rapports |
| 39 | **Téléphone et tablette** (le tiroir est fait à l'étape 35) : listes principales (stock, tickets, clients, rapports) en cartes sur écran étroit ; caisse utilisable sur tablette (total collé en bas). | Client |
| 40 | **Identité et finitions** : nom et logo de la boutique (configuration, décision 3) dans le menu, la connexion et le reçu ; quantités sans zéros inutiles (« 20 kg ») ; unités traduites (décision 4) ; textes d'aide et messages relus. | Client, Infrastructure (configuration) |
| 41 | **Vérification** : parcours chronométrés dans le navigateur (vente de 3 articles, vente avec rendu, clôture), contrôle clavier seul, téléphone 375 px, tablette 768 px, impression du reçu ; README, CI, démonstration. Idéalement : une heure d'essai avec un vrai caissier et un vrai gérant. | — |

Chaque étape garde `dotnet build` et `dotnet test` au vert et fait l'objet d'un commit.

## Décisions (prises le 16/11/2026)

1. **Articles rapides à la caisse** : choisis par le Gérant, qui coche « vente rapide » sur un conditionnement
   (nouveau champ dans Referentiel, permission `Referentiel.Articles.Gerer`). La grille reste stable.
2. **Session restée ouverte sur un poste** : le Gérant peut la clôturer à la place de son titulaire, avec les espèces
   comptées. Nouvelle règle, à reporter dans le dossier d'analyse : l'opération est journalisée (Gérant, horodatage,
   titulaire) et l'écart reste à valider comme pour toute clôture (étape 37, module Caisse).
3. **Identité** : nom de la boutique et logo facultatif (configuration `ENTREPRISE_LOGO`, PNG, JPEG ou WebP) ; pas encore
   de logo : l'icône de l'application s'affiche en attendant.
4. **Unités** : liste fixe d'unités courantes traduites côté client (boîte → box, pièce → piece…) ; une unité absente
   de la liste s'affiche telle quelle.
5. **Matériel au comptoir** : prévoir les deux cas. Boutons de 44 px au moins, pavé numérique à l'écran toujours
   disponible (affiché d'office sur écran tactile, replié sinon), et raccourcis clavier affichés.

Les alertes de « stock bas » ne sont pas prévues : elles supposent des seuils de réapprovisionnement, hors périmètre
V1. L'accueil signale seulement les stocks négatifs.
