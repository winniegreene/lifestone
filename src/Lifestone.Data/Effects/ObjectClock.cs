namespace Lifestone.Data.Effects;

/// <summary>One step of an <see cref="ObjectClock"/>.</summary>
/// <param name="Quantum">Seconds to advance the object by.</param>
/// <param name="Time">The clock value at the end of the step.</param>
public readonly record struct ObjectQuantum(double Quantum, double Time);

/// <summary>
/// The time slicing of retail <c>CPhysicsObj::update_object</c> for one object: it turns the caller's clock into the
/// quanta the object is stepped by. Frame-stepped objects take an explicit <c>now</c> (monotonic seconds advanced once
/// per frame by the host) and hand it to <see cref="Advance"/>.
/// </summary>
/// <remarks>
/// <para>
/// Re-expresses the slicing of <c>CPhysicsObj::update_object</c> (acclient 11.4186 PORTAL/cphysobj/CPhysicsObj.c;
/// function name cited, nothing copied), as the M1 spell viewer's <c>EffectActor</c> did before this was extracted:
/// an elapsed time ≤ ε (<see cref="Epsilon"/>) or &gt; <see cref="MaxElapsed"/> (2 s) is skipped (the clock jumps to
/// <c>now</c>); a gap of at most <see cref="MaxQuantum"/> (0.2 s) is one step; a longer gap is split into steps of
/// <see cref="MaxQuantum"/>, and a final remainder above <see cref="MinQuantum"/> (1/30 s) is one more step while a
/// smaller one is carried to the next call (the clock stops at the end of the last step).
/// </para>
/// <para>
/// Allocation-free: <see cref="Advance"/> returns a struct enumerable, so <c>foreach (var (quantum, time) in
/// clock.Advance(now))</c> costs nothing per frame. <see cref="UpdateTime"/> moves to the end of each step as the step is
/// taken, so the pending steps are the clock's own: enumerating the result twice, or a second <see cref="Advance"/>,
/// never steps the same time twice.
/// </para>
/// </remarks>
public sealed class ObjectClock
{
    /// <summary>Largest step in seconds (retail 0.2).</summary>
    public const double MaxQuantum = 1.0 / 5.0;

    /// <summary>A split gap's remainder at or below this many seconds is carried to the next call (retail 1/30).</summary>
    public const double MinQuantum = 1.0 / 30.0;

    /// <summary>A gap longer than this many seconds is skipped, not stepped (retail 2.0).</summary>
    public const double MaxElapsed = 2.0;

    /// <summary>A gap at or below this many seconds is skipped (retail <c>F_EPSILON</c>, 0.0002 as a float).</summary>
    public const double Epsilon = 0.00019999999;

    private double _remaining;
    private bool _split;
    private bool _stepping;

    /// <summary>Starts the clock at <paramref name="now"/>.</summary>
    public ObjectClock(double now)
    {
        UpdateTime = now;
    }

    /// <summary>The clock value at the end of the last step taken (or of the last skip).</summary>
    public double UpdateTime { get; private set; }

    /// <summary>
    /// Plans the steps from <see cref="UpdateTime"/> to <paramref name="now"/> and returns them for a <c>foreach</c>.
    /// A skipped gap moves <see cref="UpdateTime"/> to <paramref name="now"/> at once and yields no step. Any steps
    /// still pending from an earlier call are dropped.
    /// </summary>
    public Steps Advance(double now)
    {
        var elapsed = now - UpdateTime;
        if (elapsed <= Epsilon || elapsed > MaxElapsed)
        {
            _stepping = false;
            UpdateTime = now;
            return default;
        }

        _remaining = elapsed;
        _split = elapsed > MaxQuantum;
        _stepping = true;
        return new Steps(this);
    }

    /// <summary>Takes the next pending step, moving <see cref="UpdateTime"/> to its end.</summary>
    private bool TryStep(out ObjectQuantum step)
    {
        step = default;
        if (!_stepping)
        {
            return false;
        }

        double quantum;
        if (!_split)
        {
            quantum = _remaining;
            _stepping = false;
        }
        else if (_remaining > MaxQuantum)
        {
            quantum = MaxQuantum;
            _remaining -= MaxQuantum;
        }
        else if (_remaining > MinQuantum)
        {
            quantum = _remaining;
            _stepping = false;
        }
        else
        {
            _stepping = false; // the remainder is carried: UpdateTime stays at the end of the last step
            return false;
        }

        UpdateTime += quantum;
        step = new ObjectQuantum(quantum, UpdateTime);
        return true;
    }

    /// <summary>The steps of one <see cref="Advance"/>: a struct enumerable and its own enumerator.</summary>
    public struct Steps
    {
        private readonly ObjectClock? _clock;
        private ObjectQuantum _current;

        internal Steps(ObjectClock clock)
        {
            _clock = clock;
            _current = default;
        }

        /// <summary>The step taken by the last successful <see cref="MoveNext"/>.</summary>
        public readonly ObjectQuantum Current => _current;

        /// <summary>Returns the enumerator (this value).</summary>
        public readonly Steps GetEnumerator() => this;

        /// <summary>Takes the next step from the clock.</summary>
        public bool MoveNext() => _clock is not null && _clock.TryStep(out _current);
    }
}
