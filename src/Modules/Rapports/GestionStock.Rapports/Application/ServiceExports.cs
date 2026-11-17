using System.Data.Common;
using System.Globalization;
using System.Resources;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using GestionStock.Rapports.Contracts;
using GestionStock.SharedKernel;
using Npgsql;

namespace GestionStock.Rapports.Application;

/// <summary>Nature d'une colonne exportée : elle fixe le format Excel et CSV, et les totaux.</summary>
internal enum TypeColonne
{
    Texte,
    Date,
    Quantite,
    Montant,
}

internal sealed record Colonne(string Cle, TypeColonne Type);

/// <summary>Journal prêt à écrire : colonnes et lignes (valeurs dans l'ordre des colonnes).</summary>
internal sealed record JournalExporte(string Nom, IReadOnlyList<Colonne> Colonnes, IReadOnlyList<object?[]> Lignes);

/// <summary>Fichier produit : contenu, type MIME et nom proposé au téléchargement.</summary>
internal sealed record FichierExporte(byte[] Contenu, string TypeMime, string NomFichier);

internal static class FormatsExport
{
    public const string Excel = "xlsx";
    public const string Csv = "csv";
}

/// <summary>
/// Exports comptables (UC16, décision 7) pour le comptable externe : journaux des ventes, achats, encaissements et
/// créances sur une période, et valeur du stock du jour. Excel (ClosedXML) ou CSV au format d'un Excel français
/// (séparateur « ; », UTF-8 avec BOM). Montants arrondis à l'unité (RG32), dates à l'heure de Douala. Lecture seule des
/// vues <c>v_*</c>, comme les rapports.
/// </summary>
internal sealed class ServiceExports(NpgsqlDataSource sourceDeDonnees, IHorloge horloge)
{
    public static ResourceManager Textes { get; } =
        new("GestionStock.Rapports.Ressources.TextesExports", typeof(ServiceExports).Assembly);

    public DateOnly Aujourdhui => horloge.AujourdhuiDouala;

    public async Task<FichierExporte> ExporterAsync(string journal, Periode periode, string? format, CultureInfo culture, CancellationToken annulation)
    {
        var extension = (format ?? FormatsExport.Excel).ToLowerInvariant();
        if (extension is not (FormatsExport.Excel or FormatsExport.Csv))
        {
            throw new ExceptionRapports(CodesErreurRapports.FormatInvalide);
        }

        var donnees = await LireAsync(journal, periode, annulation);
        var nomFichier = journal == JournauxComptables.ValeurStock
            ? $"{journal}-{periode.Au:yyyy-MM-dd}.{extension}"
            : $"{journal}-{periode.Du:yyyy-MM-dd}-{periode.Au:yyyy-MM-dd}.{extension}";
        return extension == FormatsExport.Csv
            ? new FichierExporte(EcrireCsv(donnees, culture), "text/csv; charset=utf-8", nomFichier)
            : new FichierExporte(EcrireExcel(donnees, culture), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nomFichier);
    }

    /// <summary>Lit un journal ; <see cref="ExceptionRapports"/> si le nom n'est pas un journal connu.</summary>
    public async Task<JournalExporte> LireAsync(string journal, Periode periode, CancellationToken annulation)
    {
        var parametres = new { Debut = periode.DebutUtc, Fin = periode.FinUtc };
        if (journal == JournauxComptables.Ventes)
        {
            return await JournalAsync(journal, Ventes, SqlVentes, parametres, annulation);
        }

        if (journal == JournauxComptables.Achats)
        {
            return await JournalAsync(journal, Achats, SqlAchats, parametres, annulation);
        }

        if (journal == JournauxComptables.Encaissements)
        {
            return await JournalAsync(journal, Encaissements, SqlEncaissements, parametres, annulation);
        }

        if (journal == JournauxComptables.Creances)
        {
            return await JournalAsync(journal, Creances, SqlCreances, parametres, annulation);
        }

        if (journal == JournauxComptables.ValeurStock)
        {
            return await JournalAsync(journal, ValeurStock, SqlValeurStock, null, annulation);
        }

        throw new ExceptionRapports(CodesErreurRapports.JournalInconnu);
    }

    // --- Journaux --------------------------------------------------------------------------------------------------

    private static readonly Colonne[] Ventes =
    [
        new("Date", TypeColonne.Date), new("Piece", TypeColonne.Texte), new("Operation", TypeColonne.Texte),
        new("Caissier", TypeColonne.Texte), new("Client", TypeColonne.Texte), new("TicketOrigine", TypeColonne.Texte),
        new("Remise", TypeColonne.Montant), new("TotalHt", TypeColonne.Montant), new("Tva", TypeColonne.Montant),
        new("TotalTtc", TypeColonne.Montant),
    ];

