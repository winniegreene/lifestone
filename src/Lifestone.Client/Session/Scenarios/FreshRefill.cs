using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>How a <see cref="FreshRefill"/> stands after a poll.</summary>
internal enum FreshRefillProgress
{
    /// <summary>The first poll has not decided yet, or the grant is in flight, or its level-up and vitals are being waited for.</summary>
    Asking,

    /// <summary>The vitals were already at their maxima (<see cref="FreshRefill.Field"/> is <c>none</c>), or the level-up refilled them (<c>level-up:&lt;old&gt;-&gt;&lt;new&gt;</c>).</summary>
    Done,

    /// <summary>The stage failed; <see cref="FreshRefill.Poll"/> named why.</summary>
    Failed,
}

/// <summary>
/// The refill stage of the fresh-character builds (M4 fresh F1, the item's live gate round 1 finding), shared by <see cref="MageBuildScenario"/> and <see cref="FighterBuildScenario"/>: after the NEW copy of the template's character is built, and before the purge, it fills the vitals by ACE's own rule.
/// ACE refills the vitals on a level-up and nowhere else a build reaches (Player_Xp.cs <c>CheckForLevelup</c> calls <c>SetMaxVitals</c>; Creature_Vitals.cs <c>SetMaxVitals</c> sets each vital's <c>Current</c> to its <c>MaxValue</c> and Player_Vitals.cs sends the three 0x02E9 updates; ACE 95ef426d),
/// while the build grants its experience first, on the creation's maxima, and only then raises the attributes and vitals, which lift the maxima and never the current values (<c>HandleActionRaiseAttribute</c>, <c>HandleActionRaiseVital</c> send updates only): the fresh BSD Mage began its journey at health 6/205, stamina 25/275,
/// mana 105/426. So when any vital is below its maximum (<see cref="PlayerQualities.TryGetVitalLevels"/>, the maxima 1, 3 and 5), the stage grants, through the mod's <c>xp</c> verb, exactly <c>XpForLevel(Level + 1) - TotalExperience</c> (one level, nothing else), then waits <see cref="WaitSeconds"/> on the session clock
/// for the read model to show the level above the old one and every vital at its maximum. The character then ends one level above its template's level, the extra experience unspent. A refused grant, a level the XpTable cannot name, a grant that added less than asked and a refill not seen in the bound fail by name; the stage never grants twice.
/// </summary>
internal sealed class FreshRefill(XpTableData table, ILogSink log, string category)
{
    /// <summary>How long each wait runs, in seconds on the session clock: the grant's answer, then the level-up and the refilled vitals in the read model.</summary>
    public const double WaitSeconds = 10;

    private static readonly (string Name, uint MaxId)[] Vitals =
    [
        ("health", MageTemplate.MaxHealth),
        ("stamina", MageTemplate.MaxStamina),
        ("mana", MageTemplate.MaxMana),
    ];

    private ToolHandle<XpResult>? _handle;
    private bool _decided;
    private int _oldLevel;
    private double _sentAt;
    private double _answeredAt = double.NaN;
    private string? _failure;

    /// <summary>The verdict's <c>refill=</c> field: <c>none</c> until the stage ends or when the vitals were already full, <c>level-up:&lt;old&gt;-&gt;&lt;new&gt;</c> after the refilling grant, <c>failed</c> when the stage failed.</summary>
    public string Field { get; private set; } = "none";

    /// <summary>The experience the stage granted (0 when it granted nothing).</summary>
    public long Granted { get; private set; }

    /// <summary>
    /// Decides on the first call and grants once; polls the grant and the read model after. <paramref name="failure"/> is the reason when the result is <see cref="FreshRefillProgress.Failed"/>, else null.
    /// </summary>
    public FreshRefillProgress Poll(IGameSessionView session, out string? failure)
    {
        failure = _failure;
        if (_failure is not null)
        {
            return FreshRefillProgress.Failed;
        }

        var q = session.State.Self.Qualities;
        var now = session.Clock.Now;
        if (!_decided)
        {
            _decided = true;
            return Start(session, q, now, out failure);
        }

        if (_handle is null)
        {
            return FreshRefillProgress.Done; // decided at the first poll: the vitals were full, nothing was granted
        }

        if (_handle.Outcome is not { } outcome)
        {
            return now - _sentAt > WaitSeconds
                ? Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the mod did not answer the grant of {Granted:N0} experience within {WaitSeconds:0.#} s"), out failure)
                : FreshRefillProgress.Asking;
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed || _handle.Result is not { } result)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the grant of {Granted:N0} experience was refused: ") + Describe(outcome), out failure);
        }

        if (result.Added < Granted)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the mod added {result.Added:N0} of the {Granted:N0} experience asked (the level-up is not certain)"), out failure);
        }

