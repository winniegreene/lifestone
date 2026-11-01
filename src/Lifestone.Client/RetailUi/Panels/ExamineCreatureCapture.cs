using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The failed-roll live capture (M5 7 D137, <c>--examine-capture-creatures=N</c>): each new creature the fight aims at (<see cref="IActionApi.LastAttackTarget"/>) is inspected through the toolbar Inspect's own path
/// (<see cref="UiTargetMode.ExamineObject"/>, <c>ClientUISystem::ExamineObject</c> 11.6096 0x005657B0: the examination window opens and sends one Identify), until N answers have come in all. ACE answers a repeat of a failed roll
/// within 5 s as failed without a roll (<c>Player.cs:289</c>, @95ef426d), so a failed creature is asked again <see cref="RetrySeconds"/> after its answer, at most <see cref="MaxAsksPerCreature"/> times; a success ends that creature.
/// The window logs its two lines per answer (<c>examine answer</c>, <c>examine shown</c>); this logs one <c>examine capture</c> line each.
/// </summary>
/// <remarks>
/// It never holds the fight: a step reads two numbers and at most sends one Identify through the Query lane (ACE answers in milliseconds, with no busy or range check); nothing waits. A creature that is gone is not asked again.
/// Game thread only (play mode steps it each frame).
/// </remarks>
public sealed class ExamineCreatureCapture : IDisposable
{
    /// <summary>Seconds after a failed answer before the creature is asked again (ACE's 5 s, and a little).</summary>
    public const double RetrySeconds = 5.2;

    /// <summary>The most times one creature is asked.</summary>
    public const int MaxAsksPerCreature = 5;

    private const string LogCategory = "examine";

    private readonly IGameSessionView _session;
    private readonly UiTargetMode _target;
    private readonly ILogSink _log;
    private readonly int _maxAnswers;
    private readonly Dictionary<uint, Entry> _creatures = [];
    private readonly IDisposable _subscription;

    /// <summary>Starts the capture over <paramref name="session"/>, asking through <paramref name="target"/>, for up to <paramref name="maxAnswers"/> answers.</summary>
    public ExamineCreatureCapture(IGameSessionView session, UiTargetMode target, ILogSink log, int maxAnswers)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAnswers, 1);
        _session = session;
        _target = target;
        _log = log;
        _maxAnswers = maxAnswers;
        _subscription = session.Events.Subscribe<AppraisalReceived>(OnAnswer);
        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"examine capture on: each new creature the fight aims at is inspected, up to {maxAnswers} answers; a failed roll is asked again after {RetrySeconds} s, at most {MaxAsksPerCreature} times"));
    }

    /// <summary>The answers taken so far.</summary>
    public int Answers { get; private set; }

    /// <summary>The failed answers taken so far.</summary>
    public int Failed { get; private set; }

    /// <summary>The asks sent so far.</summary>
    public int Asks { get; private set; }

    /// <summary>True until <see cref="Answers"/> reaches the limit.</summary>
    public bool Active => Answers < _maxAnswers;

    /// <summary>Once a frame: a new creature the fight aims at is asked; a failed one whose retry is due is asked again.</summary>
    public void Step()
    {
        if (!Active)
        {
            return;
        }

        var now = _session.Clock.Now;
        var aimed = _session.Actions.LastAttackTarget;
        if (aimed != 0 && !_creatures.ContainsKey(aimed) && IsCreature(aimed))
        {
            _creatures[aimed] = new Entry();
            Ask(aimed, now);
            return; // one ask a frame
        }

        foreach (var (guid, entry) in _creatures)
        {
            if (entry.RetryAt is { } due && now >= due && entry.Asks < MaxAsksPerCreature && _session.State.Objects.TryGet(guid, out _))
            {
                Ask(guid, now);
                return;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();

    private bool IsCreature(uint guid) =>
        guid != _session.State.Self.Guid && _session.State.Objects.TryGet(guid, out var obj) && (obj.ItemType & GameExaminePanel.CreatureItemType) != 0;

    private void Ask(uint guid, double now)
    {
        var entry = _creatures[guid];
        entry.Asks++;
        entry.RetryAt = null;
        entry.Waiting = true;
        Asks++;
        _target.ExamineObject(guid); // the toolbar Inspect's path: the examination window opens on it and sends the Identify
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"examine capture ask guid=0x{guid:X8} ask={entry.Asks}/{MaxAsksPerCreature} at={now:0.00}"));
    }

    private void OnAnswer(in AppraisalReceived received)
    {
        if (!_creatures.TryGetValue(received.Guid, out var entry) || !entry.Waiting || !Active || !_session.TryGetAppraisal(received.Guid, out var appraisal))
        {
            return;
        }

        entry.Waiting = false;
        Answers++;
        if (!appraisal.Success)
        {
            Failed++;
            entry.RetryAt = entry.Asks < MaxAsksPerCreature ? _session.Clock.Now + RetrySeconds : null;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"examine capture answer guid=0x{received.Guid:X8} name=\"{appraisal.Name}\" success={appraisal.Success} ask={entry.Asks} answers={Answers}/{_maxAnswers} failed={Failed}{(entry.RetryAt is { } at ? $" retry_at={at:0.00}" : string.Empty)}"));
    }

    private sealed class Entry
    {
        public int Asks { get; set; }

        public double? RetryAt { get; set; }

        public bool Waiting { get; set; }
    }
}