    // Un avoir vient en négatif ; la remise est déjà déduite des totaux.
    private const string SqlVentes = """
        SELECT t.date_utc, t.numero_complet, CASE WHEN t.est_avoir THEN 'Avoir' ELSE 'Vente' END AS operation,
               COALESCE(uc.designation, t.caissier) AS caissier,
               c.nom AS client, o.numero_complet AS origine, t.signe * t.remise AS remise, t.signe * t.total_ht AS ht,
               t.signe * t.total_tva AS tva, t.signe * t.total_ttc AS ttc
        FROM caisse.v_tickets t
        LEFT JOIN credit.v_clients c ON c.client_id = t.client_id
        LEFT JOIN caisse.v_tickets o ON o.ticket_id = t.ticket_origine_id
        LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = t.caissier
        WHERE t.date_utc >= @Debut AND t.date_utc < @Fin
        ORDER BY t.date_utc, t.numero_complet
        """;

    private static readonly Colonne[] Achats =
    [
        new("Date", TypeColonne.Date), new("Piece", TypeColonne.Texte), new("Operation", TypeColonne.Texte),
        new("Fournisseur", TypeColonne.Texte), new("Libelle", TypeColonne.Texte), new("Mode", TypeColonne.Texte),
        new("Reference", TypeColonne.Texte), new("MontantDu", TypeColonne.Montant), new("MontantPaye", TypeColonne.Montant),
    ];

    // Réceptions validées (marchandise et frais saisis avant la validation), frais tardifs, paiements et annulations.
    private const string SqlAchats = """
        SELECT * FROM (
            SELECT r.date_validation_utc AS date_utc, r.numero, 'Reception' AS operation, r.fournisseur,
                   r.numero_bon_livraison AS libelle, NULL AS mode, NULL AS reference,
                   r.valeur_marchandise + COALESCE((SELECT sum(f.montant) FROM achats.v_frais f
                                                    WHERE f.reception_id = r.reception_id AND NOT f.apres_validation), 0) AS du,
                   0::numeric AS paye
            FROM achats.v_receptions r
            WHERE r.statut = 'Validee' AND r.date_validation_utc >= @Debut AND r.date_validation_utc < @Fin
            UNION ALL
            SELECT f.saisi_le_utc, f.numero, 'FraisTardif', r.fournisseur, f.type || COALESCE(' - ' || f.libelle, ''),
                   NULL, NULL, f.montant, 0
            FROM achats.v_frais f
            JOIN achats.v_receptions r ON r.reception_id = f.reception_id
            WHERE f.apres_validation AND f.saisi_le_utc >= @Debut AND f.saisi_le_utc < @Fin
            UNION ALL
            SELECT p.date_utc, p.numero, CASE WHEN p.paiement_annule_id IS NULL THEN 'Paiement' ELSE 'AnnulationPaiement' END,
                   p.fournisseur, p.motif, p.mode, p.reference, 0, p.montant
            FROM achats.v_paiements_fournisseurs p
            WHERE p.date_utc >= @Debut AND p.date_utc < @Fin
        ) journal
        ORDER BY date_utc, numero
        """;

    private static readonly Colonne[] Encaissements =
    [
        new("Date", TypeColonne.Date), new("Piece", TypeColonne.Texte), new("Operation", TypeColonne.Texte),
        new("Caissier", TypeColonne.Texte), new("Client", TypeColonne.Texte), new("Mode", TypeColonne.Texte),
        new("Reference", TypeColonne.Texte), new("Montant", TypeColonne.Montant),
    ];

    // Argent réellement reçu ou rendu : paiements des tickets hors crédit (espèces moins le rendu, avoirs en négatif)
    // et remboursements de crédit.
    private const string SqlEncaissements = """
        SELECT * FROM (
            SELECT p.date_utc, p.numero_complet AS piece, CASE WHEN p.est_avoir THEN 'Avoir' ELSE 'Vente' END AS operation,
                   COALESCE(uc.designation, p.caissier) AS caissier, c.nom AS client, p.mode, p.reference,
                   p.montant_encaisse AS montant
            FROM caisse.v_paiements p
            LEFT JOIN credit.v_clients c ON c.client_id = p.client_id
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = p.caissier
            WHERE p.mode <> 'Credit' AND p.date_utc >= @Debut AND p.date_utc < @Fin
            UNION ALL
            SELECT r.date_utc, NULL, 'RemboursementCredit', COALESCE(uc.designation, r.caissier), c.nom, r.mode,
                   r.reference, r.montant
            FROM caisse.v_remboursements_credit r
            JOIN credit.v_clients c ON c.client_id = r.client_id
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = r.caissier
            WHERE r.date_utc >= @Debut AND r.date_utc < @Fin
        ) journal
        ORDER BY date_utc, piece
        """;

