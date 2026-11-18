using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Threat;

/// <summary>What <see cref="ThreatShadowLane.Note"/> asks its caller to write.</summary>
public enum ThreatShadowChange : byte
{
    /// <summary>Nothing: the pair agrees as before, differs as before, or the change falls inside the lane's line gap.</summary>
    None,

    /// <summary>The detector and the board differ, and the pair is not the one last written: one line.</summary>
    Differs,

    /// <summary>They agree again after a difference: one line.</summary>
    AgreesAgain,
}

/// <summary>A lane's counts at one moment, for an owner that reports its own share (<see cref="ThreatShadowLane.Mark"/>).</summary>
/// <param name="Agree">The asks that agreed so far.</param>
/// <param name="Differ">The asks that differed so far.</param>
public readonly record struct ThreatShadowMark(long Agree, long Differ);

/// <summary>
/// M6 16 E2a (docs/automation-engine.md section 3, "shadow first"): one detector beside the threat board. The detector decides as it always did; where it is asked, the board is asked the same question, and this lane counts the
/// asks that agree and that differ and says when a line is due: on a change only, never every frame. The lane is the session's (<see cref="ThreatBoard.Lane"/>), so its counts outlive the scenario that asks. Report-only:
/// nothing here is read by any decision. Allocation-free until a line is written.
/// </summary>
/// <param name="detector">The detector's word in the lines.</param>
public sealed class ThreatShadowLane(string detector)
{
    /// <summary>The token every line of the shadow starts with.</summary>
    public const string Token = "threat-shadow: ";

    /// <summary>The summary field of the asks that agreed.</summary>
    public const string AgreeField = "threat_shadow_agree";

    /// <summary>The summary field of the asks that differed.</summary>
    public const string DifferField = "threat_shadow_differ";

    /// <summary>The least seconds between two lines of one lane (Lifestone's own: a pair that flaps writes one line a second at most; the changes not written are counted and named in the next line).</summary>
    public const double LineGapSeconds = 1.0;

    private double _askedAt = double.NaN;
    private bool _differing;
    private bool _written;
    private long _detectorKey;
    private long _boardKey;
    private double _differSince;
    private long _differAsks;
    private double _lineAt = double.NegativeInfinity;

    /// <summary>The detector's word in the lines.</summary>
    public string Detector { get; } = detector;

    /// <summary>The asks in which the detector and the board agreed (<c>threat_shadow_agree=</c>).</summary>
    public long Agree { get; private set; }

    /// <summary>The asks in which they differed (<c>threat_shadow_differ=</c>).</summary>
    public long Differ { get; private set; }

    /// <summary>The lines this lane asked for.</summary>
    public long Lines { get; private set; }

    /// <summary>The changes that fell inside <see cref="LineGapSeconds"/> of the last line and were not written (named in the next line).</summary>
    public long Unwritten { get; private set; }

    /// <summary>True when the shadow failed and turned itself off (<see cref="Fail"/>): nothing is asked or counted any more.</summary>
    public bool Off { get; private set; }

    /// <summary>The seconds the current difference has lasted, or the last one lasted from its first ask to the first ask that agreed again.</summary>
    public double DifferSeconds { get; private set; }

    /// <summary>The asks of the current (or the last) difference.</summary>
    public long DifferAsks => _differAsks;

    /// <summary>The counts now.</summary>
    public ThreatShadowMark Mark() => new(Agree, Differ);

    /// <summary>The two summary fields for the asks since <paramref name="since"/>: <c>threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt;</c>.</summary>
    /// <param name="since">The owner's mark.</param>
    public string Fields(ThreatShadowMark since) => FieldsOf(Agree - since.Agree, Differ - since.Differ);

    /// <summary>The two summary fields for the counts given.</summary>
    /// <param name="agree">The asks that agreed.</param>
    /// <param name="differ">The asks that differed.</param>
    public static string FieldsOf(long agree, long differ) => string.Create(CultureInfo.InvariantCulture, $"{AgreeField}={agree} {DifferField}={differ}");

    /// <summary>True when this ask is new: the lane is on and <paramref name="now"/> is not the clock of the last ask (a detector asked several times in one frame is compared once: the state does not change inside a frame).</summary>
    /// <param name="now">The session clock.</param>
    public bool Due(double now) => !Off && now != _askedAt;

