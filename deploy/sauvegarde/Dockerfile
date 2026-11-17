# Image du conteneur de sauvegarde (décision 32) : outils PostgreSQL de la même version majeure que la base,
# rclone (copie hors de la boutique), age (chiffrement par clé publique), tzdata (noms de fichiers à l'heure de Douala).
# Construite par docker-compose depuis ce dossier.
FROM postgres:17-alpine

RUN apk add --no-cache rclone age tzdata bash coreutils

COPY sauvegarder.sh restaurer.sh boucle.sh /usr/local/bin/
RUN chmod 0755 /usr/local/bin/sauvegarder.sh /usr/local/bin/restaurer.sh /usr/local/bin/boucle.sh \
    && mkdir -p /sauvegardes

ENV TZ=Africa/Douala

# Pas de démon cron : une boucle qui lance un passage chaque heure (les variables d'environnement restent visibles).
ENTRYPOINT ["boucle.sh"]