    private static readonly Colonne[] Creances =
    [
        new("Date", TypeColonne.Date), new("Client", TypeColonne.Texte), new("Telephone", TypeColonne.Texte),
        new("Operation", TypeColonne.Texte), new("Piece", TypeColonne.Texte), new("Echeance", TypeColonne.Date),
        new("Utilisateur", TypeColonne.Texte), new("Montant", TypeColonne.Montant),
    ];

    // RG43 : journal des créances. Une vente à crédit augmente la créance ; un remboursement ou une annulation la réduit.
    private const string SqlCreances = """
        SELECT o.date_utc, c.nom, c.telephone, o.type, o.numero_ticket, o.echeance_utc, COALESCE(u.designation, o.utilisateur),
               CASE WHEN o.type = 'VenteCredit' THEN o.montant ELSE -o.montant END AS montant
        FROM credit.v_operations o
        JOIN credit.v_clients c ON c.client_id = o.client_id
        LEFT JOIN identite.v_utilisateurs u ON u.matricule = o.utilisateur
        WHERE o.date_utc >= @Debut AND o.date_utc < @Fin
        ORDER BY o.date_utc, c.nom
        """;

    private static readonly Colonne[] ValeurStock =
    [
        new("Code", TypeColonne.Texte), new("Libelle", TypeColonne.Texte), new("Unite", TypeColonne.Texte),
        new("Quantite", TypeColonne.Quantite), new("Cump", TypeColonne.Montant), new("Valeur", TypeColonne.Montant),
    ];

    private const string SqlValeurStock = """
        SELECT a.code, a.libelle, a.unite_stock, sum(s.quantite_physique) AS quantite,
               CASE WHEN sum(s.quantite_physique) = 0 THEN max(s.cump)
                    ELSE sum(s.valeur) / sum(s.quantite_physique) END AS cump,
               sum(s.valeur) AS valeur
        FROM stock.v_soldes s
        JOIN referentiel.v_articles a ON a.article_id = s.article_id
        GROUP BY a.code, a.libelle, a.unite_stock
        HAVING sum(s.quantite_physique) <> 0
        ORDER BY a.code
        """;

