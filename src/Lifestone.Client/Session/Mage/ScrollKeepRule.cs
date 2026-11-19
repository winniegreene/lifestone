using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>One level of a plan line as the keep rule reads it: its numeral's number (1 for I), the spell and its power.</summary>
/// <param name="Level">1 for I, 7 for VII.</param>
/// <param name="SpellId">The spell id (the scroll's SpellDID).</param>
/// <param name="Name">The spell's name.</param>
/// <param name="Power">The spell's power.</param>
public sealed record ScrollLevel(int Level, uint SpellId, string Name, uint Power);

/// <summary>One line of the spell plan as the keep rule reads it: the line's name, its school and its levels, I first.</summary>
/// <param name="Name">The line's name.</param>
/// <param name="School">The line's school.</param>
/// <param name="Levels">The levels, I first.</param>
/// <param name="Floor">The line's floor for the plan's target (the rung's plan level for the bolt line, capped at the catalog's levels; 0 for a debuff line), as <see cref="ScrivenerLearnPlan"/> reads it.</param>
/// <param name="Covered">M6 item 14 S3: the buff bot casts the line while its stop applies to the run (<see cref="BuffBotCover"/>): no scroll of it is kept or read.</param>
public sealed record ScrollLine(string Name, MagicSchool School, IReadOnlyList<ScrollLevel> Levels, int Floor = 0, bool Covered = false);

/// <summary>What <see cref="ScrollKeepRule.Evaluate"/> says of one item.</summary>
public enum ScrollKeepKind
{
    /// <summary>The item is not a scroll (ItemType Writable 0x2000).</summary>
    NotScroll,

    /// <summary>A scroll whose weenie description carries no spell id.</summary>
    NoSpell,

    /// <summary>The spellbook has not been read yet: nothing is kept on an assumption.</summary>
    SpellbookUnread,

    /// <summary>The spell is not a level of a line the plan learns.</summary>
    NotPlanned,

    /// <summary>The spell is a level of a plan line the spellbook holds already.</summary>
    NotAbove,

    /// <summary>M6 1 fix (1-R3): the spell is a level of a plan line, but neither the plan's next level of that line (the level <see cref="ScrivenerLearnPlan"/> would buy) nor a VII (D4): the loot rules judge it.</summary>
    NotNext,

    /// <summary>The plan's next level (or a VII) of a plan line, but ACE would not let the character read it (<c>CanReadScroll</c>).</summary>
    Unreadable,

    /// <summary>Kept: read it on the next trip.</summary>
    Keep,

    /// <summary>M6 item 14 S3: a level of a plan line the buff bot casts while its stop applies to the run: not kept and not read, a VII included; the loot rules judge it.</summary>
    BotCovers,
}

/// <summary>The keep rule's answer for one item: the kind and the reason (the line, the level and why).</summary>
/// <param name="Kind">What the rule says.</param>
/// <param name="Reason">The numbers, as one line for the log; for <see cref="ScrollKeepKind.Keep"/> it is the keep-aside reason.</param>
/// <param name="Line">The plan line the spell belongs to, or null.</param>
/// <param name="Level">The spell's level of that line, or null.</param>
public sealed record ScrollKeepVerdict(ScrollKeepKind Kind, string Reason, ScrollLine? Line = null, ScrollLevel? Level = null)
{
    /// <summary>True when the scroll is kept (and read on the next trip).</summary>
    public bool Keep => Kind == ScrollKeepKind.Keep;
}

