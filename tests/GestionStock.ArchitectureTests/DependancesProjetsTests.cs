namespace GestionStock.ArchitectureTests;

/// <summary>
/// Règles de dépendance entre projets, vérifiées sur les références réellement compilées
/// (une référence de projet inutilisée n'apparaît pas, une dépendance utilisée apparaît toujours).
/// </summary>
public sealed class DependancesProjetsTests
{
    public static TheoryData<string> Contrats() => Noms(RoleAssemblage.Contrat);

    public static TheoryData<string> Domaines() => Noms(RoleAssemblage.Domaine);

    public static TheoryData<string> Implementations() => Noms(RoleAssemblage.ImplementationModule);

    [Fact]
    public void La_decouverte_trouve_les_briques_et_les_modules()
    {
        string[] attendus =
        [
            Assemblages.NomHote,
            Assemblages.NomSharedKernel,
            Assemblages.NomInfrastructure,
            "GestionStock.Achats",
            "GestionStock.Achats.Contracts",
            "GestionStock.Credit",
            "GestionStock.Credit.Contracts",
            "GestionStock.Identite",
            "GestionStock.Identite.Contracts",
            "GestionStock.Prix",
            "GestionStock.Prix.Contracts",
            "GestionStock.Referentiel",
            "GestionStock.Referentiel.Contracts",
            "GestionStock.Stock",
            "GestionStock.Stock.Contracts",
            "GestionStock.Stock.Domain",
        ];

        Assert.All(attendus, nom => Assert.NotNull(Assemblages.Trouver(nom)));
    }

    [Fact]
    public void Chaque_module_a_un_projet_Contracts()
    {
        var contrats = Assemblages.AvecRole(RoleAssemblage.Contrat).Select(a => a.Module).ToHashSet();

        Assert.All(Assemblages.AvecRole(RoleAssemblage.ImplementationModule), module =>
            Assert.True(contrats.Contains(module.Module), $"{module} n'a pas de projet GestionStock.{module.Module}.Contracts."));
    }

    [Fact]
    public void Le_SharedKernel_est_en_CSharp_pur()
    {
        AssertSeulementBibliothequeDeBase(Assemblages.Trouver(Assemblages.NomSharedKernel)!);
    }

    [Theory]
    [MemberData(nameof(Domaines))]
    public void Un_domaine_est_en_CSharp_pur_sans_aucune_dependance(string nom)
    {
        AssertSeulementBibliothequeDeBase(Assemblages.Trouver(nom)!);
    }

    [Theory]
    [MemberData(nameof(Contrats))]
    public void Un_contrat_ne_depend_que_de_la_bibliotheque_de_base_et_du_SharedKernel(string nom)
    {
        var contrat = Assemblages.Trouver(nom)!;

        var interdites = contrat.References()
            .Where(r => !Assemblages.EstBibliothequeDeBase(r) && r != Assemblages.NomSharedKernel);

        Assert.Empty(interdites);
    }

    [Fact]
    public void L_infrastructure_ne_depend_d_aucun_module()
    {
        var infrastructure = Assemblages.Trouver(Assemblages.NomInfrastructure)!;

        var interdites = infrastructure.ReferencesSolution().Where(r => r != Assemblages.NomSharedKernel);

        Assert.Empty(interdites);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void Un_module_n_en_reference_un_autre_que_par_son_projet_Contracts(string nom)
    {
        var module = Assemblages.Trouver(nom)!;
        var autorisees = new HashSet<string>(StringComparer.Ordinal)
        {
            Assemblages.NomSharedKernel,
            Assemblages.NomInfrastructure,
            $"{Assemblages.Prefixe}{module.Module}.Contracts",
            $"{Assemblages.Prefixe}{module.Module}.Domain",
        };
        autorisees.UnionWith(Assemblages.AvecRole(RoleAssemblage.Contrat).Select(c => c.Nom));

        var interdites = module.ReferencesSolution().Where(r => !autorisees.Contains(r));

        Assert.Empty(interdites);
    }

    [Fact]
    public void L_hote_n_accede_pas_aux_domaines_des_modules()
    {
        var domaines = Assemblages.AvecRole(RoleAssemblage.Domaine).Select(d => d.Nom).ToHashSet();

        var interdites = Assemblages.Hote.ReferencesSolution().Where(domaines.Contains);

        Assert.Empty(interdites);
    }

    private static void AssertSeulementBibliothequeDeBase(AssemblageSolution assemblage)
    {
        var interdites = assemblage.References().Where(r => !Assemblages.EstBibliothequeDeBase(r));

        Assert.Empty(interdites);
    }

    private static TheoryData<string> Noms(RoleAssemblage role) => [.. Assemblages.AvecRole(role).Select(a => a.Nom)];
}
