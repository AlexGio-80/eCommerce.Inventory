namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

/// <summary>
/// Dice al <see cref="CardTraderRateLimiter"/> che le chiamate fatte dentro lo scope servono a
/// qualcuno che sta aspettando davanti allo schermo, come la maschera "Nuovo Prodotto".
///
/// È un valore di contesto (<see cref="AsyncLocal{T}"/>) e non un parametro, perché fra il
/// controller e il limitatore ci sono il client e la cache delle offerte, che non devono sapere
/// chi li chiama. Tutto il resto, notturna, riprezzi e sincronizzazioni, è lavoro in background.
/// </summary>
public static class CardTraderRequestPriority
{
    private static readonly AsyncLocal<bool> Interactive = new();

    public static bool IsInteractive => Interactive.Value;

    /// <summary>Segna come interattive le chiamate fino alla chiusura dello scope.</summary>
    public static IDisposable BeginInteractive()
    {
        var previous = Interactive.Value;
        Interactive.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public Scope(bool previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interactive.Value = _previous;
        }
    }
}
