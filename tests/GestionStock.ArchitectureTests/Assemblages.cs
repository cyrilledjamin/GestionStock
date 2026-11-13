using System.Reflection;

namespace GestionStock.ArchitectureTests;

/// <summary>Rôle d'un assemblage de la solution, déduit de son nom.</summary>
internal enum RoleAssemblage
{
    Hote,
    SharedKernel,
    Infrastructure,
    Contrat,
    Domaine,
    ImplementationModule,
}

/// <summary>Un assemblage <c>GestionStock.*</c> de la solution.</summary>
internal sealed record AssemblageSolution(Assembly Assemblage, RoleAssemblage Role, string? Module)
{
    public string Nom => Assemblage.GetName().Name!;

    public override string ToString() => Nom;
}

/// <summary>
/// Découverte des assemblages de la solution à partir de l'hôte : chaque nouveau module branché sur
/// l'hôte est couvert par les règles sans modifier les tests.
/// </summary>
internal static class Assemblages
{
    public const string Prefixe = "GestionStock.";
    public const string NomHote = "GestionStock.Api";
    public const string NomSharedKernel = "GestionStock.SharedKernel";
    public const string NomInfrastructure = "GestionStock.Infrastructure";
    private const string SuffixeContrat = ".Contracts";
    private const string SuffixeDomaine = ".Domain";

    private static readonly Lazy<IReadOnlyList<AssemblageSolution>> _tous = new(Decouvrir);

    public static IReadOnlyList<AssemblageSolution> Tous => _tous.Value;

    public static AssemblageSolution Hote => Tous.Single(a => a.Role == RoleAssemblage.Hote);

    public static IEnumerable<AssemblageSolution> AvecRole(RoleAssemblage role) => Tous.Where(a => a.Role == role);

    public static AssemblageSolution? Trouver(string nom) => Tous.SingleOrDefault(a => a.Nom == nom);

    /// <summary>Noms des assemblages <c>GestionStock.*</c> effectivement utilisés par l'assemblage.</summary>
    public static IEnumerable<string> ReferencesSolution(this AssemblageSolution assemblage) =>
        assemblage.Assemblage.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith(Prefixe, StringComparison.Ordinal));

    /// <summary>Noms de tous les assemblages effectivement utilisés par l'assemblage.</summary>
    public static IEnumerable<string> References(this AssemblageSolution assemblage) =>
        assemblage.Assemblage.GetReferencedAssemblies().Select(r => r.Name!);

    /// <summary>Assemblage de la bibliothèque de base .NET (C# pur).</summary>
    public static bool EstBibliothequeDeBase(string nom) =>
        nom is "netstandard" or "mscorlib" || nom == "System" || nom.StartsWith("System.", StringComparison.Ordinal);

    private static List<AssemblageSolution> Decouvrir()
    {
        var trouves = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var aVisiter = new Queue<Assembly>([Assembly.Load(NomHote)]);

        while (aVisiter.TryDequeue(out var assemblage))
        {
            if (!trouves.TryAdd(assemblage.GetName().Name!, assemblage))
            {
                continue;
            }

            foreach (var reference in assemblage.GetReferencedAssemblies())
            {
                if (reference.Name!.StartsWith(Prefixe, StringComparison.Ordinal) && !trouves.ContainsKey(reference.Name))
                {
                    aVisiter.Enqueue(Assembly.Load(reference));
                }
            }
        }

        return [.. trouves.Values.Select(Classer).OrderBy(a => a.Nom, StringComparer.Ordinal)];
    }

    private static AssemblageSolution Classer(Assembly assemblage)
    {
        var nom = assemblage.GetName().Name!;
        return nom switch
        {
            NomHote => new(assemblage, RoleAssemblage.Hote, null),
            NomSharedKernel => new(assemblage, RoleAssemblage.SharedKernel, null),
            NomInfrastructure => new(assemblage, RoleAssemblage.Infrastructure, null),
            _ when nom.EndsWith(SuffixeContrat, StringComparison.Ordinal) =>
                new(assemblage, RoleAssemblage.Contrat, NomModule(nom[..^SuffixeContrat.Length])),
            _ when nom.EndsWith(SuffixeDomaine, StringComparison.Ordinal) =>
                new(assemblage, RoleAssemblage.Domaine, NomModule(nom[..^SuffixeDomaine.Length])),
            _ => new(assemblage, RoleAssemblage.ImplementationModule, NomModule(nom)),
        };
    }

    /// <summary><c>GestionStock.Stock</c> → <c>Stock</c>.</summary>
    private static string NomModule(string nomSansSuffixe) => nomSansSuffixe[Prefixe.Length..];
}
