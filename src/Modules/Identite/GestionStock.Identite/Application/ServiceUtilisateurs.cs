using System.Text.RegularExpressions;
using GestionStock.Identite.Contracts;
using GestionStock.Identite.Infrastructure;
using GestionStock.Infrastructure.Persistance;
using GestionStock.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Identite.Application;

/// <summary>Compte tel que l'Administrateur le voit : fiche, rôles et statut.</summary>
internal sealed record CompteUtilisateur(
    Guid Id,
    string Matricule,
    string Prenom,
    string Nom,
    string NomComplet,
    string? Telephone,
    IReadOnlyList<string> Roles,
    string Langue,
    bool Actif,
    bool MotDePasseProvisoire);

internal sealed record LigneJournalCompte(DateTime DateUtc, ActionCompte Action, string Auteur, string? Detail);

internal sealed record FicheCompte(CompteUtilisateur Compte, IReadOnlyList<LigneJournalCompte> Journal);

/// <summary>Création d'un compte : matricule facultatif (généré sinon), mot de passe initial provisoire.</summary>
internal sealed record SaisieCompte(
    string? Matricule,
    string? Prenom,
    string? Nom,
    string? Telephone,
    IReadOnlyList<string>? Roles,
    string? Langue,
    string? MotDePasse);

internal sealed record SaisieFiche(string? Prenom, string? Nom, string? Telephone, IReadOnlyList<string>? Roles, string? Langue);