        if (double.IsNaN(_answeredAt))
        {
            _answeredAt = now;
        }

        var level = q.IsKnown ? q.Level : 0;
        var read = Read(q, out var full, out var text);
        if (read && full && level > _oldLevel)
        {
            Field = string.Create(CultureInfo.InvariantCulture, $"level-up:{_oldLevel}->{level}");
            log.Info(category, "refill: the level-up refilled the vitals (" + Field + "; " + text + ")");
            return FreshRefillProgress.Done;
        }

        return now - _answeredAt > WaitSeconds
            ? Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the level-up and the refilled vitals were not seen within {WaitSeconds:0.#} s of the grant (level {level}, was {_oldLevel}; {text})"), out failure)
            : FreshRefillProgress.Asking;
    }

    private FreshRefillProgress Start(IGameSessionView session, PlayerQualities q, double now, out string? failure)
    {
        failure = null;
        if (!q.IsKnown)
        {
            return Fail("refill: the player description has not arrived, so the vitals are unknown", out failure);
        }

        if (!Read(q, out var full, out var text))
        {
            return Fail("refill: " + text, out failure);
        }

        if (full)
        {
            log.Info(category, "refill: every vital is at its maximum (" + text + "); nothing to grant");
            return FreshRefillProgress.Done;
        }

        var level = q.Level;
        if (level < 1 || level >= table.LevelCap)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the XpTable cannot name a level above {level} (it ends at level {table.LevelCap}), so no level-up can refill the vitals ({text})"), out failure);
        }

        var total = (ulong)Math.Max(0, q.TotalExperience);
        var next = table.XpForLevel(level + 1);
        if (table.XpForLevel(level) > total || total >= next)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"refill: the read model's level and experience disagree (level {level}, total experience {total:N0}); the xp grant is not sent"), out failure);
        }

        _oldLevel = level;
        _sentAt = now;
        Granted = (long)(next - total);
        log.Info(
            category,
            string.Create(
                CultureInfo.InvariantCulture,
                $"refill: the vitals are below their maxima ({text}): ACE refills them only on a level-up (Player_Xp.cs CheckForLevelup calls SetMaxVitals; Creature_Vitals.cs SetMaxVitals sets each vital's Current to its MaxValue; ACE 95ef426d), and the build's raises lifted the maxima after its own level-up; granting {Granted:N0} experience (to the XpTable's level {level + 1} from {total:N0} at level {level}), no more"));
        _handle = session.Tools.AddXp(Granted, ActionSource.Host);
        return FreshRefillProgress.Asking;
    }

    private FreshRefillProgress Fail(string why, out string? failure)
    {
        Field = "failed";
        _failure = why;
        failure = why;
        return FreshRefillProgress.Failed;
    }

    /// <summary>Reads the three vitals; <paramref name="full"/> is true when every current value is at or above its maximum. False (and <paramref name="text"/> the reason) when a vital is missing from the read model.</summary>
    private static bool Read(PlayerQualities q, out bool full, out string text)
    {
        full = true;
        var parts = new List<string>(Vitals.Length);
        foreach (var (name, maxId) in Vitals)
        {
            if (!q.TryGetVitalLevels(maxId, out var current, out var max))
            {
                full = false;
                text = "the read model has no " + name + " vital";
                return false;
            }

            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {current}/{max}"));
            full &= current >= max;
        }

        text = string.Join(", ", parts);
        return true;
    }

    private static string Describe(in ActionOutcome o) =>
        o.Kind + (o.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{o.Code:X})")) + (o.Detail is null ? string.Empty : ": " + o.Detail);
}
