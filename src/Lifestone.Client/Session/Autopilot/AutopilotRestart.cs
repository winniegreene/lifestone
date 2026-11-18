using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 item 14 (the restart's waste): what proves the lifestone bind. The server never sends a "bound" quality, so the proof is its own confirmation of the two acts at a known lifestone: the Use ("You have attuned your spirit to this
/// Lifestone", ACE's Sanctuary, where a death sends her) at <see cref="LifestoneCell"/> and the point, and the Lifestone Tie ("You have successfully linked with the life stone", ACE's LinkedLifestone, which Lifestone Recall reads) or a
/// Lifestone Recall that landed (<see cref="Tied"/>). The autopilot keeps it in the checkpoint by the character's guid (<see cref="AutopilotCheckpoint.Bind"/>) and in the journey's facts, so a later start, in the same session or after a
/// relaunch, does not walk to the lifestone again. It is dropped only by what could have changed it: another character guid, a bind made at another lifestone (the new one replaces it), or a death whose respawn lands away from it.
/// </summary>
/// <param name="LifestoneCell">The cell of the lifestone the Use was confirmed at.</param>
/// <param name="X">The lifestone's point in the cell, x.</param>
/// <param name="Y">The lifestone's point in the cell, y.</param>
/// <param name="Z">The lifestone's point in the cell, z.</param>
/// <param name="Tied">True when the server also confirmed a Lifestone Tie there, or a Lifestone Recall landed home.</param>
/// <param name="How">What confirmed it, for the log.</param>
public sealed record BindProof(uint LifestoneCell, float X, float Y, float Z, bool Tied, string How)
{
    /// <summary>The lifestone's cell and point.</summary>
    public CellFrame At => new(LifestoneCell, new AcFrame(new Vector3(X, Y, Z), Quaternion.Identity));

    /// <summary>A proof at <paramref name="at"/>.</summary>
    /// <param name="at">The lifestone's cell and point (<c>JourneyFacts.BoundAt</c>).</param>
    /// <param name="tied">Whether the tie is confirmed too.</param>
    /// <param name="how">What confirmed it.</param>
    public static BindProof Of(CellFrame at, bool tied, string how) => new(at.CellId, at.Frame.Origin.X, at.Frame.Origin.Y, at.Frame.Origin.Z, tied, how);

    /// <summary>The proof in one phrase: where, by what, and whether the tie is in it.</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"the Use confirmed at the lifestone in cell 0x{LifestoneCell:X8} ({X:0.#}, {Y:0.#}, {Z:0.#}) by {How}, tie={(Tied ? "confirmed" : "not confirmed")}");
}

/// <summary>What <see cref="BindProofRule.Decide"/> says of a proof: whether the audit may skip the bind, the reason's token and the sentence for the log.</summary>
/// <param name="Proven">True: the bind is proven for this ground and no walk to the lifestone is made.</param>
/// <param name="Reason">The token: <c>proven</c>, or why not (<see cref="BindProofRule"/>'s names).</param>
/// <param name="Text">The sentence.</param>
public readonly record struct BindDecision(bool Proven, string Reason, string Text);

/// <summary>M6 item 14: the staleness check of a kept <see cref="BindProof"/>: read before the audit skips the bind, and at every respawn. When in doubt it is not proven, and the reason is named.</summary>
public static class BindProofRule
{
    /// <summary>The reason token of a proof that stands.</summary>
    public const string Proven = "proven";

    /// <summary>No proof is kept (the sentence says why: no checkpoint, a file written before it, another character's file).</summary>
    public const string None = "none";

    /// <summary>The proof is at another lifestone than the ground's home.</summary>
    public const string AnotherLifestone = "another-lifestone";

    /// <summary>The Use is proven, the tie is not, and the book holds Lifestone Recall (which reads the tie).</summary>
    public const string TieUnproven = "tie-unproven";

    /// <summary>A death's respawn landed away from the proven lifestone: the proof was wrong.</summary>
    public const string RespawnElsewhere = "respawn-elsewhere";

    /// <summary>
    /// Whether <paramref name="proof"/> proves the bind for a run whose home is <paramref name="home"/>: it must be at that home's lifestone (within <see cref="DeathLegVerdict.LifestoneRadiusMetres"/> of its point, the radius the respawn is
    /// judged by), and its tie must be confirmed unless the book is read and holds no Lifestone Recall (the tie serves that spell alone; a book not read yet is a doubt, and a doubt is not proven).
    /// </summary>
    /// <param name="proof">The kept proof, or null.</param>
    /// <param name="home">The ground's home.</param>
    /// <param name="knowsLifestoneRecall">Whether the spellbook holds Lifestone Recall; null while the book is not read.</param>
    /// <param name="whyNone">Why no proof is kept (the sentence of the <see cref="None"/> answer).</param>
    public static BindDecision Decide(BindProof? proof, GroundHome home, bool? knowsLifestoneRecall, string? whyNone = null)
    {
        ArgumentNullException.ThrowIfNull(home);
        if (proof is null)
        {
            return new(false, None, whyNone ?? "no proof is kept");
        }

        var homeAt = new CellFrame(home.LifestoneCell, new AcFrame(home.LifestonePoint, Quaternion.Identity));
        var metres = PositionAgreement.DistanceMetres(proof.At, in homeAt);
        if (!double.IsFinite(metres) || metres > DeathLegVerdict.LifestoneRadiusMetres)
        {
            return new(false, AnotherLifestone, string.Create(CultureInfo.InvariantCulture, $"the kept proof is at another lifestone (cell 0x{proof.LifestoneCell:X8}, {metres:0} m from {home.Town}'s in cell 0x{home.LifestoneCell:X8}, over {DeathLegVerdict.LifestoneRadiusMetres:0} m): {proof.Text}"));
        }

        if (!proof.Tied && knowsLifestoneRecall != false)
        {
            return new(false, TieUnproven, $"the Use is proven and the tie is not, and {(knowsLifestoneRecall is null ? "the spellbook is not read yet, so it may hold Lifestone Recall" : "the spellbook holds Lifestone Recall")}, which reads the tie: {proof.Text}");
        }

        return new(true, Proven, proof.Text + (proof.Tied ? string.Empty : " (not needed: the spellbook holds no Lifestone Recall)"));
    }

    /// <summary>True when a respawn that arrived at <paramref name="arrival"/> agrees with <paramref name="proof"/>: within <see cref="DeathLegVerdict.LifestoneRadiusMetres"/> of the proven lifestone.</summary>
    /// <param name="proof">The kept proof.</param>
    /// <param name="arrival">Where the death's teleport arrived.</param>
    /// <param name="metres">The distance between them.</param>
    public static bool RespawnAgrees(BindProof proof, CellFrame arrival, out double metres)
    {
        ArgumentNullException.ThrowIfNull(proof);
        metres = PositionAgreement.DistanceMetres(proof.At, in arrival);
        return double.IsFinite(metres) && metres <= DeathLegVerdict.LifestoneRadiusMetres;
    }
}

/// <summary>
/// M6 item 14: what a run that ended hands to the run the dial starts next in the same session (the level up job's move-on, <c>stop=rung-moved:&lt;rung&gt;</c>): what it proved seconds ago, so the next run's start is not a cold start. Nothing
/// in it is trusted alone: the next run reads each part against the world before it uses it (<see cref="BindProofRule"/>, <see cref="HandOverPassRule"/>, the pack for the stock), and logs the hand-over and each use in one line.
/// </summary>
/// <param name="CharacterGuid">The character the ended run played (a hand-over of another guid is refused).</param>
/// <param name="Ground">The ended run's ground.</param>
/// <param name="Stop">The ended run's stop name.</param>
/// <param name="EndedAt">The session clock when the hand-over was taken.</param>
/// <param name="Bind">The bind's proof, or null.</param>
/// <param name="PassBook">The spellbook's size the last whole buff pass stood on, or null when none ended.</param>
/// <param name="PassEndedAt">The session clock when that pass ended (NaN: none).</param>
/// <param name="Kit">The last audit's kit finding (<c>ok</c>, <c>trip</c>, <c>gap</c>, ...), or null when no audit finished.</param>
/// <param name="GateStands">
/// The lines of the gate of the rung the stop names that the ended run read met by another's standing spell, not by her book (the gates' second form: the buff bot's level VII), or null when the stop names no rung or none stood so.
/// The dial has no run when it chooses the next run's ground, so without this it read that gate on the book alone, kept her on the ground the run had just left, and the next run's first check moved on again, for ever (live, 2026-11-12).
/// </param>
/// <param name="StartQuestAsked">
/// M6 14 motes (live 2026-11-17 00:08:52: the Atlan wand's start quest failed in the Crater Caves, was marked "not called again this run", and the move-on's restart one minute later started it again from home): true when the ended run, or
/// one before it in the same chain of restarts, already asked the start quest (<c>--autopilot-start-quest</c>): it is asked once a process run, not once a restart.
/// </param>
/// <param name="QuestsFailed">M6 14 motes: the quests the ended run (and the chain before it) marked "not called again this run", by key, each with its stop name; the next run of the chain keeps them out too. Null: none.</param>
public sealed record AutopilotHandOver(uint CharacterGuid, string Ground, string Stop, double EndedAt, BindProof? Bind = null, int? PassBook = null, double PassEndedAt = double.NaN, string? Kit = null, IReadOnlyList<GateStanding>? GateStands = null, bool StartQuestAsked = false, IReadOnlyDictionary<string, string>? QuestsFailed = null)
{
    /// <summary>What stands in a learned line's place for the dial's read of the named rung's gate (<see cref="RungAsk.StandsByAnother"/>): the ended run's read of a line at <paramref name="level"/> or over, or null.</summary>
    /// <param name="line">The gate's line.</param>
    /// <param name="level">The level the gate asks.</param>
    public string? StandsAt(string line, int level)
    {
        if (GateStands is null)
        {
            return null;
        }

        foreach (var s in GateStands)
        {
            if (s.Level >= level && string.Equals(s.Line, line, StringComparison.Ordinal))
            {
                return s.Read + " (the ended run's read, handed over)";
            }
        }

        return null;
    }

    /// <summary>The hand-over in one log phrase at <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    public string Line(double now) => string.Create(CultureInfo.InvariantCulture,
        $"from the run on {Ground} that ended {Math.Max(0, now - EndedAt):0.#} s ago (stop={Stop}, character 0x{CharacterGuid:X8}): bind={(Bind is null ? "none" : Bind.Text)}; buffs={(PassBook is { } book && double.IsFinite(PassEndedAt) ? $"a whole pass ended {Math.Max(0, now - PassEndedAt):0.#} s ago on a book of {book} spells" : "no whole pass")}; kit={Kit ?? "no audit finished"}; start_quest={(StartQuestAsked ? "asked" : "not asked")}; quests_failed={(QuestsFailed is { Count: > 0 } failed ? string.Join(",", failed.Keys.Order(StringComparer.Ordinal)) : "none")}; gate={(GateStands is { Count: > 0 } stands ? $"{stands.Count} line(s) of the named rung's gate stand by another's spell ({string.Join(", ", stands.Select(s => s.Line))})" : "no line stands by another's spell")}");
}

/// <summary>M6 item 14: one line of a rung's gate the ended run read met by another's standing spell (<see cref="AutopilotHandOver.GateStands"/>).</summary>
/// <param name="Line">The gate's line.</param>
/// <param name="Level">The level the gate asks of it.</param>
/// <param name="Read">The run's read: the spell that stands, its level and its seconds left.</param>
public sealed record GateStanding(string Line, int Level, string Read);

/// <summary>
/// M6 item 14: the staleness check of a handed-over buff pass: her own pass at the start is skipped only while the pass the ended run made still stands. It stands when it ended at most <see cref="FreshSeconds"/> ago, the spellbook is the
/// size it stood on, the armour memory is seeded (the lines are read one by one from the server's enchantment list and the appraised pieces), every wanted line is up, and no line is under the re-buff margin. Anything else runs the pass, and the reason is named.
/// </summary>
public static class HandOverPassRule
{
    /// <summary>Lifestone's own: the seconds a handed-over pass is trusted (the re-buff margin's length: a pass run again could win back at most this much on each line).</summary>
    public const double FreshSeconds = BuffOptions.DefaultRebuffMarginSeconds;

    /// <summary>Whether the handed-over pass stands; the sentence says what was read either way.</summary>
    /// <param name="handOver">The hand-over, or null.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="book">The spellbook's size now.</param>
    /// <param name="seedPending">True while the armour memory is not seeded (the lines cannot be judged).</param>
    /// <param name="lines">Every wanted line as read now.</param>
    /// <param name="due">The lines under the re-buff margin now.</param>
    /// <param name="margin">The re-buff margin in seconds.</param>
    public static (bool Stands, string Why) Decide(AutopilotHandOver? handOver, double now, int book, bool seedPending, IReadOnlyList<BuffCycle.LineState> lines, IReadOnlyList<string> due, double margin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(due);
        if (handOver is not { PassBook: { } passBook } || !double.IsFinite(handOver.PassEndedAt))
        {
            return (false, "the ended run handed over no whole pass");
        }

        var age = now - handOver.PassEndedAt;
        if (age < 0 || age > FreshSeconds)
        {
            return (false, string.Create(CultureInfo.InvariantCulture, $"stale: the pass ended {age:0} s ago, over the {FreshSeconds:0} s a handed-over pass is trusted"));
        }

        if (book != passBook)
        {
            return (false, string.Create(CultureInfo.InvariantCulture, $"stale: the spellbook changed since the pass ({passBook} spells then, {book} now)"));
        }

        if (seedPending)
        {
            return (false, "in doubt: the armour memory is not seeded, so the lines cannot be read one by one");
        }

        var down = lines.Where(l => !l.Active).Select(l => l.Line.Name).ToArray();
        if (down.Length > 0)
        {
            return (false, "stale: a line has lapsed: " + string.Join(", ", down));
        }

        if (due.Count > 0)
        {
            return (false, string.Create(CultureInfo.InvariantCulture, $"stale: under the {margin:0} s margin: {string.Join(", ", due)}"));
        }

        var least = lines.Count == 0 ? "no line is wanted" : string.Create(CultureInfo.InvariantCulture, $"the least {lines.Min(l => l.Remaining):0} s left ({lines.MinBy(l => l.Remaining).Line.Name})");
        return (true, string.Create(CultureInfo.InvariantCulture, $"the whole pass that ended {age:0.#} s ago stands: {lines.Count} of {lines.Count} wanted line(s) up, {least}, the book still {book} spells"));
    }
}

/// <summary>
/// M6 item 14: the minutes budget of a resumed checkpoint. The checkpoint counts the minutes over all the run's starts (<see cref="AutopilotCheckpoint.MinutesUsed"/>), and <c>--autopilot-minutes</c> is the run's total: a relaunch that asks
/// for no more than what is already used has nothing left to run. It used to start, stop ok one second later (<c>stop=minutes</c>) and mark the run finished; now it fails at once by name, with both numbers and what to pass instead.
/// </summary>
public static class MinutesBudgetRule
{
    /// <summary>The stop name of a start whose minutes budget the checkpoint has already used.</summary>
    public const string Stop = "failed:minutes-budget-used";

    /// <summary>The refusal's sentence, or null when the run has minutes left (or has no minutes rule).</summary>
    /// <param name="minutesAsked">The option's value (0: no minutes rule).</param>
    /// <param name="minutesUsed">The minutes the checkpoint counted.</param>
    public static string? Refusal(double minutesAsked, double minutesUsed)
    {
        if (!(minutesAsked > 0) || minutesUsed < minutesAsked)
        {
            return null;
        }

        var more = Math.Ceiling(minutesUsed + minutesAsked);
        return string.Create(CultureInfo.InvariantCulture,
            $"the checkpoint being resumed has already used {minutesUsed:0.#} minutes and --autopilot-minutes asks {minutesAsked:0.#} in all (the option is the run's total over all its starts, not this start's share): nothing is left to run, so nothing is started. Pass --autopilot-minutes over {minutesUsed:0.#} (for {minutesAsked:0.#} more minutes: --autopilot-minutes={more:0}), or 0 for no minutes rule. The checkpoint is kept as it was: the next start resumes it");
    }
}