/// <summary>
/// M6 item 1 e (D4, the VII rule): which scrolls found in loot are kept to be read. Pure. A scroll (ItemType Writable, 0x2000, <c>ResupplyRun.WritableType</c>) whose spell (<c>WeenieDesc.SpellId</c>, on the wire without an appraisal:
/// ACE <c>WorldObject_Networking.cs:159</c> @95ef426d) is a level of a line the plan learns (the rung's bolt line and the debuff lines <see cref="ScrivenerLearnPlan"/> learns), that the spellbook lacks, that is the plan's NEXT level of the line (M6 1 fix, 1-R3: the level
/// <see cref="ScrivenerLearnPlan"/> would buy next on the current skill, DoD 3's "a scroll of the plan's next level") or a VII (D4: always), and that ACE lets the character read, is kept; anything else is left to the loot rules. ACE's rule (<c>Player.CanReadScroll</c>, <c>Player_Skills.cs:458-471</c> @95ef426d, the client's <see cref="ResupplyRun.CanRead(PlayerQualities, MagicSchool, uint, out string)"/>): a spell of power under 50 or from 300 is
/// read by anyone, else the school is trained and its current skill is at least the power less 50. So a VII of the plan's line (power 300) is always kept, a VI only when the skill is within 50 of 250.
/// </summary>
/// <remarks>
/// <para><b>The lines.</b> The bolt line is <see cref="SpellPlan.BoltLine"/> (the profile's Flame Bolt I to VIII; another element's SpellTable levels I to VI, or the catalog's when the session has no SpellTable); each debuff line is the
/// catalog's I to VI plus the level VII <see cref="FightDebuffs.Spells"/> holds for it (Fire Vulnerability, Imperil, Vulnerability and Magic Yield). A debuff line of another element has no VII here: <c>TODO(m6-debuff-line-vii)</c>.</para>
/// <para><b>The wiring.</b> <see cref="Hook"/> is the <c>MageLootThresholds.KeepScroll</c> delegate: a non-null reason is the keep-aside verdict, so the sale never sells the scroll. <c>ResupplyRun</c> reads every kept scroll the pack holds
/// on its next trip (the learn step's <c>ReadScrolls</c>, which asks this rule again with the pack's scrolls and the live book).</para>
/// </remarks>
public static class ScrollKeepRule
{
    /// <summary>The level D4 keeps always (VII).</summary>
    private const int ViiLevel = 7;

    /// <summary>The debuff kind each catalog debuff line is, for the VII the <see cref="FightDebuffs.Spells"/> table holds (D4).</summary>
    private static readonly Dictionary<string, DebuffKind> DebuffKinds = new(StringComparer.Ordinal)
    {
        ["Fire Vulnerability Other"] = DebuffKind.FireVulnerability,
        ["Imperil Other"] = DebuffKind.Imperil,
        ["Vulnerability Other"] = DebuffKind.Vulnerability,
        ["Magic Yield Other"] = DebuffKind.MagicYield,
    };

    /// <summary>The lines of the plan on <paramref name="rung"/> (see the class remarks): the bolt line first, then the debuff lines.</summary>
    /// <param name="rung">The rung the character plays.</param>
    /// <param name="spells">The SpellTable, or null when the session has none.</param>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="botCovers">M6 item 14 S3: the bot's profile that covers her lines for the run (<see cref="BuffBotCover.Covering"/>), or null: its lines are marked <see cref="ScrollLine.Covered"/>.</param>
    public static IReadOnlyList<ScrollLine> PlanLines(LadderRung rung, IReadOnlyDictionary<uint, SpellInfo>? spells, ScrivenerCatalog catalog, BuffBotProfile? botCovers = null)
    {
        ArgumentNullException.ThrowIfNull(rung);
        ArgumentNullException.ThrowIfNull(catalog);
        var lines = new List<ScrollLine>();
        var bolt = SpellPlan.BoltLine(rung.Element, spells, out _);
        if (bolt is not null && rung.Element is { } element && SpellPlan.BoltLineNames.TryGetValue(element, out var boltName))
        {
            lines.Add(new ScrollLine(boltName, MagicSchool.WarMagic, [.. bolt.Select((s, i) => new ScrollLevel(i + 1, s.Id, s.Name, s.Power))], Math.Min(rung.SpellLevel.Min, Math.Min(bolt.Count, ScrivenerLearnPlan.CatalogLevels)))); // the profile's spells carry no position: the list is I first
        }

        foreach (var line in ScrivenerLearnPlan.LinesFor(rung, catalog))
        {
            if ((line.Kind == ScrivenerLineKind.Bolt && bolt is not null) || line.Kind == ScrivenerLineKind.Revitalize)
            {
                // M6 14: the Revitalize Self line is the plan's only when a round of the dance nets mana on her state (ScrivenerLearnPlan.RevitalizeTarget), which this rule does not weigh: a looted scroll of it is the loot rules', as before the line was listed
                continue;
            }

            var levels = catalog.LineSpells(line.Name).Where(s => s.Level > 0).Select(s => new ScrollLevel(s.Level, s.SpellId, s.Name, s.Power)).ToList();
            if (DebuffKinds.TryGetValue(line.Name, out var kind))
            {
                levels.AddRange(FightDebuffs.Spells.Where(d => d.Kind == kind && d.Level > 6).OrderBy(d => d.Level).Select(d => new ScrollLevel(d.Level, d.Id, d.Name, d.Power)));
            }

            lines.Add(new ScrollLine(line.Name, line.School, levels, line.Kind == ScrivenerLineKind.Bolt ? Math.Min(rung.SpellLevel.Min, levels.Count(l => l.Level <= ScrivenerLearnPlan.CatalogLevels)) : 0, botCovers is not null && BuffBotCover.Covers(botCovers, line.Name)));
        }

        return lines;
    }

