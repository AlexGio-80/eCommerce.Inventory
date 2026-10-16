namespace eCommerce.Inventory.Tests.Unit;

/// <summary>
/// Orologio che avanza solo quando lo dice il test, con i timer che scattano all'avanzare.
/// Serve a provare limiti e scadenze al minuto senza aspettare davvero.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock) return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += by;
            due = _timers.Where(t => t.DueAt is { } at && at <= _now).ToList();
            foreach (var timer in due) timer.DueAt = null;
        }

        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._lock)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
            }
            return true;
        }

        public void Fire() => _callback(_state);

        public void Dispose()
        {
            lock (_owner._lock)
            {
                DueAt = null;
                _owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
