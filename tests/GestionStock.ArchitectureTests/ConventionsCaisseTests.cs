using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using GestionStock.Caisse.Domain;
using GestionStock.Caisse.Endpoints;
using GestionStock.Caisse.Infrastructure;

namespace GestionStock.ArchitectureTests;

/// <summary>Conventions du module Caisse vérifiables sans base de données.</summary>
public sealed class ConventionsCaisseTests
{
    public static TheoryData<string> CodesErreur() =>
    [
        .. typeof(CodesErreurCaisse)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    [Theory]
    [MemberData(nameof(CodesErreur))]
    public void Chaque_code_d_erreur_est_traduit_en_francais_et_en_anglais(string code)
    {
        var francais = EndpointsCaisse.Ressources.GetString(code, CultureInfo.GetCultureInfo("fr"));
        var anglais = EndpointsCaisse.Ressources.GetString(code, CultureInfo.GetCultureInfo("en"));

        Assert.False(string.IsNullOrWhiteSpace(francais), $"Traduction française absente pour {code}.");
        Assert.False(string.IsNullOrWhiteSpace(anglais), $"Traduction anglaise absente pour {code}.");
        Assert.NotEqual(francais, anglais);
    }

    [Fact]
    public void Chaque_mode_de_paiement_a_son_libelle_sur_le_recu()
    {
        Assert.All(Enum.GetNames<ModePaiement>(), mode =>
        {
            Assert.False(string.IsNullOrWhiteSpace(GenerateurRecuPdf.Textes.GetString($"Recu.Mode.{mode}", CultureInfo.GetCultureInfo("fr"))));
            Assert.False(string.IsNullOrWhiteSpace(GenerateurRecuPdf.Textes.GetString($"Recu.Mode.{mode}", CultureInfo.GetCultureInfo("en"))));
        });
    }

    [Fact]
    public void Le_PDF_du_ticket_a_une_table_de_references_exacte()
    {
        var pdf = PdfTicket.Ecrire([new LigneImprimee("Lait concentré (boîte)"), new LigneImprimee("TOTAL  12 000 XAF", Gras: true)]);
        var texte = System.Text.Encoding.Latin1.GetString(pdf);

        var debutXref = long.Parse(Regex.Match(texte, @"startxref\n(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", texte[(int)debutXref..], StringComparison.Ordinal);
        foreach (Match entree in Regex.Matches(texte, @"(\d{10}) 00000 n"))
        {
            var position = int.Parse(entree.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Matches(@"^\d+ 0 obj", texte[position..]);
        }

        Assert.Contains("(Lait concentr\u00E9 \\(bo\u00EEte\\)) Tj", texte, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Recu.Ticket")]
    [InlineData("Recu.Avoir")]
    [InlineData("Recu.AvoirSur")]
    [InlineData("Recu.Caissier")]
    [InlineData("Recu.Client")]
    [InlineData("Recu.Telephone")]
    [InlineData("Recu.Niu")]
    [InlineData("Recu.Remise")]
    [InlineData("Recu.Total")]
    [InlineData("Recu.TotalHt")]
    [InlineData("Recu.Tva")]
    [InlineData("Recu.PrixNets")]
    [InlineData("Recu.Reference")]
    [InlineData("Recu.Rendu")]
    [InlineData("Recu.Merci")]
    public void Chaque_texte_du_recu_existe_en_francais_et_en_anglais(string cle)
    {
        Assert.False(string.IsNullOrWhiteSpace(GenerateurRecuPdf.Textes.GetString(cle, CultureInfo.GetCultureInfo("fr"))));
        Assert.False(string.IsNullOrWhiteSpace(GenerateurRecuPdf.Textes.GetString(cle, CultureInfo.GetCultureInfo("en"))));
    }
}
