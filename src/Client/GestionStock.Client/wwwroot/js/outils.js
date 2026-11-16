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

    // Remet le curseur dans un champ (scan à la douchette).
    focaliser: function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.focus();
            element.select();
        }
    }
};
