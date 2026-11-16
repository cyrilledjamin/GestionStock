using GestionStock.Referentiel.Contracts;
using GestionStock.Reprise.Infrastructure;

namespace GestionStock.Reprise.Application;

/// <summary>
/// Jeu d'exemple d'une boutique de quartier (vente au détail et en gros) : données de démonstration et de test,
/// pas de produits réels. Codes-barres fictifs sous le préfixe GS1 Cameroun 617, clé de contrôle calculée.
/// </summary>
internal static class ExempleReprise
{
    public static IReadOnlyList<LigneModele> Lignes()
    {
        var rang = 0;
        string CodeSuivant()
        {
            // EAN-13 fictif : 617 (GS1 Cameroun) + 99 + rang sur 7 chiffres + clé de contrôle.
            var chiffres = $"61799{++rang:D7}";
            return chiffres + CodesBarres.CleAttendue(chiffres);
        }

        LigneModele Unite(string code, string libelle, string traduit, string unite, int decimales, decimal prix, decimal cout) =>
            new(code, libelle, traduit, unite, decimales, null, "Unite", 1m, CodeSuivant(), prix, cout);

        // Les données de l'article ne sont portées que par sa ligne Unite.
        LigneModele Autre(string code, string conditionnement, string niveau, decimal contenu, decimal prix) =>
            new(code, "", null, "", null, conditionnement, niveau, contenu, CodeSuivant(), prix, null);

        return
        [
            // Vente au poids (RG46) : le kg en unité de stock, le sac de 25 kg en conditionnement.
            Unite("RIZ-PARF", "Riz parfumé", "Fragrant rice", "kg", 3, 650, 520),
            Autre("RIZ-PARF", "Sac de 25 kg", "Sac", 25, 15000),
            Unite("SUCRE-POUD", "Sucre en poudre", "Granulated sugar", "kg", 3, 850, 690),
            Autre("SUCRE-POUD", "Sac de 50 kg", "Sac", 50, 40000),
            Unite("FARINE-BLE", "Farine de blé", "Wheat flour", "kg", 3, 550, 440),
            Autre("FARINE-BLE", "Sac de 50 kg", "Sac", 50, 25000),
            Unite("HUILE-PALM", "Huile de palme raffinée", "Refined palm oil", "litre", 3, 1350, 1120),
            Unite("PETROLE", "Pétrole lampant", "Kerosene", "litre", 3, 750, 640),

            // Vente à la pièce et au carton (RG45 : le carton coûte moins que ses unités).
            Unite("HUILE-1L", "Huile végétale 1 L", "Vegetable oil 1 L", "bouteille", 0, 1400, 1150),
            Autre("HUILE-1L", "Carton de 12", "Carton", 12, 15500),
            Unite("LAIT-CONC", "Lait concentré sucré 397 g", "Sweetened condensed milk 397 g", "boîte", 0, 700, 590),
            Autre("LAIT-CONC", "Carton de 48", "Carton", 48, 32000),
            Unite("LAIT-POUD", "Lait en poudre 400 g", "Powdered milk 400 g", "sachet", 0, 1900, 1600),
            Autre("LAIT-POUD", "Carton de 24", "Carton", 24, 44000),
            Unite("SARDINES", "Sardines à l'huile 125 g", "Sardines in oil 125 g", "boîte", 0, 450, 370),
            Autre("SARDINES", "Carton de 50", "Carton", 50, 21000),
            Unite("TOMATE-70", "Concentré de tomate 70 g", "Tomato paste 70 g", "boîte", 0, 125, 90),
            Autre("TOMATE-70", "Carton de 100", "Carton", 100, 11000),
            Unite("SPAGH-500", "Spaghetti 500 g", "Spaghetti 500 g", "paquet", 0, 400, 330),
            Autre("SPAGH-500", "Carton de 20", "Carton", 20, 7600),
            Unite("SAVON-400", "Savon de ménage 400 g", "Household soap 400 g", "pièce", 0, 300, 245),
            Autre("SAVON-400", "Carton de 48", "Carton", 48, 13500),
            Unite("EAU-150", "Eau minérale 1,5 L", "Mineral water 1.5 L", "bouteille", 0, 350, 280),
            Autre("EAU-150", "Pack de 6", "Pack", 6, 2000),
            Unite("SUCRE-MORC", "Sucre en morceaux 1 kg", "Sugar cubes 1 kg", "boîte", 0, 1100, 900),
            Autre("SUCRE-MORC", "Carton de 20", "Carton", 20, 21000),
            Unite("CUBE-BOUIL", "Bouillon cube", "Stock cube", "pièce", 0, 25, 18),
            Autre("CUBE-BOUIL", "Boîte de 60", "Pack", 60, 1350),
            Unite("SEL-1KG", "Sel iodé 1 kg", "Iodised salt 1 kg", "sachet", 0, 200, 150),
            Unite("ALLUMETTES", "Allumettes (boîte)", "Matches (box)", "boîte", 0, 50, 35),
            Autre("ALLUMETTES", "Paquet de 10", "Pack", 10, 450),
            Unite("BOUGIE", "Bougie blanche", "White candle", "pièce", 0, 100, 70),
            Unite("BISCUIT-SEC", "Biscuits secs 200 g", "Dry biscuits 200 g", "paquet", 0, 250, 190),
            Autre("BISCUIT-SEC", "Carton de 36", "Carton", 36, 8400),
        ];
    }

}
