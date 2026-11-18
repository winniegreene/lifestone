using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Mage;

/// <summary>One condition of a rung's gate: its name, whether it is met, and what was read.</summary>
/// <param name="Name">The condition.</param>
/// <param name="Met">Whether it is met.</param>
/// <param name="Read">What was read.</param>
public sealed record GateCondition(string Name, bool Met, string Read);

/// <summary>
/// M6 item 14 (the start's rung): what a start reads of the gates' lines in the state itself, before any run exists. The player's own enchantment registry arrives with the login, so a line that stands by a spell in it
/// is read; an armour line is on the worn pieces, whose spells the server sends only in an appraisal (<see cref="ArmourBuffLedger"/>), so no start can read it.
/// </summary>
/// <param name="StandsNow">A gate line that stands on her now at a level or over, by a spell in her registry with time left: what stands (the spell, its seconds left), or null.</param>
/// <param name="WayOutReads">Why the "up" of a gate line cannot be read by a start and is left to the run's way out (it reads the whole gate live before she leaves home), or null when a start reads the line itself.</param>
public sealed record GateStartRead(Func<string, int, string?> StandsNow, Func<string, string?> WayOutReads);

/// <summary>
/// M6 7 AK (the owner, 2026-11-06): she does not go to the Lodge until the ladder's <see cref="RungGate"/> holds: the level, the skills trained, each named buff line up at its level, Armor Self and Bludgeoning Protection at the highest level she can reach. The rung move reads this; the move-on level alone no longer sends her.
/// M6 7 AR (the owner's correction of 2026-11-06): the Burrow has its own, so gates chain: a gate that holds her drops her to the rung before it, whose gate is read in turn (<see cref="Walk"/>). Pure.
/// </summary>
public static class RungGateRule
{
    /// <summary>The level the rung decision should use: <paramref name="level"/>, or the level before the gate's rung while the gate holds her (<paramref name="conditions"/> not all met).</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="conditions">The gate's conditions, or null to read none (no gate is due).</param>
    public static int HeldLevel(Ladder ladder, int level, IReadOnlyList<GateCondition>? conditions)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return GateAt(ladder, level) is { } gate && conditions is not null && conditions.Any(c => !c.Met) ? ladder.Rungs.Single(r => r.Key == gate.Rung).Band.From - 1 : level;
    }

    /// <summary>The level the rung decision reads through every gate in the way: from <paramref name="level"/>, each gate due there is asked; a gate that holds her drops the level to the rung before it, and that rung's gate is asked in turn.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="conditions">A gate's conditions, or null when that gate is not read (she already stands on its ground).</param>
    public static int Walk(Ladder ladder, int level, Func<RungGate, IReadOnlyList<GateCondition>?> conditions)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(conditions);
        var at = level;
        while (GateAt(ladder, at) is { } gate)
        {
            var held = HeldLevel(ladder, at, conditions(gate));
            if (held == at)
            {
                break;
            }

            at = held;
        }

        return at;
    }

    /// <summary>The gate of the rung whose band holds <paramref name="level"/>, or null.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    public static RungGate? GateAt(Ladder ladder, int level)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        foreach (var gate in ladder.Gates)
        {
            if (ladder.Rungs.Single(r => r.Key == gate.Rung).Band.Holds(level))
            {
                return gate;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="level"/> stands in the gate's rung, so the gate decides.</summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="level">The character's level.</param>
    public static bool Due(Ladder ladder, int level)
    {
        return GateAt(ladder, level) is not null;
    }

    /// <summary>
    /// M6 7 AO (live, stretch 4: "Focus Self I learned: NOT MET" with Focus Self II and III in the book, because the plan skips level I): a line at level n is learned when the book holds level n or any higher level of that line. The line is found by its profile name or by its spells' name (<see cref="LineOf"/>), so a spelling of the data can never hold the gate shut for good.
    /// </summary>
    /// <param name="profile">The buff profile that holds the lines.</param>
    /// <param name="book">Whether the book holds a spell id.</param>
    /// <returns>The <c>knows</c> of <see cref="Standing"/> and <see cref="Check"/>.</returns>
    public static Func<string, int, bool> BookKnows(IBuffProfile profile, Func<uint, bool> book)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(book);
        return (name, level) =>
        {
            var line = LineOf(profile, name);
            for (var i = level - 1; level >= 1 && i < line.Spells.Count; i++)
            {
                if (book(line.Spells[i].Id))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>The line of <paramref name="profile"/> a gate names: by the profile's name ("Bludgeon Protection Self") or its spells' ("Bludgeoning Protection Self"). A name that is neither throws: a gate that could never open fails loudly (TODO(rung-gate-line) if it ever does).</summary>
    /// <param name="profile">The buff profile.</param>
    /// <param name="name">The name the gate carries.</param>
    public static ProfileLine LineOf(IBuffProfile profile, string name)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.LineNamed(name) ?? profile.BuffOrder.FirstOrDefault(l => string.Equals(l.DatRoot, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"TODO(rung-gate-line): the gate names '{name}', which is no line of the profile: the gate could never open");
    }

    /// <summary>What can be read before the run: the skills and the book.</summary>
    /// <param name="gate">The gate.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="knows">Whether a line is learned at a level (<see cref="BookKnows"/>).</param>
    /// <param name="stands">
    /// M6 item 14 (the gates' second form): what stands in a learned line's place, or null. A line that stands at the gate's level or over by another's stronger spell (the buff bot's level VII) needs no scroll of her own:
    /// the condition is met and its read says what stands, never "in the book". Null (the default), or a null answer: the book alone, as before.
    /// </param>
    public static IReadOnlyList<GateCondition> Standing(RungGate gate, PlayerQualities q, Func<string, int, bool> knows, Func<string, int, string?>? stands = null)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(knows);
        var list = new List<GateCondition>();
        foreach (var id in gate.Skills)
        {
            var trained = q.TryGetSkill(id, out var skill) && skill.AdvancementClass >= SkillRules.TrainedClass;
            list.Add(new GateCondition(MageTemplate.SkillName(id) + " trained", trained, trained ? "trained" : "untrained"));
        }

        foreach (var line in gate.Lines)
        {
            var held = knows(line.Line, line.Level);
            var standing = held ? null : stands?.Invoke(line.Line, line.Level);
            list.Add(new GateCondition(string.Create(CultureInfo.InvariantCulture, $"{line.Line} {ScrivenerLearnPlan.Numeral(line.Level)} learned"), held || standing is not null, held ? "in the book" : standing ?? "not in the book"));
        }

        return list;
    }

    /// <summary>
    /// M6 7 AV (live, 2026-11-06: a fresh run at level 17 chose the Lodge on the book alone, with no buff up): the gate as a run's start reads it, before any pass has run. <see cref="Standing"/>, then each line "up" as NOT MET, by name
    /// (<see cref="UnreadAtStart"/>): a buff standing cannot be read before the run's first pass, so it is never assumed.
    /// </summary>
    /// <param name="gate">The gate.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="knows">Whether a line is learned at a level (<see cref="BookKnows"/>).</param>
    /// <param name="stands">M6 item 14: what stands in a learned line's place (<see cref="Standing"/>).</param>
    public static IReadOnlyList<GateCondition> AtStart(RungGate gate, PlayerQualities q, Func<string, int, bool> knows, Func<string, int, string?>? stands = null)
    {
        var list = new List<GateCondition>(Standing(gate, q, knows, stands));
        foreach (var line in gate.Lines)
        {
            list.Add(new GateCondition(line.Line + " up", false, UnreadAtStart));
        }

        return list;
    }

    /// <summary>What a condition that can only be read after a pass says at a run's start.</summary>
    public const string UnreadAtStart = "not readable before the run's first pass: counted as not met";

    /// <summary>
    /// M6 item 14 (the start's rung; live, 2026-11-12 UTC, three relaunches at level 29: each built its first run on the outskirts, sold the Cave's stock, and moved on to the Cave at its buff stage, 337 s later): the gate as a
    /// start reads it when the state itself shows lines standing on her (<see cref="GateStartRead"/>). <see cref="Standing"/> with the lines that stand in a learned line's place; then each line "up": met, with what
    /// stands, for a line the state shows standing; left out, by name (<see cref="LeftToTheWayOut"/>), for a line no start can read and the run's way out reads before she leaves home; NOT MET
    /// (<see cref="UnreadAtStart"/>) for every other line. Nothing is assumed: a line is met only by what the state holds now. A line asked "at the highest she can reach" (<see cref="GateLine.Highest"/>) is read
    /// at the gate's own level: her reach is a run's read (the live skill), and the way out makes it.
    /// </summary>
    /// <param name="gate">The gate.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="knows">Whether a line is learned at a level (<see cref="BookKnows"/>).</param>
    /// <param name="read">What the start reads in the state.</param>
    public static IReadOnlyList<GateCondition> AtStart(RungGate gate, PlayerQualities q, Func<string, int, bool> knows, GateStartRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var list = new List<GateCondition>(Standing(gate, q, knows, read.StandsNow));
        var left = LeftToTheWayOut(gate, read);
        foreach (var line in gate.Lines)
        {
            if (read.StandsNow(line.Line, line.Level) is { } standing)
            {
                list.Add(new GateCondition(line.Line + " up", true, standing));
            }
            else if (!left.Contains(line.Line))
            {
                list.Add(new GateCondition(line.Line + " up", false, UnreadAtStart));
            }
        }

        return list;
    }

    /// <summary>
    /// M6 item 14: the lines of <paramref name="gate"/> whose "up" a start leaves to the run's way out (<see cref="GateStartRead.WayOutReads"/>), by name. None unless the state shows at least one line of the gate
    /// standing: a gate is never opened by what was left unread alone (M6 7 AV: never on the book alone).
    /// </summary>
    /// <param name="gate">The gate.</param>
    /// <param name="read">What the start reads in the state.</param>
    public static IReadOnlyList<string> LeftToTheWayOut(RungGate gate, GateStartRead read)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(read);
        if (!gate.Lines.Any(l => read.StandsNow(l.Line, l.Level) is not null))
        {
            return [];
        }

        return [.. gate.Lines.Where(l => read.StandsNow(l.Line, l.Level) is null && read.WayOutReads(l.Line) is not null).Select(l => l.Line)];
    }

    /// <summary>
    /// M6 14 gate lapse (the cycle's check: check, act, check): whether she can recast a gate's line herself where she stands, as a player would: the level the pass would cast (<paramref name="reach"/>: the highest her live skill reaches at the
    /// pass's chance that her pack has the components for, <see cref="BuffCycle.ReachLevel"/>) is at least the gate's (<paramref name="want"/>), that spell is in her book and on one of her spell bars (the autopilot casts only from the
    /// spell bar), and her mana now covers the most the cast can cost (<see cref="GearCasts.ManaCeiling"/>, every worn piece for an armour line). Null when she can (<paramref name="can"/> says what was read), else the first rule that
    /// fails, by name. Pure.
    /// </summary>
    /// <param name="line">The gate's line, as the buff profile holds it.</param>
    /// <param name="reach">The level the pass would cast (0: none her skill and pack allow).</param>
    /// <param name="want">The gate's level for the line.</param>
    /// <param name="book">Her spellbook (the spells and the bars).</param>
    /// <param name="mana">Her mana now.</param>
    /// <param name="worn">The items she wears (an armour line's cost grows with them).</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="can">What was read when she can; empty otherwise.</param>
    public static string? RecastHerself(ProfileLine line, int reach, int want, Spellbook book, uint mana, int worn, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo> spells, out string can)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spells);
        can = string.Empty;
        var c = CultureInfo.InvariantCulture;
        if (reach < 1 || reach > line.Spells.Count)
        {
            return "no level of it is in reach now (her live skill at the pass's chance, or the components in her pack)";
        }

        if (reach < want)
        {
            return string.Create(c, $"the level she can cast now, {ScrivenerLearnPlan.Numeral(reach)}, is under the gate's {ScrivenerLearnPlan.Numeral(want)}");
        }

        var spell = line.Spells[reach - 1];
        if (!book.Knows(spell.Id))
        {
            return $"{spell.Name} is not in her book";
        }

        if (!book.SpellBars.Any(bar => !bar.IsDefault && bar.Contains(spell.Id)))
        {
            return $"{spell.Name} is on none of her spell bars (the autopilot casts only from the spell bar)";
        }

        if (!spells.TryGetValue(spell.Id, out var info))
        {
            return string.Create(c, $"TODO(m6-gate-recast-spell): the spell table has no spell {spell.Id} ({spell.Name})");
        }

        var cost = GearCasts.ManaCeiling(info, worn);
        if (mana < cost)
        {
            return string.Create(c, $"her mana {mana} is under the most {spell.Name} can cost, {cost}");
        }

        can = string.Create(c, $"{spell.Name}: in her book and on her spell bar, her mana {mana} covers up to {cost}, her skill and pack reach level {ScrivenerLearnPlan.Numeral(reach)} of the gate's {ScrivenerLearnPlan.Numeral(want)}");
        return null;
    }

    /// <summary>The whole gate on the state: <see cref="Standing"/>, then each line up at its level (<paramref name="reach"/> gives the highest level she can cast now, for a line whose <see cref="GateLine.Highest"/> is set).</summary>
    /// <param name="gate">The gate.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="knows">Whether the book holds a level of a line.</param>
    /// <param name="active">The level of a line standing on her (on every worn piece for an armor line), 0 when none.</param>
    /// <param name="reach">The highest level of a line the rule gives on the live skill.</param>
    /// <param name="stands">M6 item 14: what stands in a learned line's place (<see cref="Standing"/>).</param>
    public static IReadOnlyList<GateCondition> Check(RungGate gate, PlayerQualities q, Func<string, int, bool> knows, Func<string, int> active, Func<string, int> reach, Func<string, int, string?>? stands = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(reach);
        var list = new List<GateCondition>(Standing(gate, q, knows, stands));
        foreach (var line in gate.Lines)
        {
            var want = line.Highest ? Math.Max(line.Level, reach(line.Line)) : line.Level;
            var up = active(line.Line);
            list.Add(new GateCondition(string.Create(CultureInfo.InvariantCulture, $"{line.Line} up"), up >= want, string.Create(CultureInfo.InvariantCulture, $"level {up} of {want}")));
        }

        return list;
    }
}