    /// <summary>The answer for one item over <paramref name="lines"/>, the character's spellbook and current skills.</summary>
    /// <param name="item">The item (a corpse's, or one in the pack).</param>
    /// <param name="lines">The plan's lines (<see cref="PlanLines"/>).</param>
    /// <param name="book">The character's spellbook.</param>
    /// <param name="q">The character's qualities (the current skills, the buffs counted, as ACE's <c>Current</c>).</param>
    /// <param name="chance">The plan's cast chance (<see cref="Ladder.PlanCastChance"/>), which sets the line's next level on the current skill.</param>
    /// <param name="spells">M6 sprint (the owner's rule of 2026-11-19: a scroll that drops is picked up and read when she does not know it): the SpellTable; with it, every scroll whose spell the book lacks and ACE lets her read now is
    /// kept (<see cref="ScrollKeepKind.Keep"/>, any school, any level, a line the buff bot casts included), one ACE would refuse now is left to the loot rules (<see cref="ScrollKeepKind.Unreadable"/>). Null: the plan lines' rule as before.</param>
    public static ScrollKeepVerdict Evaluate(WorldObject item, IReadOnlyList<ScrollLine> lines, Spellbook book, PlayerQualities q, double chance, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(q);
        var c = CultureInfo.InvariantCulture;
        if ((item.ItemType & ResupplyRun.WritableType) == 0)
        {
            return new ScrollKeepVerdict(ScrollKeepKind.NotScroll, "not a scroll (ItemType Writable 0x2000)");
        }

        if (item.Weenie.SpellId is not { } id)
        {
            return new ScrollKeepVerdict(ScrollKeepKind.NoSpell, "a Writable with no spell id in its description");
        }

        if (!book.IsKnown)
        {
            return new ScrollKeepVerdict(ScrollKeepKind.SpellbookUnread, "the spellbook is not read yet (no PlayerDescription): nothing is kept on an assumption");
        }

        if (spells is not null && spells.TryGetValue(id, out var info))
        {
            return Unknown(id, info, book, q);
        }

        foreach (var line in lines)
        {
            var level = line.Levels.FirstOrDefault(l => l.SpellId == id);
            if (level is null)
            {
                continue;
            }

            if (line.Covered)
            {
                return new ScrollKeepVerdict(ScrollKeepKind.BotCovers, string.Create(c, $"{level.Name} is level {level.Level} of the plan's {line.Name} line, which the buff bot casts at level VII while its stop applies: not kept, not read"), line, level);
            }

            var highest = line.Levels.Where(l => book.Knows(l.SpellId)).Select(l => l.Level).DefaultIfEmpty(0).Max();
            if (book.Knows(level.SpellId))
            {
                return new ScrollKeepVerdict(ScrollKeepKind.NotAbove, string.Create(c, $"{level.Name} is level {level.Level} of the plan's {line.Name} line and the book holds it already: not kept"), line, level);
            }

            // M6 1 fix (1-R3): only the plan's next level of the line (the level the learn plan buys on the current skill), and a VII always (D4); a level above it would be read and then hide the plan's own level from the buy.
            var why = string.Empty;
            var next = level.Level == ViiLevel;
            if (!next)
            {
                if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(line.School), out var skill, out var noSkill))
                {
                    return new ScrollKeepVerdict(ScrollKeepKind.NotNext, string.Create(c, $"{level.Name} is level {level.Level} of the plan's {line.Name} line, but the plan has no next level: {noSkill}: not kept"), line, level);
                }

                var target = ScrivenerLearnPlan.TargetLevel(skill, chance, [.. line.Levels.Where(l => l.Level <= ScrivenerLearnPlan.CatalogLevels).OrderBy(l => l.Level).Select(l => l.Power)], line.Floor);
                if (level.Level != target)
                {
                    return new ScrollKeepVerdict(ScrollKeepKind.NotNext, string.Create(c, $"{level.Name} is level {level.Level} of the plan's {line.Name} line; the plan's next level on {line.School} skill {skill} at chance {chance:0.00} is {ScrivenerLearnPlan.Numeral(target)} (the book holds level {highest}): not kept"), line, level);
                }

                why = string.Create(c, $"the plan's next level on {line.School} skill {skill} at chance {chance:0.00}; ");
            }
            else
            {
                why = "a VII, kept always (D4); ";
            }

            var read = ResupplyRun.CanRead(q, line.School, level.Power, out var readWhy);
            if (read != LearnReason.None)
            {
                return new ScrollKeepVerdict(ScrollKeepKind.Unreadable, string.Create(c, $"{level.Name} is level {level.Level} of the plan's {line.Name} line, {why}but ACE would refuse the read ({readWhy}): not kept"), line, level);
            }

