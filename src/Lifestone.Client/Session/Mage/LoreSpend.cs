using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-lore-spend</c>): the Arcane Lore a wield will read. ACE checks a piece's ItemDifficulty against the CURRENT Arcane Lore (<c>CheckUseRequirements</c>, WorldObject_Use.cs:203-210 @95ef426d: <c>GetCreatureSkill(Skill.ArcaneLore).Current</c>,
/// the buffs on her in, Focus's through the formula Focus / 3) once, at the wield (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335), and spells lit stay lit until the piece runs dry or comes off (ManaConsumersTick, Player_Tick.cs:681-699).
/// So the number to reach is the lore at the next visit's gear pick, buffed: her ranks and Focus, the worn pieces' lasting spells (an Arcane Prowess cantrip, a Focus cantrip), and the buff bot's lines while the stop at the bot applies
/// (Aliester's Boon +40 and its Focus line: the bot renews them at each visit, the gear phase runs before its stop, and the trip home is called before they run out, so they stand at the pick). Her own passes' buffs lapse and are not
/// counted; a bot line that has lapsed now is not counted either (the ranks bought are then more than the pick needs, never fewer).
/// </summary>
public static class StandingLore
{
    /// <summary>Every spell the buff bot casts, by id (every profile's lines: <see cref="Lifestone.ServerTools.BuffBotWire.Profiles"/>).</summary>
    public static IReadOnlySet<uint> BotSpells { get; } = Lifestone.ServerTools.BuffBotWire.Profiles.SelectMany(p => p.Lines).Select(l => l.SpellId).ToHashSet();

