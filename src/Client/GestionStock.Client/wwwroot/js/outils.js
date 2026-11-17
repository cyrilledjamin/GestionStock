// Petits outils du navigateur appelés depuis Blazor (impression et téléchargement sur le poste client).
window.gestionStock = {
    // Enregistre un fichier reçu de l'API (ex. reçu PDF) sur le poste.
    telecharger: function (nom, type, base64) {
        const octets = Uint8Array.from(atob(base64), function (c) { return c.charCodeAt(0); });
        const url = URL.createObjectURL(new Blob([octets], { type: type }));
        const lien = document.createElement("a");
        lien.href = url;
        lien.download = nom;
        document.body.appendChild(lien);
        lien.click();
        lien.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
    },

    // Imprime la page : la feuille de style d'impression n'affiche que le reçu (80 mm).
    imprimer: function () {
        window.print();
    },

    // Langue de la page (attribut lang de <html>), celle de l'interface choisie.
    definirLangue: function (langue) {
        document.documentElement.lang = langue;
    },

    // Remet le curseur dans un champ (scan à la douchette).
    focaliser: function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.focus();
            // Un champ est sélectionné (le scan suivant remplace l'ancien texte) ; un bouton n'a pas de sélection.
            if (typeof element.select === "function") {
                element.select();
            }
        }
    },

    // Fait défiler la page jusqu'à un élément (caisse sur tablette : du panier au paiement).
    defiler: function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.scrollIntoView({ behavior: "smooth", block: "start" });
        }
    },

    // Écran tactile (doigt comme pointeur principal) : la caisse affiche alors son pavé numérique d'office.
    tactile: function () {
        return window.matchMedia("(pointer: coarse)").matches;
    },

    // Raccourcis clavier d'une page : les touches choisies sont relayées à la méthode .NET « Raccourci ».
    // Renvoie un objet dont « arreter » retire l'écouteur quand la page se ferme.
    raccourcis: function (composant, touches) {
        const ecouter = function (evenement) {
            if (touches.indexOf(evenement.key) >= 0 && !evenement.repeat) {
                evenement.preventDefault();
                composant.invokeMethodAsync("Raccourci", evenement.key);
            }
        };
        document.addEventListener("keydown", ecouter);
        return {
            arreter: function () {
                document.removeEventListener("keydown", ecouter);
            }
        };
    }
};
