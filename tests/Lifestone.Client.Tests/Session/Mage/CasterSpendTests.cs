using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit.Abstractions;
using Server = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.Server;
using View = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.View;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-caster-spend</c>; the owner's ruling, 2026-11-14: she should definitely use the fire wand she has). Qwinnie at level 62 holds a Fire Sceptre 0x8000A919 that wants base War Magic 290; she has 287, and her spend gave War Magic its 13.4% share.
/// What these prove: the caster pick scores a caster short of a skill-base wield requirement as if wielded and names its gain over the held caster; the spend buys the points first when that gain beats the swap margin and the points are in reach
/// of this allocation and the next one or two, after the floors and before every share, each raise and the decision on their lines; the pick then wields it; a shortfall out of reach is logged with its numbers and changes nothing; of two casters
/// in reach the best gain a million is bought; a caster whose wield did not land is held back for the run.
/// <para>The spend runs on <see cref="XpSpendRuleTests"/>' scripted ACE over a table whose prices rise with the rank as retail's do (a rank of the specialised list costs 10 x (2i + 1)), so the plan balances her skills and three points of War Magic
/// are a real reordering, not the flat table's fill. The casters are the live log's of 2026-11-14 12:43 UTC: the Staff 0x800020C5 she wields (ManaConversionMod 0.08, WeaponDefense 1.22: "mana conversion +12.8%, melee defence +37%" with her
/// auras VI) and the Fire Sceptre 0x8000A919 as the examine panel read it (Melee Defense +7%, Mana Conversion +4%, Fire +1%, wield base War Magic 290, activation Arcane Lore 205); her bolt Ilservian's Flame (35 mana, power 300).</para>
/// </summary>
public sealed class CasterSpendTests(ITestOutputHelper output)
{
    private const uint Fire = 0x10, StaffGuid = 0x800020C5, SceptreGuid = 0x8000A919, ElectricGuid = 0x80005AD5, PiercingGuid = 0x800003C0;
    private const MageStatKind S = MageStatKind.Skill;

    /// <summary>The level step of the test table: level 62 starts at 671,000, 63 at 682,000, 64 at 693,000.</summary>
    private const ulong LevelStep = 11_000;

    /// <summary>Her total: level 62, 7,000 from level 63.</summary>
    private const ulong Total = 675_000;

    /// <summary>The unassigned pool this allocation found.</summary>
    private const long Pool = 7_000;

    /// <summary>Her rate on the test table, shaped as hers (4.5 M an hour against 5.5 M a level at 62): a level of 11,000 in about 1.2 h.</summary>
    private const double Rate = 9_000;

    private static readonly XpTableData Table = Rising();

    private static Ladder L => LadderLoader.WarMage;

    /// <summary>A table whose lists rise with the rank (a rank costs k x (2i + 1): attributes 20, vitals 12, trained skills 15, specialised 10), the level list <see cref="LevelStep"/> a level.</summary>
    private static XpTableData Rising()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(XpTableData.DataId);
            int[] counts = [400, 400, 400, 400, 275];
            foreach (var count in counts)
            {
                w.Write((uint)count);
            }

            uint[] k = [20, 12, 15, 10];
            for (var list = 0; list < 4; list++)
            {
                for (var i = 0; i <= counts[list]; i++)
                {
                    w.Write((uint)(k[list] * i * i));
                }
            }

            for (var level = 0; level <= counts[4]; level++)
            {
                w.Write(level == 0 ? 0UL : (ulong)(level - 1) * LevelStep);
            }

