using System.Data.Common;
using Dapper;
using GestionStock.SharedKernel;
using Npgsql;

namespace GestionStock.Rapports.Application;

// --- Ventes et marge ---------------------------------------------------------------------------------------------

internal sealed record TotauxVentes(
    int NombreVentes,
    int NombreAvoirs,
    decimal ChiffreAffairesTtc,
    decimal ChiffreAffairesHt,
    decimal CoutDesVentes,
    decimal Marge,
    decimal? TauxMarge,
    decimal Remises);

internal sealed record VentesDuJour(DateOnly Jour, int NombreTickets, decimal ChiffreAffairesTtc, decimal ChiffreAffairesHt, decimal CoutDesVentes, decimal Marge);

internal sealed record VentesArticle(Guid ArticleId, string Code, string Libelle, string UniteStock, decimal Quantite, decimal ChiffreAffairesHt, decimal CoutDesVentes, decimal Marge, decimal? TauxMarge);

internal sealed record VentesCaissier(string Caissier, int NombreTickets, decimal ChiffreAffairesTtc);

internal sealed record RapportVentes(
    DateOnly Du,
    DateOnly Au,
    TotauxVentes Totaux,
    IReadOnlyList<VentesDuJour> ParJour,
    IReadOnlyList<VentesArticle> ParArticle,
    IReadOnlyList<VentesCaissier> ParCaissier);

// --- Stock et valeur ---------------------------------------------------------------------------------------------

internal sealed record LigneStock(Guid ArticleId, string Code, string Libelle, string UniteStock, int PrecisionQuantite, decimal Quantite, decimal Cump, decimal Valeur);

internal sealed record RapportStock(decimal ValeurTotale, int NombreArticles, IReadOnlyList<LigneStock> Articles, IReadOnlyList<LigneStock> StocksNegatifs);

// --- Créances et dettes ------------------------------------------------------------------------------------------

internal sealed record CreanceClient(Guid ClientId, string Nom, string Telephone, decimal PlafondCredit, decimal SoldeDu, decimal MontantEnRetard, DateTime? PlusAncienneEcheanceUtc);

internal sealed record DetteFournisseurRapport(Guid FournisseurId, string Fournisseur, int NombreReceptions, decimal MontantDu, decimal MontantPaye, decimal ResteDu);

internal sealed record RapportCreancesDettes(
    decimal TotalCreances,
    decimal TotalEnRetard,
    decimal TotalDettes,
    IReadOnlyList<CreanceClient> Clients,
    IReadOnlyList<DetteFournisseurRapport> Fournisseurs);

// --- Caisse et anomalies -----------------------------------------------------------------------------------------

internal sealed record SessionRapport(
    Guid SessionId,
    string NumeroCaisse,
    string Caissier,
    DateTime OuvertureUtc,
    DateTime? ClotureUtc,
    string Statut,
    int NombreVentes,
    int NombreAvoirs,
    decimal VentesNettes,
    decimal? EspecesTheoriques,
    decimal? EspecesComptees,
    decimal? Ecart,
    string? ValideePar);

internal sealed record TicketSignale(Guid TicketId, string Numero, DateTime DateUtc, string Caissier, decimal TotalTtc, decimal Remise, string? ValidePar, string? TicketOrigine);

internal sealed record FraisNonIncorpore(Guid FraisId, Guid ReceptionId, string Reception, string Type, string? Libelle, decimal Montant, decimal MontantNonIncorpore, DateTime SaisiLeUtc);

internal sealed record RapportCaisse(
    DateOnly Du,
    DateOnly Au,
    decimal TotalEcarts,
    IReadOnlyList<SessionRapport> Sessions,
    IReadOnlyList<TicketSignale> Remises,
    IReadOnlyList<TicketSignale> Avoirs,
    IReadOnlyList<TicketSignale> CreditsValides,
    IReadOnlyList<LigneStock> StocksNegatifs,
    IReadOnlyList<FraisNonIncorpore> FraisNonIncorpores);

