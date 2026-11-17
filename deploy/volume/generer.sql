-- Jeu de données volumineux pour mesurer les performances (tranche 9, étape 56). NE JAMAIS LANCER SUR LA BASE D'UNE BOUTIQUE.
--
-- Échelle 1 = grosse boutique sur 3 ans : 20 000 articles, 2 000 tickets par jour sur 4 postes, environ 3 lignes par
-- ticket (≈ 2,2 millions de tickets, ≈ 6,5 millions de mouvements de stock), 1 000 clients à crédit, 50 fournisseurs et
-- une réception par fournisseur et par mois. Le nombre de jours (3 ans) est fixe ; l'échelle réduit le volume par jour.
--
-- Usage (base de docker-compose, échelle 0,1 par défaut) :
--   docker compose -f deploy/docker-compose.yml exec -T db psql -U gestionstock -d gestionstock -v echelle=0.1 < deploy/volume/generer.sql
--
-- Le jeu reste cohérent avec les règles :
-- - chaque article a un coût d'achat fixe, si bien que son CUMP vaut ce coût quel que soit l'ordre du journal (RG17) ;
-- - les soldes de stock (site et emplacement de vente) sont la somme du journal, et une réception au début de chaque
--   mois couvre les ventes du mois (pas de stock négatif) ;
-- - les ventes à crédit d'un mois sont remboursées au début du mois suivant, avec leurs imputations (RG42) ; la dette
--   d'un client est la somme de ses ventes non remboursées.
-- Le contrôle des soldes (ENF04) doit donc passer sans écart sur le jeu généré.
--
-- Codes : articles VOL000001…, postes VOL-1 à VOL-4 (caissiers S900001…), réceptions RV-0000001…, EAN-13 en 619… (hors
-- des préfixes 2x réservés au poids variable, RG27).

\set ON_ERROR_STOP on
\if :{?echelle}
\else
  \set echelle 0.1
\endif
\timing on

BEGIN;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM referentiel.article WHERE code LIKE 'VOL%') THEN
        RAISE EXCEPTION 'Le jeu volumineux est déjà dans cette base (articles VOL…).';
    END IF;
END $$;

SELECT setseed(0.42);

CREATE TEMP TABLE p ON COMMIT DROP AS
SELECT greatest(50, round(20000 * :echelle))::int AS nb_articles,
       greatest(10, round(2000 * :echelle))::int AS tickets_jour,
       1095 AS jours,
       4 AS postes,
       greatest(20, round(1000 * :echelle))::int AS nb_clients,
       50 AS nb_fournisseurs,
       0.03 AS part_credit,
       (current_date - 1095) AS debut,
       (SELECT id FROM referentiel.site WHERE type = 2 ORDER BY code LIMIT 1) AS site_id,
       (SELECT e.id FROM referentiel.emplacement e JOIN referentiel.site s ON s.id = e.site_id
         WHERE s.type = 2 AND e.type = 8 ORDER BY s.code LIMIT 1) AS emplacement_id;

SELECT nb_articles, tickets_jour, jours, nb_clients, debut FROM p;

-- Référentiel : articles, conditionnement à l'unité, code EAN-13 valide, prix de vente.
CREATE TEMP TABLE a ON COMMIT DROP AS
SELECT i,
       gen_random_uuid() AS article_id,
       gen_random_uuid() AS conditionnement_id,
       'VOL' || lpad(i::text, 6, '0') AS code,
       'Article volume ' || i AS libelle,
       (100 + (i * 37) % 4900)::numeric AS cout,
       (ceil((100 + (i * 37) % 4900) * 1.3 / 25) * 25)::numeric AS prix,
       1 + i % (SELECT nb_fournisseurs FROM p) AS fournisseur
FROM generate_series(1, (SELECT nb_articles FROM p)) i;
CREATE INDEX ON a (i);
CREATE INDEX ON a (article_id);

INSERT INTO referentiel.article (id, code, libelle, libelle_traduit, unite_stock, precision_quantite, mode_suivi, gere_dlc, actif)
SELECT article_id, code, libelle, NULL, 'pièce', 0, 0, false, true FROM a;

INSERT INTO referentiel.conditionnement (id, article_id, libelle, niveau, quantite_unite_stock, actif, vente_rapide)
SELECT conditionnement_id, article_id, 'pièce', 1, 1, true, i <= 12 FROM a;

INSERT INTO referentiel.code_article (id, conditionnement_id, valeur, type, principal, actif)
SELECT gen_random_uuid(), conditionnement_id, base || ((10 - somme % 10) % 10)::text, 1, true, true
FROM (SELECT conditionnement_id, base,
             (SELECT sum(substr(base, k, 1)::int * CASE WHEN k % 2 = 0 THEN 3 ELSE 1 END) FROM generate_series(1, 12) k) AS somme
      FROM (SELECT conditionnement_id, '619' || lpad(i::text, 9, '0') AS base FROM a) x) y;