    /// <summary>The Arcane Lore that stands at a wield (see the class remarks), or null when it is not read; <paramref name="why"/> names the reading: <c>Arcane Lore 222 now, 222 standing (base 164; ...)</c>.</summary>
    /// <param name="q">Her qualities.</param>
    /// <param name="botApplies">True while the stop at the buff bot applies to the run.</param>
    /// <param name="why">The reading, for the lines.</param>
    public static int? Of(PlayerQualities q, bool botApplies, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        var formula = FighterGearCheck.ArcaneLoreFormula;
        if (!q.IsKnown || !SkillRules.TryCurrent(q, MageTemplate.ArcaneLore, in formula, SkillRules.TrainedClass, out var now) || !SkillRules.TryBase(q, MageTemplate.ArcaneLore, in formula, SkillRules.TrainedClass, out var baseLore))
        {
            why = "TODO(m6-lore-unread): her Arcane Lore or Focus is not in the description";
            return null;
        }

        var all = q.Enchantments;
        var standing = all.IsDefaultOrEmpty ? ImmutableArray<EnchantmentDesc>.Empty : [.. all.Where(e => e.Duration < 0.0 || (botApplies && BotSpells.Contains(e.SpellId)))];
        _ = SkillRules.TryCurrentOver(q, MageTemplate.ArcaneLore, in formula, SkillRules.TrainedClass, standing, out var stands);
        var lasting = all.IsDefaultOrEmpty ? 0 : all.Count(e => e.Duration < 0.0);
        var bot = botApplies && !all.IsDefaultOrEmpty ? all.Count(e => e.Duration >= 0.0 && BotSpells.Contains(e.SpellId)) : 0;
        why = string.Create(
            CultureInfo.InvariantCulture,
            $"Arcane Lore {now} now, {stands} standing at the wield (base {baseLore}; ACE reads the current lore at the wield: counted, the worn pieces' {lasting} lasting spell(s) and {(botApplies ? $"the buff bot's {bot} line(s) on her" : "no line of the buff bot (its stop does not apply)")}; not counted, her own buffs)");
        return (int)Math.Min(stands, int.MaxValue);
    }

    /// <summary>
    /// M6 14 own lore (the owner's ruling, 2026-11-17: so many of her buffs had fallen that she could no longer activate the shoes: the bot's 3,600 s lines from 11:26 lapsed about 12:26, Aliester's Boon among them): her Arcane Enlightenment Self
    /// spells, by id, with their <c>stat_Mod_Val</c> (the world's spell table, read 2026-11-17: 678 I 10, 679 II 15, 680 III 20, 681 IV 25, 682 V 30, 683 VI 35, 4510 the Incantation 45; type 36880 = 0x9010, key 14 Arcane Lore).
    /// </summary>
    public static IReadOnlyList<(uint Spell, int Points)> OwnEnlightenment { get; } = [(678, 10), (679, 15), (680, 20), (681, 25), (682, 30), (683, 35), (4510, 45)];

    /// <summary>The Arcane Enlightenment Self line, by the shop's name.</summary>
    public const string EnlightenmentLine = "Arcane Enlightenment Self";

    /// <summary>The points of the highest Arcane Enlightenment Self level her book holds (<see cref="OwnEnlightenment"/>), 0 when none.</summary>
    /// <param name="knows">Whether her book holds a spell.</param>
    public static int OwnEnlightenmentPoints(Func<uint, bool> knows)
    {
        ArgumentNullException.ThrowIfNull(knows);
        var best = 0;
        foreach (var (spell, points) in OwnEnlightenment)
        {
            if (points > best && knows(spell))
            {
                best = points;
            }
        }

        return best;
    }

    /// <summary>
    /// M6 14 own lore: the Arcane Lore she stands at on her OWN: her ranks and Focus, the worn pieces' lasting spells, and her own Arcane Enlightenment Self at the highest level her book holds (<see cref="OwnEnlightenmentPoints"/>), never
    /// the buff bot's lines (its Aliester's Boon, +40, and Calming Gaze, +40 Focus, lapse after 3,600 s and her gear then reads her own lore at its next wield). The lore the experience spend raises her gear's needs toward. Null when unread.
    /// </summary>
    /// <param name="q">Her qualities.</param>
    /// <param name="knows">Whether her book holds a spell.</param>
    /// <param name="why">The reading, for the lines.</param>
    public static int? OwnOf(PlayerQualities q, Func<uint, bool> knows, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(knows);
        var lasting = Of(q, botApplies: false, out var lastingWhy);
        if (lasting is not { } l)
        {
            why = lastingWhy;
            return null;
        }

        var own = OwnEnlightenmentPoints(knows);
        var withBot = Of(q, botApplies: true, out _);
        why = string.Create(
            CultureInfo.InvariantCulture,
            $"Arcane Lore {l + own} on her own ({lastingWhy}; her own {EnlightenmentLine}: {(own > 0 ? "+" + own.ToString(CultureInfo.InvariantCulture) : "no level in her book")}; with the buff bot's lines on her now {withBot?.ToString(CultureInfo.InvariantCulture) ?? "unread"}, not counted: they lapse)");
        return l + own;
    }
}

/// <summary>M6 14 (<c>m6-lore-spend</c>): a piece she holds whose spells her Arcane Lore alone keeps off, with the wear pick's verdict at its ItemDifficulty (<see cref="WearPick.LoreNeed"/>).</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Name">Its name.</param>
/// <param name="Difficulty">Its ItemDifficulty.</param>
/// <param name="Kind">What the pick would do at that lore: Wear or Replace.</param>
/// <param name="Gain">The pick's gain at that lore, in percent of her hour.</param>
/// <param name="Margin">The margin it beat.</param>
public sealed record LoreWant(uint Guid, string Name, int Difficulty, SelfEquipKind Kind, double Gain, double Margin)
{
    /// <summary>M6 14 own lore: a piece she wears whose spells are off (her Arcane Lore was under its ItemDifficulty at its wield): lit by a re-wield at that lore (<see cref="LoreSpend.WornWants"/>).</summary>
    public bool Worn { get; init; }

