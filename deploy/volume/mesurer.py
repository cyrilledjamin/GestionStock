#!/usr/bin/env python3
"""Mesures de performance de l'API sur le jeu volumineux (tranche 9, étape 56). NE PAS LANCER SUR UNE BOUTIQUE.

Mesure le temps de réponse des routes chaudes (une requête d'échauffement, puis N mesures : médiane, 95e centile,
maximum, taille), la validation d'un ticket de 3 lignes (ENF05 : moins d'1 s) et la durée du contrôle des soldes.
Un compte « Mesure » cumulant les rôles est créé par l'Administrateur ; une session est ouverte sur le premier poste
puis clôturée.

Usage : python3 deploy/volume/mesurer.py --url http://localhost:8080 --mot-de-passe <ADMIN_MOT_DE_PASSE> [--repetitions 10]
        [--sortie mesures.md]
Bibliothèque standard seule (aucune dépendance).
"""

import argparse
import datetime
import json
import math
import re
import sys
import time
import urllib.error
import urllib.request
import uuid

MOT_DE_PASSE = "Mesure123!"


class Api:
    def __init__(self, url):
        self.url = url.rstrip("/")
        self.jeton = None

    def appel(self, methode, route, corps=None):
        """(statut, durée en ms, taille en octets, contenu JSON ou None)."""
        donnees = json.dumps(corps).encode() if corps is not None else None
        requete = urllib.request.Request(self.url + route, data=donnees, method=methode)
        requete.add_header("Accept-Encoding", "identity")
        if donnees is not None:
            requete.add_header("Content-Type", "application/json")
        if self.jeton:
            requete.add_header("Authorization", "Bearer " + self.jeton)
        debut = time.perf_counter()
        try:
            with urllib.request.urlopen(requete, timeout=600) as reponse:
                contenu = reponse.read()
                statut = reponse.status
        except urllib.error.HTTPError as erreur:
            contenu = erreur.read()
            statut = erreur.code
        duree = (time.perf_counter() - debut) * 1000
        try:
            valeur = json.loads(contenu) if contenu else None
        except ValueError:
            valeur = None
        return statut, duree, len(contenu), valeur

    def exiger(self, methode, route, corps=None):
        statut, _, _, valeur = self.appel(methode, route, corps)
        if statut >= 400:
            sys.exit(f"{methode} {route} : {statut} {valeur}")
        return valeur

    def connecter(self, identifiant, mot_de_passe):
        self.jeton = None
        self.jeton = self.exiger("POST", "/api/identite/connexion", {"identifiant": identifiant, "motDePasse": mot_de_passe})["accessToken"]


def centile(valeurs, p):
    triees = sorted(valeurs)
    return triees[max(0, math.ceil(p / 100 * len(triees)) - 1)]


def ean13(numero):
    base = "619" + str(numero).zfill(9)
    somme = sum(int(c) * (3 if i % 2 else 1) for i, c in enumerate(base))
    return base + str((10 - somme % 10) % 10)


