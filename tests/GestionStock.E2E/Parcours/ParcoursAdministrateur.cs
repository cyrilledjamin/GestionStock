using GestionStock.E2E.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GestionStock.E2E.Parcours;

/// <summary>Administrateur (8.3) : comptes du personnel, rôles, mot de passe provisoire, exploitation.</summary>
public sealed class ParcoursAdministrateur(Navigateur navigateur) : ParcoursBase(navigateur)
{
    [Parcours]
    public async Task Cree_un_compte_que_l_employe_active_puis_le_desactive()
    {
        await SeConnecterAsync(Environnement.Administrateur, Environnement.MotDePasseAdministrateur);

        // Création : matricule généré, deux rôles, mot de passe provisoire.
        await AllerAsync("Utilisateurs");
        await Champ("Prenom").FillAsync("Awa");
        await Champ("Nom").FillAsync($"Parcours{Suffixe}");
        await Champ("MotDePasseProvisoire").FillAsync("Provis0ire!");
        await Champ("Role.Caissier").CheckAsync();
        await Champ("Role.Magasinier").CheckAsync();
        await Bouton("CreerCompte").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync($"Awa Parcours{Suffixe}");
        var matricule = (await Page.Locator("h1 code").InnerTextAsync()).Trim();
        Assert.Matches("^S[0-9]{6}$", matricule);
        await VoirAsync("MotDePasseProvisoireEnAttente");

        // L'employé : mot de passe provisoire, aucune permission tant qu'il ne l'a pas changé.
        await SeDeconnecterAsync();
        await SeConnecterAsync(matricule, "Provis0ire!");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("mon-compte$"));
        await VoirAsync("ChangerMotDePasseObligatoire");
        await Expect(Menu().GetByRole(AriaRole.Link, new() { Name = T["Stock"], Exact = true })).ToHaveCountAsync(0);
        await Champ("MotDePasseActuel").FillAsync("Provis0ire!");
        await Champ("NouveauMotDePasse").FillAsync(Donnees.MotDePasse);
        await Champ("ConfirmerMotDePasse").FillAsync(Donnees.MotDePasse);
        await Bouton("ChangerMotDePasse").ClickAsync();
        // Mot de passe provisoire remplacé : retour à l'accueil, les permissions des rôles s'appliquent.
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex($"^{T["Accueil"]}"));

        // Ses deux rôles s'appliquent : la caisse (Caissier) et le stock (Magasinier), pas les utilisateurs.
        await Expect(Menu().GetByRole(AriaRole.Link, new() { Name = T["Caisse"], Exact = true })).ToBeVisibleAsync();
        await Expect(Menu().GetByRole(AriaRole.Link, new() { Name = T["Stock"], Exact = true })).ToBeVisibleAsync();
        await Expect(Menu().GetByRole(AriaRole.Link, new() { Name = T["Utilisateurs"], Exact = true })).ToHaveCountAsync(0);
        await SeDeconnecterAsync();

        // Départ de l'employé : désactivation journalisée, connexion refusée ensuite.
        await SeConnecterAsync(Environnement.Administrateur, Environnement.MotDePasseAdministrateur);
        await AllerAsync("Utilisateurs");
        await Page.GetByRole(AriaRole.Link, new() { Name = $"Awa Parcours{Suffixe}" }).ClickAsync();
        await Bouton("Desactiver").ClickAsync();
        await VoirAsync("CompteDesactive");
        await Expect(Page.GetByRole(AriaRole.Table)).ToContainTextAsync(T["ActionCompte.Desactivation"]);
        await SeDeconnecterAsync();

        await Champ("Matricule").FillAsync(matricule);
        await Champ("MotDePasse").FillAsync(Donnees.MotDePasse);
        await Bouton("SeConnecter").ClickAsync();
        await Expect(Page.Locator(".message").First).ToBeVisibleAsync();
        await Expect(Menu()).ToHaveCountAsync(0);
    }

    [Parcours]
    public async Task Lance_le_controle_des_soldes_et_consulte_les_sauvegardes()
    {
        await SeConnecterAsync(Environnement.Administrateur, Environnement.MotDePasseAdministrateur);

        // Accueil : bloc Exploitation.
        await VoirAsync("Exploitation");

        await AllerAsync("Controles");
        await Bouton("LancerControle").ClickAsync();
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex(
            System.Text.RegularExpressions.Regex.Escape(T["ControleTermine", "§", "§"]).Replace("§", "[0-9]+"))))
            // Sur un long historique (jeu volumineux), le contrôle relit des millions de mouvements : jusqu'à 2 minutes.
            .ToBeVisibleAsync(new() { Timeout = 120_000 });

        await AllerAsync("Sauvegardes");
        await VoirAsync("SauvegardeLocale");
        await VoirAsync("Restauration");
    }
}
