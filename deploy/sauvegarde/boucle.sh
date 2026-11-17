#!/usr/bin/env bash
# Lance un passage de sauvegarde au démarrage, puis chaque heure à la minute SAUVEGARDE_MINUTE (5 par défaut).
# Un passage qui échoue est consigné par sauvegarder.sh ; la boucle continue.
set -uo pipefail

minute="${SAUVEGARDE_MINUTE:-5}"

if [ "${SAUVEGARDE_AU_DEMARRAGE:-true}" = "true" ]; then
  sauvegarder.sh || true
fi

while true; do
  maintenant=$(date +%s)
  # Prochaine heure pleine + minute choisie (heure de Douala, sans heure d'été : un calcul en secondes suffit).
  debut_heure=$(( maintenant - maintenant % 3600 ))
  prochain=$(( debut_heure + minute * 60 ))
  if [ "$prochain" -le "$maintenant" ]; then
    prochain=$(( prochain + 3600 ))
  fi
  sleep $(( prochain - maintenant ))
  sauvegarder.sh || true
done
