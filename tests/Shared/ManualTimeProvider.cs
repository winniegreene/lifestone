namespace Lifestone.Tests.Shared;

/// <summary>
/// A <see cref="TimeProvider"/> whose time moves only when a test moves it (<see cref="Advance"/>,
/// <see cref="SetUtcNow"/>), for code that takes its timeouts, deadlines and clocks from a <see cref="TimeProvider"/>
/// (the M2 clock rule: network, session and action timeouts). Hand-written so no test-time package is needed.
/// </summary>
/// <remarks>
/// <para><b>Timers fire on <see cref="Advance"/>.</b> A timer from <see cref="CreateTimer"/> (and so
/// <c>Task.Delay(TimeSpan, TimeProvider)</c>, <c>new CancellationTokenSource(TimeSpan, TimeProvider)</c> and
/// <c>Task.WaitAsync(TimeSpan, TimeProvider)</c>) fires only inside <see cref="Advance"/> or <see cref="SetUtcNow"/>,
/// synchronously on the calling thread, never on the thread that creates it. Advancing past several due times fires
/// the timers in due order, each with the clock set to its own due time; timers due at the same instant fire in the
/// order they were created. A periodic timer fires once per elapsed period. A timer due now (due time zero) fires on the
/// next advance, even <c>Advance(TimeSpan.Zero)</c>. Callbacks may create, change or dispose timers; a timer a callback
/// makes due within the same advance fires in that advance.</para>
/// <para>Thread-safe; callbacks run outside the internal lock.</para>
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>The wall-clock time a provider starts at unless told otherwise: 2000-01-01 00:00 UTC.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _start;
    private long _elapsedTicks;

    /// <summary>A provider at <see cref="DefaultStart"/>.</summary>
    public ManualTimeProvider()
        : this(DefaultStart)
    {
    }

    /// <summary>A provider whose wall clock starts at <paramref name="start"/> (its timestamps start at 0).</summary>
    public ManualTimeProvider(DateTimeOffset start)
    {
        _start = start.ToUniversalTime();
    }

    /// <summary>Time advanced since creation.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromTicks(_elapsedTicks);
            }
        }
    }

    /// <summary>Timers created and not disposed (fired one-shot timers count until disposed, as system timers do).</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>Timestamps are <see cref="TimeSpan"/> ticks.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>UTC, so local and UTC times agree in every test.</summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _start.AddTicks(_elapsedTicks);
        }
    }

    /// <summary>Ticks advanced since creation.</summary>
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _elapsedTicks;
        }
    }

    /// <summary>Moves time forward by <paramref name="delta"/>, firing every timer that falls due on the way.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        long target;
        lock (_gate)
        {
            target = _elapsedTicks + delta.Ticks;
        }

        AdvanceTo(target);
    }

    /// <summary>Moves the wall clock forward to <paramref name="utcNow"/>, firing the timers that fall due on the way.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="utcNow"/> is before the current time (time never runs backwards).</exception>
    public void SetUtcNow(DateTimeOffset utcNow)
    {
        long target;
        lock (_gate)
        {
            target = (utcNow.ToUniversalTime() - _start).Ticks;
            if (target < _elapsedTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(utcNow), utcNow, $"time never runs backwards: the clock is at {_start.AddTicks(_elapsedTicks):o}");
            }
        }

        AdvanceTo(target);
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // Validate before registering, as TimeProvider.System does, so a bad interval leaves no dead timer behind.
        ValidateInterval(dueTime, nameof(dueTime));
        ValidateInterval(period, nameof(period));
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    private void AdvanceTo(long targetTicks)
    {
        while (true)
        {
            ManualTimer? next = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.DueTicks <= targetTicks && (next is null || timer.DueTicks < next.DueTicks))
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    // A nested advance (from a callback) may already have gone further: time never runs backwards.
                    _elapsedTicks = Math.Max(_elapsedTicks, targetTicks);
                    return;
                }

                _elapsedTicks = Math.Max(_elapsedTicks, next.DueTicks);
                next.DueTicks = next.PeriodTicks > 0 ? next.DueTicks + next.PeriodTicks : ManualTimer.Disabled;
            }

            next.Fire();
        }
    }

    private static void ValidateInterval(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(name, value, "must be non-negative or Timeout.InfiniteTimeSpan");
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public const long Disabled = long.MaxValue;

        private bool _disposed;

        /// <summary>Elapsed ticks at which the timer fires next, or <see cref="Disabled"/>; guarded by the owner's lock.</summary>
        public long DueTicks { get; set; } = Disabled;

        /// <summary>The period in ticks; 0 for a one-shot timer. Guarded by the owner's lock.</summary>
        public long PeriodTicks { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateInterval(dueTime, nameof(dueTime));
            ValidateInterval(period, nameof(period));
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                DueTicks = dueTime == Timeout.InfiniteTimeSpan ? Disabled : owner._elapsedTicks + dueTime.Ticks;
                PeriodTicks = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                DueTicks = Disabled;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
