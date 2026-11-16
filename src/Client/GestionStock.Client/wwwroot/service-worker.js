// En développement, le service worker ne met rien en cache : chaque rechargement prend les fichiers à jour.
// La version publiée (service-worker.published.js) garde l'application sur le poste pour démarrer hors ligne.
self.addEventListener('fetch', () => { });
