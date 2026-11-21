using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Messages;
using Xunit.Abstractions;
using Server = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.Server;
using View = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.View;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-lore-spend</c>; the owner, 2026-11-14: Arcane Lore is trained and rises with experience; the climber's spend raises it when gear she holds needs it). Live: Qwinnie at level 66, Arcane Lore 222, held pieces of ItemDifficulty 225 to 291
/// whose spells never lit. What this proves: the wear pick keeps a piece whose spells her Arcane Lore alone keeps off with its verdict at its ItemDifficulty; the spend buys the lore toward it, after the floors and before every share, when
/// that gain beats what the same experience brings her magic skills and the ranks are in reach, each raise and the reading on their lines; the pick then wears it; with no held piece waiting on the lore nothing is bought; a gain that does not
/// pay buys nothing.
/// <para>The spend runs on <see cref="XpSpendRuleTests"/>' scripted ACE over <see cref="CasterSpendTests"/>' rising table (a trained rank costs 15 x (2i + 1)) with every stat at the plan of her total, so a spend with no lore read buys nothing. The
/// ring casts Major War Magic Aptitude (spell 2534, +15: <see cref="CantripRows"/>); her bolt is Ilservian's Flame (35 mana, power 300).</para>
/// </summary>
public sealed class LoreSpendTests(ITestOutputHelper output)
{
    private const uint Fire = 0x10, RingGuid = 0x80007DBA, MajorWarMagicAptitude = 2534;
    private const MageStatKind S = MageStatKind.Skill;
    private const ulong LevelStep = 11_000, Total = 675_000;
    private const long Pool = 7_000;
    private const double Rate = 9_000;

    private static readonly XpTableData Table = Rising();

    private static Ladder L => LadderLoader.WarMage;

    /// <summary>The caster spend's table: a rank costs k x (2i + 1) (attributes 20, vitals 12, trained skills 15, specialised 10), the level list <see cref="LevelStep"/> a level.</summary>
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

    private static Server Make(ActionHarness h, Func<StatTarget, (int Ranks, bool Untrained)> start) => new(
        h,
        Total,
        Pool,
        start: start,
        ints: new Dictionary<uint, int> { [PlayerQualities.LevelProperty] = 62, [PlayerInventory.EncumbranceProperty] = 0 },
        table: Table,
        classOf: t => t.Kind == S && t.Id == MageTemplate.MeleeDefense ? 1u : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3u : 2u,
        attributeStart: MageTemplate.CreationValue);

    /// <summary>Qwinnie at level 62 with every stat at the plan of her total (the climber's split, the health floor, the caps); Arcane Lore trained, no rank of it (the climber's split gives it none).</summary>
    private static (ActionHarness H, Server Server) Qwinnie62()
    {
        var control = new ActionHarness();
        control.Present();
        _ = Make(control, t => (0, t.Kind == S && t.Id == MageTemplate.MeleeDefense));
        var probe = new XpSpendRule(Table, control.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: L);
        probe.Begin(new View(control));
        var plan = probe.Plan!;
        var h = new ActionHarness();
        h.Present();
        var server = Make(h, t => (t.Kind == S && t.Id == MageTemplate.MeleeDefense ? 0 : plan.Targets.Where(p => p.Kind == t.Kind && p.Id == t.Id).Select(p => p.Ranks).DefaultIfEmpty(0).Max(), t.Kind == S && t.Id == MageTemplate.MeleeDefense));
        return (h, server);
    }