/// <summary>
/// Rapports V1 (décision 33) : ventes et marge, stock et valeur, créances et dettes, caisse et anomalies.
/// Lecture seule (section 7 : CQRS côté lecture) : uniquement les vues <c>v_*</c> publiées par les modules, dans une
/// transaction en lecture seule ; aucun accès aux tables ni aux DbContext des autres modules.
/// </summary>
internal sealed class ServiceRapports(NpgsqlDataSource sourceDeDonnees, IHorloge horloge)
{
    /// <summary>Écart de caisse ou frais non incorporé en dessous duquel rien n'est signalé (arrondis internes).</summary>
    private const decimal SeuilSignalement = 0.5m;

    static ServiceRapports()
    {
        // Colonnes en snake_case (vues PostgreSQL) vers propriétés en PascalCase.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public DateOnly Aujourdhui => horloge.AujourdhuiDouala;

    public async Task<RapportVentes> VentesAsync(Periode periode, CancellationToken annulation)
    {
        // Une ligne par ticket × article : chiffre d'affaires des lignes et coût des mouvements de stock du ticket
        // (sorties et retours au CUMP du moment), signés : un avoir vient en déduction.
        const string Sql = """
            WITH lignes AS (
                SELECT l.ticket_id, l.article_id, l.date_utc, l.caissier, l.signe,
                       sum(l.quantite_stock) AS quantite, sum(l.montant_ttc) AS ttc, sum(l.montant_ht) AS ht
                FROM caisse.v_lignes_vendues l
                WHERE l.date_utc >= @Debut AND l.date_utc < @Fin
                GROUP BY l.ticket_id, l.article_id, l.date_utc, l.caissier, l.signe
            ),
            couts AS (
                SELECT m.document_id AS ticket_id, m.article_id, sum(m.quantite * m.cout_unitaire) AS cout
                FROM stock.v_mouvements m
                WHERE m.type IN ('VenteCaisse', 'AvoirCaisse') AND m.document_id IN (SELECT ticket_id FROM lignes)
                GROUP BY m.document_id, m.article_id
            )
            SELECT l.ticket_id, l.article_id, a.code, a.libelle, a.unite_stock, l.date_utc, l.caissier, l.signe,
                   l.quantite, l.ttc, l.ht, COALESCE(c.cout, 0) AS cout
            FROM lignes l
            JOIN referentiel.v_articles a ON a.article_id = l.article_id
            LEFT JOIN couts c ON c.ticket_id = l.ticket_id AND c.article_id = l.article_id
            """;
        const string SqlTickets = """
            SELECT t.ticket_id, t.est_avoir, t.remise, t.signe
            FROM caisse.v_tickets t
            WHERE t.date_utc >= @Debut AND t.date_utc < @Fin
            """;

        var (lignes, tickets) = await LireAsync(
            async (connexion, transaction) => (
                (await connexion.QueryAsync<LigneVente>(new CommandDefinition(Sql, Parametres(periode), transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<TicketVente>(new CommandDefinition(SqlTickets, Parametres(periode), transaction, cancellationToken: annulation))).ToList()),
            annulation);

        var ht = lignes.Sum(l => l.Signe * l.Ht);
        var cout = lignes.Sum(l => l.Signe * l.Cout);
        var totaux = new TotauxVentes(
            tickets.Count(t => !t.EstAvoir),
            tickets.Count(t => t.EstAvoir),
            lignes.Sum(l => l.Signe * l.Ttc),
            ht,
            cout,
            ht - cout,
            Taux(ht - cout, ht),
            tickets.Sum(t => t.Signe * t.Remise));

        var parJour = lignes
            .GroupBy(l => Periode.JourDouala(l.DateUtc))
            .Select(g => new VentesDuJour(
                g.Key,
                g.Select(l => l.TicketId).Distinct().Count(),
                g.Sum(l => l.Signe * l.Ttc),
                g.Sum(l => l.Signe * l.Ht),
                g.Sum(l => l.Signe * l.Cout),
                g.Sum(l => l.Signe * (l.Ht - l.Cout))))
            .OrderBy(j => j.Jour)
            .ToList();
        var parArticle = lignes
            .GroupBy(l => (l.ArticleId, l.Code, l.Libelle, l.UniteStock))
            .Select(g =>
            {
                var chiffre = g.Sum(l => l.Signe * l.Ht);
                var marge = chiffre - g.Sum(l => l.Signe * l.Cout);
                return new VentesArticle(g.Key.ArticleId, g.Key.Code, g.Key.Libelle, g.Key.UniteStock, g.Sum(l => l.Signe * l.Quantite), chiffre, chiffre - marge, marge, Taux(marge, chiffre));
            })
            .OrderByDescending(a => a.Marge)
            .ToList();
        var parCaissier = lignes
            .GroupBy(l => l.Caissier)
            .Select(g => new VentesCaissier(g.Key, g.Select(l => l.TicketId).Distinct().Count(), g.Sum(l => l.Signe * l.Ttc)))
            .OrderByDescending(c => c.ChiffreAffairesTtc)
            .ToList();

        return new RapportVentes(periode.Du, periode.Au, totaux, parJour, parArticle, parCaissier);
    }

    public async Task<RapportStock> StockAsync(CancellationToken annulation)
    {
        var articles = await LireAsync((connexion, transaction) => ListeStockAsync(connexion, transaction, annulation), annulation);
        return new RapportStock(articles.Sum(a => a.Valeur), articles.Count(a => a.Quantite != 0m), articles, [.. articles.Where(a => a.Quantite < 0m)]);
    }

    public async Task<RapportCreancesDettes> CreancesDettesAsync(CancellationToken annulation)
    {
        // RG43 : une vente à crédit est en retard quand son échéance est passée et qu'il en reste dû.
        const string SqlClients = """
            SELECT c.client_id, c.nom, c.telephone, c.plafond_credit, c.solde_du,
                   COALESCE((SELECT sum(v.reste_du) FROM credit.v_ventes_credit v
                             WHERE v.client_id = c.client_id AND v.reste_du > 0 AND v.echeance_utc < @Maintenant), 0) AS montant_en_retard,
                   (SELECT min(v.echeance_utc) FROM credit.v_ventes_credit v
                    WHERE v.client_id = c.client_id AND v.reste_du > 0) AS plus_ancienne_echeance_utc
            FROM credit.v_clients c
            WHERE c.solde_du > 0
            """;
        const string SqlFournisseurs = """
            SELECT r.fournisseur_id, r.fournisseur, count(*)::int AS nombre_receptions,
                   sum(r.valeur_marchandise + r.total_frais) AS montant_du, sum(r.montant_paye) AS montant_paye
            FROM achats.v_receptions r
            WHERE r.statut = 'Validee'
            GROUP BY r.fournisseur_id, r.fournisseur
            HAVING sum(r.valeur_marchandise + r.total_frais) - sum(r.montant_paye) > 0
            """;

        var maintenant = new { Maintenant = horloge.MaintenantUtc };
        var (clients, fournisseurs) = await LireAsync(
            async (connexion, transaction) => (
                (await connexion.QueryAsync<LigneCreance>(new CommandDefinition(SqlClients, maintenant, transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<LigneDette>(new CommandDefinition(SqlFournisseurs, transaction: transaction, cancellationToken: annulation))).ToList()),
            annulation);

        var creances = clients
            .Select(c => new CreanceClient(c.ClientId, c.Nom, c.Telephone, c.PlafondCredit, c.SoldeDu, c.MontantEnRetard, c.PlusAncienneEcheanceUtc))
            .OrderByDescending(c => c.MontantEnRetard).ThenByDescending(c => c.SoldeDu)
            .ToList();
        var dettes = fournisseurs
            .Select(f => new DetteFournisseurRapport(f.FournisseurId, f.Fournisseur, f.NombreReceptions, f.MontantDu, f.MontantPaye, f.MontantDu - f.MontantPaye))
            .OrderByDescending(f => f.ResteDu)
            .ToList();
        return new RapportCreancesDettes(creances.Sum(c => c.SoldeDu), creances.Sum(c => c.MontantEnRetard), dettes.Sum(d => d.ResteDu), creances, dettes);
    }

    public async Task<RapportCaisse> CaisseAsync(Periode periode, CancellationToken annulation)
    {
        const string SqlSessions = """
            SELECT s.session_id, s.numero_caisse, s.caissier, s.ouverture_utc, s.cloture_utc, s.statut,
                   s.nombre_ventes::int AS nombre_ventes, s.nombre_avoirs::int AS nombre_avoirs, s.ventes_nettes,
                   s.especes_theoriques, s.especes_comptees, s.ecart, s.validee_par
            FROM caisse.v_sessions s
            WHERE s.ouverture_utc >= @Debut AND s.ouverture_utc < @Fin
            ORDER BY s.ouverture_utc
            """;

        // Remises, avoirs et ventes à crédit validées par un responsable : opérations journalisées à surveiller.
        const string SqlTickets = """
            SELECT t.ticket_id, t.numero_complet AS numero, t.date_utc, t.caissier, t.total_ttc, t.remise, t.est_avoir,
                   t.remise_validee_par, t.credit_valide_par, o.numero_complet AS ticket_origine
            FROM caisse.v_tickets t
            LEFT JOIN caisse.v_tickets o ON o.ticket_id = t.ticket_origine_id
            WHERE t.date_utc >= @Debut AND t.date_utc < @Fin
              AND (t.remise > 0 OR t.est_avoir OR t.credit_valide_par IS NOT NULL)
            ORDER BY t.date_utc
            """;
        const string SqlFrais = """
            SELECT f.frais_id, f.reception_id, f.numero AS reception, f.type, f.libelle, f.montant, f.montant_non_incorpore,
                   f.saisi_le_utc
            FROM achats.v_frais f
            WHERE f.saisi_le_utc >= @Debut AND f.saisi_le_utc < @Fin AND f.montant_non_incorpore >= @Seuil
            ORDER BY f.saisi_le_utc
            """;

        var parametres = new { Debut = periode.DebutUtc, Fin = periode.FinUtc, Seuil = SeuilSignalement };
        var (sessions, tickets, frais, stock) = await LireAsync(
            async (connexion, transaction) => (
                (await connexion.QueryAsync<SessionRapport>(new CommandDefinition(SqlSessions, parametres, transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<LigneTicket>(new CommandDefinition(SqlTickets, parametres, transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<FraisNonIncorpore>(new CommandDefinition(SqlFrais, parametres, transaction, cancellationToken: annulation))).ToList(),
                await ListeStockAsync(connexion, transaction, annulation)),
            annulation);

        static TicketSignale Signale(LigneTicket t, string? validePar) =>
            new(t.TicketId, t.Numero, t.DateUtc, t.Caissier, t.TotalTtc, t.Remise, validePar, t.TicketOrigine);

        return new RapportCaisse(
            periode.Du,
            periode.Au,
            sessions.Sum(s => s.Ecart ?? 0m),
            sessions,
            [.. tickets.Where(t => t.Remise > 0m).Select(t => Signale(t, t.RemiseValideePar))],
            [.. tickets.Where(t => t.EstAvoir).Select(t => Signale(t, null))],
            [.. tickets.Where(t => t.CreditValidePar is not null).Select(t => Signale(t, t.CreditValidePar))],
            [.. stock.Where(a => a.Quantite < 0m)],
            frais);
    }

    private static async Task<List<LigneStock>> ListeStockAsync(NpgsqlConnection connexion, DbTransaction transaction, CancellationToken annulation)
    {
        // V1 mono-site : un solde par article ; la somme prépare le multi-sites.
        const string Sql = """
            SELECT a.article_id, a.code, a.libelle, a.unite_stock, a.precision_quantite,
                   sum(s.quantite_physique) AS quantite,
                   CASE WHEN sum(s.quantite_physique) = 0 THEN max(s.cump)
                        ELSE round(sum(s.valeur) / sum(s.quantite_physique), 4) END AS cump,
                   sum(s.valeur) AS valeur
            FROM stock.v_soldes s
            JOIN referentiel.v_articles a ON a.article_id = s.article_id
            GROUP BY a.article_id, a.code, a.libelle, a.unite_stock, a.precision_quantite
            ORDER BY a.code
            """;
        return [.. await connexion.QueryAsync<LigneStock>(new CommandDefinition(Sql, transaction: transaction, cancellationToken: annulation))];
    }

    /// <summary>Exécute les lectures sur une connexion dédiée, dans une transaction en lecture seule (instantané cohérent).</summary>
    private async Task<T> LireAsync<T>(Func<NpgsqlConnection, DbTransaction, Task<T>> lecture, CancellationToken annulation)
    {
        await using var connexion = await sourceDeDonnees.OpenConnectionAsync(annulation);
        await using var transaction = await connexion.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, annulation);
        await connexion.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY", transaction: transaction, cancellationToken: annulation));
        var resultat = await lecture(connexion, transaction);
        await transaction.CommitAsync(annulation);
        return resultat;
    }

    private static object Parametres(Periode periode) => new { Debut = periode.DebutUtc, Fin = periode.FinUtc };

    private static decimal? Taux(decimal marge, decimal chiffre) =>
        chiffre == 0m ? null : decimal.Round(marge * 100m / chiffre, 2, MidpointRounding.AwayFromZero);

    // Lignes brutes lues par Dapper (propriétés renseignées par nom de colonne).
    private sealed class LigneVente
    {
        public Guid TicketId { get; init; }

        public Guid ArticleId { get; init; }

        public string Code { get; init; } = "";

        public string Libelle { get; init; } = "";

        public string UniteStock { get; init; } = "";

        public DateTime DateUtc { get; init; }

        public string Caissier { get; init; } = "";

        public int Signe { get; init; }

        public decimal Quantite { get; init; }

        public decimal Ttc { get; init; }

        public decimal Ht { get; init; }

        public decimal Cout { get; init; }
    }

    private sealed class TicketVente
    {
        public Guid TicketId { get; init; }

        public bool EstAvoir { get; init; }

        public decimal Remise { get; init; }

        public int Signe { get; init; }
    }

    private sealed class LigneCreance
    {
        public Guid ClientId { get; init; }

        public string Nom { get; init; } = "";

        public string Telephone { get; init; } = "";

        public decimal PlafondCredit { get; init; }

        public decimal SoldeDu { get; init; }

        public decimal MontantEnRetard { get; init; }

        public DateTime? PlusAncienneEcheanceUtc { get; init; }
    }

    private sealed class LigneDette
    {
        public Guid FournisseurId { get; init; }

        public string Fournisseur { get; init; } = "";

        public int NombreReceptions { get; init; }

        public decimal MontantDu { get; init; }

        public decimal MontantPaye { get; init; }
    }

    private sealed class LigneTicket
    {
        public Guid TicketId { get; init; }

        public string Numero { get; init; } = "";

        public DateTime DateUtc { get; init; }

        public string Caissier { get; init; } = "";

        public decimal TotalTtc { get; init; }

        public decimal Remise { get; init; }

        public bool EstAvoir { get; init; }

        public string? RemiseValideePar { get; init; }

        public string? CreditValidePar { get; init; }

        public string? TicketOrigine { get; init; }
    }
}
