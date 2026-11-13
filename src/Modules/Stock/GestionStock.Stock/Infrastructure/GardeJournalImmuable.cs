using GestionStock.Stock.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionStock.Stock.Infrastructure;

/// <summary>
/// Dernier rempart de RG01 côté application : refuse toute modification ou suppression d'un mouvement,
/// et toute suppression d'un solde (pas de suppression physique des données métier).
/// </summary>
internal sealed class GardeJournalImmuable : SaveChangesInterceptor
{
    public static readonly GardeJournalImmuable Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verifier(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Verifier(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Verifier(DbContext? contexte)
    {
        if (contexte is null)
        {
            return;
        }

        foreach (var entree in contexte.ChangeTracker.Entries())
        {
            var interdit = entree switch
            {
                { Entity: MouvementStock, State: EntityState.Modified or EntityState.Deleted } => true,
                { Entity: StockSite or StockEmplacement, State: EntityState.Deleted } => true,
                _ => false,
            };

            if (interdit)
            {
                throw new InvalidOperationException(
                    $"RG01 : {entree.Entity.GetType().Name} ne peut pas être {(entree.State == EntityState.Deleted ? "supprimé" : "modifié")}.");
            }
        }
    }
}
