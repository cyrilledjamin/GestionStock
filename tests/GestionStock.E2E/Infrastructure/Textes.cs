using System.Globalization;
using System.Xml.Linq;

namespace GestionStock.E2E.Infrastructure;

/// <summary>
/// Textes de l'interface lus dans les fichiers de ressources du client (copiés à côté des tests) : un parcours cherche un
/// bouton par sa clé (« Valider »), pas par son libellé, et tourne aussi bien en français qu'en anglais.
/// </summary>
public sealed class Textes
{
    private static readonly Lazy<Textes> _francais = new(() => new Textes("Textes.resx", "fr"));
    private static readonly Lazy<Textes> _anglais = new(() => new Textes("Textes.en.resx", "en"));

    private readonly Dictionary<string, string> _valeurs;

    private Textes(string fichier, string langue)
    {
        Langue = langue;
        var chemin = Path.Combine(AppContext.BaseDirectory, "Ressources", fichier);
        _valeurs = XDocument.Load(chemin).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    /// <summary>« fr » ou « en ».</summary>
    public string Langue { get; }

    public static Textes Pour(string langue) => langue == "en" ? _anglais.Value : _francais.Value;

    /// <summary>Le texte d'une clé, avec ses arguments ({0}, {1}…) comme dans le client.</summary>
    public string this[string cle, params object[] arguments] => _valeurs.TryGetValue(cle, out var valeur)
        ? arguments.Length == 0 ? valeur : string.Format(CultureInfo.GetCultureInfo(Langue), valeur, arguments)
        : throw new KeyNotFoundException($"Clé de texte inconnue : {cle}");
}
