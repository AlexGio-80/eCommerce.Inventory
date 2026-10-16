using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

/// <summary>
/// Limite di 20 richieste al minuto verso Card Trader, condiviso da tutta l'applicazione
/// (<b>singleton</b>), con la precedenza alle richieste interattive.
///
/// Il limitatore standard a finestra fissa serviva in ordine d'arrivo, e chi lavora in sequenza
/// riprende un permesso appena ottenuto il precedente: il 15/10, dopo ogni blocco di 20 carte
/// pubblicate, il riprezzo bruciava i 20 permessi della finestra in una decina di secondi e la
/// maschera "Nuovo Prodotto" restava in coda fino a 55 secondi per carta. Qui le richieste segnate
/// con <see cref="CardTraderRequestPriority"/> passano davanti a quelle in background e, finché
/// la maschera è in uso, il background lascia libere <see cref="InteractiveReserve"/> richieste per
/// finestra. Quando nessuno usa la maschera il background ha di nuovo tutto il limite.
///
/// Non c'è più un tetto alla coda: prima, oltre 10 richieste in attesa, il limitatore lanciava
/// un'eccezione che il client trasformava in "nessuna offerta", cioè un mercato vuoto inventato.
/// </summary>
public class CardTraderRateLimiter : IDisposable
{
    public const int PermitLimit = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Richieste per finestra che il background non può usare mentre la maschera è in uso.</summary>
    public const int InteractiveReserve = 5;

    /// <summary>Per quanto tempo dopo l'ultima richiesta interattiva la riserva resta attiva.</summary>
    public static readonly TimeSpan InteractiveActivityWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan LoggedWait = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly ILogger<CardTraderRateLimiter> _logger;
    private readonly object _lock = new();
    private readonly LinkedList<Waiter> _interactiveQueue = new();
    private readonly LinkedList<Waiter> _backgroundQueue = new();

    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _used;
    private DateTimeOffset? _lastInteractiveAt;
    private ITimer? _timer;
    private bool _disposed;

    public CardTraderRateLimiter(ILogger<CardTraderRateLimiter> logger) : this(logger, TimeProvider.System) { }

    public CardTraderRateLimiter(ILogger<CardTraderRateLimiter> logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
    }

    public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interactive = CardTraderRequestPriority.IsInteractive;
        Waiter waiter;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var now = _time.GetUtcNow();
            RollWindow(now);
            if (interactive) _lastInteractiveAt = now;

            // Chi arriva non scavalca chi aspetta già con la stessa precedenza, e il background
            // non scavalca mai una richiesta interattiva in attesa.
            var queue = interactive ? _interactiveQueue : _backgroundQueue;
            var nobodyAhead = queue.Count == 0 && (interactive || _interactiveQueue.Count == 0);

            if (nobodyAhead && CanTake(interactive, now))
            {
                _used++;
                return ValueTask.CompletedTask;
            }

            waiter = new Waiter(interactive, now);
            waiter.Node = queue.AddLast(waiter);
            ScheduleTimer(now);
        }

        if (cancellationToken.CanBeCanceled)
        {
            waiter.Registration = cancellationToken.Register(() => Cancel(waiter, cancellationToken));
        }

        return new ValueTask(waiter.Completion.Task);
    }

    private bool CanTake(bool interactive, DateTimeOffset now)
    {
        var reserveActive = _lastInteractiveAt is { } last && now - last < InteractiveActivityWindow;
        var limit = interactive || !reserveActive ? PermitLimit : PermitLimit - InteractiveReserve;
        return _used < limit;
    }

    private void RollWindow(DateTimeOffset now)
    {
        if (now - _windowStart < Window) return;
        _windowStart = now;
        _used = 0;
    }

    private void ScheduleTimer(DateTimeOffset now)
    {
        var due = _windowStart + Window - now;
        if (due < TimeSpan.FromMilliseconds(1)) due = TimeSpan.FromMilliseconds(1);

        _timer ??= _time.CreateTimer(_ => OnWindowElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void OnWindowElapsed()
    {
        var granted = new List<Waiter>();
        DateTimeOffset now;

        lock (_lock)
        {
            if (_disposed) return;

            now = _time.GetUtcNow();
            RollWindow(now);

            Drain(_interactiveQueue, interactive: true, now, granted);
            if (_interactiveQueue.Count == 0)
            {
                Drain(_backgroundQueue, interactive: false, now, granted);
            }

            if (_interactiveQueue.Count > 0 || _backgroundQueue.Count > 0)
            {
                ScheduleTimer(now);
            }
        }

        foreach (var waiter in granted)
        {
            waiter.Registration.Dispose();

            var waited = now - waiter.QueuedAt;
            if (waited >= LoggedWait)
            {
                // Solo le attese della maschera a livello Information: quelle del background sono la
                // norma durante la notturna e riempirebbero il log senza dire nulla.
                _logger.Log(waiter.Interactive ? LogLevel.Information : LogLevel.Debug,
                    "Attesa di {Seconds:0.0} s per il limite di Card Trader ({Priority})",
                    waited.TotalSeconds, waiter.Interactive ? "interattiva" : "background");
            }

            waiter.Completion.TrySetResult();
        }
    }

    private void Drain(LinkedList<Waiter> queue, bool interactive, DateTimeOffset now, List<Waiter> granted)
    {
        while (queue.First is { } node && CanTake(interactive, now))
        {
            queue.RemoveFirst();
            node.Value.Node = null;
            _used++;
            granted.Add(node.Value);
        }
    }

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (waiter.Node?.List is not { } list) return;
            list.Remove(waiter.Node);
            waiter.Node = null;
        }

        waiter.Completion.TrySetCanceled(cancellationToken);
    }

    public void Dispose()
    {
        List<Waiter> pending;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            pending = _interactiveQueue.Concat(_backgroundQueue).ToList();
            _interactiveQueue.Clear();
            _backgroundQueue.Clear();
        }

        foreach (var waiter in pending)
        {
            waiter.Registration.Dispose();
            waiter.Completion.TrySetCanceled();
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Waiter
    {
        public Waiter(bool interactive, DateTimeOffset queuedAt)
        {
            Interactive = interactive;
            QueuedAt = queuedAt;
        }

        public bool Interactive { get; }
        public DateTimeOffset QueuedAt { get; }
        public LinkedListNode<Waiter>? Node { get; set; }
        public CancellationTokenRegistration Registration { get; set; }

        // Le continuazioni non devono girare dentro il ciclo che assegna i permessi.
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