    /// <summary>The pick's verdict as the lines print it.</summary>
    public string Verdict => Worn
        ? string.Create(CultureInfo.InvariantCulture, $"she wears it with its spells off: a re-wield at that lore lights them, gain {Gain:0.###}% of her hour")
        : string.Create(CultureInfo.InvariantCulture, $"the wear pick would {(Kind == SelfEquipKind.Wear ? "wear" : "replace with")} it at that lore, gain {Gain:0.###}% of her hour over the margin {Margin:0.##}");
}

/// <summary>M6 14 (<c>m6-lore-spend</c>): what the experience spend reads for the Arcane Lore rule (<see cref="LoreSpend.Decide"/>).</summary>
/// <param name="Wants">The pieces she holds that wait on her Arcane Lore (<see cref="LoreSpend.Wants"/>).</param>
/// <param name="Standing">Her Arcane Lore at a wield (<see cref="StandingLore"/>), or null when unread.</param>
/// <param name="Lore">The lore's reading, for the lines.</param>
/// <param name="Context">The wear pick's context of her (her numbers and the ground), which weighs the same experience on her magic skills; null when no pick is held (nothing is bought).</param>
/// <param name="Terms">The wear pick's terms, likewise.</param>
/// <param name="XpPerHour">Her measured experience an hour, or null.</param>
/// <param name="Rate">Where the rate comes from, or why there is none.</param>
public sealed record LoreSpendFacts(IReadOnlyList<LoreWant> Wants, int? Standing, string Lore, GearContext? Context, WearTerms? Terms, double? XpPerHour, string Rate);

/// <summary>M6 14 (<c>m6-lore-spend</c>): what the spend decided for Arcane Lore.</summary>
/// <param name="Chosen">The piece the lore is raised toward, or null.</param>
/// <param name="Target">The Arcane Lore target the planner buys after the floors and before the shares (<see cref="MageSpendingPlanner.Compute"/>'s <c>beforeShares</c>), or null.</param>
/// <param name="Cost">The experience those ranks cost from what she has spent on Arcane Lore, 0 with none chosen.</param>
/// <param name="Line">The decision's line.</param>
/// <param name="Others">Every other piece's line, each with its numbers and reason.</param>
public sealed record LoreSpendDecision(LoreWant? Chosen, StatTarget? Target, long Cost, string Line, IReadOnlyList<string> Others);

/// <summary>
/// M6 14 (<c>m6-lore-spend</c>; the owner, 2026-11-14: Arcane Lore is trained and rises with experience; the climber's spend raises it when gear she holds needs it). Live: Qwinnie at level 66, Arcane Lore 222, looted pieces of ItemDifficulty 225 to 291 whose
/// spells never lit (<c>gear: no gain from 'Studded Leather Shirt' ... activation: Arcane Lore 222 &lt; 260</c>).
/// </summary>
/// <remarks>
/// <para><b>The pieces.</b> Every armour, clothing or jewellery piece in her packs the wear pick keeps for the review because her Arcane Lore alone keeps its spells off, with the pick's own verdict at its ItemDifficulty
/// (<see cref="WearScore.Judge"/>: the piece judged again with her lore there, every term in, the armour, the burden and the load among them): only one the pick would then wear (<see cref="LoreWant"/>).</para>
/// <para><b>The price.</b> The points are the difficulty less her standing lore (<see cref="StandingLore"/>: the lore ACE reads at the wield); one rank is one point (Arcane Lore is Focus / 3 plus its start and its ranks), priced from the XpTable's list of
/// its class against what she has spent on it, and in hours at her measured rate.</para>
/// <para><b>It pays</b> when the piece's gain beats what the same experience brings her on her magic skills, by the pick's own terms (<see cref="WearScore.SkillPoints"/>: the ranks it buys of each of War Magic, Life Magic, Item and Creature Enchantment
/// and Mana Conversion from where she stands, the best of them): the experience the lore takes is the experience those skills do not get. <b>In reach</b> as the caster's points are (<see cref="CasterSpend.Allocations"/>,
/// <see cref="CasterSpend.ReachedBy"/>: this allocation less the floors, the next level-up's and the one after). No constant of its own.</para>
/// <para><b>The choice.</b> Of the pieces that pay and are in reach (M6 14 own lore: the worn pieces whose spells are off among them, <see cref="WornWants"/>), the highest ItemDifficulty (the owner, 2026-11-17: her ranks toward it light every
/// piece below it too), then the larger gain. The ranks go to the planner as a target after the
/// floors and before every share; the walk buys them first while the pool covers their next rank and otherwise goes on to the shares, as with the caster's points.</para>
/// <para><b>The wear.</b> The next visit's gear phase releases the review pieces and judges every held piece again on her lore then (<c>ResupplyRun.SelfEquip</c>): with the lore there, its spells count and the pick wears it.</para>
/// </remarks>
public static class LoreSpend
{
    private const double Eps = 1e-6;

