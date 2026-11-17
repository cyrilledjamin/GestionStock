#!/usr/bin/env bash
# Un passage de sauvegarde (décision 32).
#
# 1. Sauvegarde locale, chaque heure : base complète (pg_dump, format custom) + volume des fichiers (reçus, clés qui
#    signent les jetons de connexion), réunis dans une archive datée sur le disque externe monté en /sauvegardes.
#    Rotation : horaires 48 h ; la première de chaque jour gardée 30 jours ; la première de chaque mois gardée 12 mois.
# 2. Copie hors de la boutique, une fois par jour (au premier passage qui suit 24 h sans copie réussie, ce qui rattrape
#    un PC éteint la nuit) : archive chiffrée avec age pour la clé publique du propriétaire (la clé privée n'est jamais
#    sur le serveur), envoyée par rclone. Rotation distante : 30 quotidiennes, 12 mensuelles.
#
# Chaque étape écrit son compte rendu dans exploitation.sauvegarde (lu par l'application pour alerter).
# Variables : PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE (connexion) ; SAUVEGARDE_DISTANT (ex. « boutique:gestionstock »,
# vide = pas de copie hors site) ; SAUVEGARDE_CLE_PUBLIQUE (une ou plusieurs clés age, séparées par des espaces) ;
# RCLONE_CONFIG (fichier de configuration de rclone).
set -uo pipefail

REPERTOIRE="${SAUVEGARDE_REPERTOIRE_INTERNE:-/sauvegardes}"
FICHIERS="${SAUVEGARDE_FICHIERS:-/donnees/fichiers}"
DISTANT="${SAUVEGARDE_DISTANT:-}"
CLES="${SAUVEGARDE_CLE_PUBLIQUE:-}"
GARDER_HORAIRES_MINUTES=$(( 48 * 60 ))
GARDER_QUOTIDIENNES=30
GARDER_MENSUELLES=12
TEMOIN_HORS_SITE="$REPERTOIRE/.derniere-copie-hors-site"

journal() { echo "$(date '+%Y-%m-%d %H:%M:%S') sauvegarde : $*"; }

maintenant_utc() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }

# consigner TYPE DEBUT REUSSIE FICHIER TAILLE MESSAGE : compte rendu dans la base. Un échec ici (base arrêtée, migration
# pas encore appliquée) n'arrête pas la sauvegarde : il est seulement journalisé.
consigner() {
  local type="$1" debut="$2" reussie="$3" fichier="$4" taille="$5" message="$6"
  psql -X -q -v ON_ERROR_STOP=1 \
    -v type="$type" -v debut="$debut" -v fin="$(maintenant_utc)" -v reussie="$reussie" \
    -v fichier="$fichier" -v taille="$taille" -v message="${message:0:2000}" <<'SQL' >/dev/null 2>&1 || journal "compte rendu non enregistré dans la base ($type)"
INSERT INTO exploitation.sauvegarde (type, debut_utc, fin_utc, reussie, fichier, taille, message)
VALUES (:'type', :'debut', :'fin', :'reussie'::boolean, NULLIF(:'fichier', ''), NULLIF(:'taille', '')::bigint, NULLIF(:'message', ''));
SQL
}

# garder_plus_recents DOSSIER NOMBRE : supprime les archives les plus anciennes au-delà de NOMBRE (noms datés, tri lexical).
garder_plus_recents() {
  local dossier="$1" nombre="$2"
  ls -1 "$dossier"/gestionstock-*.tar 2>/dev/null | sort | head -n -"$nombre" | while read -r ancien; do
    rm -f -- "$ancien"
  done
}

# Copie sans dupliquer l'espace quand le disque le permet (lien physique), sinon copie simple.
dupliquer() { ln -f "$1" "$2" 2>/dev/null || cp -f "$1" "$2"; }

