using Npgsql;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>Préparation de la chaîne de connexion PostgreSQL lue dans la configuration.</summary>
internal static class ChaineConnexion
{
    private const string CleChiffrementGss = "GSS Encryption Mode";

    /// <summary>
    /// Désactive le chiffrement GSS (Kerberos) sauf si la configuration le demande explicitement.
    /// Par défaut, Npgsql tente une négociation GSS qui exige libgssapi_krb5, absente de l'image Linux
    /// de l'API : la tentative échoue et pollue les logs à chaque connexion. L'authentification se fait
    /// par mot de passe sur un serveur local ; Kerberos n'est pas utilisé.
    /// </summary>
    public static string Preparer(string chaine)
    {
        var constructeur = new NpgsqlConnectionStringBuilder(chaine);
        if (!ContientCle(chaine, CleChiffrementGss))
        {
            constructeur.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        return constructeur.ConnectionString;
    }

    /// <summary>
    /// Indique si la chaîne fournit la clé, quelle que soit sa graphie (« GSS Encryption Mode »,
    /// « gssencryptionmode »). <see cref="NpgsqlConnectionStringBuilder.ContainsKey"/> ne convient pas :
    /// il répond vrai pour toute option connue de Npgsql, même absente de la chaîne.
    /// </summary>
    private static bool ContientCle(string chaine, string cle)
    {
        var cles = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = chaine }.Keys;
        return cles.Cast<string>().Any(c => Normaliser(c) == Normaliser(cle));

        static string Normaliser(string valeur) => valeur.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }
}
