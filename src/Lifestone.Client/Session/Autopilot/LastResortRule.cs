namespace Lifestone.Client.Session.Autopilot;

/// <summary>What the last resort reads before it acts (M6 8 N): one frame's facts, no engine.</summary>
public readonly record struct LastResortInput
{
    /// <summary>The character's health is above zero.</summary>
    public bool Alive { get; init; }

    /// <summary>The session is in the world.</summary>
    public bool InWorld { get; init; }

    /// <summary>The body is present (not in portal space) and placed.</summary>
    public bool Placed { get; init; }

    /// <summary>The action lanes have something queued or in flight (ACE's <c>TooBusyToRecall</c> reads the player's own busy state; the client's own actions are the part it can see).</summary>
    public bool Busy { get; init; }

    /// <summary>True when the journey bound at a lifestone this session (ACE's Sanctuary), false when it did not, null when the client cannot know (a character that kept a sanctuary from an earlier session): the server's refusal line is the check then.</summary>
    public bool? SanctuaryKnown { get; init; }
}

/// <summary>The rule's answer: go, or wait one frame (busy), or stop by a named refusal.</summary>
/// <param name="Verdict">What to do.</param>
/// <param name="Reason">The token of a refusal or a wait, null on go.</param>
/// <param name="Why">The sentence for the log.</param>
public readonly record struct LastResortDecision(LastResortVerdict Verdict, string? Reason, string Why);

/// <summary>The three answers of <see cref="LastResortRule.Check"/>.</summary>
public enum LastResortVerdict
{
    /// <summary>Stand still and send the recall.</summary>
    Go,

    /// <summary>The lanes are busy: look again next frame, bounded by <see cref="LastResortRule.BusySeconds"/>.</summary>
    Wait,

    /// <summary>Not this frame and not by waiting: the stop <see cref="LastResortRule.StopPrefix"/> plus the reason.</summary>
    Refuse,
}

/// <summary>
/// M6 8 N (D120, the owner's ruling: yes to the last-resort recall): <c>/lifestone</c> as the survival layer's last resort, taken only when a ladder errand failed and no way home worked. ACE's conditions (@95ef426d
/// <c>WorldObjects/Player_Location.cs</c>:132-195): refused with a PK timer, with recalls disabled, when too busy and with no Sanctuary (a chat line); it costs half the current mana up front, forces the peace mode, and after the
/// motion teleports to the Sanctuary unless the player moved more than <see cref="MoveThresholdMetres"/> (<c>RecallMoveThreshold</c>, :51; <c>YouHaveMovedTooFar</c>). So she stands still, and the arrival is a check after.
/// </summary>
public static class LastResortRule
{
    /// <summary>The stop's prefix: <c>failed:leave:lifestone-recall:&lt;reason&gt;</c>.</summary>
    public const string StopPrefix = "failed:leave:lifestone-recall:";

    /// <summary>ACE's <c>RecallMoveThreshold</c>: a recall that finds her further than this from where she started is refused (@95ef426d <c>Player_Location.cs</c>:51); an arrival is judged by the same distance.</summary>
    public const double MoveThresholdMetres = 8.0;

    /// <summary>How long a busy lane is waited for before the last resort refuses by name, in seconds (Lifestone's own: the station that just ended has released its actions in a frame or two).</summary>
    public const double BusySeconds = 5;

    /// <summary>How long after the teleport the body is waited for to be placed away from where it started, in seconds (the tracker's own bound is ACE's delay plus its margin; this is the time to read the new position).</summary>
    public const double SettleSeconds = 8;

    /// <summary>Check before: the answer for one frame.</summary>
    public static LastResortDecision Check(in LastResortInput input)
    {
        if (!input.InWorld)
        {
            return new(LastResortVerdict.Refuse, "not-in-world", "the session is not in the world");
        }

        if (!input.Alive)
        {
            return new(LastResortVerdict.Refuse, "not-alive", "her health reads zero");
        }

        if (!input.Placed)
        {
            return new(LastResortVerdict.Refuse, "portal-space", "she is in portal space or not placed");
        }

        if (input.SanctuaryKnown == false)
        {
            return new(LastResortVerdict.Refuse, "no-sanctuary", "the journey never bound at a lifestone and ACE has no Sanctuary for her (Player_Location.cs:152)");
        }

        if (input.Busy)
        {
            return new(LastResortVerdict.Wait, "busy", "her actions are queued or in flight (ACE refuses a recall while she is busy, Player_Location.cs:146)");
        }

        return new(LastResortVerdict.Go, null, input.SanctuaryKnown == true ? "a sanctuary is known (the journey bound)" : "the sanctuary is not readable by the client: the server's refusal line is the check (Player_Location.cs:152)");
    }

    /// <summary>Check after: she has arrived when the body is present again and stands further than <see cref="MoveThresholdMetres"/> from where she started, or in another cell.</summary>
    public static bool Arrived(bool present, bool sameCell, double metresFromStart) => present && (!sameCell || metresFromStart > MoveThresholdMetres);

    /// <summary>The owner's widening of D120, after long round 3: the stops that say no way home worked: a way home that failed by the recall or by the portals, and a mage who stands nowhere the known state recognises.</summary>
    /// <param name="stop">The stop's name.</param>
    public static bool CoversStop(string stop) =>
        stop == "failed:known-state:elsewhere" || stop.StartsWith("failed:travel:recall-home", StringComparison.Ordinal) || stop.StartsWith("failed:travel:rung-change-home", StringComparison.Ordinal);

    /// <summary>
    /// M6 8 (the owner's reading of D120 after the climb's first death, 2026-11-11): no way home works before the walk begins. She stands in a dungeon whose hunt keeps a hall out below a level (the profile's
    /// <c>hunt.keep_out</c> and <c>keep_out_under_level</c>: packs she is not expected to live), she is under that level, and the way home the travel rule chose is the walk (no usable Lifestone Recall: not in the
    /// book, or refused this run). The walk out passes what the hunt keeps out, so it is no way home: <c>/lifestone</c> at once. D120 stands: it is still the last resort.
    /// </summary>
    /// <param name="onDungeon">The body stands on a dungeon ground.</param>
    /// <param name="level">Her level.</param>
    /// <param name="keepOutCells">The cells that ground's hunt keeps out.</param>
    /// <param name="keepOutUnderLevel">The level under which it keeps them out.</param>
    /// <param name="wayIsTheWalk">The travel rule's way home is the rung change (the walk and its portals), not the recall.</param>
    public static bool NoSafeWalkOut(bool onDungeon, int level, int keepOutCells, int keepOutUnderLevel, bool wayIsTheWalk) =>
        onDungeon && wayIsTheWalk && keepOutCells > 0 && level < keepOutUnderLevel;

    /// <summary>The stop name for a refusal or a failed arrival.</summary>
    public static string Stop(string reason) => StopPrefix + reason;
}
