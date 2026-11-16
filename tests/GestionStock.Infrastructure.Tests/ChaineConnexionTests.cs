using GestionStock.Infrastructure.Persistance;
using Npgsql;

namespace GestionStock.Infrastructure.Tests;

public sealed class ChaineConnexionTests
{
    private const string Base = "Host=localhost;Port=5432;Database=gestionstock;Username=gestionstock;Password=secret";

    [Fact]
    public void Le_chiffrement_GSS_est_desactive_par_defaut()
    {
        var resultat = new NpgsqlConnectionStringBuilder(ChaineConnexion.Preparer(Base));

        Assert.Equal(GssEncryptionMode.Disable, resultat.GssEncryptionMode);
    }

    [Theory]
    [InlineData("GSS Encryption Mode=Prefer")]
    [InlineData("gssencryptionmode=prefer")]
    public void Un_mode_GSS_explicite_dans_la_configuration_est_conserve(string option)
    {
        var resultat = new NpgsqlConnectionStringBuilder(ChaineConnexion.Preparer($"{Base};{option}"));

        Assert.Equal(GssEncryptionMode.Prefer, resultat.GssEncryptionMode);
    }

    [Fact]
    public void Les_autres_parametres_sont_inchanges()
    {
        var resultat = new NpgsqlConnectionStringBuilder(ChaineConnexion.Preparer(Base));

        Assert.Equal("localhost", resultat.Host);
        Assert.Equal(5432, resultat.Port);
        Assert.Equal("gestionstock", resultat.Database);
        Assert.Equal("gestionstock", resultat.Username);
        Assert.Equal("secret", resultat.Password);
    }
}