sauvegarde_locale() {
  local debut horodatage jour mois travail archive nom
  debut=$(maintenant_utc)
  horodatage=$(date '+%Y%m%d-%H%M')
  jour=$(date '+%Y%m%d')
  mois=$(date '+%Y%m')
  nom="gestionstock-$horodatage.tar"
  mkdir -p "$REPERTOIRE/horaire" "$REPERTOIRE/quotidien" "$REPERTOIRE/mensuel" || return 1

  # Travail dans le même disque que la destination : le renommage final est atomique, une archive à moitié écrite
  # n'apparaît jamais sous un nom daté.
  travail=$(mktemp -d "$REPERTOIRE/.en-cours-XXXXXX") || { consigner Locale "$debut" false "" "" "répertoire $REPERTOIRE inaccessible"; return 1; }
  local erreur=""
  if ! sortie=$(pg_dump -Fc -f "$travail/base.dump" 2>&1); then
    erreur="pg_dump : $sortie"
  elif ! sortie=$(pg_restore --list "$travail/base.dump" 2>&1 >/dev/null); then
    erreur="archive de la base illisible : $sortie"
  elif [ -d "$FICHIERS" ] && ! sortie=$(tar -czf "$travail/fichiers.tar.gz" -C "$FICHIERS" . 2>&1); then
    erreur="archive des fichiers : $sortie"
  else
    [ -f "$travail/fichiers.tar.gz" ] || tar -czf "$travail/fichiers.tar.gz" -T /dev/null
    if ! sortie=$(tar -cf "$travail/$nom" -C "$travail" base.dump fichiers.tar.gz 2>&1); then
      erreur="archive : $sortie"
    fi
  fi

  if [ -n "$erreur" ]; then
    rm -rf -- "$travail"
    journal "échec de la sauvegarde locale : $erreur"
    consigner Locale "$debut" false "" "" "$erreur"
    return 1
  fi

  archive="$REPERTOIRE/horaire/$nom"
  mv -f "$travail/$nom" "$archive"
  rm -rf -- "$travail"

  ls "$REPERTOIRE/quotidien/gestionstock-$jour-"*.tar >/dev/null 2>&1 || dupliquer "$archive" "$REPERTOIRE/quotidien/$nom"
  ls "$REPERTOIRE/mensuel/gestionstock-$mois"*.tar >/dev/null 2>&1 || dupliquer "$archive" "$REPERTOIRE/mensuel/$nom"

  find "$REPERTOIRE/horaire" -name 'gestionstock-*.tar' -mmin +"$GARDER_HORAIRES_MINUTES" -delete
  garder_plus_recents "$REPERTOIRE/quotidien" "$GARDER_QUOTIDIENNES"
  garder_plus_recents "$REPERTOIRE/mensuel" "$GARDER_MENSUELLES"

  local taille
  taille=$(stat -c %s "$archive")
  journal "sauvegarde locale $nom ($taille octets)"
  consigner Locale "$debut" true "horaire/$nom" "$taille" ""
}

copie_hors_site() {
  [ -n "$DISTANT" ] || return 0

  # Une copie par jour : la dernière réussie date de moins de 24 h ?
  if [ -f "$TEMOIN_HORS_SITE" ] && [ $(( $(date +%s) - $(cat "$TEMOIN_HORS_SITE") )) -lt 86400 ]; then
    return 0
  fi

  local debut jour mois source chiffre nom erreur="" destinataires=()
  debut=$(maintenant_utc)
  jour=$(date '+%Y%m%d')
  mois=$(date '+%Y%m')
  nom="gestionstock-$jour.tar.age"

  for cle in $CLES; do destinataires+=(-r "$cle"); done
  if [ ${#destinataires[@]} -eq 0 ]; then
    erreur="aucune clé publique (SAUVEGARDE_CLE_PUBLIQUE) : une copie hors de la boutique n'est jamais envoyée en clair"
  else
    source=$(ls -1 "$REPERTOIRE"/horaire/gestionstock-*.tar 2>/dev/null | sort | tail -n 1)
    if [ -z "$source" ]; then
      erreur="aucune sauvegarde locale à copier"
    else
      chiffre=$(mktemp "$REPERTOIRE/.hors-site-XXXXXX")
      if ! sortie=$(age "${destinataires[@]}" -o "$chiffre" "$source" 2>&1); then
        erreur="chiffrement : $sortie"
      elif ! sortie=$(rclone copyto "$chiffre" "$DISTANT/quotidien/$nom" 2>&1); then
        erreur="envoi : $sortie"
      else
        if ! rclone lsf "$DISTANT/mensuel/" 2>/dev/null | grep -q "^gestionstock-$mois"; then
          rclone copyto "$chiffre" "$DISTANT/mensuel/$nom" >/dev/null 2>&1 || journal "copie mensuelle hors site non envoyée"
        fi

        for dossier in quotidien:"$GARDER_QUOTIDIENNES" mensuel:"$GARDER_MENSUELLES"; do
          rclone lsf --files-only "$DISTANT/${dossier%%:*}/" 2>/dev/null | grep '^gestionstock-.*\.tar\.age$' | sort | head -n -"${dossier##*:}" |
            while read -r ancien; do rclone deletefile "$DISTANT/${dossier%%:*}/$ancien" >/dev/null 2>&1; done
        done
      fi
      local taille
      taille=$(stat -c %s "$chiffre" 2>/dev/null || echo "")
      rm -f -- "$chiffre"
    fi
  fi

  if [ -n "$erreur" ]; then
    journal "échec de la copie hors site : $erreur"
    consigner HorsSite "$debut" false "" "" "$erreur"
    return 1
  fi

  date +%s > "$TEMOIN_HORS_SITE"
  journal "copie hors site quotidien/$nom envoyée"
  consigner HorsSite "$debut" true "quotidien/$nom" "${taille:-}" ""
}

statut=0
sauvegarde_locale || statut=1
copie_hors_site || statut=1
exit "$statut"
