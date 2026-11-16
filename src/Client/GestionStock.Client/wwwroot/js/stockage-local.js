// Stockage du poste dans IndexedDB (7.8) : données de la caisse et file des opérations faites hors ligne.
// Il survit à la fermeture de l'onglet et au redémarrage du PC ; la persistance est demandée au navigateur pour qu'il
// ne le vide pas de lui-même quand le disque se remplit.
// Deux magasins : « cache » (catalogue, prix, clients…) et « file » (opérations à envoyer, dans l'ordre des clés).
window.stockageLocal = (function () {
    const nomBase = "gestionstock";
    const version = 1;
    let ouverture = null;

    function base() {
        if (!ouverture) {
            ouverture = new Promise(function (resoudre, rejeter) {
                const demande = indexedDB.open(nomBase, version);
                demande.onupgradeneeded = function () {
                    const db = demande.result;
                    if (!db.objectStoreNames.contains("cache")) { db.createObjectStore("cache"); }
                    if (!db.objectStoreNames.contains("file")) { db.createObjectStore("file"); }
                };
                demande.onsuccess = function () { resoudre(demande.result); };
                demande.onerror = function () { ouverture = null; rejeter(demande.error); };
            });
        }
        return ouverture;
    }

    function transaction(magasin, mode, action) {
        return base().then(function (db) {
            return new Promise(function (resoudre, rejeter) {
                const tx = db.transaction(magasin, mode);
                const resultat = action(tx.objectStore(magasin));
                tx.oncomplete = function () { resoudre(resultat && "result" in resultat ? resultat.result : undefined); };
                tx.onerror = function () { rejeter(tx.error); };
                tx.onabort = function () { rejeter(tx.error); };
            });
        });
    }

    return {
        // Valeur (texte JSON) d'une clé, ou null.
        lire: function (magasin, cle) {
            return transaction(magasin, "readonly", function (m) { return m.get(cle); }).then(function (v) { return v ?? null; });
        },
        ecrire: function (magasin, cle, json) {
            return transaction(magasin, "readwrite", function (m) { m.put(json, cle); });
        },
        supprimer: function (magasin, cle) {
            return transaction(magasin, "readwrite", function (m) { m.delete(cle); });
        },
        // Toutes les valeurs du magasin, dans l'ordre croissant des clés.
        lister: function (magasin) {
            return transaction(magasin, "readonly", function (m) { return m.getAll(); });
        },
        // Demande au navigateur de ne jamais vider ce stockage ; renvoie vrai s'il l'accepte.
        persister: function () {
            return navigator.storage && navigator.storage.persist ? navigator.storage.persist() : Promise.resolve(false);
        },
        // Écoute les changements de connexion du navigateur ; relaie « EnLigne » / « HorsLigne » à .NET.
        ecouterConnexion: function (composant) {
            window.addEventListener("online", function () { composant.invokeMethodAsync("ConnexionChangee", true); });
            window.addEventListener("offline", function () { composant.invokeMethodAsync("ConnexionChangee", false); });
            return navigator.onLine;
        }
    };
})();