            return new ScrollKeepVerdict(ScrollKeepKind.Keep, string.Create(c, $"scroll of {level.Name}: level {level.Level} of the plan's {line.Name} line, {why}the book's highest is {highest}, and ACE lets the character read it ({readWhy}): kept to be read"), line, level);
        }

        return new ScrollKeepVerdict(ScrollKeepKind.NotPlanned, string.Create(c, $"spell {id} is no level of a line the plan learns"));
    }

    /// <summary>
    /// M6 sprint (the owner's rule of 2026-11-19): a looted scroll is kept to be read when its spell is not in the book and ACE lets her read it now (<c>Player.CanReadScroll</c>, Player_Skills.cs:458-471 @95ef426d:
    /// <see cref="ResupplyRun.CanRead(PlayerQualities, MagicSchool, uint, out string)"/>); known, or refused now, it is the loot rules'. The verdict's <see cref="ScrollKeepVerdict.Level"/> names the spell (level 0: no plan line).
    /// </summary>
    private static ScrollKeepVerdict Unknown(uint id, SpellInfo info, Spellbook book, PlayerQualities q)
    {
        var c = CultureInfo.InvariantCulture;
        var level = new ScrollLevel(0, id, info.Name, info.Power);
        if (book.Knows(id))
        {
            return new ScrollKeepVerdict(ScrollKeepKind.NotAbove, string.Create(c, $"{info.Name} ({id}) is in the book already: not kept"), null, level);
        }

        if (ResupplyRun.CanRead(q, info.School, info.Power, out var readWhy) != LearnReason.None)
        {
            return new ScrollKeepVerdict(ScrollKeepKind.Unreadable, string.Create(c, $"{info.Name} ({id}) is not in the book, but ACE would refuse the read now ({readWhy}): not kept"), null, level);
        }

        return new ScrollKeepVerdict(ScrollKeepKind.Keep, string.Create(c, $"scroll of {info.Name} ({id}, {info.School}, power {info.Power}): not in her book and ACE lets her read it ({readWhy}): kept to be read (the owner's rule of 2026-11-19)"), null, level);
    }

    /// <summary>
    /// The delegate <c>MageLootThresholds.KeepScroll</c> takes: the reason a scroll is kept, or null. The rung is the ground's at the character's level at each call (<see cref="ScrivenerLearnPlan.RungOf"/>), so the book, the skills and the plan
    /// are those of the time; a ground no rung names keeps nothing.
    /// </summary>
    /// <param name="ladder">The ladder (<c>LadderLoader.WarMage</c>).</param>
    /// <param name="groundKey">The ground's profile key.</param>
    /// <param name="spells">The SpellTable, or null.</param>
    /// <param name="book">The session's spellbook, read at each call.</param>
    /// <param name="qualities">The session's qualities, read at each call.</param>
    /// <param name="catalog">The scrivener catalog (default: <see cref="ScrivenerCatalog.Default"/>).</param>
    /// <param name="botCovers">M6 item 14 S3: the bot's profile that covers her lines now (<see cref="BuffBotCover.Covering"/>; null from it: none), read at each call; null: never.</param>
    public static Func<WorldObject, string?> Hook(Ladder ladder, string groundKey, IReadOnlyDictionary<uint, SpellInfo>? spells, Func<Spellbook> book, Func<PlayerQualities> qualities, ScrivenerCatalog? catalog = null, Func<BuffBotProfile?>? botCovers = null)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(groundKey);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(qualities);
        var cat = catalog ?? ScrivenerCatalog.Default;
        string? cachedRung = null;
        BuffBotProfile? cachedCover = null;
        IReadOnlyList<ScrollLine> cached = [];
        return item =>
        {
            if ((item.ItemType & ResupplyRun.WritableType) == 0)
            {
                return null; // the common case first: no allocation for a non-scroll
            }

            var q = qualities();
            if (ScrivenerLearnPlan.RungOf(ladder, groundKey, q.Level) is not { } rung)
            {
                return null;
            }

            var cover = botCovers?.Invoke();
            if (!string.Equals(cachedRung, rung.Key, StringComparison.Ordinal) || !ReferenceEquals(cover, cachedCover))
            {
                (cachedRung, cachedCover, cached) = (rung.Key, cover, PlanLines(rung, spells, cat, cover));
            }

            var verdict = Evaluate(item, cached, book(), q, ladder.PlanCastChance, spells); // M6 sprint: with the SpellTable, every unknown scroll she can read
            return verdict.Keep ? verdict.Reason : null;
        };
    }
}