    /// <summary>The pieces of <paramref name="held"/> the wear pick keeps for the review because her Arcane Lore alone keeps their spells off, and that it would wear with the lore there (see the class remarks).</summary>
    /// <param name="judge">The wear pick (its <see cref="SelfEquipJudge.Wear"/> terms set).</param>
    /// <param name="held">The pieces in her packs.</param>
    /// <param name="worn">The pieces she wears.</param>
    /// <param name="skills">Her numbers.</param>
    public static IReadOnlyList<LoreWant> Wants(SelfEquipJudge judge, IReadOnlyList<GearCandidate> held, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        var wants = new List<LoreWant>();
        if (judge.Wear is null)
        {
            return wants;
        }

        foreach (var c in held)
        {
            if ((c.ItemType & SelfEquipJudge.CasterType) != 0 || c.Appraisal is not { IsNotFound: false })
            {
                continue;
            }

            var choice = judge.Judge(c, worn, skills);
            if (choice is { Kind: SelfEquipKind.Review, LoreNeed: { } need, LoreKind: SelfEquipKind.Wear or SelfEquipKind.Replace })
            {
                wants.Add(new LoreWant(c.Guid, c.Name, need, choice.LoreKind, choice.LoreGain, choice.LoreMargin));
            }
        }

        return wants;
    }

    /// <summary>
    /// M6 14 own lore (the owner, 2026-11-17: the worn shoes' spells stayed off at Arcane Lore 273 against 275): every piece she wears (not the caster) whose spells are off for her Arcane Lore alone (not lit on her, <see cref="GearContext.LitBy"/>; every
    /// other activation requirement met with her lore at its ItemDifficulty), with what lighting it brings her (<see cref="WearScore.LitGain"/>): a want of its own, the gear phase re-wields it once her lore is there.
    /// </summary>
    /// <param name="judge">The wear pick (its <see cref="SelfEquipJudge.Wear"/> terms set).</param>
    /// <param name="worn">The pieces she wears.</param>
    /// <param name="skills">Her numbers.</param>
    public static IReadOnlyList<LoreWant> WornWants(SelfEquipJudge judge, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        var wants = new List<LoreWant>();
        if (judge.Wear is not { } terms)
        {
            return wants;
        }

        var context = judge.Context(skills);
        foreach (var w in worn)
        {
            if ((w.ItemType & SelfEquipJudge.CasterType) != 0 || w.Appraisal is not { IsNotFound: false } a || context.LitBy?.Invoke(w.Guid) == true
                || SelfEquipJudge.LoreShort(a, skills, out var lore, out var difficulty) is null || WearScore.SpellsOff(a, SelfEquipJudge.WithLore(skills, lore, difficulty)) is not null)
            {
                continue;
            }

            var gain = WearScore.LitGain(worn, context, terms, lore, difficulty);
            if (gain > Eps)
            {
                wants.Add(new LoreWant(w.Guid, w.Name, difficulty, SelfEquipKind.Wear, gain, 0) { Worn = true });
            }
        }

        return wants;
    }

