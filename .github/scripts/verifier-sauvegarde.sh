#!/usr/bin/env bash
# Sauvegardes sur l'application déployée (tranche 8, décision 32) :
# passage de sauvegarde (archive locale + copie chiffrée vers un distant rclone), comptes rendus dans la base et dans
# l'API, puis restauration complète de la copie hors site (base et fichiers), l'API arrêtée, et contrôle des soldes
# sans écart sur la base restaurée.
# Usage : verifier-sauvegarde.sh <adresse de l'API> <identifiant administrateur> <mot de passe administrateur>
#                                <fichier de la clé privée age> <distant rclone, ex. essai:/sauvegardes/hors-site>
# Variable : COMPOSE (commande docker compose du déploiement).
set -euo pipefail

API="$1"
CLE="$4"
DISTANT="$5"
JSON='Content-Type: application/json'
COMPOSE="${COMPOSE:-docker compose -f deploy/docker-compose.yml}"

connexion() {
  curl -fsS -H "$JSON" -d "$(jq -n --arg i "$1" --arg m "$2" '{identifiant: $i, motDePasse: $m}')" \
    "$API/api/identite/connexion" | jq -r .accessToken
}

requete() { $COMPOSE exec -T db psql -U gestionstock -d gestionstock -v ON_ERROR_STOP=1 -tAc "$1"; }

attendre_api() {
  for _ in $(seq 1 30); do
    curl -fsS "$API/health/pret" > /dev/null 2>&1 && return 0
    sleep 2
  done
  echo "L'API n'est pas prête après 60 secondes."
  return 1
}

admin=$(connexion "$2" "$3")

# 1. Passage de sauvegarde. Le passage du démarrage du conteneur a pu envoyer la copie du jour : le témoin est retiré
#    pour forcer une nouvelle copie hors site, sur la base migrée.
$COMPOSE exec -T sauvegarde rm -f /sauvegardes/.derniere-copie-hors-site
$COMPOSE exec -T sauvegarde sauvegarder.sh

test "$(requete "SELECT count(*) FROM exploitation.sauvegarde WHERE type = 'Locale' AND reussie AND fichier LIKE 'horaire/gestionstock-%.tar' AND taille > 0")" -ge 1
test "$(requete "SELECT count(*) FROM exploitation.sauvegarde WHERE type = 'HorsSite' AND reussie AND fichier LIKE 'quotidien/gestionstock-%.tar.age'")" -ge 1
curl -fsS -H "Authorization: Bearer $admin" "$API/api/exploitation/etat" \
  | jq -e '.derniereSauvegardeLocale != null and .derniereSauvegardeHorsSite != null
           and (.sauvegardeLocaleEnRetard | not) and (.sauvegardeHorsSiteEnRetard | not)' > /dev/null
curl -fsS -H "Authorization: Bearer $admin" "$API/api/exploitation/sauvegardes?type=HorsSite" | jq -e 'length >= 1' > /dev/null
echo "Sauvegarde locale et copie hors site consignées."

# 2. La copie hors site est chiffrée (en-tête age), jamais une archive lisible ; l'archive locale est complète.
copie=$($COMPOSE exec -T sauvegarde sh -c "rclone lsf '$DISTANT/quotidien/' | grep '^gestionstock-.*\.tar\.age$' | sort | tail -n 1")
test -n "$copie"
$COMPOSE exec -T sauvegarde sh -c "rclone cat '$DISTANT/quotidien/$copie' | head -c 21" | grep -qx 'age-encryption.org/v1'
$COMPOSE exec -T sauvegarde sh -c 'tar -tf "$(ls -1 /sauvegardes/horaire/gestionstock-*.tar | sort | tail -n 1)"' \
  | sort | tr '\n' ' ' | grep -qx 'base.dump fichiers.tar.gz '
test "$($COMPOSE exec -T sauvegarde sh -c 'ls -1 /sauvegardes/quotidien/gestionstock-*.tar | wc -l')" -ge 1
echo "Copie hors site chiffrée : $copie"

# 3. Sans --oui, la restauration refuse d'agir.
if $COMPOSE exec -T sauvegarde restaurer.sh "$DISTANT/quotidien/$copie" > /dev/null 2>&1; then
  echo "La restauration a agi sans confirmation."
  exit 1
fi

# 4. Une modification après la sauvegarde, que la restauration doit effacer.
avant=$(requete "SELECT count(*) FROM identite.v_utilisateurs")
curl -fsS -H "$JSON" -H "Authorization: Bearer $admin" \
  -d '{"prenom":"Apres","nom":"Sauvegarde","roles":["Caissier"],"langue":"fr","motDePasse":"Provis0ire!"}' \
  "$API/api/identite/utilisateurs" > /dev/null
test "$(requete "SELECT count(*) FROM identite.v_utilisateurs")" -eq $(( avant + 1 ))

# 5. Restauration complète depuis la copie hors site (téléchargée, déchiffrée par la clé privée), l'API arrêtée.
$COMPOSE stop api
$COMPOSE exec -T sauvegarde sh -c 'umask 077; cat > /tmp/cle-age.txt' < "$CLE"
$COMPOSE exec -T sauvegarde restaurer.sh --oui --cle /tmp/cle-age.txt "$DISTANT/quotidien/$copie"
$COMPOSE exec -T sauvegarde rm -f /tmp/cle-age.txt
$COMPOSE start api
attendre_api

test "$(requete "SELECT count(*) FROM identite.v_utilisateurs")" -eq "$avant"
# Le jeton émis avant la restauration reste valide : les clés qui signent les jetons ont été restaurées avec les fichiers.
curl -fsS -H "Authorization: Bearer $admin" "$API/api/identite/moi" > /dev/null
echo "Restauration : base et fichiers revenus à l'état de la sauvegarde."

# 6. Sur la base restaurée, chaque solde est conforme à son journal.
controle=$(curl -fsS -X POST -H "Authorization: Bearer $admin" "$API/api/exploitation/controles")
jq -e '.statut == "Termine" and .ecarts == 0' <<<"$controle" > /dev/null
echo "Contrôle des soldes après restauration : $(jq -r .soldesControles <<<"$controle") soldes, aucun écart."
