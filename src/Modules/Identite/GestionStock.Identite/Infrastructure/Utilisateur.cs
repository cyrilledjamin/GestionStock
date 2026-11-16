using Microsoft.AspNetCore.Identity;

namespace GestionStock.Identite.Infrastructure;

/// <summary>
/// Compte nominatif d'un membre du personnel (ENF06). L'identifiant de connexion est son <b>matricule</b>
/// (<see cref="IdentityUser{TKey}.UserName"/>), tracé dans tous les documents (ENF03) : il ne change plus après la
/// création. Données personnelles limitées au prénom, au nom et au téléphone (<see cref="IdentityUser{TKey}.PhoneNumber"/>),
/// conformément à la loi n° 2024/017.
/// </summary>
internal sealed class Utilisateur : IdentityUser<Guid>
{
    public const int LongueurMaxNom = 100;
    public const int LongueurMaxTelephone = 20;
    public const int LongueurMaxMatricule = 20;

    public Utilisateur()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public string Prenom { get; set; } = "";

    public string Nom { get; set; } = "";

    /// <summary>Langue de l'interface (fr ou en, ENF09), appliquée à la connexion.</summary>
    public string Langue { get; set; } = LanguesInterface.Francais;

    /// <summary>Un compte désactivé (départ d'un employé) n'est jamais supprimé : ses documents restent tracés.</summary>
    public bool Actif { get; set; } = true;

    /// <summary>Mot de passe fixé par l'Administrateur : l'utilisateur doit le changer avant toute autre action.</summary>
    public bool MotDePasseProvisoire { get; set; }

    public string NomComplet => $"{Prenom} {Nom}".Trim();
}

internal static class LanguesInterface
{
    public const string Francais = "fr";
    public const string Anglais = "en";

    public static IReadOnlyList<string> Toutes { get; } = [Francais, Anglais];
}

/// <summary>Opération journalisée sur un compte (ENF03 : utilisateur et horodatage).</summary>
internal enum ActionCompte
{
    Creation = 1,
    Modification = 2,
    Desactivation = 3,
    Reactivation = 4,
    ReinitialisationMotDePasse = 5,
    ChangementMotDePasse = 6,
}

/// <summary>Journal immuable des opérations sur les comptes ; aucun mot de passe n'y figure.</summary>
internal sealed class JournalCompte
{
    public const int LongueurMaxDetail = 500;

    private JournalCompte()
    {
    }

    public Guid Id { get; private set; }

    public Guid UtilisateurId { get; private set; }

    public DateTime DateUtc { get; private set; }

    public ActionCompte Action { get; private set; }

    /// <summary>Matricule de l'auteur (Administrateur, l'utilisateur lui-même, ou « systeme » au premier démarrage).</summary>
    public string Auteur { get; private set; } = "";

    public string? Detail { get; private set; }

    public static JournalCompte Ecrire(Guid utilisateurId, ActionCompte action, string auteur, DateTime maintenantUtc, string? detail = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UtilisateurId = utilisateurId,
        DateUtc = maintenantUtc,
        Action = action,
        Auteur = auteur,
        Detail = detail is { Length: > LongueurMaxDetail } ? detail[..LongueurMaxDetail] : detail,
    };
}