    private static Appraisal Appraised(uint guid, Dictionary<uint, int> ints, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
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

    /// <summary>A ring of Major War Magic Aptitude (burden 15, item mana 800) of ItemDifficulty <paramref name="difficulty"/>, for either finger.</summary>
    private static GearCandidate Ring(int difficulty) =>
        new(RingGuid, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(RingGuid, new() { [5] = 15, [107] = 800, [108] = 800, [109] = difficulty }, MajorWarMagicAptitude));

    private static WearTerms Terms() => new(L.WearScore, new CasterTerms(L.CasterScore, new CasterBolt("Ilservian's Flame", 35, 300), CasterAuras.None), 0, null, ArmourLines.None) { Capacity = 17_850 };

    private static SelfEquipJudge Judge() => new SelfEquipJudge([], Fire, id => CantripRows.Of(id)?.Name, null).WithWear(Terms());

    /// <summary>The facts as the autopilot reads them (<c>LadderLoreFacts</c>): the pieces in her packs judged by the wear pick on her numbers now, her standing lore (no buff on her), the pick's terms.</summary>
    private static LoreSpendFacts Facts(ActionHarness h, params GearCandidate[] pack)
    {
        var q = h.State.Self.Qualities;
        var skills = new QualitiesSkills(q);
        var judge = Judge();
        return new LoreSpendFacts(LoreSpend.Wants(judge, pack, [], skills), StandingLore.Of(q, botApplies: false, out var why), why, judge.Context(skills), judge.Wear, Rate, "the test's rate");
    }

    private static int Lore(ActionHarness h) => new QualitiesSkills(h.State.Self.Qualities).Current(MageTemplate.ArcaneLore)!.Value;

    private XpSpendRule Spend(ActionHarness h, Server server, Func<LoreSpendFacts?>? facts)
    {
        var rule = new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: L, loreFacts: facts);
        rule.Begin(new View(h));
        XpSpendRuleTests.Frames(h, server, rule.Tick);
        foreach (var line in h.Log.Lines.Where(l => l.Contains("xp: lore:", StringComparison.Ordinal) || l.Contains("xp: raise", StringComparison.Ordinal) || l.Contains("xp: ended", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        return rule;
    }

    [Fact]
    public void Arcane_Lore_is_raised_toward_a_held_pieces_difficulty_when_the_gain_pays_and_not_when_no_held_piece_needs_it()
    {
        // No held piece waits on her lore (the ring's difficulty is met, so the pick would wear it now): nothing is bought, and the reading says why.
        var (h0, control) = Qwinnie62();
        var lore = Lore(h0);
        var met = Spend(h0, control, () => Facts(h0, Ring(lore)));
        Assert.Null(met.Failure);
        Assert.Equal(XpSpendEnd.PlanReached, met.End);
        Assert.Empty(control.Raised);
        Assert.Null(met.LoreDecision!.Target);
        Assert.StartsWith("no piece she holds waits on her Arcane Lore (Arcane Lore ", met.LoreDecision.Line, StringComparison.Ordinal);
        Assert.Contains(h0.Log.Lines, l => l.Contains("xp: lore: no piece she holds waits on her Arcane Lore", StringComparison.Ordinal));

        // Ten points short: ACE would leave its spells off, so the pick keeps it for the review with its verdict at the lore it needs, a wear in the free finger.
        var (h, server) = Qwinnie62();
        Assert.Equal(lore, Lore(h));
        var ring = Ring(lore + 10);
        var review = Judge().Judge(ring, [], new QualitiesSkills(h.State.Self.Qualities));
        output.WriteLine(review.ToString());
        Assert.Equal(SelfEquipKind.Review, review.Kind);
        Assert.Equal((lore + 10, SelfEquipKind.Wear), (review.LoreNeed, review.LoreKind));
        Assert.True(review.LoreGain > 0, review.Why);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"with Arcane Lore {lore + 10} (its ItemDifficulty) the wear pick would wear it"), review.Why, StringComparison.Ordinal);

        // The spend buys the ten ranks first (ten trained ranks: 15 x 10 x 10 = 1,500), each raise named for the ring, before the shares. M6 holding at 60 (the owner's decision, 2026-11-20): level 62 is the Holding's rung now, whose
        // health is none on purpose, and the shares then spend what is left (one Max Mana rank, 55 -> 56 for 1,332); on the Quarters' rung, the band before, the spend stopped after the lore.
        var rule = Spend(h, server, () => Facts(h, ring));
        Assert.Null(rule.Failure);
        var decision = rule.LoreDecision!;
        Assert.Equal(RingGuid, decision.Chosen!.Guid);
        Assert.Equal(new StatTarget(S, MageTemplate.ArcaneLore, "Arcane Lore", false, 10), decision.Target);
        Assert.Equal(1_500, decision.Cost);
        Assert.Equal(Enumerable.Repeat((S, MageTemplate.ArcaneLore), 10), server.Raised.Take(10).Select(r => (r.Kind, r.Id)));
        Assert.Equal(1_500u, (uint)server.Raised.Take(10).Sum(r => (long)r.Xp));
        Assert.DoesNotContain(server.Raised.Skip(10), r => r.Kind == S && r.Id == MageTemplate.ArcaneLore);
        Assert.Equal(lore + 10, Lore(h));
        var line = Assert.Single(h.Log.Lines, l => l.Contains("xp: lore: buy first: 'Ring' 0x80007DBA: ItemDifficulty ", StringComparison.Ordinal));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"ItemDifficulty {lore + 10} against her Arcane Lore {lore}: 10 point(s), Arcane Lore ranks 0 -> 10 for 1,500 (0.2 h of her play at 9,000 an hour (the test's rate)); the wear pick would wear it at that lore, gain "), line, StringComparison.Ordinal);
        Assert.Contains(": it pays; in reach by this allocation of this allocation 7,000 (the pool 7,000 less the floors' 0)", line, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Arcane Lore {lore} now, {lore} standing at the wield (base {lore}; ACE reads the current lore at the wield"), line, StringComparison.Ordinal);
        Assert.Equal(10, h.Log.Lines.Count(l => l.Contains("xp: raise Arcane Lore", StringComparison.Ordinal) && l.Contains(string.Create(CultureInfo.InvariantCulture, $"(the Arcane Lore rule: toward 'Ring' 0x80007DBA's ItemDifficulty {lore + 10}, before the shares)"), StringComparison.Ordinal)));
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("xp: not on the list: Arcane Lore", StringComparison.Ordinal));

        // The next visit's pick, on the lore the spend left: its spells light, and it goes on the free finger.
        var worn = Judge().Judge(ring, [], new QualitiesSkills(h.State.Self.Qualities));
        output.WriteLine(worn.ToString());
        Assert.Equal(SelfEquipKind.Wear, worn.Kind);
        Assert.Null(worn.LoreNeed);

        // A piece whose gain does not pay: the 7,875 its fifteen ranks would cost buys a magic skill a few ranks, worth more of her hour than a gain of 0.00001.
        var q = h.State.Self.Qualities;
        var skills = new QualitiesSkills(q);
        var judge = Judge();
        var dear = new LoreSpendFacts([new LoreWant(0x80009999, "Cap", lore + 25, SelfEquipKind.Replace, 0.00001, 0.1)], Lore(h), "her lore", judge.Context(skills), judge.Wear, Rate, "the test's rate");
        var no = LoreSpend.Decide(Table, q, dear, Pool, 0);
        output.WriteLine(no.Line + " | " + string.Join(" | ", no.Others));
        Assert.Null(no.Target);
        Assert.Contains(no.Others, o => o.StartsWith("'Cap' 0x80009999: ItemDifficulty ", StringComparison.Ordinal) && o.Contains("Arcane Lore ranks 10 -> 25 for 7,875", StringComparison.Ordinal) && o.Contains("the same experience on her magic skills: ", StringComparison.Ordinal) && o.Contains(" rank(s) of ", StringComparison.Ordinal) && o.Contains(": it does not pay; ", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 own lore and capacity (live ladder58, 2026-11-17: she wore 'Shoes' 0x8000C7B0 of ItemDifficulty 275 with their spells off at her Arcane Lore 273). A pair of Major Strength shoes she wears, two points over her lore: the
    /// worn piece is a want of its own (<see cref="LoreSpend.WornWants"/>), its gain the capacity its Major Strength gives at the load line (+2,250); the spend buys the two ranks first (15 x 2 x 2 = 60 on the rising table), and on the lore
    /// it leaves the shoes' spells would light at the re-wield.
    /// </summary>
    [Fact]
    public void A_worn_piece_two_points_over_her_lore_has_the_lore_raised_to_light_it()
    {
        const uint Shoes = 0x8000C7B0, MajorStrength = 2576;
        var (h, server) = Qwinnie62();
        var lore = Lore(h);
        var shoes = new WornPiece(Shoes, "Shoes", 0x2, 0x100, 0x10000, Appraised(Shoes, new() { [5] = 90, [28] = 242, [107] = 1_120, [108] = 1_120, [109] = lore + 2 }, MajorStrength));
        var terms = Terms() with { Carried = 17_850 };
        var judge = new SelfEquipJudge([], Fire, id => CantripRows.Of(id)?.Name, null).WithWear(terms);
        LoreSpendFacts Read()
        {
            var q = h.State.Self.Qualities;
            var skills = new QualitiesSkills(q);
            return new LoreSpendFacts(LoreSpend.WornWants(judge, [shoes], skills), StandingLore.OwnOf(q, _ => false, out var why), why, judge.Context(skills), judge.Wear, Rate, "the test's rate");
        }

        var facts = Read();
        var want = Assert.Single(facts.Wants);
        output.WriteLine($"{want.Name} {want.Difficulty} {want.Verdict}");
        Assert.True(want.Worn);
        Assert.Equal(lore + 2, want.Difficulty);
        Assert.Equal(L.WearScore.Burden * (2_250.0 / 17_850 * 100.0), want.Gain, 6); // the capacity of Major Strength at the load line: +15 Strength = +2,250
        Assert.Contains($"Arcane Lore {lore} on her own", facts.Lore, StringComparison.Ordinal);

        var rule = Spend(h, server, Read);
        Assert.Null(rule.Failure);
        Assert.Equal(Shoes, rule.LoreDecision!.Chosen!.Guid);
        Assert.Equal(new StatTarget(S, MageTemplate.ArcaneLore, "Arcane Lore", false, 2), rule.LoreDecision.Target);
        Assert.Equal(60, rule.LoreDecision.Cost);
        Assert.Equal(lore + 2, Lore(h));
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: lore: buy first: 'Shoes' 0x8000C7B0: ItemDifficulty ", StringComparison.Ordinal) && l.Contains("she wears it with its spells off: a re-wield at that lore lights them", StringComparison.Ordinal));
        Assert.Null(WearScore.SpellsOff(shoes.Appraisal!, new QualitiesSkills(h.State.Self.Qualities)));
    }

    [Fact]
    public void The_lore_rule_raises_toward_the_highest_need_that_pays_and_her_own_enlightenment_counts()
    {
        var (h, _) = Qwinnie62();
        var lore = Lore(h);
        var q = h.State.Self.Qualities;
        var skills = new QualitiesSkills(q);
        var judge = Judge();
        var low = new LoreWant(0x80000001, "Low", lore + 2, SelfEquipKind.Wear, 5, 0);
        var high = new LoreWant(0x80000002, "High", lore + 4, SelfEquipKind.Wear, 5, 0) { Worn = true };
        var decision = LoreSpend.Decide(Table, q, new LoreSpendFacts([low, high], lore, "her lore", judge.Context(skills), judge.Wear, Rate, "the test's rate"), Pool, 0);
        output.WriteLine(decision.Line + " | " + string.Join(" | ", decision.Others));
        Assert.Equal(0x80000002u, decision.Chosen!.Guid);
        Assert.Contains(decision.Others, o => o.StartsWith("'Low' 0x80000001", StringComparison.Ordinal) && o.Contains("light it too", StringComparison.Ordinal));

        // her own Arcane Enlightenment Self counts at the highest level her book holds (V: +30 in the world's spell table), the bot's lines never
        Assert.Equal(30, StandingLore.OwnEnlightenmentPoints(id => id is 678 or 682));
        Assert.Equal(StandingLore.Of(q, botApplies: false, out _) + 30, StandingLore.OwnOf(q, id => id == 682, out var why));
        Assert.Contains("her own Arcane Enlightenment Self: +30", why, StringComparison.Ordinal);
        Assert.True(EverySpellPlan.GearNeedsLore([("Shoes", 275), ("Ring", 237)], 209, out var need));
        Assert.Contains("'Shoes' asks Arcane Lore 275 against her unbuffed 209", need, StringComparison.Ordinal);
        Assert.False(EverySpellPlan.GearNeedsLore([("Ring", 200)], 209, out _));
    }
}
