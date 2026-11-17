using System.Runtime.CompilerServices;

namespace GestionStock.E2E.Infrastructure;

/// <summary>Un parcours de bout en bout : ignoré sans application déployée (E2E_URL).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ParcoursAttribute : FactAttribute
{
    public ParcoursAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = "E2E_URL non défini : aucune application déployée à tester.";
        SkipUnless = nameof(Environnement.Actif);
        SkipType = typeof(Environnement);
        Timeout = 180_000;
    }
}