INSERT INTO prix.prix_vente (id, conditionnement_id, prix_ttc, debut_validite, saisi_par, date_saisie_utc)
SELECT gen_random_uuid(), conditionnement_id, prix, (SELECT debut FROM p) - 1, 'volume', now() FROM a;

-- Fournisseurs et clients.
CREATE TEMP TABLE f ON COMMIT DROP AS
SELECT n, gen_random_uuid() AS id FROM generate_series(1, (SELECT nb_fournisseurs FROM p)) n;
INSERT INTO achats.fournisseur (id, nom, telephone, tolerance_surlivraison_pct, actif)
SELECT id, 'Fournisseur volume ' || n, NULL, 0, true FROM f;

CREATE TEMP TABLE c ON COMMIT DROP AS
SELECT n, gen_random_uuid() AS id FROM generate_series(1, (SELECT nb_clients FROM p)) n;
INSERT INTO credit.client (id, nom, telephone, plafond_credit, delai_paiement_jours, dlc_residuelle_min_jours, solde_du, actif)
SELECT id, 'Client volume ' || n, '+237 6' || lpad(n::text, 8, '0'), 500000, 30, 0, 0, true FROM c;

-- Sessions : une par poste et par jour, de 7 h à 19 h (heure de Douala), comptées et validées.
CREATE TEMP TABLE s ON COMMIT DROP AS
SELECT gen_random_uuid() AS id,
       d,
       'VOL-' || poste AS numero_caisse,
       'S9' || lpad(poste::text, 5, '0') AS caissier,
       (((SELECT debut FROM p) + d)::timestamp + interval '6 hours') AT TIME ZONE 'UTC' AS ouverture
FROM generate_series(0, (SELECT jours FROM p) - 1) d, generate_series(1, (SELECT postes FROM p)) poste;
CREATE INDEX ON s (numero_caisse, d);

-- Tickets : numérotés en continu par poste (RG34), une part à crédit.
CREATE TEMP TABLE t ON COMMIT DROP AS
SELECT x.*, row_number() OVER (PARTITION BY x.numero_caisse ORDER BY x.date_utc) AS numero
FROM (SELECT gen_random_uuid() AS id,
             s.id AS session_id,
             s.numero_caisse,
             s.caissier,
             s.d,
             s.ouverture + random() * interval '12 hours' AS date_utc,
             CASE WHEN random() < (SELECT part_credit FROM p)
                  THEN ((SELECT array_agg(id) FROM c))[1 + floor(random() * (SELECT nb_clients FROM p))::int] END AS client_id
      FROM s, generate_series(1, ceil((SELECT tickets_jour FROM p)::numeric / (SELECT postes FROM p))::int) k) x;
CREATE INDEX ON t (id);

-- Lignes : 1 à 5 par ticket, 1 à 3 unités ; les premiers articles se vendent plus (tirage au carré).
CREATE TEMP TABLE l ON COMMIT DROP AS
SELECT gen_random_uuid() AS id, g.ticket_id, g.ordre, a.article_id, a.conditionnement_id, a.libelle, a.prix, a.cout,
       g.quantite, g.date_utc, g.caissier
FROM (SELECT t.id AS ticket_id, t.date_utc, t.caissier, k AS ordre,
             1 + floor(power(random(), 2) * (SELECT nb_articles FROM p))::int AS indice,
             (1 + floor(random() * 3))::numeric AS quantite
      FROM t CROSS JOIN LATERAL generate_series(1, 1 + floor(random() * 5)::int + 0 * t.numero::int) k) g
JOIN a ON a.i = g.indice;
CREATE INDEX ON l (ticket_id);

CREATE TEMP TABLE tt ON COMMIT DROP AS
SELECT ticket_id, sum(quantite * prix) AS total FROM l GROUP BY ticket_id;
CREATE INDEX ON tt (ticket_id);

INSERT INTO caisse.session_caisse (id, numero_caisse, caissier, ouverture_utc, fond_caisse, statut, cloture_utc,
                                   especes_theoriques, especes_comptees, ecart, validee_par, validation_utc, cloturee_par)
SELECT s.id, s.numero_caisse, s.caissier, s.ouverture, 10000, 'Validee', s.ouverture + interval '12 hours 30 minutes',
       10000 + coalesce(e.especes, 0), 10000 + coalesce(e.especes, 0), 0, 'volume', s.ouverture + interval '13 hours', s.caissier
FROM s LEFT JOIN (SELECT t.session_id, sum(tt.total) AS especes FROM t JOIN tt ON tt.ticket_id = t.id
                  WHERE t.client_id IS NULL GROUP BY t.session_id) e ON e.session_id = s.id;

