using System.Diagnostics;

namespace Lifestone.Client.Scheduling;

/// <summary>One step of a <see cref="StagedWork"/>: a name for the log and the work, which returns false when the whole build cannot go on.</summary>
/// <param name="Name">The step's name, one word with underscores (the log field is <c>&lt;name&gt;_ms</c>).</param>
/// <param name="Run">The step's work; false ends the build as failed (the step has logged why).</param>
public readonly record struct StagedStep(string Name, Func<bool> Run);

/// <summary>
/// A build cut into a fixed list of steps that run one at a time, each in a frame of its own (M5 item 8, <c>m5-8-login-walk-and-frames</c>: the player's build,
/// 15-25 ms in one frame at login, once 43 ms, run as three steps on three consecutive frames). The same list run in one call (<see cref="RunAll"/>) is the
/// single build it replaces: the steps run in order, each once. Engine-agnostic; the host decides when the next frame comes (<see cref="RunNext"/> per frame).
/// </summary>
/// <remarks>
/// <para>Each step's wall time is kept (<see cref="Timings"/>) and offered as log fields (<see cref="TimingFields"/>), so the login's line names the part that cost the most.</para>
/// <para>A step that throws leaves the build failed and rethrows; a cancelled build runs no further step (<see cref="Cancel"/> runs its clean-up once).</para>
/// </remarks>
public sealed class StagedWork
{
    private readonly StagedStep[] _steps;
    private readonly double[] _milliseconds;
    private readonly Func<double> _nowMs;
    private readonly Action? _onCancel;

    /// <summary>A staged build of <paramref name="steps"/>, run in the order given.</summary>
    /// <param name="steps">The steps (at least one, names unique).</param>
    /// <param name="onCancel">Frees what the steps already built when <see cref="Cancel"/> comes before the last step; null for nothing.</param>
    /// <param name="nowMs">The clock in milliseconds (tests give their own); null for the monotonic stopwatch.</param>
    public StagedWork(IReadOnlyList<StagedStep> steps, Action? onCancel = null, Func<double>? nowMs = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("a staged build needs at least one step", nameof(steps));
        }

        _steps = [.. steps];
        if (_steps.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != _steps.Length)
        {
            throw new ArgumentException("the steps' names must be unique", nameof(steps));
        }

        _milliseconds = new double[_steps.Length];
        _onCancel = onCancel;
        _nowMs = nowMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>The number of steps.</summary>
    public int Count => _steps.Length;

    /// <summary>The steps that have run (a failed step counts: it ran).</summary>
    public int Done { get; private set; }

    /// <summary>True once every step has run and none failed or was cancelled.</summary>
    public bool IsComplete => Done == _steps.Length && !IsFailed && !IsCancelled;

    /// <summary>True when a step returned false or threw: no further step runs.</summary>
    public bool IsFailed { get; private set; }

    /// <summary>True after <see cref="Cancel"/> came before the build finished.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>True while a step is still to run (not failed, not cancelled, not complete).</summary>
    public bool HasNext => !IsFailed && !IsCancelled && Done < _steps.Length;

    /// <summary>The name of the step <see cref="RunNext"/> runs next, or null.</summary>
    public string? NextName => HasNext ? _steps[Done].Name : null;

    /// <summary>The step names in order.</summary>
    public IReadOnlyList<string> Names => [.. _steps.Select(s => s.Name)];

    /// <summary>The wall time of each step that has run, in milliseconds, in step order.</summary>
    public IReadOnlyList<double> Timings => [.. _milliseconds.Take(Done)];

    /// <summary>The wall time of every step that has run, in milliseconds.</summary>
    public double TotalMilliseconds => _milliseconds.Take(Done).Sum();

    /// <summary>
    /// Runs the next step (one frame's share) and returns true when it ran and the build can go on or has just completed; false when it failed, the build was cancelled
    /// or there was no step left. Check <see cref="HasNext"/> afterwards for whether another frame is needed.
    /// </summary>
    public bool RunNext()
    {
        if (!HasNext)
        {
            return false;
        }

        var index = Done;
        var started = _nowMs();
        var ok = false;
        try
        {
            ok = _steps[index].Run();
        }
        finally
        {
            _milliseconds[index] = _nowMs() - started;
            Done = index + 1;
            if (!ok)
            {
                IsFailed = true;
            }
        }

        return ok;
    }

    /// <summary>Runs every remaining step in this call (the single-frame build: the offline player, tests). False when a step failed.</summary>
    public bool RunAll()
    {
        while (HasNext)
        {
            if (!RunNext())
            {
                return false;
            }
        }

        return IsComplete;
    }

    /// <summary>Stops the build: no step runs after this, and the clean-up runs once when the build was not complete (a log off or a re-create between two steps).</summary>
    public void Cancel()
    {
        if (IsCancelled || IsComplete)
        {
            return;
        }

        IsCancelled = true;
        _onCancel?.Invoke();
    }

    /// <summary>
    /// The log fields of the steps that ran: <c>build_steps</c> (done of count, e.g. "3/3") then <c>&lt;name&gt;_ms</c> for each, in order. One call per login, so the allocation is no concern.
    /// </summary>
    public (string Key, object? Value)[] TimingFields() =>
    [
        ("build_steps", $"{Done}/{_steps.Length}"),
        .. Enumerable.Range(0, Done).Select(i => ($"{_steps[i].Name}_ms", (object?)_milliseconds[i])),
    ];
}