/// <summary>
/// Gestion des comptes du personnel (Administrateur, 8.3) : fiche, rôles, désactivation, mot de passe provisoire.
/// Chaque opération est journalisée (ENF03). Un compte n'est jamais supprimé : ses documents restent tracés.
/// </summary>
internal sealed partial class ServiceUtilisateurs(
    UserManager<Utilisateur> utilisateurs,
    IdentiteDbContext contexte,
    IUniteDeTravail uniteDeTravail,
    IHorloge horloge)
{
    /// <summary>Auteur des opérations faites au démarrage (Administrateur initial).</summary>
    public const string AuteurSysteme = "systeme";

    public async Task<IReadOnlyList<CompteUtilisateur>> ListerAsync(CancellationToken annulation)
    {
        var roles = await RolesParUtilisateur(annulation);
        var comptes = await contexte.Users.AsNoTracking().OrderBy(u => u.Nom).ThenBy(u => u.Prenom).ThenBy(u => u.UserName).ToListAsync(annulation);
        return [.. comptes.Select(u => VersCompte(u, roles.GetValueOrDefault(u.Id) ?? []))];
    }

    public async Task<FicheCompte> ObtenirAsync(Guid id, CancellationToken annulation)
    {
        var utilisateur = await contexte.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, annulation)
            ?? throw new ExceptionIdentite(CodesErreurIdentite.CompteIntrouvable);
        return await VersFiche(utilisateur, annulation);
    }

    /// <summary>Crée un compte, dans une seule transaction ; le mot de passe initial est provisoire sauf au premier démarrage.</summary>
    public async Task<FicheCompte> CreerAsync(SaisieCompte saisie, string auteur, bool motDePasseProvisoire, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var (prenom, nom, telephone, roles, langue) = Controler(new SaisieFiche(saisie.Prenom, saisie.Nom, saisie.Telephone, saisie.Roles, saisie.Langue));
        var matricule = saisie.Matricule?.Trim() ?? "";
        if (matricule.Length > 0 && !FormeMatricule().IsMatch(matricule))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.MatriculeInvalide);
        }

        var nouveau = new Utilisateur
        {
            Prenom = prenom,
            Nom = nom,
            PhoneNumber = telephone,
            Langue = langue,
            MotDePasseProvisoire = motDePasseProvisoire,
        };

        // Mot de passe contrôlé avant de tirer un numéro : un refus ne consomme pas de matricule (la séquence ne recule pas).
        await ExigerMotDePasseValide(nouveau, saisie.MotDePasse);

        var utilisateur = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                if (matricule.Length == 0)
                {
                    matricule = await MatriculeGenere(jeton);
                }
                else if (await utilisateurs.FindByNameAsync(matricule) is not null)
                {
                    throw new ExceptionIdentite(CodesErreurIdentite.IdentifiantDejaUtilise, matricule);
                }

                nouveau.UserName = matricule;
                Exiger(await utilisateurs.CreateAsync(nouveau, saisie.MotDePasse ?? ""));
                Exiger(await utilisateurs.AddToRolesAsync(nouveau, roles));
                contexte.JournalComptes.Add(JournalCompte.Ecrire(nouveau.Id, ActionCompte.Creation, auteur, horloge.MaintenantUtc, $"{nouveau.NomComplet} ; {string.Join(", ", roles)}"));
                await contexte.SaveChangesAsync(jeton);
                return nouveau;
            },
            annulation);
        return await VersFiche(utilisateur, annulation);
    }

    /// <summary>Modifie la fiche et les rôles ; le matricule ne change pas (identifiant tracé dans les documents).</summary>
    public async Task<FicheCompte> ModifierAsync(Guid id, SaisieFiche saisie, string auteur, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var (prenom, nom, telephone, roles, langue) = Controler(saisie);
        var utilisateur = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var compte = await Trouver(id);
                var actuels = await utilisateurs.GetRolesAsync(compte);
                if (actuels.Contains(Roles.Administrateur) && !roles.Contains(Roles.Administrateur))
                {
                    await ExigerAutreAdministrateurActif(compte.Id, jeton);
                }

                compte.Prenom = prenom;
                compte.Nom = nom;
                compte.PhoneNumber = telephone;
                compte.Langue = langue;
                Exiger(await utilisateurs.UpdateAsync(compte));
                Exiger(await utilisateurs.RemoveFromRolesAsync(compte, actuels.Except(roles)));
                Exiger(await utilisateurs.AddToRolesAsync(compte, roles.Except(actuels)));
                if (!actuels.Order().SequenceEqual(roles.Order()))
                {
                    // Les droits changent : les sessions en cours doivent se renouveler avec les nouveaux rôles.
                    Exiger(await utilisateurs.UpdateSecurityStampAsync(compte));
                }

                contexte.JournalComptes.Add(JournalCompte.Ecrire(compte.Id, ActionCompte.Modification, auteur, horloge.MaintenantUtc, $"{compte.NomComplet} ; {string.Join(", ", roles)}"));
                await contexte.SaveChangesAsync(jeton);
                return compte;
            },
            annulation);
        return await VersFiche(utilisateur, annulation);
    }

    /// <summary>Départ d'un employé : connexion, renouvellement de session et validations refusés ; rien n'est supprimé.</summary>
    public async Task<FicheCompte> DesactiverAsync(Guid id, string auteur, CancellationToken annulation)
    {
        var utilisateur = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var compte = await Trouver(id);
                if (await utilisateurs.IsInRoleAsync(compte, Roles.Administrateur))
                {
                    await ExigerAutreAdministrateurActif(compte.Id, jeton);
                }

                compte.Actif = false;
                compte.LockoutEnabled = true;
                compte.LockoutEnd = DateTimeOffset.MaxValue;
                Exiger(await utilisateurs.UpdateAsync(compte));
                Exiger(await utilisateurs.UpdateSecurityStampAsync(compte));
                contexte.JournalComptes.Add(JournalCompte.Ecrire(compte.Id, ActionCompte.Desactivation, auteur, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
                return compte;
            },
            annulation);
        return await VersFiche(utilisateur, annulation);
    }

    public async Task<FicheCompte> ReactiverAsync(Guid id, string auteur, CancellationToken annulation)
    {
        var utilisateur = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var compte = await Trouver(id);
                compte.Actif = true;
                compte.LockoutEnd = null;
                compte.AccessFailedCount = 0;
                Exiger(await utilisateurs.UpdateAsync(compte));
                contexte.JournalComptes.Add(JournalCompte.Ecrire(compte.Id, ActionCompte.Reactivation, auteur, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
                return compte;
            },
            annulation);
        return await VersFiche(utilisateur, annulation);
    }

    /// <summary>Mot de passe oublié : l'Administrateur fixe un mot de passe provisoire ; les sessions en cours sont fermées.</summary>
    public async Task<FicheCompte> ReinitialiserMotDePasseAsync(Guid id, string? motDePasse, string auteur, CancellationToken annulation)
    {
        var utilisateur = await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var compte = await Trouver(id);
                await ExigerMotDePasseValide(compte, motDePasse);
                Exiger(await utilisateurs.RemovePasswordAsync(compte));
                Exiger(await utilisateurs.AddPasswordAsync(compte, motDePasse!));
                compte.MotDePasseProvisoire = true;
                if (compte.Actif)
                {
                    // Un verrouillage après trop d'essais est levé ; un compte désactivé le reste.
                    compte.LockoutEnd = null;
                    compte.AccessFailedCount = 0;
                }

                Exiger(await utilisateurs.UpdateAsync(compte));
                contexte.JournalComptes.Add(JournalCompte.Ecrire(compte.Id, ActionCompte.ReinitialisationMotDePasse, auteur, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
                return compte;
            },
            annulation);
        return await VersFiche(utilisateur, annulation);
    }

    /// <summary>L'utilisateur change son propre mot de passe ; un mot de passe provisoire cesse de l'être.</summary>
    public async Task ChangerMonMotDePasseAsync(string matricule, string? actuel, string? nouveau, CancellationToken annulation)
    {
        await uniteDeTravail.ExecuterAsync(
            async jeton =>
            {
                var compte = await utilisateurs.FindByNameAsync(matricule) ?? throw new ExceptionIdentite(CodesErreurIdentite.CompteIntrouvable);
                if (string.IsNullOrEmpty(actuel) || !await utilisateurs.CheckPasswordAsync(compte, actuel))
                {
                    throw new ExceptionIdentite(CodesErreurIdentite.MotDePasseActuelInvalide);
                }

                if (string.Equals(actuel, nouveau, StringComparison.Ordinal))
                {
                    throw new ExceptionIdentite(CodesErreurIdentite.MotDePasseIdentique);
                }

                await ExigerMotDePasseValide(compte, nouveau);
                Exiger(await utilisateurs.ChangePasswordAsync(compte, actuel, nouveau!));
                compte.MotDePasseProvisoire = false;
                Exiger(await utilisateurs.UpdateAsync(compte));
                contexte.JournalComptes.Add(JournalCompte.Ecrire(compte.Id, ActionCompte.ChangementMotDePasse, matricule, horloge.MaintenantUtc));
                await contexte.SaveChangesAsync(jeton);
                return compte;
            },
            annulation);
    }

    public async Task ChangerMaLangueAsync(string matricule, string? langue, CancellationToken annulation)
    {
        if (langue is null || !LanguesInterface.Toutes.Contains(langue))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.LangueInconnue);
        }

        var compte = await utilisateurs.FindByNameAsync(matricule) ?? throw new ExceptionIdentite(CodesErreurIdentite.CompteIntrouvable);
        compte.Langue = langue;
        Exiger(await utilisateurs.UpdateAsync(compte));
    }

    public async Task<CompteUtilisateur?> ProfilAsync(string matricule, CancellationToken annulation)
    {
        var compte = await contexte.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == matricule.ToUpperInvariant(), annulation);
        return compte is null ? null : VersCompte(compte, [.. await utilisateurs.GetRolesAsync(compte)]);
    }

    // --- Règles ----------------------------------------------------------------------------------------------------

    private static (string Prenom, string Nom, string? Telephone, List<string> Roles, string Langue) Controler(SaisieFiche saisie)
    {
        var prenom = saisie.Prenom?.Trim() ?? "";
        var nom = saisie.Nom?.Trim() ?? "";
        if (prenom.Length == 0 || nom.Length == 0)
        {
            throw new ExceptionIdentite(CodesErreurIdentite.NomObligatoire);
        }

        if (prenom.Length > Utilisateur.LongueurMaxNom || nom.Length > Utilisateur.LongueurMaxNom)
        {
            throw new ExceptionIdentite(CodesErreurIdentite.NomTropLong);
        }

        var telephone = saisie.Telephone?.Trim();
        if (!string.IsNullOrEmpty(telephone) && !FormeTelephone().IsMatch(telephone))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.TelephoneInvalide);
        }

        var roles = (saisie.Roles ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (roles.Count == 0)
        {
            throw new ExceptionIdentite(CodesErreurIdentite.RoleObligatoire);
        }

        if (roles.Any(r => !Roles.Tous.Contains(r, StringComparer.Ordinal)))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.RoleInconnu);
        }

        var langue = string.IsNullOrWhiteSpace(saisie.Langue) ? LanguesInterface.Francais : saisie.Langue.Trim();
        if (!LanguesInterface.Toutes.Contains(langue))
        {
            throw new ExceptionIdentite(CodesErreurIdentite.LangueInconnue);
        }

        return (prenom, nom, string.IsNullOrEmpty(telephone) ? null : telephone, roles, langue);
    }

    /// <summary>
    /// Il reste toujours un Administrateur actif, sinon plus personne ne peut gérer les comptes. Un verrou de transaction
    /// sérialise ce contrôle entre deux opérations simultanées.
    /// </summary>
    private async Task ExigerAutreAdministrateurActif(Guid compteId, CancellationToken annulation)
    {
        await contexte.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('identite.administrateurs'))", annulation);
        var roleAdministrateur = IdentiteDbContext.IdentifiantsRoles[Roles.Administrateur];
        var autres = await contexte.UserRoles
            .Where(ur => ur.RoleId == roleAdministrateur && ur.UserId != compteId)
            .Join(contexte.Users, ur => ur.UserId, u => u.Id, (ur, u) => u)
            .CountAsync(u => u.Actif, annulation);
        if (autres == 0)
        {
            throw new ExceptionIdentite(CodesErreurIdentite.DernierAdministrateur);
        }
    }

    private async Task ExigerMotDePasseValide(Utilisateur compte, string? motDePasse)
    {
        foreach (var validateur in utilisateurs.PasswordValidators)
        {
            var resultat = await validateur.ValidateAsync(utilisateurs, compte, motDePasse ?? "");
            if (!resultat.Succeeded)
            {
                throw new ExceptionIdentite(CodesErreurIdentite.MotDePasseRefuse, string.Join(" ", resultat.Errors.Select(e => e.Code)));
            }
        }
    }

    /// <summary>Prochain matricule libre de la forme S000001 (un matricule saisi peut déjà l'occuper).</summary>
    private async Task<string> MatriculeGenere(CancellationToken annulation)
    {
        while (true)
        {
            var numero = await contexte.Database.SqlQuery<long>($"SELECT nextval('identite.numero_matricule') AS \"Value\"").SingleAsync(annulation);
            var matricule = $"S{numero:D6}";
            if (await utilisateurs.FindByNameAsync(matricule) is null)
            {
                return matricule;
            }
        }
    }

    private async Task<Utilisateur> Trouver(Guid id) =>
        await utilisateurs.FindByIdAsync(id.ToString()) ?? throw new ExceptionIdentite(CodesErreurIdentite.CompteIntrouvable);

    private static void Exiger(IdentityResult resultat)
    {
        if (resultat.Succeeded)
        {
            return;
        }

        var code = resultat.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
            ? CodesErreurIdentite.MotDePasseRefuse
            : resultat.Errors.Any(e => e.Code is "DuplicateUserName" or "InvalidUserName")
                ? CodesErreurIdentite.IdentifiantDejaUtilise
                : throw new InvalidOperationException($"Opération sur le compte refusée : {string.Join(", ", resultat.Errors.Select(e => e.Code))}");
        throw new ExceptionIdentite(code, string.Join(" ", resultat.Errors.Select(e => e.Code)));
    }

    private async Task<Dictionary<Guid, List<string>>> RolesParUtilisateur(CancellationToken annulation) =>
        (await contexte.UserRoles
            .Join(contexte.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(annulation))
        .GroupBy(r => r.UserId)
        .ToDictionary(g => g.Key, g => g.Select(r => r.Name!).Order(StringComparer.Ordinal).ToList());

    private async Task<FicheCompte> VersFiche(Utilisateur utilisateur, CancellationToken annulation)
    {
        var roles = (await RolesParUtilisateur(annulation)).GetValueOrDefault(utilisateur.Id) ?? [];
        var journal = await contexte.JournalComptes.AsNoTracking()
            .Where(j => j.UtilisateurId == utilisateur.Id)
            .OrderByDescending(j => j.DateUtc)
            .Select(j => new LigneJournalCompte(j.DateUtc, j.Action, j.Auteur, j.Detail))
            .ToListAsync(annulation);
        return new FicheCompte(VersCompte(utilisateur, roles), journal);
    }

    private static CompteUtilisateur VersCompte(Utilisateur u, IReadOnlyList<string> roles) =>
        new(u.Id, u.UserName!, u.Prenom, u.Nom, u.NomComplet, u.PhoneNumber, roles, u.Langue, u.Actif, u.MotDePasseProvisoire);

    // Matricule : lettres, chiffres, point, tiret, souligné (caractères admis par Identity pour un identifiant).
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,20}$")]
    private static partial Regex FormeMatricule();

    // Téléphone : chiffres, espaces et « + » initial, 8 à 20 caractères (ex. +237 699 12 34 56).
    [GeneratedRegex(@"^\+?[0-9 ]{8,19}$")]
    private static partial Regex FormeTelephone();
}