INSERT INTO caisse.ticket_caisse (id, id_idempotence, session_id, numero_caisse, numero, numero_complet, date_utc, caissier,
                                  est_avoir, ticket_origine_id, client_id, total_ttc, total_ht, total_tva, taux_tva, rendu,
                                  remise_validee_par, credit_valide_par)
SELECT t.id, gen_random_uuid(), t.session_id, t.numero_caisse, t.numero, t.numero_caisse || '-' || lpad(t.numero::text, 6, '0'),
       t.date_utc, t.caissier, false, NULL, t.client_id, tt.total, tt.total, 0, 0, 0, NULL, NULL
FROM t JOIN tt ON tt.ticket_id = t.id;

INSERT INTO caisse.ligne_ticket (id, ticket_id, ordre, conditionnement_id, article_id, libelle, libelle_conditionnement,
                                 code_scanne, quantite, quantite_unite_stock, prix_unitaire, remise, taux_tva, montant_ttc,
                                 lot_presume, ligne_origine_id)
SELECT id, ticket_id, ordre, conditionnement_id, article_id, libelle, 'pièce', NULL, quantite, quantite, prix, 0, 0,
       quantite * prix, false, NULL
FROM l;

INSERT INTO caisse.paiement (id, ticket_id, mode, montant, reference_transaction)
SELECT gen_random_uuid(), t.id, CASE WHEN t.client_id IS NULL THEN 'Especes' ELSE 'Credit' END, tt.total, NULL
FROM t JOIN tt ON tt.ticket_id = t.id;

INSERT INTO caisse.compteur_caisse (numero_caisse, dernier_numero)
SELECT numero_caisse, max(numero) FROM t GROUP BY numero_caisse
ON CONFLICT (numero_caisse) DO UPDATE SET dernier_numero = greatest(caisse.compteur_caisse.dernier_numero, excluded.dernier_numero);

-- Réceptions : une par fournisseur et par mois, au début du mois, qui couvre les ventes du mois de chaque article.
CREATE TEMP TABLE besoin ON COMMIT DROP AS
SELECT l.article_id, date_trunc('month', l.date_utc AT TIME ZONE 'UTC')::date AS mois, sum(l.quantite) AS quantite
FROM l GROUP BY 1, 2;

CREATE TEMP TABLE r ON COMMIT DROP AS
SELECT gen_random_uuid() AS id, x.fournisseur, x.mois, row_number() OVER (ORDER BY x.mois, x.fournisseur) AS n,
       (greatest(x.mois, (SELECT debut FROM p))::timestamp + interval '5 hours') AT TIME ZONE 'UTC' AS date_utc
FROM (SELECT DISTINCT a.fournisseur, b.mois FROM besoin b JOIN a ON a.article_id = b.article_id) x;

CREATE TEMP TABLE lr ON COMMIT DROP AS
SELECT gen_random_uuid() AS id, r.id AS reception_id, r.date_utc,
       row_number() OVER (PARTITION BY r.id ORDER BY a.i) AS ordre, a.article_id, b.quantite + 5 AS quantite, a.cout
FROM besoin b JOIN a ON a.article_id = b.article_id JOIN r ON r.fournisseur = a.fournisseur AND r.mois = b.mois;

INSERT INTO achats.reception (id, numero, fournisseur_id, numero_bon_livraison, statut, date_creation_utc, cree_par,
                              date_validation_utc, validee_par)
SELECT r.id, 'RV-' || lpad(r.n::text, 7, '0'), f.id, NULL, 'Validee', r.date_utc, 'volume', r.date_utc, 'volume'
FROM r JOIN f ON f.n = r.fournisseur;

INSERT INTO achats.ligne_reception (id, reception_id, ordre, article_id, quantite, cout_unitaire, frais_approche_alloues)
SELECT id, reception_id, ordre, article_id, quantite, cout, 0 FROM lr;

-- Journal de stock : entrées des réceptions, sorties des ventes, vers et depuis l'emplacement de vente.
INSERT INTO stock.mouvement_stock (date_utc, type, article_id, site_id, emplacement_source_id, emplacement_destination_id,
                                   lot_id, numero_serie_id, quantite, cout_unitaire, type_document, document_id, utilisateur,
                                   id_idempotence)
SELECT lr.date_utc, 'EntreeReception', lr.article_id, p.site_id, NULL, p.emplacement_id, NULL, NULL, lr.quantite, lr.cout,
       'Reception', lr.reception_id, 'volume', gen_random_uuid()
FROM lr, p
ORDER BY lr.date_utc;

