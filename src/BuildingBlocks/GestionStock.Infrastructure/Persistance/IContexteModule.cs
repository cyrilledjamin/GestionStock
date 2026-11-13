using Microsoft.EntityFrameworkCore;

namespace GestionStock.Infrastructure.Persistance;

/// <summary>Enveloppe d'un DbContext de module, pour que l'unité de travail les enrôle tous.</summary>
internal interface IContexteModule
{
    DbContext Contexte { get; }
}

internal sealed class ContexteModule(DbContext contexte) : IContexteModule
{
    public DbContext Contexte { get; } = contexte;
}
