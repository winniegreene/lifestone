using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>What the spell plan says of one line's scroll (<see cref="ScrivenerLearnPlan.Needs"/>).</summary>
public enum LearnNeedKind
{
    /// <summary>The book lacks the target level, the character can read it (ACE's <c>CanReadScroll</c>) and a scrivener of the ground's home town sells it: a trip buys and reads it.</summary>
    Due,

    /// <summary>The book holds the target level itself.</summary>
    Known,

    /// <summary>The line's school is not trained.</summary>
    Untrained,

    /// <summary>No level of the line reaches the plan's chance on the current skill (and the line has no rung floor).</summary>
    NoLevel,

    /// <summary>The target level is not readable yet: the current skill is under its power less ACE's margin of 50 (bought once ACE lets the character read it).</summary>
    SkillShort,

    /// <summary>
    /// No scrivener of the ground's home town sells the target level (D40: V at the Master Scriveners' towns, VI at the Grand Masters'): the resupply's learn goes there by the Town Network (M6 4 t4a, closes
    /// <c>TODO(m6-scrivener-town-network)</c>: <c>Scenarios.Journey.TownNetworkTrip</c> chooses the town of <see cref="LearnNeed.Towns"/>), or names the stop when no town is admitted.
    /// </summary>
    OffHome,

    /// <summary>M6 7 AK (the owner: Armor Self IV and Bludgeoning Protection Self IV first, no question): the line waits: the coin is saved for <see cref="Ladder.FirstScrolls"/>.</summary>
    Saving,

    /// <summary>
    /// M6 item 14 S3: the buff bot casts the line at level VII while its stop applies to the run (<see cref="BuffBotCover"/>): the spell plan calls no trip for it. M6 14 own buffs (the owner, 2026-11-17: she relies on her self-buffs):
    /// her own level of it is the every-spell rule's (<see cref="EverySpellPlan"/>: her bootstrap right after the gates, read on her own bootstrap), bought on her trips as her coin allows.
    /// </summary>
    BotCovers,

    /// <summary>M6 14 (<c>m6-banes</c>): the line is planned from a later character level (<see cref="ScrivenerLine.FromLevel"/>: the six other armour banes): named with the level, nothing bought until she reaches it.</summary>
    Later,

    /// <summary>M6 14 (<c>m6-caster</c>): a caster aura that would add nothing to any caster she holds and can wield (<see cref="CasterGain.NoGain"/>): named, nothing bought until a caster it adds to is in her hands or packs.</summary>
    NoGain,
}

/// <summary>One line's answer of the plan: the line, the target level and its spell (null when none), the kind and the numbers, and the towns that sell the target.</summary>
/// <param name="Line">The catalog line.</param>
/// <param name="Target">The target level (0 when none).</param>
/// <param name="Spell">The target level's spell, or null.</param>
/// <param name="Kind">What the plan says.</param>
/// <param name="Why">The numbers (skill, chance, power, the rung's floor).</param>
/// <param name="Towns">The towns whose catalog scriveners sell the target (empty when none).</param>
public sealed record LearnNeed(ScrivenerLine Line, int Target, ScrivenerSpell? Spell, LearnNeedKind Kind, string Why, IReadOnlyList<string> Towns);

