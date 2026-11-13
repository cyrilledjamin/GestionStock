namespace GestionStock.Stock.Contracts;

/// <summary>
/// Règle de gestion du stock violée, telle que la voient les autres modules.
/// Le <see cref="Code"/> (ex. « Stock.StockInsuffisant ») est la clé de traduction fr / en.
/// </summary>
public sealed class ExceptionStock : Exception
{
    public ExceptionStock(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public ExceptionStock()
        : this("Stock.Inconnu", "Erreur du module Stock.")
    {
    }

    public ExceptionStock(string message)
        : this("Stock.Inconnu", message)
    {
    }

    public ExceptionStock(string message, Exception innerException)
        : this("Stock.Inconnu", message, innerException)
    {
    }

    public string Code { get; }
}
