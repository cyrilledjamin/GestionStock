#!/usr/bin/env bash
# Restaure une sauvegarde (décision 32) : la base et le volume des fichiers.
#
# Usage (l'API arrêtée, voir docs/exploitation.md) :
#   restaurer.sh --oui ARCHIVE [--cle FICHIER_CLE_PRIVEE] [--base NOM] [--sans-fichiers]
#
# ARCHIVE : chemin d'une sauvegarde locale (ex. /sauvegardes/horaire/gestionstock-20261117-1405.tar), ou d'une copie
#           hors site chiffrée (.tar.age), locale ou distante (ex. boutique:gestionstock/quotidien/gestionstock-20261117.tar.age,
#           téléchargée par rclone).
# --cle   : fichier de la clé privée age, obligatoire pour une copie .tar.age (apportée par le propriétaire).
# --base  : base à restaurer ; PGDATABASE par défaut. Une autre base est créée au besoin (restauration d'essai).
# --sans-fichiers : ne touche pas au volume des fichiers (restauration d'essai).
# --oui   : confirmation explicite ; la base cible est remplacée par la sauvegarde.
set -euo pipefail

archive="" cle="" base="${PGDATABASE:-}" fichiers=true confirme=false
while [ $# -gt 0 ]; do
  case "$1" in
    --cle) cle="$2"; shift 2 ;;
    --base) base="$2"; shift 2 ;;
    --sans-fichiers) fichiers=false; shift ;;
    --oui) confirme=true; shift ;;
    -*) echo "Option inconnue : $1" >&2; exit 2 ;;
    *) archive="$1"; shift ;;
  esac
done

if [ -z "$archive" ] || [ -z "$base" ]; then
  sed -n '2,15p' "$0" >&2
  exit 2
fi

if [[ ! "$base" =~ ^[a-z_][a-z0-9_]*$ ]]; then
  echo "Nom de base invalide : $base (minuscules, chiffres et _)." >&2
  exit 2
fi

if [ "$confirme" != true ]; then
  echo "La base « $base »$([ "$fichiers" = true ] && echo " et le volume des fichiers") vont être remplacés par $archive." >&2
  echo "Relancer avec --oui pour confirmer." >&2
  exit 2
fi

travail=$(mktemp -d)
trap 'rm -rf -- "$travail"' EXIT

# Copie distante : téléchargée d'abord.
if [ ! -f "$archive" ] && [[ "$archive" == *:* ]]; then
  echo "Téléchargement de $archive…"
  rclone copyto "$archive" "$travail/$(basename "$archive")"
  archive="$travail/$(basename "$archive")"
fi

if [[ "$archive" == *.age ]]; then
  [ -n "$cle" ] || { echo "Copie chiffrée : indiquer la clé privée avec --cle." >&2; exit 2; }
  age -d -i "$cle" -o "$travail/archive.tar" "$archive"
  archive="$travail/archive.tar"
fi

tar -xf "$archive" -C "$travail" base.dump fichiers.tar.gz
pg_restore --list "$travail/base.dump" >/dev/null

if ! psql -X -tAc "SELECT 1 FROM pg_database WHERE datname = '$base'" -d postgres | grep -q 1; then
  echo "Création de la base $base…"
  createdb "$base"
fi

echo "Restauration de la base $base…"
pg_restore --clean --if-exists --no-owner --exit-on-error --single-transaction -d "$base" "$travail/base.dump"

if [ "$fichiers" = true ]; then
  repertoire="${SAUVEGARDE_FICHIERS:-/donnees/fichiers}"
  echo "Restauration des fichiers dans $repertoire…"
  find "$repertoire" -mindepth 1 -delete
  tar -xzf "$travail/fichiers.tar.gz" -C "$repertoire" --same-owner
fi

echo "Restauration terminée. Redémarrer l'API : docker compose -f deploy/docker-compose.yml start api"
