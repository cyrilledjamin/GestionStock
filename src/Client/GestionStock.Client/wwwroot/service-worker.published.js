// Application installable (PWA, décision 24) : tous les fichiers du client sont gardés sur le poste à l'installation,
// pour que la caisse se recharge même quand le serveur est injoignable (7.8, ENF08).
// Les appels à l'API (/api/...) ne passent jamais par ce cache : ils vont au serveur, ou échouent hors ligne et la
// caisse met alors l'opération en file d'attente.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(installer(event)));
self.addEventListener('activate', event => event.waitUntil(activer(event)));
self.addEventListener('fetch', event => event.respondWith(repondre(event)));

const prefixeCache = 'gestionstock-';
const nomCache = `${prefixeCache}${self.assetsManifest.version}`;
const aGarder = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.svg$/];
const aIgnorer = [/^service-worker\.js$/];

async function installer() {
    const fichiers = self.assetsManifest.assets
        .filter(f => aGarder.some(motif => motif.test(f.url)))
        .filter(f => !aIgnorer.some(motif => motif.test(f.url)))
        .map(f => new Request(f.url, { integrity: f.hash, cache: 'no-cache' }));
    await caches.open(nomCache).then(cache => cache.addAll(fichiers));
}

async function activer() {
    // Nouvelle version déployée : les caches des versions précédentes sont supprimés.
    const noms = await caches.keys();
    await Promise.all(noms.filter(nom => nom.startsWith(prefixeCache) && nom !== nomCache).map(nom => caches.delete(nom)));
}

async function repondre(event) {
    const requete = event.request;
    if (requete.method !== 'GET' || new URL(requete.url).pathname.startsWith('/api/')) {
        return fetch(requete);
    }

    // Toute navigation (ex. /caisse) reçoit index.html : l'application route elle-même.
    const cle = requete.mode === 'navigate' ? 'index.html' : requete;
    const cache = await caches.open(nomCache);
    return (await cache.match(cle)) || fetch(requete);
}