def main():
    arguments = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    arguments.add_argument("--url", required=True)
    arguments.add_argument("--admin", default="admin")
    arguments.add_argument("--mot-de-passe", required=True)
    arguments.add_argument("--repetitions", type=int, default=10)
    arguments.add_argument("--ventes", type=int, default=30)
    arguments.add_argument("--sortie")
    options = arguments.parse_args()

    api = Api(options.url)
    api.connecter(options.admin, options.mot_de_passe)
    fiche = api.exiger("POST", "/api/identite/utilisateurs", {
        "prenom": "Mesure", "nom": "Performances", "roles": ["Gerant", "Comptable", "Caissier", "Magasinier"],
        "langue": "fr", "motDePasse": "Provis0ire!"})
    matricule = fiche["compte"]["matricule"]
    api.connecter(matricule, "Provis0ire!")
    api.exiger("POST", "/api/identite/moi/mot-de-passe", {"actuel": "Provis0ire!", "nouveau": MOT_DE_PASSE})
    api.connecter(matricule, MOT_DE_PASSE)

    # Repères dans le jeu volumineux.
    aujourd_hui = datetime.date.today()
    mois = (aujourd_hui - datetime.timedelta(days=30)).isoformat()
    sessions = api.exiger("GET", "/api/caisse/sessions")
    session_vol = next((s["id"] for s in sessions if s["numeroCaisse"].startswith("VOL-")), sessions[0]["id"] if sessions else None)
    article = api.exiger("GET", "/api/referentiel/scan/" + ean13(1))
    clients = api.exiger("GET", "/api/credit/clients?recherche=Client%20volume%201")

    routes = [
        ("Accueil : tableau de bord", "/api/rapports/tableau-de-bord"),
        ("Accueil : tendance sur 30 jours", "/api/rapports/tendance"),
        ("Rapport des ventes, 30 jours", f"/api/rapports/ventes?du={mois}&au={aujourd_hui}"),
        ("Rapport de caisse, 30 jours", f"/api/rapports/caisse?du={mois}&au={aujourd_hui}"),
        ("Rapport du stock", "/api/rapports/stock"),
        ("Rapport créances et dettes", "/api/rapports/creances-dettes"),
        ("Sessions de caisse", "/api/caisse/sessions"),
        ("Tickets d'une session", f"/api/caisse/sessions/{session_vol}/tickets"),
        ("Soldes de stock", "/api/stock/soldes"),
        ("Journal d'un article", f"/api/stock/articles/{article['articleId']}/mouvements"),
        ("Articles", "/api/referentiel/articles"),
        ("Catalogue de la caisse", "/api/referentiel/catalogue-caisse"),
        ("Grille de prix", "/api/prix/grille"),
        ("Scan d'un code-barres", "/api/referentiel/scan/" + ean13(17)),
        ("Recherche d'un article", "/api/referentiel/vendables?recherche=volume%2012"),
        ("Clients à crédit", "/api/credit/clients"),
        ("Fiche d'un client", f"/api/credit/clients/{clients[0]['id']}" if clients else "/api/credit/clients"),
        ("Réceptions", "/api/achats/receptions"),
        ("Dettes fournisseurs", "/api/achats/dettes"),
        ("Inventaires", "/api/inventaires"),
    ]

    lignes = []
    for libelle, route in routes:
        statut, _, _, _ = api.appel("GET", route)  # échauffement
        durees, taille = [], 0
        for _ in range(options.repetitions):
            statut, duree, taille, _ = api.appel("GET", route)
            durees.append(duree)
        modele = re.sub(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|619[0-9]{10}", "{id}", route.split("?")[0])
        lignes.append((libelle, modele, statut, centile(durees, 50), centile(durees, 95), max(durees), taille))
        print(f"{libelle:32} {statut} médiane {centile(durees, 50):8.0f} ms  max {max(durees):8.0f} ms  {taille / 1024:8.0f} Ko", flush=True)

    # Validation d'un ticket de 3 lignes (ENF05), sur une session ouverte pour la mesure.
    poste = api.exiger("GET", "/api/caisse/postes")[0]
    api.exiger("POST", "/api/caisse/sessions", {"numeroCaisse": poste, "fondCaisse": 0})
    vendables = [api.exiger("GET", "/api/referentiel/scan/" + ean13(n))["conditionnementId"] for n in (3, 5, 8)]
    ventes = []
    for _ in range(options.ventes + 1):
        statut, duree, _, valeur = api.appel("POST", "/api/caisse/tickets", {
            "idIdempotence": str(uuid.uuid4()),
            "lignes": [{"conditionnementId": c, "quantite": 1} for c in vendables],
            "paiements": [{"mode": "Especes", "montant": 1000000}]})
        if statut >= 400:
            sys.exit(f"Vente refusée : {statut} {valeur}")
        ventes.append(duree)
    ventes = ventes[1:]  # sans l'échauffement
    api.exiger("POST", "/api/caisse/sessions/courante/cloture", {"especesComptees": 0})
    lignes.append(("Validation d'un ticket de 3 lignes", "POST /api/caisse/tickets", 201, centile(ventes, 50), centile(ventes, 95), max(ventes), 0))
    print(f"{'Ticket de 3 lignes (ENF05)':32} médiane {centile(ventes, 50):8.0f} ms  p95 {centile(ventes, 95):8.0f} ms", flush=True)

    # Contrôle des soldes (ENF04), une fois.
    api.connecter(options.admin, options.mot_de_passe)
    statut, duree, _, controle = api.appel("POST", "/api/exploitation/controles")
    print(f"Contrôle des soldes : {statut} {duree / 1000:.1f} s, {controle.get('soldesControles') if controle else '?'} soldes, "
          f"{controle.get('ecarts') if controle else '?'} écart(s)", flush=True)

    tableau = ["| Mesure | Route | Statut | Médiane (ms) | 95e centile (ms) | Max (ms) | Taille (Ko) |",
               "| --- | --- | --- | --- | --- | --- | --- |"]
    for libelle, route, statut, mediane, p95, maximum, taille in lignes:
        tableau.append(f"| {libelle} | `{route}` | {statut} | {mediane:.0f} | {p95:.0f} | {maximum:.0f} | {taille / 1024:.0f} |")
    tableau.append(f"| Contrôle des soldes | `POST /api/exploitation/controles` | {statut} | {duree:.0f} | | | |")
    resultat = "\n".join(tableau)
    if controle:
        resultat += f"\n\nContrôle des soldes : {controle.get('soldesControles')} soldes, {controle.get('ecarts')} écart(s)."
    print("\n" + resultat)
    if options.sortie:
        with open(options.sortie, "w", encoding="utf-8") as fichier:
            fichier.write(resultat + "\n")


if __name__ == "__main__":
    main()
