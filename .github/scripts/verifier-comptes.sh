#!/usr/bin/env bash
# Cycle de vie d'un compte du personnel sur l'application déployée (tranche 5) :
# création par l'Administrateur (matricule généré, mot de passe provisoire), connexion sans permission,
# changement du mot de passe, droits du rôle, journal du compte, désactivation.
# Usage : verifier-comptes.sh <adresse de l'API> <identifiant administrateur> <mot de passe administrateur>
set -euo pipefail

API="$1"
JSON='Content-Type: application/json'

connexion() {
  curl -fsS -H "$JSON" -d "$(jq -n --arg i "$1" --arg m "$2" '{identifiant: $i, motDePasse: $m}')" \
    "$API/api/identite/connexion" | jq -r .accessToken
}

statut() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

admin=$(connexion "$2" "$3")

# Création : matricule généré (S + 6 chiffres), mot de passe provisoire fixé par l'Administrateur.
fiche=$(curl -fsS -H "$JSON" -H "Authorization: Bearer $admin" \
  -d '{"prenom":"Awa","nom":"Ndiaye","telephone":"+237 690 00 00 00","roles":["Caissier","Magasinier"],"langue":"fr","motDePasse":"Provis0ire!"}' \
  "$API/api/identite/utilisateurs")
id=$(jq -r .compte.id <<<"$fiche")
matricule=$(jq -r .compte.matricule <<<"$fiche")
[[ "$matricule" =~ ^S[0-9]{6}$ ]] || { echo "Matricule inattendu : $matricule"; exit 1; }
jq -e '.compte.nomComplet == "Awa Ndiaye" and .compte.motDePasseProvisoire and (.journal | length) == 1' <<<"$fiche" > /dev/null
echo "Compte créé : $matricule"

# Mot de passe provisoire : connexion possible, mais aucune permission.
provisoire=$(connexion "$matricule" "Provis0ire!")
curl -fsS -H "Authorization: Bearer $provisoire" "$API/api/identite/moi" \
  | jq -e '.motDePasseProvisoire and .permissions == [] and .nomComplet == "Awa Ndiaye"' > /dev/null
test "$(statut -H "Authorization: Bearer $provisoire" "$API/api/caisse/postes")" = "403"

# Changement du mot de passe par l'employé : les droits de ses deux rôles s'appliquent.
test "$(statut -H "$JSON" -H "Authorization: Bearer $provisoire" \
  -d '{"actuel":"Provis0ire!","nouveau":"Definit1f!"}' "$API/api/identite/moi/mot-de-passe")" = "204"
test "$(statut -H "$JSON" -d "{\"identifiant\":\"$matricule\",\"motDePasse\":\"Provis0ire!\"}" "$API/api/identite/connexion")" = "401"
employe=$(connexion "$matricule" "Definit1f!")
curl -fsS -H "Authorization: Bearer $employe" "$API/api/identite/moi" \
  | jq -e '(.motDePasseProvisoire | not) and (.permissions | index("Caisse.Vendre")) and (.permissions | index("Inventaire.Compter"))' > /dev/null
test "$(statut -H "Authorization: Bearer $employe" "$API/api/caisse/postes")" = "200"

# Désactivation (départ) : connexion refusée ; le compte et son journal restent.
curl -fsS -X POST -H "Authorization: Bearer $admin" "$API/api/identite/utilisateurs/$id/desactivation" \
  | jq -e '(.compte.actif | not) and ([.journal[].action] | length) == 3' > /dev/null
curl -s -H "$JSON" -d "{\"identifiant\":\"$matricule\",\"motDePasse\":\"Definit1f!\"}" "$API/api/identite/connexion" \
  | grep -q 'Identite.CompteDesactive'
echo "Cycle de vie du compte $matricule vérifié."