/// <summary>
/// M6 item 1 c (D41, D40): the scrolls the spell plan learns on a ground's town trips, beside the ground's own <c>learn</c> list. The lines are the bolt line of the rung's element (<see cref="SpellPlan.BoltLineNames"/>) and the debuff lines the
/// fight casts (<see cref="FightDebuffs"/>: the element's vulnerability and Imperil Other of Life Magic, Vulnerability Other and Magic Yield Other of Creature Enchantment), each only while its school is trained. A line's target is the highest
/// level whose chance on the CURRENT skill (<see cref="GearCasts.TryCurrentSkill"/>, the buffs counted, as ACE reads it) is at least the ladder's <see cref="Ladder.PlanCastChance"/>, never below the rung's plan level for the bolt line
/// (<see cref="LadderRung.SpellLevel"/>'s min), within the catalog's levels (I to VI; VII is the loot's, slice e). Only that level is bought, once the book lacks it itself (a higher level held does not count: M6 1 fix 1-R3) and ACE lets the character read it
/// (<c>Player.CanReadScroll</c>, Player_Skills.cs 458-471 @95ef426d: <c>ResupplyRun.CanRead</c>); a scroll already in the pack is read, not bought again (the resupply's step). The plan applies on a ground some
/// rung plays (<see cref="PlayedGround"/>); the rung is the one whose band holds the character's level, else the last that starts at or under it, else the first.
/// </summary>
/// <remarks>
/// M6 7 X (spells are the armor; r2b: level 10, 49 health, Armor Self I, dead to a Female Tusker in 5 s): the plan also learns the protective lines by the same rule: Armor Self, the protection line of each damage type the ground's targets carry
/// (<see cref="IncomingElements"/>, the profile's <c>damage_types</c>: data, never a constant) and the recovery lines the catalog lists (those whose value pays: Mana Renewal Self). The protective lines come after the bolt and before the
/// debuffs, ordered by the value of the step per coin (<see cref="ProtectiveValue"/>); a recovery line comes last and never calls a trip by itself (<see cref="LogisticsCounters.LearnDue"/>).
/// <para>M6 7 AD (the dance needs no Mana Conversion: ACE only discounts the cost with it, <c>Creature_Magic.cs:41-44</c> @95ef426d; r3d, level 10: dead at 8 mana with 52 stamina unused): Stamina to Mana Self comes right after the bolt, the cheapest thing on the list
/// (50 pyreals at I) and what the mana of every rung under 32 waits on. Its level is <see cref="DanceTarget"/>, not the highest at the chance. At level 10, with no Mana Conversion, Revitalize Self did not pay: a stamina point costs 0.6 mana at I, 0.92 at II, 0.89 at III and a press gives back 0.45 to 0.55.
/// M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores her mana): those numbers were right for level 10, and the rule now reads them live: Revitalize Self
/// (<see cref="ScrivenerLineKind.Revitalize"/>) stands right after the press, and a level of it joins the plan only when one round nets mana on her state (<see cref="RevitalizeTarget"/>: its mean boost cut to her standing stamina, through the best press, over its own
/// expected cost on her Mana Conversion), the level that nets the most; otherwise the line is named with its numbers and nothing is bought. Once a level she can pay is in the book, <c>Journey.StaminaDance.Covers</c> lets the potions go.</para>
/// <para>M6 7 AN: the "always push the next level" rule stops at these two lines on the numbers: at her size a press nets +15.6 at I, +8.5 at II, +1.3 at III and -5.8 at IV from stamina 57 (0.45 S - 10 at I), and a press plus Revitalize I is about -1 per two casts, so neither a higher
/// Stamina to Mana nor any Revitalize Self is pushed there (<see cref="DanceTarget"/> and <see cref="RevitalizeTarget"/> read it live, the higher levels come back at a stamina and a Mana Conversion where they net more). The coin's order after <see cref="Ladder.FirstScrolls"/> and the bootstrap: the drains and the heal she fights with
/// (<see cref="ScrivenerLineKind.Sustain"/>, 50 to 250 pyreals), then the regeneration lines (<see cref="ScrivenerLineKind.Recovery"/>, the owner: before more Item scrolls), then the rest.</para>
/// <para>M6 14 (<c>m6-life-through</c>, the owner's rule of 2026-11-12: an attacker her bolt cannot reach is fought with Life Magic; live that day a Tusker Crimsonback behind a torch hit her while every bolt met the torch,
/// and she knew no Harm Other): Harm Other (<see cref="ScrivenerLineKind.Attack"/>) stands right after the drains and the heal. One level is bought (<see cref="HomeFirstNeed"/>): the highest at the plan's chance on her
/// standing Life Magic that a scrivener of the ground's home town sells, so a level V the skill casts never leaves her with no Harm Other while Holtburg sells IV; once the home's level is in the book, the higher one
/// is the Town Network's, as any line's.</para>
/// <para>M6 14 (<c>m6-banes</c>, the owner's order of 2026-11-12): the item armor lines are eight: Impenetrability, Bludgeon Bane and the six other banes (Piercing, Blade, Lightning, Flame, Frost, Acid; the catalog's I to VI).
/// Each is read at the buff bar on the skill her pass reaches and bought by Harm Other's rule (<see cref="HomeFirstNeed"/>): Holtburg's Scrivener of Item Magic sells every level IV, the Master Scriveners of Item Magic level V
/// and the Grand Masters level VI, so a skill that casts V never leaves her without a line while IV is on sale at home; V and VI are the Town Network's once IV is in the book. The plan's level is one she can cast
/// (<see cref="ArmorLevelInReach"/>): a level whose scarab no vendor of the ground's trips sells is not bought; on a Holtburg ground with no town stock that is level V (the Gold Scarab), so IV stands as her level there. M6 item 14 (m6-goldscarab):
/// a ground whose town stock buys the scarab on the way out (the Gold and the Pyreal Scarab at Uziz: the Cave, the Cavern, the Abode, the Habitat, the Quarters, the Barracks) has it in reach, so V and VI are her levels there as her skill allows. Her components come with each scroll
/// (<c>ResupplyRun</c>'s scroll kit, the foci formula while she holds the Foci of Artifice: the level's scarab and its Prismatic Tapers; ACE <c>SpellFormula.GetFociFormula</c>, Entity/SpellFormula.cs:339-381 @95ef426d).
/// The six are planned from the catalog's <c>from_level</c> (<see cref="ScrivenerLine.FromLevel"/>, 32: the first level of the rung whose band ends at the level 40 the owner's order names); under it each answers
/// <see cref="LearnNeedKind.Later"/>: on the tusker rungs before it they would cut nothing (every target there deals bludgeon), while six more scrolls a level and six more casts a pass at home are coin and minutes a level 16 to 31 mage is short of.</para>
/// <para>M6 14 (<c>m6-caster</c>; the owner, 2026-11-13: the caster she wields is buffed with her own item magic, the buff bot casts none of it): the caster auras (<see cref="ScrivenerLineKind.CasterAura"/>: Spirit Drinker, Hermetic Link,
/// Defender) come after the item armor lines and before the debuffs: every line that keeps her alive first. Their order is the file's, by what each adds to her hour at the ladder's <c>caster_score</c> weights (Spirit Drinker V: +5% bolt
/// damage on a caster of the bolt's element, about 2.5%; Hermetic Link V: x1.5 on the Wand's 0.06, about 0.6%; Defender V: +0.13 melee defence, about 0.3%). Each is read at the buff bar on the standing skill and bought by the banes' rule
/// (<see cref="HomeFirstNeed"/>: Holtburg's Scrivener of Item Magic sells IV, the Master Scriveners V, the Grand Masters VI), and planned only while it adds to a caster she holds (<see cref="CasterGain"/>, else
/// <see cref="LearnNeedKind.NoGain"/>).</para>
/// </remarks>
public static class ScrivenerLearnPlan
{
    /// <summary>
    /// M6 7 AS (spec 2: the travel spells arrive by 16; the Burrow's exit lies under its hall, so a trip home needs Lifestone Recall): the travel lines the plan buys, in this order (the tie only serves the recall, ACE's LinkedLifestone, WorldObject_Magic.cs:966-976
    /// @95ef426d), each at the buff bar on the live skill (<see cref="IsBuffLine"/>). Portal Recall and the portal ties are the dungeons' own and stay unbought here.
    /// </summary>
    public static IReadOnlyList<string> TravelLines { get; } = ["Lifestone Recall", "Lifestone Tie"];

    /// <summary>
    /// M6 item 14 (the den run): the two travel lines the order adds, read before every other line (<see cref="LinesFor"/>'s <c>orderLines</c>): Primary Portal Tie 47 (power 150) and Primary Portal Recall 48 (power 100), both Item
    /// Enchantment and both sold by the Journeyman Scrivener of Item Magic 20213 (scriveners.json). No rung of the ladder asks for them: only an order does.
    /// </summary>
    public static IReadOnlyList<string> DenRunLines { get; } = ["Primary Portal Tie", "Primary Portal Recall"];

    /// <summary>The catalog's levels of a line (I to VI); VII is the loot's (D4).</summary>
    public const int CatalogLevels = 6;

    /// <summary>The element-free debuff lines the fight casts (<see cref="FightDebuffs"/>'s Imperil, Vulnerability and Magic Yield rows).</summary>
    public static IReadOnlyList<string> PlainDebuffLines { get; } = ["Imperil Other", "Vulnerability Other", "Magic Yield Other"];