            for (var level = 0; level <= counts[4]; level++)
            {
                w.Write(level >= 2 ? 1u : 0u);
            }
        }

        return XpTableData.Decode(stream.ToArray());
    }

    private static Appraisal Appraised(uint guid, Dictionary<uint, int> ints, Dictionary<uint, double> floats, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            floats.ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    private static GearCandidate Caster(uint guid, string name, Dictionary<uint, int> ints, Dictionary<uint, double> floats) =>
        new(guid, name, 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, new Dictionary<uint, int>(ints) { [1] = 32768, [5] = 50 }, floats));

    private static GearCandidate Staff() => Caster(StaffGuid, "Staff", new() { [19] = 876 }, new() { [29] = 1.22, [144] = 0.08 });

    /// <summary>A sceptre of the live log's: its element's bonus (Fire +1% by default), Mana Conversion +4%, Melee Defense +7%, wield base War Magic <paramref name="need"/>, activation Arcane Lore 205.</summary>
    private static GearCandidate Sceptre(uint guid = SceptreGuid, string name = "Fire Sceptre", int need = 290, int element = 0x10, double bonus = 1.01, double conversion = 0.04, int level = 0) => Caster(
        guid,
        name,
        new Dictionary<uint, int> { [19] = 6737, [45] = element, [109] = 205, [158] = 2, [159] = 34, [160] = need }.Concat(level > 0 ? [new(270, 7), new(271, 1), new(272, level)] : Array.Empty<KeyValuePair<uint, int>>()).ToDictionary(),
        new() { [29] = 1.07, [144] = conversion, [152] = bonus });

    private static WornPiece Worn(GearCandidate c) => new(c.Guid, c.Name, c.ItemType, SelfEquipJudge.Held, 0, c.Appraisal);

    /// <summary>The live terms: the ladder's weights, Ilservian's Flame, her auras VI (Hermetic Link x1.6, Defender +0.15, Spirit Drinker +0.06); a hold-back memory of the test's own.</summary>
    private static SelfEquipJudge Judge(CasterHeldBack? heldBack = null) =>
        new([], Fire, _ => null, null, null, new CasterTerms(L.CasterScore, new CasterBolt("Ilservian's Flame", 35, 300), new CasterAuras(6, 6, 6))) { HeldBack = heldBack ?? new CasterHeldBack() };

    /// <summary>Her numbers for the pick: base War Magic <paramref name="warBase"/>, Mana Conversion 260, Arcane Lore 150.</summary>
    private sealed class Qwinnie(int warBase = 287, int level = 62) : IGearSkills
    {
        public int Level => level;

        public int AdvancementClass(uint skill) => skill == MageTemplate.WarMagic ? 3 : 2;

        public int? Current(uint skill) => skill switch { MageTemplate.WarMagic => warBase + 40, MageTemplate.ManaConversion => 260, MageTemplate.ArcaneLore => 150, _ => null };

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? warBase : null;
    }

    // ------------------------------------------------------------------------------------------------ the scripted spend

    /// <summary>
    /// Qwinnie on the scripted ACE at level 62: every stat at the plan of her total (the climber's split, the health floor and the caps), so a spend with no caster read buys nothing; Focus and Self created so her base War Magic is
    /// <paramref name="warBase"/>; War Magic and Life Magic specialised, Melee Defense untrained.
    /// </summary>
    private static (ActionHarness H, Server Server) Qwinnie62(int warBase = 287)
    {
        var plan = ControlPlan();
        int Planned(StatTarget t) => plan.Targets.Where(p => p.Kind == t.Kind && p.Id == t.Id).Select(p => p.Ranks).DefaultIfEmpty(0).Max();
        var war = Planned(new StatTarget(S, MageTemplate.WarMagic, "War Magic", true, 0));
        var focus = Planned(new StatTarget(MageStatKind.Attribute, MageTemplate.Focus, "Focus", false, 0));
        var start = (uint)((2 * (warBase - war)) - focus); // (start + ranks) x 2 / 4 = warBase - the War Magic ranks
        var h = new ActionHarness();
        h.Present();
        var server = new Server(
            h,
            Total,
            Pool,
            start: t => (t.Kind == S && t.Id == MageTemplate.MeleeDefense ? 0 : Planned(t), t.Kind == S && t.Id == MageTemplate.MeleeDefense),
            ints: new Dictionary<uint, int> { [PlayerQualities.LevelProperty] = 62, [PlayerInventory.EncumbranceProperty] = 0 },
            table: Table,
            classOf: t => t.Kind == S && t.Id == MageTemplate.MeleeDefense ? 1u : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3u : 2u,
            attributeStart: id => id is MageTemplate.Focus or MageTemplate.Self ? start : MageTemplate.CreationValue(id));
        return (h, server);
    }

    /// <summary>The plan of her total with every stat at 0 (the planner does not read the ranks she holds but for the health floor's Endurance and Max Health, which are 0 here as the plan then puts them).</summary>
    private static SpendingPlan ControlPlan()
    {
        var h = new ActionHarness();
        h.Present();
        _ = new Server(
            h,
            Total,
            Pool,
            start: t => (0, t.Kind == S && t.Id == MageTemplate.MeleeDefense),
            ints: new Dictionary<uint, int> { [PlayerQualities.LevelProperty] = 62, [PlayerInventory.EncumbranceProperty] = 0 },
            table: Table,
            classOf: t => t.Kind == S && t.Id == MageTemplate.MeleeDefense ? 1u : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3u : 2u,
            attributeStart: MageTemplate.CreationValue);
        var probe = new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: L);
        probe.Begin(new View(h));
        return probe.Plan!;
    }

    private XpSpendRule Spend(ActionHarness h, Server server, Func<CasterSpendFacts?>? facts)
    {
        var rule = new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: L, casterFacts: facts);
        rule.Begin(new View(h));
        XpSpendRuleTests.Frames(h, server, rule.Tick);
        foreach (var line in h.Log.Lines.Where(l => l.Contains("xp: caster:", StringComparison.Ordinal) || l.Contains("xp: raise", StringComparison.Ordinal) || l.Contains("xp: shares:", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        return rule;
    }

    private static CasterSpendFacts Facts(ActionHarness h, SelfEquipJudge judge, params GearCandidate[] pack) =>
        new(judge.ShortCasters(pack, [Worn(Staff())], new QualitiesSkills(h.State.Self.Qualities)), Rate, "the test's rate: a level in about 1.2 h, as hers");

    private static int? WarBase(ActionHarness h) => new QualitiesSkills(h.State.Self.Qualities).Base(MageTemplate.WarMagic);

    [Fact]
    public void The_live_case_three_War_Magic_points_are_bought_first_and_the_pick_then_wields_the_Fire_Sceptre()
    {
        // With no caster read, her plan is reached: the spend buys nothing and the sceptre stays out of reach of her hands.
        var (h0, control) = Qwinnie62();
        Assert.Equal(287, WarBase(h0));
        var plain = Spend(h0, control, null);
        Assert.Equal(XpSpendEnd.PlanReached, plain.End);
        Assert.Empty(control.Raised);
        Assert.Null(plain.CasterDecision);

        // Read, the sceptre scores as if wielded over the Staff by more than the swap margin, and its three points cost three ranks of War Magic: in reach of this allocation.
        var (h, server) = Qwinnie62();
        var judge = Judge();
        var rule = Spend(h, server, () => Facts(h, judge, Sceptre()));
        Assert.Null(rule.Failure);
        var decision = rule.CasterDecision!;
        Assert.Equal(SceptreGuid, decision.Chosen!.Guid);
        Assert.Equal([new CasterSkillShort(MageTemplate.WarMagic, 287, 290)], decision.Chosen.Skills);
        Assert.True(decision.Chosen.Gain > L.CasterScore.SwapMargin, decision.Chosen.Numbers);
        var war = server.RanksOf(S, MageTemplate.WarMagic);
        Assert.Equal([(S, MageTemplate.WarMagic), (S, MageTemplate.WarMagic), (S, MageTemplate.WarMagic)], server.Raised.Select(r => (r.Kind, r.Id)));
        var cost = Table.SpecializedSkillXpList[(int)war] - Table.SpecializedSkillXpList[(int)war - 3];
        Assert.Equal(cost, (uint)decision.Cost);
        Assert.Equal(cost, (uint)server.Raised.Sum(r => (long)r.Xp));
        Assert.Equal(290, WarBase(h));

        // The lines: the decision with its numbers (the gain, the points, the price in experience and in hours, the reach), and each raise named as the caster's.
        var line = Assert.Single(h.Log.Lines, l => l.Contains("xp: caster: buy first: 'Fire Sceptre' 0x8000A919: as wielded ", StringComparison.Ordinal));
        Assert.Contains("against the held 'Staff' 0x800020C5 ", line, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"3 base points of War Magic (base 287 < 290): War Magic ranks {war - 3} -> {war} for {cost:N0}"), line, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"{cost:N0} experience in all, {cost / Rate:0.0} h of her play at 9,000 an hour (the test's rate: a level in about 1.2 h, as hers), against "), line, StringComparison.Ordinal);
        Assert.Contains("in reach: its last rank is bought by this allocation of this allocation 7,000 (the pool 7,000 less the floors' 0), the next level-up's 7,000 (to level 63) and the one after 11,000 (to level 64)", line, StringComparison.Ordinal);
        Assert.Equal(3, h.Log.Lines.Count(l => l.Contains("xp: raise War Magic", StringComparison.Ordinal) && l.Contains("(the caster's points: toward 'Fire Sceptre' 0x8000A919's wield requirement, before the shares)", StringComparison.Ordinal)));

        // The pick, read on the skills the spend left: the sceptre goes on in place of the Staff (the swap at the next home visit).
        var pick = judge.BestCaster([Sceptre()], [Worn(Staff())], new QualitiesSkills(h.State.Self.Qualities), out var why);
        output.WriteLine(why);
        Assert.NotNull(pick);
        Assert.Equal((SelfEquipKind.Swap, SceptreGuid, StaffGuid), (pick.Kind, pick.Guid, pick.Replaces));
        Assert.Contains("wield: skill 34 base 290 >= 290", pick.Why, StringComparison.Ordinal);

        // The next spend: nothing is short any more, so the plan is her balanced one again (War Magic waits while the others catch up).
        var next = new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: L, casterFacts: () => Facts(h, judge, Sceptre()));
        next.Begin(new View(h));
        Assert.Equal("no caster she holds is short of a wield requirement: nothing is bought first", next.CasterDecision!.Line);
        Assert.Empty(next.CasterDecision.Pushes);
    }

    [Fact]
    public void A_shortfall_out_of_reach_of_the_next_allocations_is_logged_with_its_numbers_and_changes_nothing()
    {
        // A sceptre at base 302: fifteen ranks of War Magic, about 32,000, against 7,000 now, 7,000 to level 63 and 11,000 to 64.
        var (h, server) = Qwinnie62();
        var rule = Spend(h, server, () => Facts(h, Judge(), Sceptre(need: 302)));

        Assert.Null(rule.Failure);
        Assert.Null(rule.CasterDecision!.Chosen);
        Assert.Empty(rule.CasterDecision.Pushes);
        Assert.Empty(server.Raised); // the plan of before: reached, nothing bought
        Assert.Equal(XpSpendEnd.PlanReached, rule.End);
        var war = (int)server.RanksOf(S, MageTemplate.WarMagic);
        var cost = Table.SpecializedSkillXpList[war + 15] - Table.SpecializedSkillXpList[war];
        var line = Assert.Single(h.Log.Lines, l => l.Contains("xp: caster: 'Fire Sceptre' 0x8000A919: as wielded ", StringComparison.Ordinal));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"15 base points of War Magic (base 287 < 302): War Magic ranks {war} -> {war + 15} for {cost:N0}"), line, StringComparison.Ordinal);
        Assert.Contains("out of reach of this allocation 7,000 (the pool 7,000 less the floors' 0), the next level-up's 7,000 (to level 63) and the one after 11,000 (to level 64) (25,000 in all): nothing changes", line, StringComparison.Ordinal);
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: caster: none of the 1 caster(s) she holds short of a wield requirement has its points bought first", StringComparison.Ordinal));
        Assert.Equal(287, WarBase(h));
    }

    // ------------------------------------------------------------------------------------------------ the gain, the choice and the hold-back (pure)

    [Fact]
    public void The_pick_scores_a_short_caster_as_if_wielded_and_names_its_gain_over_the_held_one()
    {
        var judge = Judge();
        var skills = new Qwinnie();
        var pack = new[] { Sceptre(), Sceptre(ElectricGuid, "Electric Sceptre", element: 0x40), Sceptre(0x80009999, "Level Sceptre", need: 280, level: 70) };
        var shorts = judge.ShortCasters(pack, [Worn(Staff())], skills);
        foreach (var s in shorts)
        {
            output.WriteLine($"{s.Name}: {s.Numbers}; {string.Join(", ", s.Skills)}; {s.Other}");
        }

        // The Fire Sceptre on the fire rung: its +1% and her Spirit Drinker's +6% count as for a usable caster (0.5 a percent), its Mana Conversion and defence as well.
        var fire = shorts.Single(s => s.Guid == SceptreGuid);
        var asUsable = GearScore.Caster(SceptreGuid, "Fire Sceptre", Sceptre().Appraisal!, judge.Context(new Qwinnie(warBase: 290)));
        Assert.Equal(asUsable.CasterScore, fire.Score, 9);
        Assert.Equal(7.0, asUsable.WarPercent, 6);
        var staff = GearScore.Caster(StaffGuid, "Staff", Staff().Appraisal!, judge.Context(skills));
        Assert.Equal(fire.Score - staff.CasterScore, fire.Gain, 9);
        Assert.True(fire.Beats);
        Assert.Null(fire.Other);
        Assert.Equal("3 base points of War Magic (base 287 < 290)", Assert.Single(fire.Skills).ToString());

        // The Electric Sceptre's bonus is off the rung's element: only its Mana Conversion and defence count, under the Staff's.
        var electric = shorts.Single(s => s.Guid == ElectricGuid);
        Assert.False(electric.Beats, electric.Numbers);

        // A caster short of a level as well: no experience spend gives a level, so it is named and never bought toward.
        var level = shorts.Single(s => s.Guid == 0x80009999);
        Assert.Empty(level.Skills);
        Assert.StartsWith("short of wield: level 62 < 70", level.Other, StringComparison.Ordinal);

        // The pick's line names each short caster's gain beside its requirement.
        Assert.Null(judge.BestCaster(pack, [Worn(Staff())], skills, out var why));
        output.WriteLine(why);
        Assert.Contains("3 short of a wield requirement ('Fire Sceptre' 0x8000A919 wield: skill 34 base 287 < 290 (", why, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"): as wielded {fire.Score:0.##} against the held 'Staff' 0x800020C5 {staff.CasterScore:0.##}: gain +{fire.Gain:0.##} of her hour, over the swap margin 0.1; short 3 base points of War Magic (base 287 < 290)"), why, StringComparison.Ordinal);
        Assert.Contains("of her hour, not over the swap margin 0.1; short 3 base points of War Magic (base 287 < 290)", why, StringComparison.Ordinal);
    }

    /// <summary>Qwinnie's qualities for the pure decision: level <paramref name="level"/>, War Magic specialised at <paramref name="ranks"/> ranks (the list's price spent), the totals given.</summary>
    private static PlayerQualities Qualities(XpTableData table, int ranks, long total, long pool, int level = 62)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Int64s[PlayerQualities.TotalExperienceProperty] = total;
        q.Int64s[PlayerQualities.AvailableExperienceProperty] = pool;
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)ranks, 1, 3u, table.SpecializedSkillXpList[ranks], 0, 0, 0);
        q.Skills[MageTemplate.MagicDefense] = new SkillDesc(MageTemplate.MagicDefense, 10, 1, 2u, table.TrainedSkillXpList[10], 0, 0, 0);
        return q;
    }

    private static CasterShortfall Short(uint guid, string name, int need, double score, int skill = (int)MageTemplate.WarMagic, string? other = null) =>
        new(guid, name, [new CasterSkillShort((uint)skill, 287, need)], other, score, "the held 'Staff' 0x800020C5", 3.17, 0.1, score > 3.27);

    [Fact]
    public void Of_the_casters_in_reach_the_best_gain_a_million_is_bought_and_each_other_is_named_with_its_reason()
    {
        // War Magic at 100 ranks (base 287): a point costs about 2,000 on the test table. The Fire Sceptre at 290 (+1.9 for three points) against a Piercing Sceptre at 288 (+0.9 for one) and one at 300 (+3.0 for thirteen).
        var q = Qualities(Table, 100, (long)Total, 30_000);
        var facts = new CasterSpendFacts(
            [
                Short(SceptreGuid, "Fire Sceptre", 290, 5.08),
                Short(PiercingGuid, "Piercing Sceptre", 288, 4.07),
                Short(0x80001111, "Grand Sceptre", 300, 6.17),
                Short(ElectricGuid, "Electric Sceptre", 290, 3.2),
                Short(0x80002222, "Defence Sceptre", 290, 5.0, skill: (int)MageTemplate.MagicDefense),
            ],
            null,
            "no rate is measured yet");
        var d = CasterSpend.Decide(Table, q, facts, 30_000, 0, MageTemplate.OnClimberList);
        output.WriteLine(d.Line);
        foreach (var other in d.Others)
        {
            output.WriteLine(other);
        }

        // Gain a million: the Piercing Sceptre 0.9 for one rank (2,010) is 448; the Fire 1.91 for three (6,090) is 314; the Grand 3.0 for thirteen is far less.
        Assert.Equal(PiercingGuid, d.Chosen!.Guid);
        Assert.Equal([new SkillPush(MageTemplate.WarMagic, 101, "War Magic base 288 lets her wield 'Piercing Sceptre' 0x800003C0")], d.Pushes);
        Assert.Equal(2_010, d.Cost);
        Assert.Contains("the time is not priced: no rate is measured yet", d.Line, StringComparison.Ordinal);
        Assert.Contains("the best gain a million of the 3 in reach", d.Line, StringComparison.Ordinal);
        Assert.Contains(d.Others, o => o.StartsWith("'Fire Sceptre' 0x8000A919: in reach (", StringComparison.Ordinal) && o.EndsWith("but 'Piercing Sceptre' 0x800003C0 gains more for its experience (313.629 against 447.761 of her hour a million): not bought", StringComparison.Ordinal));
        Assert.Contains(d.Others, o => o.StartsWith("'Electric Sceptre' 0x80005AD5: as wielded 3.2 against the held 'Staff' 0x800020C5 3.17: gain +0.03 of her hour, not over the swap margin 0.1: no points bought", StringComparison.Ordinal));
        Assert.Contains(d.Others, o => o.Contains("'Defence Sceptre' 0x80002222: ", StringComparison.Ordinal) && o.EndsWith("Magic Defense is not a skill her spend raises: no points bought", StringComparison.Ordinal));

        // The reach reads each rank from one allocation, its leftover to the shares: three ranks of 2,000 do not fit two allocations of 5,000, they fit three.
        Assert.Equal(1, CasterSpend.ReachedBy([2_000, 2_000, 2_000], [5_000, 5_000]));
        Assert.Equal(-1, CasterSpend.ReachedBy([2_000, 2_000, 2_000, 2_000, 2_000], [5_000, 5_000]));
        Assert.Equal(2, CasterSpend.ReachedBy([3_000], [1_000, 2_000, 3_000]));
        Assert.Equal(0, CasterSpend.ReachedBy([], [0]));
    }

    [Fact]
    public void A_caster_whose_wield_did_not_land_is_held_back_for_the_run_by_the_pick_and_the_spend()
    {
        var heldBack = new CasterHeldBack();
        var judge = Judge(heldBack);
        Assert.NotNull(judge.BestCaster([Sceptre()], [Worn(Staff())], new Qwinnie(warBase: 290), out _));

        heldBack.Hold(SceptreGuid, "caster-wield-failed: the wield of 'Fire Sceptre' 0x8000A919 did not land: the server's answer ServerError (code 0x468)");
        Assert.Null(judge.BestCaster([Sceptre()], [Worn(Staff())], new Qwinnie(warBase: 290), out var why));
        output.WriteLine(why);
        Assert.Contains("1 held back for the run ('Fire Sceptre' 0x8000A919: caster-wield-failed: the wield of 'Fire Sceptre' 0x8000A919 did not land", why, StringComparison.Ordinal);
        Assert.Contains("keep 'Staff' 0x800020C5", why, StringComparison.Ordinal);
        Assert.Same(heldBack, judge.WithCaster(judge.Caster).HeldBack); // a judge made from it keeps the memory

        // The spend reads it as a caster no experience makes wieldable this run.
        var s = Assert.Single(judge.ShortCasters([Sceptre()], [Worn(Staff())], new Qwinnie()));
        Assert.StartsWith("held back for the run (caster-wield-failed: ", s.Other, StringComparison.Ordinal);
        var d = CasterSpend.Decide(Table, Qualities(Table, 100, (long)Total, 30_000), new CasterSpendFacts([s], 4_500_000, "test"), 30_000, 0, MageTemplate.OnClimberList);
        Assert.Null(d.Chosen);
        Assert.StartsWith("'Fire Sceptre' 0x8000A919: held back for the run (caster-wield-failed: ", Assert.Single(d.Others), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ her numbers on the retail table

    /// <summary>The run's figures of 2026-11-14 12:41 UTC: 160,929,560 earned, War Magic raised 166 -&gt; 167 for 1,521,666 (base 287), 309,029 left unassigned after the spend; the rate the brief measured, about 4.5 M an hour.</summary>
    public sealed class Retail(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
    {
        [RequiresDatsFact]
        public void Her_three_points_on_the_retail_table_cost_about_an_hour_first_against_eight_at_War_Magics_share()
        {
            var table = XpTableData.Load(fixture.Dats);
            const long Earned = 160_929_560, Left = 309_029;
            Assert.InRange(table.SpecializedSkillXpList[167] - 1_521_666u, table.SpecializedSkillXpList[166], table.SpecializedSkillXpList[167]); // the live raise line's price ended rank 167 from inside rank 166
            Assert.InRange(Earned, (long)table.CharacterLevelXpList[62], (long)table.CharacterLevelXpList[63] - 1);
            var q = Qualities(table, 167, Earned, Left); // the spend leaves a stat's experience on its rank's price: War Magic's is the list's at 167
            var share = (double)table.SpecializedSkillXpList[167] / Earned;
            Assert.InRange(share, 0.1335, 0.1345); // the shares line's "War Magic ... run 13.4%"
            var facts = new CasterSpendFacts([Short(SceptreGuid, "Fire Sceptre", 290, 5.08)], 4_500_000, "the brief's 4.5 M an hour");
            var d = CasterSpend.Decide(table, q, facts, Left, 0, MageTemplate.OnClimberList);
            output.WriteLine(d.Line);

            Assert.Equal(SceptreGuid, d.Chosen!.Guid);
            Assert.Equal([new SkillPush(MageTemplate.WarMagic, 170, "War Magic base 290 lets her wield 'Fire Sceptre' 0x8000A919")], d.Pushes);
            Assert.Equal((long)(table.SpecializedSkillXpList[170] - table.SpecializedSkillXpList[167]), d.Cost);
            Assert.InRange(d.Cost / 4_500_000.0, 1.0, 1.3); // about an hour of her play first: the ranks rise from 1.52 M to about 1.9 M
            Assert.Contains("its last rank is bought by the level-up after it", d.Line, StringComparison.Ordinal); // the 309,029 left and the 617,751 to level 63 cover no rank; the 12.2 M to level 64 covers the three
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"the next level-up's {(long)table.CharacterLevelXpList[63] - Earned:N0} (to level 63) and the one after {table.CharacterLevelXpList[64] - table.CharacterLevelXpList[63]:N0} (to level 64)"), d.Line, StringComparison.Ordinal);
            var shareHours = d.Cost / (4_500_000.0 * share);
            Assert.InRange(shareHours, 7.5, 9.5); // against eight to nine at War Magic's share
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"against {shareHours:0.0} h at the skill's running share"), d.Line, StringComparison.Ordinal);
        }
    }
}
