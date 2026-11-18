using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Hosting;

/// <summary>
/// The fault guard of a host's per-frame loop (M5 item 0 c2; <c>m5-nav-one-waypoint (closed)</c>'s last gate wrote 8,642 identical traces because nothing
/// caught an exception thrown in a frame): the host wraps its frame in <c>try</c>/<c>catch</c> and hands the exception to <see cref="Catch"/>. The first
/// fault is reported ONCE, as an error in the runner's category ("&lt;runner&gt;: &lt;stage&gt; threw &lt;ExceptionType&gt;: &lt;message&gt;") and then the whole exception
/// (type, message, stack) once, and the guard trips: <see cref="Reason"/> is the one-line text for the runner's own fail path (log off if logged in, a verdict
/// of ok=false naming it, a non-zero quit). A later fault is the same run failing again while it ends: it is counted, never reported, and the runner finishes.
/// Engine-free, game thread only.
/// </summary>
/// <param name="runner">The runner's name as the report gives it (for example <c>JourneyLegRunner</c>).</param>
/// <param name="category">The runner's log category.</param>
/// <param name="log">Where the report goes.</param>
public sealed class LegFaultGuard(string runner, string category, ILogSink log)
{
    private readonly string _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly string _category = category ?? throw new ArgumentNullException(nameof(category));
    private readonly ILogSink _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>True once a fault was reported: the runner is failing, and its frames run no step.</summary>
    public bool Tripped { get; private set; }

    /// <summary>The first fault as one line, "&lt;runner&gt;: &lt;stage&gt; threw &lt;ExceptionType&gt;: &lt;message&gt;", or null before any.</summary>
    public string? Reason { get; private set; }

    /// <summary>Faults after the first (counted, never reported).</summary>
    public int Repeats { get; private set; }

    /// <summary>Whether the guard catches <paramref name="exception"/>: everything but running out of memory (which nothing can recover).</summary>
    public static bool Guards(Exception exception) => exception is not OutOfMemoryException;

    /// <summary>
    /// Takes one fault. Returns true for the first (reported now, the guard trips: the caller starts its fail path with <see cref="Reason"/>); false for a
    /// later one (nothing is reported: the caller ends the run now).
    /// </summary>
    /// <param name="stage">Where the exception came from (for example <c>_Process</c> or the runner's stage).</param>
    /// <param name="exception">The exception.</param>
    public bool Catch(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Tripped)
        {
            Repeats++;
            return false;
        }

        Tripped = true;
        Reason = string.Create(
            CultureInfo.InvariantCulture,
            $"{_runner}: {stage} threw {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
        _log.Error(_category, Reason);
        _log.Error(_category, _runner + ": the stack of the fault above (reported once): " + exception);
        return true;
    }
}
