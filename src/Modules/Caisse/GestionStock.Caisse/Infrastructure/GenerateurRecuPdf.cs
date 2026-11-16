using System.Globalization;
using System.Resources;
using GestionStock.Caisse.Contracts;

namespace GestionStock.Caisse.Infrastructure;

/// <summary>
/// Reçu PDF au format ticket 80 mm (décision 26), bilingue (ENF09) : montants arrondis à l'unité en XAF (RG32),
/// heure de Douala, détail HT / TVA / TTC si l'entreprise est assujettie, prix nets sinon (RG37).
/// </summary>
internal sealed class GenerateurRecuPdf : IGenerateurRecu
{
    public static ResourceManager Textes { get; } =
        new("GestionStock.Caisse.Ressources.TextesRecu", typeof(GenerateurRecuPdf).Assembly);

    private static readonly TimeZoneInfo Douala = TrouverDouala();

    public byte[] GenererPdf(Recu recu, string langue)
    {
        ArgumentNullException.ThrowIfNull(recu);
        var culture = CultureInfo.GetCultureInfo(langue == "en" ? "en-GB" : "fr-FR");
        string T(string cle) => Textes.GetString(cle, culture) ?? cle;
        string Montant(decimal valeur) => decimal.Round(valeur, 0, MidpointRounding.AwayFromZero).ToString("N0", culture);
        string Quantite(decimal valeur) => valeur.ToString("#,##0.###", culture);

        var tiret = new string('-', PdfTicket.Colonnes);
        var lignes = new List<LigneImprimee> { new(Centrer(recu.Entreprise), Gras: true) };
        foreach (var info in new[] { recu.Adresse, recu.Telephone is null ? null : $"{T("Recu.Telephone")} {recu.Telephone}", recu.NumeroContribuable is null ? null : $"{T("Recu.Niu")} {recu.NumeroContribuable}" })
        {
            if (!string.IsNullOrWhiteSpace(info))
            {
                lignes.Add(new(Centrer(info)));
            }
        }

        lignes.Add(new(tiret));
        lignes.Add(new($"{T(recu.EstAvoir ? "Recu.Avoir" : "Recu.Ticket")} {recu.NumeroTicket}", Gras: true));
        if (recu.NumeroTicketOrigine is not null)
        {
            lignes.Add(new($"{T("Recu.AvoirSur")} {recu.NumeroTicketOrigine}"));
        }

        lignes.Add(new(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(recu.DateUtc, DateTimeKind.Utc), Douala).ToString("g", culture)));
        lignes.Add(new($"{T("Recu.Caissier")} {recu.Caissier}"));
        if (recu.Client is not null)
        {
            lignes.Add(new($"{T("Recu.Client")} {recu.Client}"));
        }

        lignes.Add(new(tiret));
        foreach (var ligne in recu.Lignes)
        {
            foreach (var morceau in Couper(ligne.Libelle))
            {
                lignes.Add(new(morceau));
            }

            lignes.Add(new(Aligner($"  {Quantite(ligne.Quantite)} x {Montant(ligne.PrixUnitaire)}", Montant(ligne.Montant + ligne.Remise))));
            if (ligne.Remise > 0m)
            {
                lignes.Add(new(Aligner($"  {T("Recu.Remise")}", $"-{Montant(ligne.Remise)}")));
            }
        }

        lignes.Add(new(tiret));
        lignes.Add(new(Aligner(T("Recu.Total"), $"{Montant(recu.TotalTtc)} XAF"), Gras: true));
        if (recu.AssujettiTva)
        {
            lignes.Add(new(Aligner(T("Recu.TotalHt"), Montant(recu.TotalHt))));
            lignes.Add(new(Aligner($"{T("Recu.Tva")} {recu.TauxTva.ToString("0.##", culture)} %", Montant(recu.TotalTva))));
        }
        else
        {
            lignes.Add(new(T("Recu.PrixNets")));
        }

        foreach (var paiement in recu.Paiements)
        {
            lignes.Add(new(Aligner(T($"Recu.Mode.{paiement.Mode}"), Montant(paiement.Montant))));
            if (!string.IsNullOrWhiteSpace(paiement.Reference))
            {
                lignes.Add(new($"  {T("Recu.Reference")} {paiement.Reference}"));
            }
        }

        if (recu.Rendu > 0m)
        {
            lignes.Add(new(Aligner(T("Recu.Rendu"), Montant(recu.Rendu)), Gras: true));
        }

        lignes.Add(new(tiret));
        lignes.Add(new(Centrer(T("Recu.Merci"))));
        return PdfTicket.Ecrire(lignes);
    }

    private static string Centrer(string texte)
    {
        var court = texte.Length > PdfTicket.Colonnes ? texte[..PdfTicket.Colonnes] : texte;
        return new string(' ', (PdfTicket.Colonnes - court.Length) / 2) + court;
    }

    private static string Aligner(string gauche, string droite)
    {
        var espace = PdfTicket.Colonnes - droite.Length - 1;
        var debut = gauche.Length > espace ? gauche[..espace] : gauche;
        return debut.PadRight(espace + 1) + droite;
    }

    private static IEnumerable<string> Couper(string texte)
    {
        for (var debut = 0; debut < texte.Length; debut += PdfTicket.Colonnes)
        {
            yield return texte.Substring(debut, Math.Min(PdfTicket.Colonnes, texte.Length - debut));
        }
    }

    private static TimeZoneInfo TrouverDouala()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Douala");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Africa/Douala", TimeSpan.FromHours(1), "Douala", "WAT");
        }
    }
}
