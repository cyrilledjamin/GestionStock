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
    IReadOnlyList<FraisNonIncorpore> FraisNonIncorpores,
    IReadOnlyList<AnomalieRapport> Anomalies);

/// <summary>Anomalie de synchronisation d'une opération faite hors ligne (7.8), traitée ou non par le Gérant.</summary>
internal sealed record AnomalieRapport(
    Guid AnomalieId,
    string Type,
    string NumeroCaisse,
    string Caissier,
    string? NumeroDocument,
    string Detail,
    DateTime DateUtc,
    bool Traitee,
    string? TraiteePar,
    string? Commentaire);

// --- Tableau de bord de l'accueil --------------------------------------------------------------------------------

/// <summary>
/// Chiffres de l'accueil du Gérant et du Comptable : ventes du jour et points à surveiller, tirés des mêmes calculs
/// que les rapports (une seule requête HTTP, ENF10).
/// </summary>
internal sealed record TableauDeBord(
    DateOnly Jour,
    TotauxVentes VentesDuJour,
    int SessionsOuvertes,
    int SessionsAValider,
    decimal EcartsAValider,
    int StocksNegatifs,
    decimal ValeurStock,
    decimal TotalCreances,
    decimal TotalEnRetard,
    decimal TotalDettes,
    int AnomaliesATraiter);