INSERT INTO stock.mouvement_stock (date_utc, type, article_id, site_id, emplacement_source_id, emplacement_destination_id,
                                   lot_id, numero_serie_id, quantite, cout_unitaire, type_document, document_id, utilisateur,
                                   id_idempotence)
SELECT l.date_utc, 'VenteCaisse', l.article_id, p.site_id, p.emplacement_id, NULL, NULL, NULL, l.quantite, l.cout,
       'TicketCaisse', l.ticket_id, l.caissier, gen_random_uuid()
FROM l, p
ORDER BY l.date_utc;

-- Soldes : somme du journal ; CUMP = coût fixe de l'article.
CREATE TEMP TABLE solde ON COMMIT DROP AS
SELECT a.article_id, a.cout, e.quantite - coalesce(v.quantite, 0) AS quantite
FROM a
JOIN (SELECT article_id, sum(quantite) AS quantite FROM lr GROUP BY article_id) e ON e.article_id = a.article_id
LEFT JOIN (SELECT article_id, sum(quantite) AS quantite FROM l GROUP BY article_id) v ON v.article_id = a.article_id;

INSERT INTO stock.stock_site (article_id, site_id, quantite_physique, quantite_reservee, quantite_bloquee, cump)
SELECT solde.article_id, p.site_id, solde.quantite, 0, 0, solde.cout FROM solde, p;

INSERT INTO stock.stock_emplacement (id, article_id, emplacement_id, lot_id, quantite_physique)
SELECT gen_random_uuid(), solde.article_id, p.emplacement_id, NULL, solde.quantite FROM solde, p;

-- Crédit : ventes à crédit, remboursées au début du mois suivant (Mobile Money, hors espèces de la session).
CREATE TEMP TABLE vc ON COMMIT DROP AS
SELECT gen_random_uuid() AS id, t.client_id, t.date_utc, tt.total AS montant, t.id AS ticket_id,
       t.numero_caisse || '-' || lpad(t.numero::text, 6, '0') AS numero_ticket, t.caissier,
       date_trunc('month', t.date_utc AT TIME ZONE 'UTC')::date AS mois
FROM t JOIN tt ON tt.ticket_id = t.id WHERE t.client_id IS NOT NULL;

INSERT INTO credit.operation_credit (id, client_id, date_utc, type, montant, document_id, numero_ticket, echeance_utc, utilisateur, validee_par)
SELECT id, client_id, date_utc, 'VenteCredit', montant, ticket_id, numero_ticket, date_utc + interval '30 days', caissier, NULL FROM vc;

CREATE TEMP TABLE rb ON COMMIT DROP AS
SELECT gen_random_uuid() AS id, gen_random_uuid() AS remboursement_id, x.client_id, x.mois, x.montant,
       ((x.mois + interval '1 month')::date - (SELECT debut FROM p)) AS d,
       ((x.mois + interval '1 month')::timestamp + interval '9 hours') AT TIME ZONE 'UTC' AS date_utc
FROM (SELECT client_id, mois, sum(montant) AS montant FROM vc GROUP BY client_id, mois) x
WHERE (x.mois + interval '1 month')::date < (SELECT debut + jours FROM p);

INSERT INTO caisse.remboursement_credit (id, id_idempotence, session_id, client_id, mode, montant, reference_transaction, date_utc, caissier)
SELECT rb.remboursement_id, gen_random_uuid(), s.id, rb.client_id, 'MobileMoney', rb.montant, 'VOLUME', rb.date_utc, s.caissier
FROM rb JOIN s ON s.numero_caisse = 'VOL-1' AND s.d = rb.d;

INSERT INTO credit.operation_credit (id, client_id, date_utc, type, montant, document_id, numero_ticket, echeance_utc, utilisateur, validee_par)
SELECT id, client_id, date_utc, 'Remboursement', montant, remboursement_id, NULL, NULL, 'S900001', NULL FROM rb;

INSERT INTO credit.imputation (id, operation_id, vente_id, montant)
SELECT gen_random_uuid(), rb.id, vc.id, vc.montant FROM rb JOIN vc ON vc.client_id = rb.client_id AND vc.mois = rb.mois;

UPDATE credit.client cl
SET solde_du = coalesce(v.montant, 0) - coalesce(r.montant, 0)
FROM c
LEFT JOIN (SELECT client_id, sum(montant) AS montant FROM vc GROUP BY client_id) v ON v.client_id = c.id
LEFT JOIN (SELECT client_id, sum(montant) AS montant FROM rb GROUP BY client_id) r ON r.client_id = c.id
WHERE cl.id = c.id;

SELECT (SELECT count(*) FROM t) AS tickets, (SELECT count(*) FROM l) AS lignes, (SELECT count(*) FROM lr) AS lignes_reception,
       (SELECT count(*) FROM vc) AS ventes_credit;

COMMIT;

ANALYZE;