    /// <summary>
    /// The rung of <paramref name="ladder"/> that names <paramref name="groundKey"/> for a character of <paramref name="level"/>, or null when no rung names the ground. A rung names the ground it plays (<see cref="PlayedGround"/>) and,
    /// M6 redoubt, its outdoor fallback ground (<see cref="RungFallback"/>: the outdoor ground of a rung whose own ground a level-limited portal guards), so a run that stands on the fallback keeps the band's learn plan.
    /// </summary>
    /// <param name="ladder">The ladder.</param>
    /// <param name="groundKey">The ground's profile key.</param>
    /// <param name="level">The character's level.</param>
    public static LadderRung? RungOf(Ladder ladder, string groundKey, int level)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        var naming = ladder.Rungs.Where(r => string.Equals(PlayedGround(r), groundKey, StringComparison.Ordinal) || RungFallback.Names(r, groundKey)).ToList();
        return naming.FirstOrDefault(r => r.Band.Holds(level)) ?? naming.LastOrDefault(r => r.Band.From <= level) ?? naming.FirstOrDefault();
    }

    /// <summary>
    /// The ground a rung plays: its own ground when that profile exists (not pending), else its first alternate whose profile exists, else null (a quest rung, or nothing to play yet). A ground listed only as a later alternate (the den, the
    /// Holding rung's second) is not a ladder ground here, so BSD Mage's den trips are unchanged.
    /// </summary>
    /// <param name="rung">The rung.</param>
    public static string? PlayedGround(LadderRung rung)
    {
        ArgumentNullException.ThrowIfNull(rung);
        return !rung.Pending && rung.GroundKey is { } own ? own : rung.Alternates.FirstOrDefault(a => !a.Pending)?.GroundKey;
    }

    /// <summary>ACE's <c>DamageType</c> bits by war element (ACE.Entity/Enum/DamageType.cs:12-18 @95ef426d).</summary>
    private static readonly (uint Bit, string Element)[] DamageBits = [(0x1, "slash"), (0x2, "pierce"), (0x4, "bludgeon"), (0x8, "cold"), (0x10, "fire"), (0x20, "acid"), (0x40, "electric")];

    /// <summary>
    /// M6 7 X: the elements of the damage the ground's targets deal (the OR of the <see cref="GroundRole.Target"/> rows' <see cref="GroundTarget.DamageTypes"/>, the profile's <c>damage_types</c>, whose 0x0004 is bludgeon); an avoided creature
    /// is not fought and does not count. Empty when no target says (the outdoor outskirts' creatures carry no such field: their rung learns no protection line, <c>TODO(m6-outskirts-damage-types)</c>, logged by the trip).
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static IReadOnlyList<string> IncomingElements(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var mask = 0u;
        foreach (var target in ground.Targets)
        {
            if (target.Role == GroundRole.Target)
            {
                mask |= target.DamageTypes;
            }
        }

        return [.. DamageBits.Where(b => (mask & b.Bit) != 0).Select(b => b.Element)];
    }

    /// <summary>The lines the plan learns on <paramref name="rung"/>: the element's bolt line, Stamina to Mana Self, Revitalize Self (M6 14), the bootstrap, Armor Self and the protection lines of <see cref="IncomingElements"/>, the sustain lines, the regeneration lines, the other self buffs, the item armor lines (the catalog's order: Impenetrability, Bludgeon Bane, then the six other banes), the element's vulnerability and <see cref="PlainDebuffLines"/> (each that the catalog holds).</summary>
    /// <param name="rung">The rung.</param>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="ground">The rung's ground, or null to read it from the catalog of grounds (<see cref="PlayedGround"/>).</param>
    /// <param name="orderLines">M6 item 14: the travel lines an order adds (<see cref="DenRunLines"/>), first in the list, or null: none. M6 14 the element line: also the bolt lines a job needs (<see cref="JobBolt.Needs"/>), learned by the same rule as the rung's.</param>
    public static IReadOnlyList<ScrivenerLine> LinesFor(LadderRung rung, ScrivenerCatalog catalog, HuntingGround? ground = null, IReadOnlyList<string>? orderLines = null)
    {
        ArgumentNullException.ThrowIfNull(rung);
        ArgumentNullException.ThrowIfNull(catalog);
        var lines = new List<ScrivenerLine>();
        if (orderLines is { Count: > 0 })
        {
            lines.AddRange(orderLines.Select(catalog.Line).OfType<ScrivenerLine>().Where(l => l.Kind is ScrivenerLineKind.Travel or ScrivenerLineKind.Bolt)); // M6 item 14: the order's scrolls first; M6 14 the element line: and the bolt line a job needs (JobBolt)
        }

        if (rung.Element is { } element)
        {
            lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Bolt && l.Element == element));
        }

        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Dance));
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Revitalize)); // M6 14 (the owner, 2026-11-14): the dance's stamina, right after the press; a level only when a round nets mana (RevitalizeTarget)
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.SelfBuff && BuffReach.Bootstrap.Contains(l.Name, StringComparer.Ordinal))); // M6 7 AK: the owner's: the bootstrap first
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Armor));
        var played = ground ?? (PlayedGround(rung) is { } key ? GroundCatalog.TryGet(key) : null);
        if (played is not null)
        {
            var incoming = IncomingElements(played);
            lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Protection && incoming.Contains(l.Element ?? string.Empty, StringComparer.Ordinal)));
        }

        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Travel && TravelLines.Contains(l.Name, StringComparer.Ordinal))); // M6 7 AS: Lifestone Recall, then Lifestone Tie: right after the protections the first scrolls name, before the drains
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Sustain)); // M6 7 AN: the brief's coin order: Drain Health I and II, Drain Stamina I, Heal Self II
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Attack)); // M6 14 (m6-life-through): Harm Other, with the drains she fights with
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Recovery)); // M6 7 AN (the owner): right after the two Life IVs and the bootstrap, before more Item scrolls
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.SelfBuff && !BuffReach.Bootstrap.Contains(l.Name, StringComparer.Ordinal))); // M6 7 AK: the file's order: the masteries, the attributes, Invulnerability
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.ItemArmor));
        lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.CasterAura)); // M6 14 (m6-caster): after every line that keeps her alive, before the debuffs; the file's order: Spirit Drinker, Hermetic Link, Defender (by what each adds to her hour)

        if (rung.Element is { } debuffElement)
        {
            lines.AddRange(catalog.Lines.Where(l => l.Kind == ScrivenerLineKind.Debuff && l.Element == debuffElement));
        }

        foreach (var name in PlainDebuffLines)
        {
            if (catalog.Line(name) is { } line)
            {
                lines.Add(line);
            }
        }

        return [.. lines.Distinct()]; // M6 14: a job's bolt line that is also the rung's is one line
    }

    /// <summary>The elements of <see cref="IncomingElements"/> the catalog holds no protection line for: the plan cannot learn them (the climber's buff profile casts only the bludgeon line, <c>TODO(m6-protection-line-other-types)</c>, logged by the trip).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="catalog">The scrivener catalog.</param>
    public static IReadOnlyList<string> UncoveredElements(HuntingGround ground, ScrivenerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(catalog);
        return [.. IncomingElements(ground).Where(e => !catalog.Lines.Any(l => l.Kind == ScrivenerLineKind.Protection && l.Element == e))];
    }

    /// <summary>The plan's answer for every line of the rung (see the class remarks). Allocates: call it on a trip or when the inputs changed, not per frame.</summary>
    /// <param name="ground">The ground (its home town).</param>
    /// <param name="rung">The rung (<see cref="RungOf"/>).</param>
    /// <param name="chance">The plan's chance (<see cref="Ladder.PlanCastChance"/>).</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="ladder">M6 7 AK: the ladder whose buff bar (<see cref="Ladder.PlanBuffChance"/>) reads the buff lines on the skill the bootstrap reaches (<see cref="BuffReach"/>) and whose <see cref="Ladder.FirstScrolls"/> the coin is saved for; null: the plan of before.</param>
    /// <param name="botCovers">
    /// M6 item 14 S3: the bot's profile that covers her lines for the run (<see cref="BuffBotCover.Covering"/>: the profile the run asks the bot for, while the cover is on): each line it casts answers <see cref="LearnNeedKind.BotCovers"/> (never due, so never bought or read), the first scrolls it casts are no
    /// goal the coin is saved for, and her own buff lines (the item lines, the travel lines) are read on the skill the bot's lines give (<see cref="BuffReach.Walk"/>). Null (the default): the plan of before.
    /// </param>
    /// <param name="orderLines">M6 item 14: the travel lines an order adds, read first (<see cref="DenRunLines"/>), or null: none.</param>
    /// <param name="casters">M6 14 (<c>m6-caster</c>): what the caster auras add to the casters she holds (<see cref="CasterGain.Of"/>), or null: not read, so none of the three is bought (named).</param>
    public static IReadOnlyList<LearnNeed> Needs(HuntingGround ground, LadderRung rung, double chance, PlayerQualities q, Spellbook book, ScrivenerCatalog catalog, Ladder? ladder = null, BuffBotProfile? botCovers = null, IReadOnlyList<string>? orderLines = null, CasterGain? casters = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        var needs = new List<LearnNeed>();
        var reach = ladder is null ? null : BuffReach.Walk(q, ladder.PlanBuffChance, (line, level) => catalog.LineSpells(line).Any(s => s.Level == level), botCovers is not null);
        var standing = ladder is null ? null : Standing(q, book, catalog, ladder, botCovers is not null); // M6 14 T: the stable inputs, one read for every line
        var stable = standing is null || ladder is null ? null : new StableRead(standing, BuffReach.StandingBoost(standing, ladder.PlanBuffChance, EnduranceLine, (l, level) => BookHolds(book, catalog, l, level), botCovers));
        foreach (var line in LinesFor(rung, catalog, ground, orderLines))
        {
            if (botCovers is not null && BuffBotCover.Covers(botCovers, line.Name))
            {
                needs.Add(new LearnNeed(line, 0, null, LearnNeedKind.BotCovers, BuffBotCover.Note(botCovers, line.Name), []));
                continue;
            }

            if (line.Kind == ScrivenerLineKind.CasterAura && (casters ?? CasterGain.Unread).NoGain(line.Name) is { } noGain)
            {
                // M6 14 (m6-caster): an aura that adds to no caster she holds is not bought: named, nothing spent
                needs.Add(new LearnNeed(line, 0, null, LearnNeedKind.NoGain, noGain, []));
                continue;
            }

            if (line.FromLevel is { } from && q.Level < from)
            {
                // M6 14 (m6-banes): a line planned from a later level: named, never due, so the low rungs' coin order and pass are as before
                needs.Add(new LearnNeed(line, 0, null, LearnNeedKind.Later, string.Create(CultureInfo.InvariantCulture, $"planned from level {from} (scriveners.json from_level; she is level {q.Level}): no scroll of it is bought before"), []));
                continue;
            }

            var steps = reach?.Steps.Where(s => s.Line == line.Name).ToList();
            if (steps is { Count: > 1 })
            {
                // the bootstrap climbs by its own steps: each level it stands on is a scroll of its own
                foreach (var step in steps.Where(s => !book.Knows(catalog.LineSpells(line.Name)[s.Level - 1].SpellId)))
                {
                    needs.Add(Need(ground, rung, chance, q, book, catalog, line, ladder, reach, step.Level, stable));
                }

                continue;
            }

            if (line.Kind == ScrivenerLineKind.Sustain)
            {
                // M6 7 AN: every level up to the plan's is a scroll of its own, lowest first (Drain Health I and II): the top level alone would leave a fresh book without I.
                // M6 sprint (live 2026-11-19 03:16-03:23 UTC, run 4, level 9, Life Magic 158 standing): every level the catalog holds was planned, capped only by what ACE lets her read, so the plan bought Heal Self IV (cast
                // chance 0.636) and Drain Health Other IV (her level at the plan's 0.95 was II) and sent her by the Town Network to Dryreach for Heal Self V (chance 0.050, 10,000 pyreals, its Gold Scarabs not held). Now the
                // levels stop at the plan's level on her skill (Need's target at the plan's chance), level I at the least.
                var top = Math.Max(1, Need(ground, rung, chance, q, book, catalog, line, ladder, reach, 0, stable).Target);
                var missing = catalog.LineSpells(line.Name).Where(s => s.Level <= top && !book.Knows(s.SpellId)).ToList();
                if (missing.Count > 0)
                {
                    needs.AddRange(missing.Select(s => Need(ground, rung, chance, q, book, catalog, line, ladder, reach, s.Level, stable)));
                    continue;
                }
            }

            var need = line.Kind is ScrivenerLineKind.Attack or ScrivenerLineKind.ItemArmor or ScrivenerLineKind.CasterAura ? HomeFirstNeed(ground, rung, chance, q, book, catalog, line, ladder, reach, stable) : Need(ground, rung, chance, q, book, catalog, line, ladder, reach, 0, stable);
            needs.Add(line.Kind == ScrivenerLineKind.CasterAura && casters is not null ? need with { Why = need.Why + "; it adds to her caster: " + casters.On(line.Name) } : need);
        }

        var sorted = ByValue(needs, ground, book, catalog);
        return ladder is null || reach is null ? sorted : SavedFor(sorted, ground, rung, chance, q, book, catalog, ladder, reach, botCovers, stable);
    }

    /// <summary>The stable inputs of one plan read: the standing skills and the standing Endurance line's points.</summary>
    private sealed record StableRead(BuffReachResult Skills, int EnduranceBoost);

    /// <summary>
    /// M6 14 T (live, 2026-11-12 05:23 UTC: the visit's plan read Stamina to Mana Self II as the target, at Life Magic 206 and a stamina of 89; the visit then spent 69,280 experience, and 80 s later the counters read level III, at 210 and 92, and
    /// called a trip the visit had just not served): what the plan reads a line's level on. Never the enchantments on her at the moment of the read (a buff that lapsed, one not yet cast, a creature's debuff), which differ between the
    /// counters' read in the field and the trip's read in town: her raw attributes, ranks and vitae with the lines she keeps standing (<see cref="BuffReach.Walk"/> over the levels her own book holds, or the buff bot's lines while its stop
    /// applies). The counters and the trip both come here through <see cref="Needs"/>, so the same character gives the same answer; what still moves it is hers and lasting: a rank, a level, a scroll read, the vitae.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="ladder">The ladder (its buff bar).</param>
    /// <param name="botCovers">True while the buff bot covers its lines.</param>
    /// <returns>The standing skills, or null when Focus or Self is not read (the caller then reads the current skill, as before).</returns>
    public static BuffReachResult? Standing(PlayerQualities q, Spellbook book, ScrivenerCatalog catalog, Ladder ladder, bool botCovers = false)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ladder);
        return BuffReach.Walk(q, ladder.PlanBuffChance, (line, level) => BookHolds(book, catalog, line, level), botCovers);
    }

    private static bool BookHolds(Spellbook book, ScrivenerCatalog catalog, string line, int level)
    {
        foreach (var spell in catalog.LineSpells(line))
        {
            if (spell.Level == level)
            {
                return book.Knows(spell.SpellId);
            }
        }

        // M6 14 own buffs: a level the plan's catalog does not list (its self lines run to IV) is read from the whole shop (the every-spell rule buys V and VI of her own lines; the owner, 2026-11-17)
        foreach (var spell in catalog.Stock.LineSpells(line))
        {
            if (spell.Level == level)
            {
                return book.Knows(spell.SpellId);
            }
        }

        return false;
    }

    /// <summary>The line of the stamina's attribute buff, which she keeps standing as the others.</summary>
    public const string EnduranceLine = "Endurance Self";

    /// <summary>
    /// M6 14 T: the stamina's maximum the dance line's level is read on: the vital's start and ranks and the raw Endurance, with the Endurance line she keeps standing (<see cref="BuffReach.StandingBoost"/>) and the vitae, as ACE sums a secondary
    /// attribute; never the maximum with whatever enchantments are on her now, and never the stamina she holds. False when the vital is not read.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="enduranceBoost">The standing Endurance line's points.</param>
    /// <param name="max">The standing maximum.</param>
    public static bool TryStandingStamina(PlayerQualities q, int enduranceBoost, out uint max)
    {
        ArgumentNullException.ThrowIfNull(q);
        max = 0;
        if (!q.IsKnown || !q.TryGetVital(MageTemplate.MaxStamina, out var vital))
        {
            return false;
        }

        var endurance = q.TryGetAttribute(MageTemplate.Endurance, out var raw) ? raw.Start + raw.Ranks : 0; // an Endurance the description lacks adds nothing, as the vital's own maximum reads it (PlayerQualities.TryGetVitalLevels)
        var vitae = q.TryGetVitae(out var v) ? v.Enchantment.StatModValue : 1.0;
        max = (uint)Math.Max(0, Math.Round((vital.Start + vital.Ranks + endurance + Math.Max(0, enduranceBoost)) * vitae, MidpointRounding.AwayFromZero));
        return true;
    }

    /// <summary>True for a line read on the bootstrap's skill at the buff bar.</summary>
    /// <param name="kind">The line's kind.</param>
    public static bool IsBuffLine(ScrivenerLineKind kind) => kind is ScrivenerLineKind.SelfBuff or ScrivenerLineKind.ItemArmor or ScrivenerLineKind.Armor or ScrivenerLineKind.Protection or ScrivenerLineKind.Recovery or ScrivenerLineKind.Travel or ScrivenerLineKind.CasterAura; // M6 7 AS: the travel lines are read at the buff bar too; M6 14 (m6-caster): the caster auras

    /// <summary>
    /// M6 7 AK (the owner: Armor Self IV and Bludgeoning Protection Self IV before any other level III or IV scroll, no question; on a short purse she saves for them): once every goal of <see cref="Ladder.FirstScrolls"/> casts at the bar on the skill the catalog's levels reach, its
    /// missing goals stand right after the bolt and the dance, in the file's order, and every other due line waits (<see cref="LearnNeedKind.Saving"/>, named) until none is missing.
    /// </summary>
    private static List<LearnNeed> SavedFor(IReadOnlyList<LearnNeed> sorted, HuntingGround ground, LadderRung rung, double chance, PlayerQualities q, Spellbook book, ScrivenerCatalog catalog, Ladder ladder, BuffReachResult reach, BuffBotProfile? botCovers = null, StableRead? stable = null)
    {
        // M6 item 14 S3: a first scroll the buff bot casts is no goal: nothing is saved for a scroll that is never bought (every goal covered: the list stands as sorted).
        var goals = ladder.FirstScrolls.Where(g => !(botCovers is not null && BuffBotCover.Covers(botCovers, g.Line))).Select(g => (Goal: g, Line: catalog.Line(g.Line), Spell: catalog.LineSpells(g.Line).FirstOrDefault(s => s.Level == g.Level))).Where(g => g.Line is not null && g.Spell is not null).ToList();
        var missing = goals.Where(g => !book.Knows(g.Spell!.SpellId)).ToList();
        var inReach = goals.Count > 0 && goals.All(g => SpellPlan.Chance(reach.SkillOf(g.Line!.School), g.Spell!.Power) >= ladder.PlanBuffChance);
        if (missing.Count == 0 || !inReach)
        {
            return [.. sorted];
        }

        var ids = goals.Select(g => g.Spell!.SpellId).ToHashSet();
        var total = missing.Sum(g => ground.PriceOf(g.Spell!.ScrollWcid) ?? 0);
        var note = string.Create(CultureInfo.InvariantCulture, $"saving for {string.Join(", ", missing.Select(g => g.Spell!.Name))}: {total:N0} pyreals, the owner's first scrolls");
        static bool Ahead(LearnNeed n) => n.Line.Kind is ScrivenerLineKind.Bolt or ScrivenerLineKind.Dance && n.Target <= 2; // the cheap lines she fights with; "any other level III or IV scroll" waits, Flame Bolt III among them
        var head = sorted.Where(Ahead).ToList();
        head.AddRange(missing.Select(g => Need(ground, rung, chance, q, book, catalog, g.Line!, ladder, reach, g.Goal.Level, stable)));
        head.AddRange(sorted.Where(n => !Ahead(n) && (n.Spell is null || !ids.Contains(n.Spell.SpellId))).Select(n => n.Kind == LearnNeedKind.Due ? n with { Kind = LearnNeedKind.Saving, Why = n.Why + ": " + note } : n));
        return head;
    }

    /// <summary>M6 7 X: the protective lines (their block stands where <see cref="LinesFor"/> puts it) in the order of the value of the step per coin, highest first; a tie keeps the catalog's order, a line with no step last.</summary>
    private static List<LearnNeed> ByValue(List<LearnNeed> needs, HuntingGround ground, Spellbook book, ScrivenerCatalog catalog)
    {
        static bool Protective(LearnNeed n) => n.Line.Kind is ScrivenerLineKind.Armor or ScrivenerLineKind.Protection;
        var first = needs.FindIndex(Protective);
        if (first < 0)
        {
            return needs;
        }

        double Value(LearnNeed n)
        {
            if (n.Spell is not { } spell)
            {
                return 0;
            }

            var price = spell.Sellers.Select(s => ground.VendorOf(s.VendorWcid)?.Sells.FirstOrDefault(w => w.Wcid == spell.ScrollWcid)?.Price).FirstOrDefault(p => p is not null)
                ?? (catalog.ScrivenerOf(spell.Sellers[0].VendorWcid)?.PriceOf(spell.ScrollValue) ?? 0);
            return ProtectiveValue.PerCoin(ProtectiveValue.Gain(catalog.LineSpells(n.Line.Name), spell, book), price);
        }

        var block = needs.Where(Protective).OrderByDescending(Value).ToList();
        needs.RemoveAll(Protective);
        needs.InsertRange(first, block);
        return needs;
    }

    /// <summary>
    /// The line's target level on the current skill: the highest level whose chance is at least <paramref name="chance"/>, never under the line's <paramref name="floor"/> (the rung's plan level for the bolt line). The one rule the buy
    /// (<see cref="Need"/>) and the keep rule (<see cref="ScrollKeepRule"/>, M6 1 fix 1-R3) share.
    /// </summary>
    /// <param name="skill">The current skill.</param>
    /// <param name="chance">The plan's chance.</param>
    /// <param name="powers">The line's powers, I first (the catalog's levels I to VI).</param>
    /// <param name="floor">The line's floor (0: none).</param>
    public static int TargetLevel(uint skill, double chance, IReadOnlyList<uint> powers, int floor) => Math.Max(SpellPlan.LevelAt(skill, chance, powers), floor);

    /// <summary>
    /// M6 7 AD: the level of the dance line to hold: of the levels up to <paramref name="top"/> (the plan's chance on the standing skill, the 95% rule), the one whose press nets the most mana (<see cref="ManaOrder.NetYield"/>) from the stamina's standing maximum (<see cref="TryStandingStamina"/>, M6 14 T), the lower on a tie; 0 when
    /// none nets any. A higher level costs more than it adds at a low stamina: at 50 stamina I nets 12.5, II 5, III -2.5; II passes I at 110 stamina (I's cap of 50 drained), and from there the highest level at 160 or more. The price does not enter: a rest's presses pay any of these scrolls
    /// in the first hour.
    /// </summary>
    /// <param name="spells">The line's spells, I first.</param>
    /// <param name="top">The highest level at the plan's chance.</param>
    /// <param name="q">The player's qualities (the stamina's standing maximum; the Mana Conversion, 0 when untrained).</param>
    /// <param name="note">The numbers, for the need's <see cref="LearnNeed.Why"/>.</param>
    /// <param name="enduranceBoost">M6 14 T: the points of the Endurance line she keeps standing (<see cref="BuffReach.StandingBoost"/>); 0: none.</param>
    public static int DanceTarget(IReadOnlyList<ScrivenerSpell> spells, int top, PlayerQualities q, out string note, int enduranceBoost = 0)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(q);
        note = string.Empty;
        if (top <= 0)
        {
            return 0;
        }

        if (!TryStandingStamina(q, enduranceBoost, out var staminaMax) || staminaMax == 0)
        {
            note = "; the stamina is not read: level I, the cheapest press";
            return 1;
        }

        ManaOrder.TryManaConversion(q, out var conversion);
        var (best, bestNet) = (0, 0.0);
        for (var i = 0; i < top && i < spells.Count && i < BuffStatMods.StaminaToManaBaseMana.Count; i++)
        {
            if (BuffStatMods.TransferOf(spells[i].SpellId) is not { } row)
            {
                continue;
            }

            var level = new DanceLevel(spells[i].SpellId, BuffStatMods.StaminaToManaBaseMana[i], spells[i].Power, row);
            var net = ManaOrder.NetYield(in level, staminaMax, conversion);
            if (net > bestNet)
            {
                (best, bestNet) = (i + 1, net);
            }
        }

        note = string.Create(CultureInfo.InvariantCulture, $"; at stamina {staminaMax} (the standing maximum) level {Numeral(best)} nets the most, {bestNet:0.#} a press");
        return best;
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores her mana quickly, with no potion at all): the level of Revitalize Self to hold. Of the levels up to
    /// <paramref name="top"/> (the plan's chance on her standing Life Magic, as <see cref="DanceTarget"/> reads the press's), the one whose round nets the most mana: the level restores its mean boost (<see cref="BuffStatMods.Revitalize"/>, ACE's roll in
    /// [boost, boost + variance]), cut to the stamina's standing maximum (<see cref="TryStandingStamina"/>); the best press of <paramref name="presses"/> up to <paramref name="pressTop"/> then nets from that stamina
    /// (<see cref="ManaOrder.NetYield"/>: the give less the press's expected cost); and the round pays the level's own expected cost on her Mana Conversion (<see cref="ManaConversionModel.ExpectedCost"/> over its base mana,
    /// <see cref="BuffStatMods.RevitalizeBaseMana"/>: a Life Magic self spell's <c>GearCasts.ManaCeiling</c>). The lower level on a tie; 0 when no level's round nets any, so the line stays off and <paramref name="note"/> says why with the numbers
    /// (at level 10, with no Mana Conversion, every round loses: I restores 25 stamina for 15 mana and a press gives back about 11 for it). The same arithmetic as <c>Journey.StaminaDance.Covers</c>, which then lets the potions go once the level is in her book.
    /// </summary>
    /// <param name="spells">The Revitalize Self spells, I first.</param>
    /// <param name="top">The highest level at the plan's chance.</param>
    /// <param name="presses">The Stamina to Mana Self spells, I first.</param>
    /// <param name="pressTop">The press's highest level at the plan's chance.</param>
    /// <param name="q">The player's qualities (the stamina's standing maximum; the Mana Conversion, 0 when untrained).</param>
    /// <param name="note">The numbers, for the need's <see cref="LearnNeed.Why"/>.</param>
    /// <param name="enduranceBoost">The points of the Endurance line she keeps standing (<see cref="BuffReach.StandingBoost"/>); 0: none.</param>
    public static int RevitalizeTarget(IReadOnlyList<ScrivenerSpell> spells, int top, IReadOnlyList<ScrivenerSpell> presses, int pressTop, PlayerQualities q, out string note, int enduranceBoost = 0)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(presses);
        ArgumentNullException.ThrowIfNull(q);
        var c = CultureInfo.InvariantCulture;
        note = string.Empty;
        if (top <= 0)
        {
            return 0;
        }

        if (!TryStandingStamina(q, enduranceBoost, out var staminaMax) || staminaMax == 0)
        {
            note = "; the stamina is not read: a round of the dance is not weighed on a guess, so no level";
            return 0;
        }

        var levels = new List<(int Level, DanceLevel Press)>();
        for (var i = 0; i < pressTop && i < presses.Count && i < BuffStatMods.StaminaToManaBaseMana.Count; i++)
        {
            if (BuffStatMods.TransferOf(presses[i].SpellId) is { } row)
            {
                levels.Add((i + 1, new DanceLevel(presses[i].SpellId, BuffStatMods.StaminaToManaBaseMana[i], presses[i].Power, row)));
            }
        }

        if (levels.Count == 0)
        {
            note = "; no Stamina to Mana Self level stands at the plan's chance: no press for the stamina, so no level";
            return 0;
        }

        ManaOrder.TryManaConversion(q, out var conversion);
        var (best, bestNet, bestRestored, bestCost, bestPress, bestPressNet) = (0, 0.0, 0u, 0.0, 0, 0.0);
        var rounds = new List<string>();
        for (var i = 0; i < top && i < spells.Count && i < BuffStatMods.RevitalizeBaseMana.Count; i++)
        {
            if (BuffStatMods.BoostOf(spells[i].SpellId) is not { } boost)
            {
                continue;
            }

            var restored = (uint)Math.Min(staminaMax, Math.Round((boost.Boost + boost.Max) / 2.0)); // the mean of ACE's roll, cut at the maximum as the vital update cuts it
            var cost = ManaConversionModel.ExpectedCost(BuffStatMods.RevitalizeBaseMana[i], spells[i].Power, conversion);
            var (press, pressNet) = (0, double.MinValue);
            foreach (var (level, dance) in levels)
            {
                var net = ManaOrder.NetYield(in dance, restored, conversion);
                if (net > pressNet)
                {
                    (press, pressNet) = (level, net);
                }
            }

            var round = pressNet - cost;
            rounds.Add(string.Create(c, $"{Numeral(i + 1)} {round:+0.#;-0.#;0}"));
            if (round > bestNet)
            {
                (best, bestNet, bestRestored, bestCost, bestPress, bestPressNet) = (i + 1, round, restored, cost, press, pressNet);
            }
        }

        var each = string.Join(", ", rounds);
        note = best > 0
            ? string.Create(c, $"; a round on Mana Conversion {conversion} at stamina {staminaMax} (the standing maximum): Revitalize Self {Numeral(best)} restores {bestRestored} stamina for an expected {bestCost:0.#} mana, then Stamina to Mana Self {Numeral(bestPress)} nets {bestPressNet:0.#} from it: {bestNet:0.#} mana a round, the most ({each})")
            : string.Create(c, $"; no round nets mana on Mana Conversion {conversion} at stamina {staminaMax} (the standing maximum): the stamina a level restores gives back less through the best press than the level costs ({each}), so Revitalize Self stays off");
        return best;
    }

    /// <summary>
    /// M6 14 (<c>m6-life-through</c>): the need of an attack line (Harm Other), and, M6 14 (<c>m6-banes</c>, the owner's order of 2026-11-12: every armour protection on her armour before the Direlands), of an item armor line
    /// (Impenetrability and the seven banes): a line she must not be without while the plan's level waits for a trip to another town. The line's own answer first (<see cref="Need"/>: the highest level at the plan's chance,
    /// an attack line's on the standing skill at the combat bar, an armor line's at the buff bar). When that level is sold only
    /// off home, the highest level under it that a scrivener of the home town sells is bought first (named with the level it stands in for); with that one in the book, the off-home answer stands and the Town Network
    /// fetches the higher level as it does any line's. With no level under it sold at home, the off-home answer as it is.
    /// </summary>
    private static LearnNeed HomeFirstNeed(HuntingGround ground, LadderRung rung, double chance, PlayerQualities q, Spellbook book, ScrivenerCatalog catalog, ScrivenerLine line, Ladder? ladder, BuffReachResult? reach, StableRead? stable)
    {
        var own = Need(ground, rung, chance, q, book, catalog, line, ladder, reach, 0, stable);
        if (line.Kind is ScrivenerLineKind.ItemArmor or ScrivenerLineKind.CasterAura && own.Target > 0 && ArmorLevelInReach(ground, own.Target, out var scarab) is var castable and > 0 && castable < own.Target)
        {
            // M6 14 (m6-banes): a level she could not cast is not the plan's: the level under it whose scarab her trips sell, named with the scarab that is missing
            var capped = Need(ground, rung, chance, q, book, catalog, line, ladder, reach, castable, stable);
            own = capped with { Why = string.Create(CultureInfo.InvariantCulture, $"{capped.Why}; level {Numeral(own.Target)} burns a {scarab} no vendor of this ground's trips sells, so she could not cast it: the plan's level is {Numeral(castable)} (TODO(m6-armor-scarab-reach))") };
        }
        if (own.Kind != LearnNeedKind.OffHome || own.Spell is not { } far)
        {
            return own;
        }

        var home = catalog.LineSpells(line.Name).LastOrDefault(s => s.Level < far.Level && s.Sellers.Any(seller => ground.VendorOf(seller.VendorWcid) is not null) && catalog.TownsSelling(s).Contains(ground.Home.Town, StringComparer.Ordinal));
        if (home is null || book.Knows(home.SpellId))
        {
            return own;
        }

        var stand = Need(ground, rung, chance, q, book, catalog, line, ladder, reach, home.Level, stable);
        return stand with { Why = string.Create(CultureInfo.InvariantCulture, $"{stand.Why}; the plan's level {Numeral(far.Level)} is sold only at {string.Join(", ", own.Towns)}: the highest level {ground.Home.Town} sells is bought first") };
    }

    private static LearnNeed Need(HuntingGround ground, LadderRung rung, double chance, PlayerQualities q, Spellbook book, ScrivenerCatalog catalog, ScrivenerLine line, Ladder? ladder = null, BuffReachResult? reach = null, int forced = 0, StableRead? stable = null)
    {
        var c = CultureInfo.InvariantCulture;
        if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(line.School), out var skill, out var noSkill))
        {
            return new LearnNeed(line, 0, null, LearnNeedKind.Untrained, noSkill, []);
        }

        if (line.NeedsSkill is { } needed && (!q.TryGetSkill(needed, out var neededSkill) || neededSkill.AdvancementClass < Contracts.SkillRules.TrainedClass))
        {
            return new LearnNeed(line, 0, null, LearnNeedKind.Untrained, string.Create(c, $"{MageTemplate.SkillName(needed)} is not trained: the line is not learned yet"), []);
        }

        var read = "current";
        uint? standingSkill = null;
        if (stable is not null)
        {
            skill = stable.Skills.SkillOf(line.School); // M6 14 T: the skill with the lines she keeps standing, the same number in the field and in town; never what is on her at the moment of the read
            standingSkill = skill;
            read = "standing";
        }

        if (ladder is not null && reach is not null && IsBuffLine(line.Kind))
        {
            skill = Math.Max(skill, reach.SkillOf(line.School)); // M6 7 AK: the skill the pass reaches, not what stands now; the bar is the buff's
            chance = ladder.PlanBuffChance;
        }

        var spells = catalog.LineSpells(line.Name);
        var level = SpellPlan.LevelAt(skill, chance, [.. spells.Select(s => s.Power)]);
        var floor = line.Kind == ScrivenerLineKind.Bolt ? Math.Min(rung.SpellLevel.Min, spells.Count) : 0;
        var target = TargetLevel(skill, chance, [.. spells.Select(s => s.Power)], floor);
        var danceNote = string.Empty;
        if (line.Kind == ScrivenerLineKind.Dance)
        {
            target = DanceTarget(spells, target, q, out danceNote, stable?.EnduranceBoost ?? 0);
        }
        else if (line.Kind == ScrivenerLineKind.Revitalize)
        {
            // M6 14 (the owner, 2026-11-14): the press this round funds is the dance line's, its levels up to the plan's chance on that line's own skill (both are Life Magic in the catalog, so the same number)
            var dance = catalog.Lines.FirstOrDefault(l => l.Kind == ScrivenerLineKind.Dance);
            IReadOnlyList<ScrivenerSpell> presses = dance is null ? [] : catalog.LineSpells(dance.Name);
            var pressSkill = dance is null || dance.School == line.School ? skill : stable?.Skills.SkillOf(dance.School) ?? (GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(dance.School), out var own, out _) ? own : 0);
            target = RevitalizeTarget(spells, target, presses, TargetLevel(pressSkill, chance, [.. presses.Select(s => s.Power)], 0), q, out danceNote, stable?.EnduranceBoost ?? 0);
        }

        if (forced > 0)
        {
            target = forced; // M6 7 AK: a bootstrap step or a first scroll: its own level
        }

        var numbers = string.Create(c, $"{line.School} skill {skill} ({read}), plan chance {chance:0.00}: level {Numeral(level)} at the chance{(floor > 0 ? $", rung {rung.Key}'s floor {Numeral(floor)}" : string.Empty)}{danceNote}");
        if (target == 0)
        {
            return new LearnNeed(line, 0, null, LearnNeedKind.NoLevel, numbers + ": no level", []);
        }

        var spell = spells[target - 1];
        var why = string.Create(c, $"{numbers}: target {spell.Name} (power {spell.Power}, chance {SpellPlan.Chance(skill, spell.Power):0.000})");
        if (book.Knows(spell.SpellId))
        {
            // M6 1 fix (1-R3): Known only when the book holds the target level itself; a higher level held (a looted scroll) never hides the plan's own level from the buy, and the fight's cap (AutopilotScenario.BoltLevel: the highest level
            // the book holds at or under the cap) casts the target once it is read.
            return new LearnNeed(line, target, spell, LearnNeedKind.Known, why + ": the book holds it", []);
        }

        var towns = catalog.TownsSelling(spell);
        var canRead = standingSkill is { } stood ? Scenarios.Journey.ResupplyRun.CanRead(stood, line.School, spell.Power, out var readable) : Scenarios.Journey.ResupplyRun.CanRead(q, line.School, spell.Power, out readable);
        if (canRead != Scenarios.Journey.LearnReason.None)
        {
            return new LearnNeed(line, target, spell, LearnNeedKind.SkillShort, why + ": not readable yet (" + readable + ")", towns);
        }

        return towns.Contains(ground.Home.Town, StringComparer.Ordinal) && spell.Sellers.Any(s => ground.VendorOf(s.VendorWcid) is not null)
            ? new LearnNeed(line, target, spell, LearnNeedKind.Due, why + ": " + readable + "; sold at " + ground.Home.Town, towns)
            : new LearnNeed(line, target, spell, LearnNeedKind.OffHome, why + ": " + readable + "; sold only at " + string.Join(", ", towns) + ", not at the home " + ground.Home.Town, towns);
    }

    /// <summary>
    /// M6 14 (<c>m6-banes</c>; the brief's "with its components": she casts by foci, so check what each level burns): the highest level of an item armor line, at most <paramref name="level"/>, that she can cast on the foci formula
    /// with what the ground's trips sell: level n of Impenetrability and of every bane burns the scarab of its level and Prismatic Tapers (the SpellTable's formulas through ACE's <c>SpellFormula.GetFociFormula</c>,
    /// Entity/SpellFormula.cs:339-381 @95ef426d: Lead, Iron, Copper, Silver, Gold, Pyreal at I to VI; <c>ArmourBanesDatTests</c> pins it), and a cast needs the scarab in the pack (<c>HasComponentsForSpell</c>, Player_Magic.cs:1227-1248).
    /// Holtburg's Archmage sells the Lead, Iron, Copper and Silver Scarabs; the Gold Scarab (687) and the Pyreal Scarab (690) are sold only by archmages of other towns (the world's create lists, read 2026-11-12: Zisaha of Fort
    /// Tethana among them), and no home trip of a Holtburg ground buys them, so on a ground with no town stock for it level V stays out of her reach whatever her skill: <c>TODO(m6-armor-scarab-reach)</c>. M6 item 14 (m6-goldscarab):
    /// a scarab the ground buys in another town on the way out (<see cref="HuntingGround.TownStockOf"/>: the profile's <c>town_stock</c>) is in reach as one a home vendor sells is; the TODO stays for a ground with neither. 0 when no level up to
    /// <paramref name="level"/> has its scarab on sale (a ground whose vendors list none: nothing is capped on a guess).
    /// </summary>
    /// <param name="ground">The ground (its vendors' <c>sells</c> and its town stock).</param>
    /// <param name="level">The level the plan's chance gives (1 to 6).</param>
    /// <param name="missing">The scarab of <paramref name="level"/> when no vendor of the ground sells it ("Gold Scarab 687"), else empty.</param>
    public static int ArmorLevelInReach(HuntingGround ground, int level, out string missing)
    {
        ArgumentNullException.ThrowIfNull(ground);
        missing = string.Empty;
        for (var l = Math.Min(level, CatalogLevels); l >= 1; l--)
        {
            if (!Lifestone.Data.Magic.ComponentWeenies.TryGetByComponent((uint)l, out var scarab))
            {
                return 0; // the component map names every scarab; an unknown one caps nothing
            }

            if (ground.VendorSelling(scarab.Wcid) is not null || ground.TownStockOf(scarab.Wcid) is not null)
            {
                return l;
            }

            if (l == level)
            {
                missing = string.Create(CultureInfo.InvariantCulture, $"{scarab.Name} {scarab.Wcid}");
            }
        }

        return 0;
    }

    /// <summary>The level's numeral (I to VI), or "none".</summary>
    /// <param name="level">0 to 6.</param>
    public static string Numeral(int level) => level switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII", // M6 14 (D135): the Holding's gate names the buff bot's level VII lines
        8 => "VIII",
        _ => "none",
    };
}
