# Grille de recette de la V1

Pour chaque cas d'utilisation (UC), règle de gestion (RG) et exigence non fonctionnelle (ENF) du
[dossier d'analyse](dossier-analyse.md), la grille indique :
- son périmètre ;
- les tests automatisés qui le vérifient.

Établie à l'étape 54 (17/11/2026). Elle est mise à jour avec les tests.

**Périmètre.** V1 = boutique mono-site (décision 35). Le hors ligne est limité à la caisse (décision 38).
« Non cité » : le cas n'apparaît pas dans le périmètre V1 de la décision 35 ; à confirmer avec le client avant la
mise en service.

**Projets de test :**

| Abréviation | Projet |
| --- | --- |
| E2E | `GestionStock.E2E`, parcours dans Chromium sur l'application déployée |
| Api | `GestionStock.Api.IntegrationTests` |
| StockIT | `GestionStock.Stock.IntegrationTests` |
| Dom | `GestionStock.Stock.Domain.Tests` |
| Client | `GestionStock.Client.Tests` |
| Arch | `GestionStock.ArchitectureTests` |
| Infra | `GestionStock.Infrastructure.Tests` |

Lancement : voir le README, section « Tests ».

## Cas d'utilisation

| Id | Sujet | Périmètre | Tests |
| --- | --- | --- | --- |
| UC01 | Gérer les articles | V1, sans DLC ni lots | E2E `ParcoursGerant.Cree_un_article_avec_son_carton_et_ses_prix_et_un_client` ; Api `ReferentielTests.Le_gerant_cree_un_article_que_tous_les_roles_peuvent_consulter` ; Api `ConditionnementsTests.Un_nouvel_article_a_d_office_un_conditionnement_a_l_unite` |
| UC02 | Sites et emplacements | V1, un site, emplacement de vente | Api `ReferentielTests.La_boutique_et_son_emplacement_de_vente_existent_des_l_installation` ; Arch `ConventionsReferentielTests.Les_types_d_emplacement_du_referentiel_et_du_stock_sont_identiques` |
| UC03 | Commande fournisseur | Non cité (réception simple) | — |
| UC04 | Réceptionner une livraison | V1, sans commande ni lots ; frais d'approche | E2E `ParcoursMagasinier.Receptionne_une_livraison_compte_le_stock_et_declare_une_casse` ; Api `AchatsTests.Deux_receptions_validees_font_entrer_le_stock_au_CUMP` ; Api `AchatsTests.Deux_validations_simultanees_n_ecrivent_qu_une_fois` |
| UC05 à UC10 | Rangement, commandes clients, réservation, préparation, expédition, transfert | V2 | — |
| UC11 | Réaliser un inventaire | V1 | E2E `ParcoursMagasinier.Receptionne_une_livraison_compte_le_stock_et_declare_une_casse` ; E2E `ParcoursComptable.Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte` ; Api `InventaireTests.Un_manque_constate_et_valide_sort_du_stock_au_CUMP` |
| UC12, UC13 | Retour hors caisse, blocage de lot | V2 | — |
| UC14 | Disponibilités et mouvements | V1 | E2E `ParcoursMagasinier.Receptionne_une_livraison_compte_le_stock_et_declare_une_casse` ; Api `RapportsTests.Le_rapport_de_stock_valorise_chaque_article_au_CUMP` |
| UC15 | Suggestions de réapprovisionnement | Non cité | — |
| UC16 | Exports comptables | V1 | E2E `ParcoursComptable.Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte` ; Api `ExportsTests.Le_journal_des_ventes_en_CSV_met_les_avoirs_en_negatif` ; Api `ExportsTests.La_valeur_du_stock_est_exportee_par_article` |
| UC17 | Encaisser une vente | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; E2E `ParcoursTelephone.Vend_depuis_un_telephone_avec_la_barre_du_total` ; Api `CaisseTests.Une_vente_en_especes_rend_la_monnaie_et_sort_le_stock_par_conditionnement` |
| UC18 | Émettre un avoir | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; Api `CaisseTests.Un_avoir_rend_l_argent_remet_le_stock_et_ne_rend_pas_plus_que_vendu` ; Client `TicketLocalTests.Un_avoir_local_reprend_le_prix_d_origine_et_rend_la_remise_au_prorata` |
| UC19 | Réassort de rayon | V2 | — |
| UC20 | Démarque connue | V1 | E2E `ParcoursMagasinier.Receptionne_une_livraison_compte_le_stock_et_declare_une_casse` ; Api `InventaireTests.Une_casse_declaree_puis_validee_sort_du_stock_au_CUMP` |
| UC21 | Commande à l'entrepôt | V2 | — |
| UC22 | Résoudre un code scanné | V1, EAN, UPC et code interne (pas GS1-128 ni DataMatrix) | Api `ConditionnementsTests.Un_code_EAN_valide_identifie_son_conditionnement_au_scan` ; Api `ConditionnementsTests.Un_UPC_A_se_lit_aussi_en_EAN_13_avec_un_zero_de_tete` ; Client `CatalogueLocalTests.Un_code_barres_donne_son_conditionnement_et_le_code_scanne` |
| UC23 | Imprimer des étiquettes | Non cité (RG33 parle du code interne « imprimé en étiquette ») | **Aucun** |
| UC24 | Ouvrir et clôturer une session | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; E2E `ParcoursGerant.Cloture_une_session_restee_ouverte_valide_l_ecart_et_consulte_les_rapports` ; Api `CaisseTests.Un_caissier_n_ouvre_qu_une_session_et_un_poste_n_en_porte_qu_une` |
| UC25 | Prix de vente | V1 | E2E `ParcoursGerant.Cree_un_article_avec_son_carton_et_ses_prix_et_un_client` ; Api `PrixTests.Un_prix_programme_ne_s_applique_qu_a_sa_date` ; Api `PrixTests.Seul_le_gerant_fixe_les_prix` |
| UC26 | Clients et crédit | V1 | E2E `ParcoursGerant.Cree_un_article_avec_son_carton_et_ses_prix_et_un_client` ; E2E `ParcoursComptable.Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte` ; Api `CreditTests.Le_gerant_cree_le_client_et_le_comptable_fixe_son_plafond` |
| UC27 | Remboursement de crédit | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; Api `CaisseTests.Un_remboursement_de_credit_s_encaisse_dans_la_session` ; Api `HorsLigneTests.Un_remboursement_de_credit_hors_ligne_garde_son_heure_et_n_est_compte_qu_une_fois` |
| UC28 | Ticket d'une caisse externe | Non cité (option) | — |

## Règles de gestion

| Id | Sujet | Périmètre | Tests |
| --- | --- | --- | --- |
| RG01 | Mouvements immuables | V1 | StockIT `JournalImmuableTests.Modifier_un_mouvement_enregistre_est_refuse` ; Dom `MouvementStockTests.Un_mouvement_n_expose_aucun_moyen_d_etre_modifie` |
| RG02 | Disponible = physique − réservé − bloqué | V1 partiel (ni réservé ni bloqué en V1) | Indirect seulement |
| RG03 | Pas de stock négatif hors emplacement de vente | V1 | Dom `StockEmplacementTests.Le_stock_ne_peut_pas_devenir_negatif_hors_emplacement_de_vente` ; StockIT `EnregistrementMouvementTests.Une_sortie_superieure_au_stock_d_un_emplacement_hors_vente_est_refusee_sans_rien_ecrire` |
| RG04 à RG11 | Réservation, lots, séries, DLC, FEFO et FIFO | V2 | — |
| RG12 | Réception partielle, surlivraison | V1 partiel (sans commande) | **Aucun** |
| RG13 | Disponible dès la validation | V1 | Api `AchatsTests.Deux_receptions_validees_font_entrer_le_stock_au_CUMP` |
| RG14 | Transfert « en transit » | V2 | — |
| RG15 | Écart d'inventaire à valider | V1 | E2E `ParcoursComptable.Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte` ; Api `InventaireTests.Seul_le_comptable_valide_et_un_inventaire_termine_ne_change_plus` |
| RG16 | Mouvements pendant le comptage | V1 | Api `InventaireTests.Une_marchandise_arrivee_apres_le_comptage_n_est_pas_effacee_par_la_validation` |
| RG17 | CUMP par article × site | V1 | Dom `StockSiteTests.Le_CUMP_est_la_moyenne_ponderee_des_entrees` ; StockIT `EnregistrementMouvementTests.Deux_receptions_donnent_le_cout_unitaire_moyen_pondere` |
| RG18 | Conditionnements, facteur fixe | V1 | E2E `ParcoursGerant.Cree_un_article_avec_son_carton_et_ses_prix_et_un_client` ; Api `ConditionnementsTests.Un_carton_contient_un_nombre_entier_de_pieces_mais_un_sac_peut_contenir_des_kg_decimaux` |
| RG19 | Point de commande | Non cité | — |
| RG20 | Site, emplacement de vente | V1 partiel | Api `ReferentielTests.La_boutique_et_son_emplacement_de_vente_existent_des_l_installation` |
| RG21 | Vente sans réservation | V1 | Api `CaisseTests.Une_vente_en_especes_rend_la_monnaie_et_sort_le_stock_par_conditionnement` ; StockIT `ConcurrenceTests.Des_ventes_simultanees_du_meme_article_sont_toutes_enregistrees` |
| RG22 | Caisse jamais refusée pour stock insuffisant | V1 | Api `CaisseTests.Une_vente_n_est_jamais_refusee_pour_stock_insuffisant` ; Dom `StockEmplacementTests.Une_vente_en_caisse_n_est_jamais_refusee_pour_stock_insuffisant` |
| RG23 | Démarque avec motif | V1 | E2E `ParcoursMagasinier.Receptionne_une_livraison_compte_le_stock_et_declare_une_casse` ; Api `InventaireTests.Une_demarque_refusee_exige_un_motif_et_ne_touche_pas_au_stock` |
| RG24, RG25 | Lot ou DLC en caisse, lot bloqué | V2 | — |
| RG26 | Plusieurs codes, code actif unique | V1 | Api `ConditionnementsTests.Un_code_actif_n_est_attribue_qu_une_fois_mais_peut_etre_reattribue_apres_desactivation` |
| RG27 | Codes à préfixe 2 (poids ou prix variable) | V1 partiel (structure à confirmer avec GS1 Cameroun, 8.4) | **Aucun** (non implémenté) |
| RG28, RG29 | Étiquettes d'emplacement, SSCC | V2 | — |
| RG30 | Frais d'approche, revalorisation | V1 | E2E `ParcoursComptable.Saisit_frais_et_paiement_fixe_un_plafond_valide_inventaire_et_demarque_et_exporte` ; Api `FraisApprocheTests.Les_frais_saisis_avant_validation_entrent_dans_le_cout_d_entree` ; Api `FraisApprocheTests.Un_frais_tardif_revalorise_le_stock_restant_et_garde_la_part_deja_vendue` |
| RG31 | Opérations hors ligne | V1, caisse seule (décision 38) | E2E `ParcoursCaissier.Vend_hors_ligne_puis_synchronise_au_retour_du_reseau` ; Api `HorsLigneTests.Une_vente_rejouee_deux_fois_n_est_enregistree_qu_une_fois_avec_son_numero_et_son_heure` |
| RG32 | XAF, 4 décimales, arrondi à l'unité | V1 | Dom `StockSiteTests.Le_CUMP_est_arrondi_a_quatre_decimales` ; Client `VenteEnCoursTests.Le_montant_d_une_ligne_est_arrondi_au_franc_comme_au_serveur_puis_diminue_de_la_remise` |
| RG33 | Clé de contrôle, code interne | V1 | Api `ConditionnementsTests.Un_code_invalide_est_refuse` ; Api `ConditionnementsTests.Un_code_interne_est_genere_et_reconnu_au_scan` |
| RG34 | Numérotation continue, tickets immuables | V1 | Api `CaisseTests.Les_tickets_sont_numerotes_en_continu_par_poste_meme_en_simultane` ; Api `HorsLigneTests.Un_numero_saute_est_garde_et_signale` |
| RG35 | Modes de paiement, référence Mobile Money | V1 | Api `CaisseTests.Un_ticket_repartit_son_paiement_entre_les_quatre_modes` ; Api `CaisseTests.Un_paiement_invalide_est_refuse_sans_rien_ecrire` |
| RG36 | Fond, comptage, écart, synchronisation avant clôture | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; Api `CaisseTests.La_cloture_calcule_l_ecart_que_le_comptable_valide` ; Api `HorsLigneTests.La_cloture_attend_les_ventes_hors_ligne_que_le_poste_annonce` |
| RG37 | Prix de référence, TVA | V1 | Arch `ConventionsPrixTests.Assujettie_l_entreprise_decompose_le_TTC_en_HT_et_TVA_a_19_25` ; Api `PrixTests.Le_regime_de_TVA_par_defaut_est_non_assujetti_au_taux_legal` |
| RG38 | Fonctions activables, configuration par défaut | V1 partiel | Indirect seulement |
| RG39 | Client avec nom et téléphone | V1 | Api `CreditTests.Le_nom_et_le_telephone_sont_obligatoires` |
| RG40 | Plafond de crédit, dérogation tracée | V1 | E2E `ParcoursCaissier.Vend_au_comptoir_et_a_credit_fait_un_avoir_encaisse_un_remboursement_et_cloture` ; Api `CreditTests.Une_vente_au_dela_du_plafond_exige_la_validation_d_un_responsable` ; Api `CreditTests.Deux_ventes_simultanees_ne_depassent_pas_ensemble_le_plafond` |
| RG41 | Vente à crédit : la marchandise sort du stock | V1 | **Aucun test dédié** |
| RG42 | Remboursement imputé au plus ancien | V1 | Api `CreditTests.Un_remboursement_s_impute_sur_les_ventes_les_plus_anciennes` |
| RG43 | Impayés échus | V1 | Api `CreditTests.Les_dettes_echues_apparaissent_dans_les_impayes` ; Api `ExportsTests.Le_journal_des_creances_reprend_les_ventes_a_credit` |
| RG44 | Unité de stock = plus petite unité vendue | V1 | Api `ConditionnementsTests.Un_nouvel_article_a_d_office_un_conditionnement_a_l_unite` |
| RG45 | Prix par conditionnement | V1 | E2E `ParcoursGerant.Cree_un_article_avec_son_carton_et_ses_prix_et_un_client` ; Api `PrixTests.Le_gerant_fixe_un_prix_par_conditionnement_que_la_caisse_lit` |
| RG46 | Précision de quantité, poids | V1 | Client `VenteEnCoursTests.Le_poids_se_saisit_au_millieme_et_le_reste_a_l_unite` ; Api `InventaireTests.Une_quantite_comptee_respecte_les_decimales_de_l_article` |
| RG47 | Freinte à l'inventaire | V1 | Api `InventaireTests.Un_manque_classe_en_freinte_devient_une_demarque` |

## Exigences non fonctionnelles

| Id | Sujet | Périmètre | Tests |
| --- | --- | --- | --- |
| ENF01, ENF02 | Survente sous réservations, traçabilité des lots | V2 | — |
| ENF03 | Audit (auteur, heure UTC, document) | V1 | Dom `MouvementStockTests.Le_document_d_origine_et_l_utilisateur_sont_obligatoires` ; StockIT `JournalImmuableTests.Supprimer_un_mouvement_enregistre_est_refuse` ; Api `CaisseTests.Une_remise_exige_la_validation_d_un_responsable_tracee_sur_le_ticket` |
| ENF04 | Contrôle nocturne des soldes | V1 | E2E `ParcoursAdministrateur.Lance_le_controle_des_soldes_et_consulte_les_sauvegardes` ; StockIT `ControleSoldesTests.Un_solde_fausse_donne_un_ecart_exact_par_grandeur_et_par_emplacement` |
| ENF05 | Ticket validé en moins d'1 s | V1 | Mesure `deploy/volume/mesurer.py` sur le jeu volumineux : 33 ms médiane, 55 ms au 95e centile à l'échelle 0,25 ([performances.md](performances.md)) ; workflow Performances à l'échelle 1 |
| ENF06 | Comptes nominatifs et rôles | V1, comptes locaux | E2E `ParcoursAdministrateur.Cree_un_compte_que_l_employe_active_puis_le_desactive` ; Api `ComptesTests.Seul_l_administrateur_gere_les_comptes` ; Arch `ConventionsIdentiteTests.Chaque_permission_est_attribuee_a_au_moins_un_role_connu` |
| ENF07 | Précision des nombres, dates UTC et Douala | V1 | Dom `MouvementStockTests.Une_quantite_a_plus_de_quatre_decimales_est_refusee` ; Dom `MouvementStockTests.Une_date_non_UTC_est_refusee` ; Infra `HorlogeSystemeTests.La_date_du_jour_est_celle_de_Douala` |
| ENF08 | Hors ligne sans doublon | V1, caisse seule | E2E `ParcoursCaissier.Vend_hors_ligne_puis_synchronise_au_retour_du_reseau` ; Api `HorsLigneTests.Une_vente_rejouee_deux_fois_n_est_enregistree_qu_une_fois_avec_son_numero_et_son_heure` ; Client `TicketLocalTests.Le_plafond_hors_ligne_compte_le_credit_deja_en_file_et_les_remboursements` |
| ENF09 | Bilinguisme | V1 | E2E `ParcoursAnglais.Un_caissier_anglophone_vend_et_cloture` ; Api `CaisseTests.Le_recu_PDF_est_genere_dans_la_langue_de_l_utilisateur` ; Arch `ConventionsClientTests.Chaque_texte_utilise_par_l_interface_existe_en_francais_et_en_anglais` |
| ENF10 | Faible bande passante | V1 | E2E `ParcoursConnexionLente.La_caisse_s_ouvre_et_scanne_vite_sur_une_connexion_lente` ; Api `VersionsTests.Une_liste_inchangee_repond_304_et_une_modification_change_sa_version` |
| ENF11 | Localisation des données (loi 2024/017) | V1 | Aucun test (règle de déploiement, décision 43) |

## Lacunes

**À couvrir pendant la tranche 9 :**
- **RG41** (vente à crédit et sortie de stock) ;

**À trancher avec le client avant d'ajouter des tests :**
- RG12 sans commande fournisseur ;
- RG27 (structure des codes à poids variable, 8.4) ;
- UC23 (étiquettes) ;
- RG38 (fonctions activables).

## Défauts relevés par le check (étape 54), corrigés à l'étape 55

| # | Défaut | Correction | Test |
| --- | --- | --- | --- |
| 1 | Remboursement de crédit enregistré deux fois sur un double clic (clé d'idempotence nouvelle à chaque clic) ; boutons de création actifs pendant l'appel | Clé gardée jusqu'à l'enregistrement, second clic ignoré ; création de client, article, fournisseur, inventaire et démarque protégée (bouton désactivé, un seul envoi) | E2E `ParcoursCaissier` : double clic, une seule opération au journal du client |
| 2 | 12 actions irréversibles sans confirmation | Confirmation traduite avant : validation et annulation d'inventaire, validation de réception, retrait de frais, annulation de paiement, validation d'écart, avoir, mot de passe provisoire, désactivation de compte et de code, validation et refus de démarque | E2E `ParcoursComptable` : confirmations de l'inventaire et de la démarque |
| 3 | Sessions de caisse, poste occupé, anomalies, réception, dette et fiche client montrent le matricule | Nom « Prénom Nom (matricule) » donné par l'annuaire du personnel (`IAnnuairePersonnel`) | Api `CaisseTests.La_cloture_calcule_l_ecart_que_le_comptable_valide` ; E2E `ParcoursGerant` (ligne trouvée par le nom) |
| 4 | Page déclarée en français (`<html lang>`) dans l'interface anglaise | Langue de la page fixée au démarrage | E2E `ParcoursAnglais` |
| 5 | « Aucun… » affiché pendant le chargement des listes | Indicateur d'attente jusqu'au premier chargement (13 écrans) | Pas de test automatisé (relu dans le code) |
| 6 | Erreur 500 à la première requête après un redémarrage de la base | 503 traduit `Base.Indisponible`, pool vidé, lecture redemandée par le client | Infra `GestionnaireCoupureBaseTests` ; CI : base redémarrée, 503 puis 200 |
| 7 | Erreurs avalées (choix du client, recherche de démarque, fiche article, réception) | Message d'erreur affiché | Pas de test automatisé (relu dans le code) |