    /// <summary>The armour, clothing and jewellery pieces in her packs (not worn), with the appraisals the session holds: what <see cref="Wants"/> reads.</summary>
    /// <param name="state">The read model.</param>
    public static List<GearCandidate> HeldPieces(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var list = new List<GearCandidate>();
        foreach (var item in PackLayout.Items(state))
        {
            if (state.Objects.TryGet(item.Guid, out var o) && (o.ItemType & KeepAsideRule.WearableTypes) != 0 && state.Self.Inventory.Find(item.Guid).Kind != InventoryPlaceKind.Equipped)
            {
                list.Add(SelfEquipJudge.CandidateOf(state, o));
            }
        }

        return list;
    }

    /// <summary>The decision (see the class remarks).</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="q">Her qualities now.</param>
    /// <param name="facts">The pieces, her lore, the pick's terms and her rate.</param>
    /// <param name="pool">The unassigned experience now.</param>
    /// <param name="floorsOwed">The experience the floors take first from the pool.</param>
    public static LoreSpendDecision Decide(XpTableData table, PlayerQualities q, LoreSpendFacts facts, long pool, long floorsOwed)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(facts);
        var c = CultureInfo.InvariantCulture;
        if (facts.Wants.Count == 0)
        {
            return new LoreSpendDecision(null, null, 0, $"no piece she holds waits on her Arcane Lore ({facts.Lore}): no rank is bought", []);
        }

        if (facts.Standing is not { } standing)
        {
            return new LoreSpendDecision(null, null, 0, string.Create(c, $"{facts.Wants.Count} piece(s) wait on her Arcane Lore, but {facts.Lore}: no rank is bought"), []);
        }

