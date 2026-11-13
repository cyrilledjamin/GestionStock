using NetArchTest.Rules;

namespace GestionStock.ArchitectureTests;

/// <summary>Règles sur les types (visibilité, couches internes d'un module), vérifiées avec NetArchTest.</summary>
public sealed class ReglesTypesTests
{
    /// <summary>Espace de noms des migrations EF Core : code généré, public par construction.</summary>
    private const string SuffixeMigrations = ".Migrations";

    private static readonly string[] DependancesTechniques =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "GestionStock.Infrastructure",
    ];

    public static TheoryData<string> Implementations() =>
        [.. Assemblages.AvecRole(RoleAssemblage.ImplementationModule).Select(a => a.Nom)];

    [Theory]
    [MemberData(nameof(Implementations))]
    public void Une_implementation_de_module_n_expose_que_son_point_d_entree(string nom)
    {
        var module = Assemblages.Trouver(nom)!;

        var resultat = Types.InAssembly(module.Assemblage)
            .That().ArePublic()
            .And().DoNotResideInNamespaceEndingWith(SuffixeMigrations)
            .Should().HaveName($"Module{module.Module}")
            .GetResult();

        AssertReussi(resultat, $"seul Module{module.Module} est public, le reste est internal");
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void Le_domaine_interne_d_un_module_ne_depend_d_aucune_technique(string nom)
    {
        var module = Assemblages.Trouver(nom)!;

        var resultat = Types.InAssembly(module.Assemblage)
            .That().ResideInNamespace($"{module.Nom}.Domain")
            .ShouldNot().HaveDependencyOnAny([.. DependancesTechniques, $"{module.Nom}.Application", $"{module.Nom}.Endpoints"])
            .GetResult();

        AssertReussi(resultat, "Domain ne dépend ni d'EF Core, ni d'ASP.NET Core, ni des autres couches");
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void L_application_et_l_infrastructure_ne_dependent_pas_des_endpoints(string nom)
    {
        var module = Assemblages.Trouver(nom)!;

        var resultat = Types.InAssembly(module.Assemblage)
            .That().ResideInNamespace($"{module.Nom}.Application")
            .Or().ResideInNamespace($"{module.Nom}.Infrastructure")
            .ShouldNot().HaveDependencyOn($"{module.Nom}.Endpoints")
            .GetResult();

        AssertReussi(resultat, "Endpoints appelle Application, jamais l'inverse");
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void Les_endpoints_n_accedent_pas_directement_a_la_persistance(string nom)
    {
        var module = Assemblages.Trouver(nom)!;

        var resultat = Types.InAssembly(module.Assemblage)
            .That().ResideInNamespace($"{module.Nom}.Endpoints")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", $"{module.Nom}.Infrastructure")
            .GetResult();

        AssertReussi(resultat, "Endpoints passe par Application ou par le contrat");
    }

    [Fact]
    public void L_hote_ne_depend_d_aucune_technique_de_persistance()
    {
        var resultat = Types.InAssembly(Assemblages.Hote.Assemblage)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        AssertReussi(resultat, "l'hôte ne fait qu'assembler les modules");
    }

    private static void AssertReussi(NetArchTest.Rules.TestResult resultat, string regle)
    {
        var fautifs = resultat.FailingTypeNames ?? [];
        Assert.True(resultat.IsSuccessful, $"Règle non respectée ({regle}) : {string.Join(", ", fautifs)}");
    }
}