    private async Task<JournalExporte> JournalAsync(string nom, Colonne[] colonnes, string sql, object? parametres, CancellationToken annulation)
    {
        await using var connexion = await sourceDeDonnees.OpenConnectionAsync(annulation);
        await using var transaction = await connexion.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, annulation);
        // Lecture seule ; sans compilation JIT : elle coûte près d'une seconde sur des requêtes qui en prennent une demie
        // (estimations hautes sur un long historique), mesuré sur le jeu volumineux.
        await connexion.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY; SET LOCAL jit = off", transaction: transaction, cancellationToken: annulation));
        var lignes = await Lignes(connexion, transaction, sql, parametres, colonnes.Length, annulation);
        await transaction.CommitAsync(annulation);
        return new JournalExporte(nom, colonnes, lignes);
    }

    private static async Task<List<object?[]>> Lignes(NpgsqlConnection connexion, DbTransaction transaction, string sql, object? parametres, int nombreColonnes, CancellationToken annulation)
    {
        await using var lecteur = await connexion.ExecuteReaderAsync(new CommandDefinition(sql, parametres, transaction, cancellationToken: annulation));
        var lignes = new List<object?[]>();
        while (await lecteur.ReadAsync(annulation))
        {
            var valeurs = new object?[nombreColonnes];
            for (var i = 0; i < nombreColonnes; i++)
            {
                valeurs[i] = lecteur.IsDBNull(i) ? null : lecteur.GetValue(i);
            }

            lignes.Add(valeurs);
        }

        return lignes;
    }

    // --- Écriture --------------------------------------------------------------------------------------------------

    private static string Entete(Colonne colonne, CultureInfo culture) => Textes.GetString($"Colonne.{colonne.Cle}", culture) ?? colonne.Cle;

    /// <summary>Valeur d'une opération traduite (Vente, Avoir, Paiement…) ; inchangée si elle n'est pas traduite.</summary>
    private static string Texte(object? valeur, Colonne colonne, CultureInfo culture)
    {
        var texte = Convert.ToString(valeur, CultureInfo.InvariantCulture) ?? "";
        return colonne.Cle is "Operation" or "Mode" ? Textes.GetString($"Valeur.{texte}", culture) ?? texte : texte;
    }

    private static decimal Arrondi(object? valeur, TypeColonne type) =>
        valeur is null ? 0m : decimal.Round(Convert.ToDecimal(valeur, CultureInfo.InvariantCulture), type == TypeColonne.Montant ? 0 : 4, MidpointRounding.AwayFromZero);

    private static DateTime HeureDouala(object valeur) => ((DateTime)valeur) + Periode.DecalageDouala;

    private static byte[] EcrireExcel(JournalExporte journal, CultureInfo culture)
    {
        using var classeur = new XLWorkbook();
        var feuille = classeur.Worksheets.Add(Textes.GetString($"Journal.{journal.Nom}", culture) ?? journal.Nom);
        for (var c = 0; c < journal.Colonnes.Count; c++)
        {
            feuille.Cell(1, c + 1).Value = Entete(journal.Colonnes[c], culture);
        }

        for (var l = 0; l < journal.Lignes.Count; l++)
        {
            for (var c = 0; c < journal.Colonnes.Count; c++)
            {
                var cellule = feuille.Cell(l + 2, c + 1);
                var valeur = journal.Lignes[l][c];
                var colonne = journal.Colonnes[c];
                switch (colonne.Type)
                {
                    case TypeColonne.Date when valeur is not null:
                        cellule.Value = HeureDouala(valeur);
                        cellule.Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                        break;
                    case TypeColonne.Montant or TypeColonne.Quantite when valeur is not null:
                        cellule.Value = Arrondi(valeur, colonne.Type);
                        cellule.Style.NumberFormat.Format = colonne.Type == TypeColonne.Montant ? "#,##0" : "#,##0.###";
                        break;
                    default:
                        cellule.Value = Texte(valeur, colonne, culture);
                        break;
                }
            }
        }

        // Ligne de total des montants (somme des valeurs arrondies, comme à l'écran).
        var total = journal.Lignes.Count + 2;
        feuille.Cell(total, 1).Value = Textes.GetString("Total", culture);
        for (var c = 0; c < journal.Colonnes.Count; c++)
        {
            if (journal.Colonnes[c].Type == TypeColonne.Montant && journal.Colonnes[c].Cle != "Cump")
            {
                feuille.Cell(total, c + 1).Value = journal.Lignes.Sum(l => Arrondi(l[c], TypeColonne.Montant));
                feuille.Cell(total, c + 1).Style.NumberFormat.Format = "#,##0";
            }
        }

        feuille.Row(1).Style.Font.Bold = true;
        feuille.Row(total).Style.Font.Bold = true;
        feuille.SheetView.FreezeRows(1);
        if (journal.Lignes.Count > 0)
        {
            feuille.Range(1, 1, journal.Lignes.Count + 1, journal.Colonnes.Count).SetAutoFilter();
        }

        feuille.Columns(1, journal.Colonnes.Count).AdjustToContents(1, Math.Min(total, 500));
        using var flux = new MemoryStream();
        classeur.SaveAs(flux);
        return flux.ToArray();
    }

    /// <summary>CSV pour un Excel français : « ; », UTF-8 avec BOM, fins de ligne CRLF, montants entiers sans séparateur.</summary>
    private static byte[] EcrireCsv(JournalExporte journal, CultureInfo culture)
    {
        var texte = new StringBuilder();
        texte.Append(string.Join(';', journal.Colonnes.Select(c => Echapper(Entete(c, culture))))).Append("\r\n");
        foreach (var ligne in journal.Lignes)
        {
            var cellules = journal.Colonnes.Select((colonne, c) => ligne[c] switch
            {
                null => "",
                var v when colonne.Type == TypeColonne.Date => HeureDouala(v).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                var v when colonne.Type is TypeColonne.Montant or TypeColonne.Quantite =>
                    Arrondi(v, colonne.Type).ToString(colonne.Type == TypeColonne.Montant ? "0" : "0.####", CultureInfo.InvariantCulture).Replace('.', ','),
                var v => Echapper(Texte(v, colonne, culture)),
            });
            texte.Append(string.Join(';', cellules)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texte.ToString())];
    }

    private static string Echapper(string valeur) =>
        valeur.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{valeur.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : valeur;
}
