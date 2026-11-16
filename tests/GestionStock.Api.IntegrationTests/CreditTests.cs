using System.Net;
using System.Net.Http.Json;
using GestionStock.Credit.Contracts;
using GestionStock.Identite.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GestionStock.Api.IntegrationTests;

public sealed class CreditTests(ApiDeTest api)
{
    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_gerant_cree_le_client_et_le_comptable_fixe_son_plafond()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        using var caissier = await api.ClientAvecRoleAsync(Roles.Caissier);

        var client = await CreerClientAsync(gerant);
        var plafondParGerant = await gerant.PutAsJsonAsync($"/api/credit/clients/{client.Id}/plafond", new { plafondCredit = 10000 }, Annulation);
        var plafond = await comptable.PutAsJsonAsync($"/api/credit/clients/{client.Id}/plafond", new { plafondCredit = 10000 }, Annulation);
        var creationParCaissier = await caissier.PostAsJsonAsync("/api/credit/clients", new { nom = "Autre", telephone = "699000000" }, Annulation);
        var lu = await caissier.GetFromJsonAsync<Fiche>($"/api/credit/clients/{client.Id}", Annulation);

        Assert.Equal(0m, client.PlafondCredit);
        Assert.Equal(30, client.DelaiPaiementJours);
        Assert.Equal(HttpStatusCode.Forbidden, plafondParGerant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plafond.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, creationParCaissier.StatusCode);
        Assert.Equal(10000m, lu!.Client.PlafondCredit);
    }

    [Theory]
    [InlineData("", "699000000", "Credit.NomObligatoire")]
    [InlineData("Mme Ngo", "  ", "Credit.TelephoneObligatoire")]
    public async Task Le_nom_et_le_telephone_sont_obligatoires(string nom, string telephone, string code)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);

        var reponse = await gerant.PostAsJsonAsync("/api/credit/clients", new { nom, telephone }, Annulation);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(code, (await IdentiteTests.LireProblemeAsync(reponse)).Code);
    }

    [Fact]
    public async Task Une_vente_au_dela_du_plafond_exige_la_validation_d_un_responsable()
    {
        var client = await ClientAvecPlafondAsync(5000m);

        await VendreAsync(client, 4000m);
        var refus = await Assert.ThrowsAsync<ExceptionCredit>(() => VendreAsync(client, 2000m));
        await VendreAsync(client, 2000m, valideePar: "gerant-1");
        var fiche = await LireFicheAsync(client);

        Assert.Equal(CodesErreurCredit.PlafondDepasse, refus.Code);
        Assert.Equal(6000m, fiche.Client.SoldeDu);
        Assert.Equal("gerant-1", fiche.Operations.First().ValideePar);
    }

    [Fact]
    public async Task Un_remboursement_s_impute_sur_les_ventes_les_plus_anciennes()
    {
        var client = await ClientAvecPlafondAsync(10000m);
        var ticketA = await VendreAsync(client, 3000m);
        var ticketB = await VendreAsync(client, 2000m);

        await RembourserAsync(client, 3500m);
        var trop = await Assert.ThrowsAsync<ExceptionCredit>(() => RembourserAsync(client, 2000m));
        var fiche = await LireFicheAsync(client);

        Assert.Equal(1500m, fiche.Client.SoldeDu);
        Assert.Equal(0m, await ResteDuAsync(ticketA));
        Assert.Equal(1500m, await ResteDuAsync(ticketB));
        Assert.Equal([ticketB], fiche.VentesOuvertes.Select(v => v.TicketId));
        Assert.Equal(CodesErreurCredit.RemboursementSuperieurDette, trop.Code);
    }

    [Fact]
    public async Task Un_avoir_annule_la_dette_du_ticket_dans_la_limite_de_ce_qui_reste_du()
    {
        var client = await ClientAvecPlafondAsync(10000m);
        var ticket = await VendreAsync(client, 2000m);
        await RembourserAsync(client, 500m);

        var annule = await AppelerAsync(credit => credit.AnnulerVenteAsync(new AnnulationCredit(client, ticket, Guid.NewGuid(), 2000m, "caissier"), Annulation));
        var fiche = await LireFicheAsync(client);

        Assert.Equal(1500m, annule);
        Assert.Equal(0m, fiche.Client.SoldeDu);
        Assert.Empty(fiche.VentesOuvertes);
    }

    [Fact]
    public async Task Les_dettes_echues_apparaissent_dans_les_impayes()
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var client = await CreerClientAsync(gerant, delaiPaiementJours: 0);
        await FixerPlafondAsync(client.Id, 10000m);
        await VendreAsync(client.Id, 1200m);
        await Task.Delay(50, Annulation);

        var impayes = await gerant.GetFromJsonAsync<List<ClientLu>>("/api/credit/impayes", Annulation);

        Assert.Contains(impayes!, c => c.Id == client.Id && c.MontantEnRetard == 1200m);
    }

    [Fact]
    public async Task Deux_ventes_simultanees_ne_depassent_pas_ensemble_le_plafond()
    {
        var client = await ClientAvecPlafondAsync(10000m);

        var resultats = await Task.WhenAll(
            Capturer(() => VendreAsync(client, 6000m)),
            Capturer(() => VendreAsync(client, 6000m)));
        var fiche = await LireFicheAsync(client);

        Assert.Single(resultats, r => r is null);
        Assert.Single(resultats, r => r == CodesErreurCredit.PlafondDepasse);
        Assert.Equal(6000m, fiche.Client.SoldeDu);
    }

    // --- Aides ---------------------------------------------------------------------------------------------------

    private static async Task<ClientLu> CreerClientAsync(HttpClient gerant, int? delaiPaiementJours = null)
    {
        var reponse = await gerant.PostAsJsonAsync(
            "/api/credit/clients",
            new { nom = $"Client {Guid.NewGuid():N}"[..16], telephone = "+237 699 00 00 00", delaiPaiementJours },
            Annulation);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<ClientLu>(Annulation))!;
    }

    private async Task FixerPlafondAsync(Guid client, decimal plafond)
    {
        using var comptable = await api.ClientAvecRoleAsync(Roles.Comptable);
        (await comptable.PutAsJsonAsync($"/api/credit/clients/{client}/plafond", new { plafondCredit = plafond }, Annulation)).EnsureSuccessStatusCode();
    }

    private async Task<Guid> ClientAvecPlafondAsync(decimal plafond)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        var client = await CreerClientAsync(gerant);
        await FixerPlafondAsync(client.Id, plafond);
        return client.Id;
    }

    private async Task<Guid> VendreAsync(Guid client, decimal montant, string? valideePar = null)
    {
        var ticket = Guid.NewGuid();
        await AppelerAsync(async credit =>
        {
            await credit.EnregistrerVenteAsync(new VenteACredit(client, ticket, "T-1", montant, "caissier", valideePar), Annulation);
            return true;
        });
        return ticket;
    }

    private Task RembourserAsync(Guid client, decimal montant) =>
        AppelerAsync(async credit =>
        {
            await credit.EnregistrerRemboursementAsync(new RemboursementCredit(client, montant, Guid.NewGuid(), "caissier"), Annulation);
            return true;
        });

    private Task<decimal> ResteDuAsync(Guid ticket) => AppelerAsync(credit => credit.ResteDuSurTicketAsync(ticket, Annulation));

    private async Task<Fiche> LireFicheAsync(Guid client)
    {
        using var gerant = await api.ClientAvecRoleAsync(Roles.Gerant);
        return (await gerant.GetFromJsonAsync<Fiche>($"/api/credit/clients/{client}", Annulation))!;
    }

    /// <summary>Appelle le contrat dans une portée neuve, comme la Caisse le fera dans une requête.</summary>
    private async Task<T> AppelerAsync<T>(Func<IServiceCredit, Task<T>> action)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await action(portee.ServiceProvider.GetRequiredService<IServiceCredit>());
    }

    private static async Task<string?> Capturer(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (ExceptionCredit erreur)
        {
            return erreur.Code;
        }
    }

    private sealed record ClientLu(Guid Id, string Nom, decimal PlafondCredit, int DelaiPaiementJours, decimal SoldeDu, decimal MontantEnRetard);

    private sealed record VenteOuverte(Guid TicketId, decimal ResteDu, bool EnRetard);

    private sealed record Operation(string Type, decimal Montant, string? ValideePar);

    private sealed record Fiche(ClientLu Client, List<VenteOuverte> VentesOuvertes, List<Operation> Operations);
}