        if (!q.TryGetSkill(MageTemplate.ArcaneLore, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
        {
            return new LoreSpendDecision(null, null, 0, string.Create(c, $"{facts.Wants.Count} piece(s) wait on her Arcane Lore, but it is not trained: no rank of it can be bought"), []);
        }

        if (facts.Context is not { } context || facts.Terms is not { } terms)
        {
            return new LoreSpendDecision(null, null, 0, string.Create(c, $"{facts.Wants.Count} piece(s) wait on her Arcane Lore, but no wear pick's terms are held to weigh the same experience on her magic skills: no rank is bought"), []);
        }

        var specialized = skill.AdvancementClass >= SkillRules.SpecializedClass;
        var list = specialized ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
        var from = (int)skill.Ranks;
        var spent = (long)skill.Experience;
        var allocations = CasterSpend.Allocations(table, q, pool, floorsOwed, out var reach);
        var others = new List<string>();
        var fits = new List<(LoreWant Want, int To, long Cost, int By, string Line)>();
        foreach (var want in facts.Wants.OrderBy(w => w.Difficulty).ThenByDescending(w => w.Gain).ThenBy(w => w.Guid))
        {
            var head = string.Create(c, $"'{want.Name}' 0x{want.Guid:X8}: ItemDifficulty {want.Difficulty} against her Arcane Lore {standing}");
            var points = want.Difficulty - standing;
            if (points <= 0)
            {
                others.Add(head + ": met at the wield: the next visit's gear pick lights it, no rank is bought");
                continue;
            }

            var to = from + points;
            if (to >= list.Count)
            {
                others.Add(string.Create(c, $"{head}: {points} point(s), Arcane Lore ranks {from} -> {to} is past its rank cap {list.Count - 1}: no rank is bought"));
                continue;
            }

            var prices = new List<long>();
            for (var r = from; r < to; r++)
            {
                prices.Add(Math.Max(0, (long)list[r + 1] - Math.Max(spent, (long)list[r])));
            }

            var cost = Math.Max(0, (long)list[to] - spent);
            var (alternative, alternativeWhy) = Alternative(table, q, cost, context, terms);
            var pays = want.Gain > alternative + Eps;
            var by = CasterSpend.ReachedBy(prices, allocations);
            var time = facts.XpPerHour is > 0 and var rate ? string.Create(c, $"{cost / rate:0.0} h of her play at {rate:N0} an hour ({facts.Rate})") : $"the time is not priced: {facts.Rate}";
            var perMillion = cost > 0 ? want.Gain / (cost / 1_000_000.0) : double.PositiveInfinity;
            var line = string.Create(
                c,
                $"{head}: {points} point(s), Arcane Lore ranks {from} -> {to} for {cost:N0} ({time}); {want.Verdict} ({(double.IsPositiveInfinity(perMillion) ? "free" : perMillion.ToString("0.###", c) + " a million")}); the same experience on her magic skills: {alternativeWhy}: {(pays ? "it pays" : "it does not pay")}; {(by >= 0 ? "in reach by " + (by switch { 0 => "this allocation", 1 => "the next level-up's", _ => "the level-up after it" }) : "out of reach")} of {reach}");
            if (!pays || by < 0)
            {
                others.Add(line + ": no rank is bought");
                continue;
            }

            fits.Add((want, to, cost, by, line));
        }

        if (fits.Count == 0)
        {
            return new LoreSpendDecision(null, null, 0, string.Create(c, $"none of the {facts.Wants.Count} piece(s) that wait on her Arcane Lore ({facts.Lore}) pays and is in reach: no rank is bought"), others);
        }

        // M6 14 own lore (the owner, 2026-11-17): the highest activation among the pieces that pay and are in reach: its ranks light every one below it too
        var best = fits.OrderByDescending(f => f.Want.Difficulty).ThenByDescending(f => f.Want.Gain).ThenBy(f => f.Want.Guid).First();
        foreach (var f in fits.Where(f => f.Want != best.Want))
        {
            others.Add(f.Line + string.Create(c, $": it pays, and the ranks toward '{best.Want.Name}' 0x{best.Want.Guid:X8} ({best.Want.Difficulty}) light it too"));
        }

        var target = new StatTarget(MageStatKind.Skill, MageTemplate.ArcaneLore, MageTemplate.SkillName(MageTemplate.ArcaneLore), specialized, best.To);
        return new LoreSpendDecision(best.Want, target, best.Cost, string.Create(c, $"buy first: {best.Line}; {facts.Lore}; the spend buys them after the floors and before every share, and the next visit's gear pick wears it"), others);
    }

    /// <summary>
    /// What <paramref name="xp"/> experience brings her on her magic skills by the pick's terms (<see cref="WearScore.SkillPoints"/>): for each of the skills her spend raises (<see cref="MageTemplate.ClimberSpendSkills"/>) she has trained, the ranks it buys
    /// from where she stands; the best of them, named.
    /// </summary>
    private static (double Gain, string Why) Alternative(XpTableData table, PlayerQualities q, long xp, GearContext context, WearTerms terms)
    {
        var c = CultureInfo.InvariantCulture;
        var best = (Gain: 0.0, Why: string.Create(c, $"{xp:N0} buys no rank of a magic skill she has trained that the pick weighs: 0% of her hour"));
        foreach (var id in MageTemplate.ClimberSpendSkills)
        {
            if (!q.TryGetSkill(id, out var s) || s.AdvancementClass < SkillRules.TrainedClass)
            {
                continue;
            }

            var list = s.AdvancementClass >= SkillRules.SpecializedClass ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
            var from = (int)s.Ranks;
            var to = from;
            while (to + 1 < list.Count && (long)list[to + 1] - s.Experience <= xp)
            {
                to++;
            }

            if (to == from)
            {
                continue;
            }

            var part = WearScore.SkillPoints(id, to - from, context, terms);
            if (part.Value > best.Gain)
            {
                best = (part.Value, string.Create(c, $"{to - from} rank(s) of {MageTemplate.SkillName(id)} ({from} -> {to}), {part.Value:0.###}% of her hour ({part})"));
            }
        }

        return best;
    }
}