    /// <summary>
    /// Counts one ask and says whether a line is due. <paramref name="agree"/> is the caller's verdict on the two answers' values; the keys name each side's answer coarsely (what answered and which creature, never a distance
    /// or a time that moves every frame), so a difference is written when it appears and when either side's answer changes, not while it stands.
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <param name="agree">True when the detector's answer and the board's are the same.</param>
    /// <param name="detectorKey">The detector's answer, coarsely.</param>
    /// <param name="boardKey">The board's answer, coarsely.</param>
    public ThreatShadowChange Note(double now, bool agree, long detectorKey, long boardKey)
    {
        _askedAt = now;
        if (agree)
        {
            Agree++;
            if (!_differing)
            {
                return ThreatShadowChange.None;
            }

            _differing = false;
            DifferSeconds = now - _differSince;
            if (!_written)
            {
                Unwritten++; // the difference ended inside the gap, before its line: counted, and named in the next line
                return ThreatShadowChange.None;
            }

            (_written, _lineAt) = (false, now);
            Lines++;
            return ThreatShadowChange.AgreesAgain;
        }

        Differ++;
        var fresh = !_differing;
        if (fresh)
        {
            (_differing, _written, _differSince, _differAsks) = (true, false, now, 0);
        }

        _differAsks++;
        DifferSeconds = now - _differSince;
        var same = !fresh && detectorKey == _detectorKey && boardKey == _boardKey;
        if (same && _written)
        {
            return ThreatShadowChange.None; // the difference stands as written
        }

        if (!same)
        {
            if (!fresh && !_written)
            {
                Unwritten++; // the pair before this one changed inside the gap, before its line
            }

            (_detectorKey, _boardKey, _written) = (detectorKey, boardKey, false);
        }

        if (now - _lineAt < LineGapSeconds)
        {
            return ThreatShadowChange.None; // inside the gap: written at its end if the pair still stands
        }

        (_written, _lineAt) = (true, now);
        Lines++;
        return ThreatShadowChange.Differs;
    }

    /// <summary>The line of a difference: <c>threat-shadow: &lt;detector&gt;: &lt;what it said&gt;, the board says &lt;what&gt;</c>, with the changes not written since the last line when there are any.</summary>
    /// <param name="detectorSaid">The detector's answer in words.</param>
    /// <param name="boardSays">The board's answer in words.</param>
    public string DiffersLine(string detectorSaid, string boardSays)
    {
        var skipped = TakeUnwritten();
        return string.Create(CultureInfo.InvariantCulture, $"{Token}{Detector}: {detectorSaid}, the board says {boardSays}{skipped}");
    }

    /// <summary>The line of a difference's end: <c>threat-shadow: &lt;detector&gt;: agrees again after &lt;s&gt; s (&lt;n&gt; ask(s) differed)</c>.</summary>
    public string AgreesLine()
    {
        var skipped = TakeUnwritten();
        return string.Create(CultureInfo.InvariantCulture, $"{Token}{Detector}: agrees again after {DifferSeconds:0.#} s ({_differAsks} ask(s) differed){skipped}");
    }

    /// <summary>The shadow threw: it is turned off for the session and says so once, loudly. A shadow must never cost the detector it stands beside anything.</summary>
    /// <param name="error">What was thrown.</param>
    /// <param name="log">The owner's log.</param>
    /// <param name="category">The owner's log category.</param>
    public void Fail(Exception error, ILogSink log, string category)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(log);
        if (Off)
        {
            return;
        }

        Off = true;
        log.Error(category, $"{Token}{Detector}: TODO(threat-shadow-failed): the shadow threw {error.GetType().Name}: {error.Message}; it is off for this session ({AgreeField}={Agree} {DifferField}={Differ} so far); the detector decides as ever");
    }

    private string TakeUnwritten()
    {
        if (Unwritten == _unwrittenSaid)
        {
            return string.Empty;
        }

        var word = string.Create(CultureInfo.InvariantCulture, $" (+{Unwritten - _unwrittenSaid} change(s) inside {LineGapSeconds:0.#} s not written)");
        _unwrittenSaid = Unwritten;
        return word;
    }

    private long _unwrittenSaid;
}
