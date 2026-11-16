using System.Globalization;
using System.Reflection;
using GestionStock.Identite.Application;
using GestionStock.Identite.Contracts;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Identite : permissions attribuées, messages traduits.</summary>
public sealed class ConventionsIdentiteTests
{
    public static TheoryData<string> PermissionsDeclarees() => [.. Constantes(typeof(Permissions))];

    public static TheoryData<string> CodesErreur() => [.. Constantes(typeof(CodesErreurIdentite))];

    [Theory]
    [MemberData(nameof(PermissionsDeclarees))]
    public void Chaque_permission_est_attribuee_a_au_moins_un_role_connu(string permission)
    {
        Assert.True(MatricePermissions.RolesParPermission.TryGetValue(permission, out var roles), $"{permission} n'est attribuée à aucun rôle.");
        Assert.NotEmpty(roles);
        Assert.All(roles, role => Assert.Contains(role, Roles.Tous));
    }

    [Fact]
    public void La_matrice_ne_contient_que_des_permissions_declarees()
    {
        var declarees = Constantes(typeof(Permissions)).ToHashSet(StringComparer.Ordinal);

        Assert.All(MatricePermissions.RolesParPermission.Keys, permission => Assert.Contains(permission, declarees));
    }

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = CodesErreurIdentite.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = CodesErreurIdentite.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }

    private static IEnumerable<string> Constantes(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