/// <summary>
/// Rapports V1 (décision 33) : ventes et marge, stock et valeur, créances et dettes, caisse et anomalies.
/// Le personnel est désigné par « Prénom Nom (matricule) » (vue <c>identite.v_utilisateurs</c>).
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
        // Ticket × article : chiffre d'affaires des lignes et coût des mouvements de stock du ticket (sorties et retours au
        // CUMP du moment), signés : un avoir vient en déduction. Le regroupement par jour (de Douala), par article et par
        // caissier est fait par la base (GROUPING SETS) : seules les sommes reviennent, pas chaque ligne de la période.
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
                -- type_document en premier : l'index (type_document, document_id) du journal évite de le lire en entier.
                WHERE m.type_document = 'TicketCaisse' AND m.type IN ('VenteCaisse', 'AvoirCaisse')
                  AND m.document_id IN (SELECT ticket_id FROM lignes)
                GROUP BY m.document_id, m.article_id
            ),
            detail AS (
                SELECT l.ticket_id, l.article_id, ((l.date_utc AT TIME ZONE 'UTC') + @Decalage)::date AS jour, l.caissier, l.signe,
                       l.quantite, l.ttc, l.ht, COALESCE(c.cout, 0) AS cout
                FROM lignes l
                LEFT JOIN couts c ON c.ticket_id = l.ticket_id AND c.article_id = l.article_id
            )
            SELECT g.niveau, g.jour, g.article_id, a.code, a.libelle, a.unite_stock, COALESCE(uc.designation, g.caissier) AS caissier,
                   g.nombre_tickets, g.quantite, g.ttc, g.ht, g.cout
            FROM (SELECT GROUPING(jour, article_id, caissier) AS niveau, jour, article_id, caissier,
                         count(DISTINCT ticket_id)::int AS nombre_tickets, sum(signe * quantite) AS quantite,
                         sum(signe * ttc) AS ttc, sum(signe * ht) AS ht, sum(signe * cout) AS cout
                  FROM detail
                  GROUP BY GROUPING SETS ((jour), (article_id), (caissier))) g
            LEFT JOIN referentiel.v_articles a ON a.article_id = g.article_id
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = g.caissier
            """;
        const string SqlTickets = """
            SELECT count(*) FILTER (WHERE NOT t.est_avoir)::int AS ventes, count(*) FILTER (WHERE t.est_avoir)::int AS avoirs,
                   COALESCE(sum(t.signe * t.remise), 0) AS remise
            FROM caisse.v_tickets t
            WHERE t.date_utc >= @Debut AND t.date_utc < @Fin
            """;

        var parametres = new { Debut = periode.DebutUtc, Fin = periode.FinUtc, Decalage = Periode.DecalageDouala };
        var (agregats, tickets) = await LireAsync(
            async (connexion, transaction) => (
                (await connexion.QueryAsync<AgregatVentes>(new CommandDefinition(Sql, parametres, transaction, cancellationToken: annulation))).ToList(),
                await connexion.QuerySingleAsync<SyntheseTickets>(new CommandDefinition(SqlTickets, parametres, transaction, cancellationToken: annulation))),
            annulation);

        // GROUPING(jour, article_id, caissier) : 3 = par jour, 5 = par article, 6 = par caissier.
        var jours = agregats.Where(a => a.Niveau == 3).ToList();
        var ht = jours.Sum(j => j.Ht);
        var cout = jours.Sum(j => j.Cout);
        var totaux = new TotauxVentes(
            tickets.Ventes,
            tickets.Avoirs,
            jours.Sum(j => j.Ttc),
            ht,
            cout,
            ht - cout,
            Taux(ht - cout, ht),
            tickets.Remise);

        var parJour = jours
            .Select(j => new VentesDuJour(j.Jour!.Value, j.NombreTickets, j.Ttc, j.Ht, j.Cout, j.Ht - j.Cout))
            .OrderBy(j => j.Jour)
            .ToList();
        var parArticle = agregats
            .Where(a => a.Niveau == 5)
            .Select(a => new VentesArticle(a.ArticleId!.Value, a.Code ?? "", a.Libelle ?? "", a.UniteStock ?? "", a.Quantite, a.Ht, a.Cout, a.Ht - a.Cout, Taux(a.Ht - a.Cout, a.Ht)))
            .OrderByDescending(a => a.Marge)
            .ToList();
        var parCaissier = agregats
            .Where(a => a.Niveau == 6)
            .Select(a => new VentesCaissier(a.Caissier ?? "", a.NombreTickets, a.Ttc))
            .OrderByDescending(c => c.ChiffreAffairesTtc)
            .ToList();

        return new RapportVentes(periode.Du, periode.Au, totaux, parJour, parArticle, parCaissier);
    }

    /// <summary>Tableau de bord de l'accueil : ventes d'aujourd'hui (jour de Douala), caisse, stock, créances et dettes.</summary>
    public async Task<TableauDeBord> TableauDeBordAsync(CancellationToken annulation)
    {
        // Sessions de toutes dates : une session comptée hier et pas encore validée reste à faire.
        const string SqlSessions = """
            SELECT count(*) FILTER (WHERE s.statut = 'Ouverte') AS ouvertes,
                   count(*) FILTER (WHERE s.statut = 'Comptee') AS a_valider,
                   COALESCE(sum(s.ecart) FILTER (WHERE s.statut = 'Comptee'), 0) AS ecarts,
                   (SELECT count(*) FROM caisse.v_anomalies a WHERE NOT a.traitee) AS anomalies
            FROM caisse.v_sessions s
            """;

        var jour = Aujourdhui;
        var ventes = await VentesAsync(new Periode(jour, jour), annulation);
        var stock = await StockAsync(annulation);
        var creances = await CreancesDettesAsync(annulation);
        var sessions = await LireAsync(
            (connexion, transaction) => connexion.QuerySingleAsync<SyntheseSessions>(new CommandDefinition(SqlSessions, transaction: transaction, cancellationToken: annulation)),
            annulation);
        return new TableauDeBord(
            jour,
            ventes.Totaux,
            (int)sessions.Ouvertes,
            (int)sessions.AValider,
            sessions.Ecarts,
            stock.StocksNegatifs.Count,
            stock.ValeurTotale,
            creances.TotalCreances,
            creances.TotalEnRetard,
            creances.TotalDettes,
            (int)sessions.Anomalies);
    }

    /// <summary>Jours de la tendance de l'accueil (ventes et marge des derniers jours, aujourd'hui compris).</summary>
    public const int JoursTendance = 30;

    /// <summary>
    /// Ventes et marge jour par jour sur les <see cref="JoursTendance"/> derniers jours (graphique de l'accueil), mêmes
    /// calculs que le rapport des ventes. Chaque jour est présent, à zéro s'il n'y a eu aucune vente : le graphique ne
    /// saute pas les dimanches. Servie à part du tableau de bord pour que ses chiffres s'affichent sans l'attendre.
    /// </summary>
    public async Task<IReadOnlyList<VentesDuJour>> TendanceAsync(CancellationToken annulation)
    {
        var fin = Aujourdhui;
        var periode = new Periode(fin.AddDays(1 - JoursTendance), fin);
        var parJour = (await VentesAsync(periode, annulation)).ParJour.ToDictionary(j => j.Jour);
        return [.. Enumerable.Range(0, JoursTendance)
            .Select(i => periode.Du.AddDays(i))
            .Select(jour => parJour.GetValueOrDefault(jour) ?? new VentesDuJour(jour, 0, 0m, 0m, 0m, 0m))];
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
        // Les montants de la vue sont des sous-requêtes par réception : calculés une fois dans la sous-requête (OFFSET 0
        // empêche de la fusionner), et non de nouveau pour le HAVING (×5 sur un historique de 3 ans).
        const string SqlFournisseurs = """
            SELECT fournisseur_id, fournisseur, count(*)::int AS nombre_receptions,
                   sum(du) AS montant_du, sum(paye) AS montant_paye
            FROM (SELECT r.fournisseur_id, r.fournisseur, r.valeur_marchandise + r.total_frais AS du, r.montant_paye AS paye
                  FROM achats.v_receptions r
                  WHERE r.statut = 'Validee'
                  OFFSET 0) receptions
            GROUP BY fournisseur_id, fournisseur
            HAVING sum(du) - sum(paye) > 0
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
            SELECT s.session_id, s.numero_caisse, COALESCE(uc.designation, s.caissier) AS caissier, s.ouverture_utc,
                   s.cloture_utc, s.statut, s.nombre_ventes::int AS nombre_ventes, s.nombre_avoirs::int AS nombre_avoirs,
                   s.ventes_nettes, s.especes_theoriques, s.especes_comptees, s.ecart,
                   COALESCE(uv.designation, s.validee_par) AS validee_par
            FROM caisse.v_sessions s
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = s.caissier
            LEFT JOIN identite.v_utilisateurs uv ON uv.matricule = s.validee_par
            WHERE s.ouverture_utc >= @Debut AND s.ouverture_utc < @Fin
            ORDER BY s.ouverture_utc
            """;

        // Remises, avoirs et ventes à crédit validées par un responsable : opérations journalisées à surveiller.
        const string SqlTickets = """
            SELECT t.ticket_id, t.numero_complet AS numero, t.date_utc, COALESCE(uc.designation, t.caissier) AS caissier,
                   t.total_ttc, t.remise, t.est_avoir,
                   COALESCE(ur.designation, t.remise_validee_par) AS remise_validee_par,
                   COALESCE(uv.designation, t.credit_valide_par) AS credit_valide_par, o.numero_complet AS ticket_origine
            FROM caisse.v_tickets t
            LEFT JOIN caisse.v_tickets o ON o.ticket_id = t.ticket_origine_id
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = t.caissier
            LEFT JOIN identite.v_utilisateurs ur ON ur.matricule = t.remise_validee_par
            LEFT JOIN identite.v_utilisateurs uv ON uv.matricule = t.credit_valide_par
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

        // Anomalies de synchronisation des opérations faites hors ligne (7.8), relevées pendant la période.
        const string SqlAnomalies = """
            SELECT a.anomalie_id, a.type, a.numero_caisse, COALESCE(uc.designation, a.caissier) AS caissier, a.numero_document,
                   a.detail, a.date_utc, a.traitee, COALESCE(ut.designation, a.traitee_par) AS traitee_par, a.commentaire
            FROM caisse.v_anomalies a
            LEFT JOIN identite.v_utilisateurs uc ON uc.matricule = a.caissier
            LEFT JOIN identite.v_utilisateurs ut ON ut.matricule = a.traitee_par
            WHERE a.date_utc >= @Debut AND a.date_utc < @Fin
            ORDER BY a.date_utc
            """;

        var parametres = new { Debut = periode.DebutUtc, Fin = periode.FinUtc, Seuil = SeuilSignalement };
        var (sessions, tickets, frais, stock, anomalies) = await LireAsync(
            async (connexion, transaction) => (
                (await connexion.QueryAsync<SessionRapport>(new CommandDefinition(SqlSessions, parametres, transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<LigneTicket>(new CommandDefinition(SqlTickets, parametres, transaction, cancellationToken: annulation))).ToList(),
                (await connexion.QueryAsync<FraisNonIncorpore>(new CommandDefinition(SqlFrais, parametres, transaction, cancellationToken: annulation))).ToList(),
                await ListeStockAsync(connexion, transaction, annulation),
                (await connexion.QueryAsync<AnomalieRapport>(new CommandDefinition(SqlAnomalies, parametres, transaction, cancellationToken: annulation))).ToList()),
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
            frais,
            anomalies);
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
        // Lecture seule ; sans compilation JIT : elle coûte près d'une seconde sur des requêtes qui en prennent une demie
        // (estimations hautes sur un long historique), mesuré sur le jeu volumineux.
        await connexion.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY; SET LOCAL jit = off", transaction: transaction, cancellationToken: annulation));
        var resultat = await lecture(connexion, transaction);
        await transaction.CommitAsync(annulation);
        return resultat;
    }

    private sealed record SyntheseSessions(long Ouvertes, long AValider, decimal Ecarts, long Anomalies);

    private static object Parametres(Periode periode) => new { Debut = periode.DebutUtc, Fin = periode.FinUtc };

    private static decimal? Taux(decimal marge, decimal chiffre) =>
        chiffre == 0m ? null : decimal.Round(marge * 100m / chiffre, 2, MidpointRounding.AwayFromZero);

    // Lignes brutes lues par Dapper (propriétés renseignées par nom de colonne).
    private sealed class AgregatVentes
    {
        public int Niveau { get; init; }

        public DateOnly? Jour { get; init; }

        public Guid? ArticleId { get; init; }

        public string? Code { get; init; }

        public string? Libelle { get; init; }

        public string? UniteStock { get; init; }

        public string? Caissier { get; init; }

        public int NombreTickets { get; init; }

        public decimal Quantite { get; init; }

        public decimal Ttc { get; init; }

        public decimal Ht { get; init; }

        public decimal Cout { get; init; }
    }

    private sealed class SyntheseTickets
    {
        public int Ventes { get; init; }

        public int Avoirs { get; init; }

        public decimal Remise { get; init; }
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
