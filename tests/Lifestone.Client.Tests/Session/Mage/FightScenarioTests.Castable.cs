using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 K (round 1c's kill task: Flame Bolt IV cast 2,577 times in 196 s into 0x0400, 0 kills, "a kit problem, never retried"): the fight casts what the pack pays for, on <c>WarBoltPick.ChooseCastable</c>'s seam. A pack that pays picks what it
/// picked; a pack short of the top bolt starts at the castable level; a bolt ACE refuses 0x0400 is dropped for the fight, once, and the next is cast; another permanent code drops it on its third refusal in a row; nothing castable ends the fight by name.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static uint[] BoltLine => [.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(s => s.Id)];

    /// <summary>The rig's bolts given a formula each (a scarab of the level's own), so the pack's components can be judged.</summary>
    private static Rig FormulaRig(FightOptions? fight = null)
    {
        var r = new Rig(fight: fight ?? new FightOptions { Kills = 1, Seconds = 600 });
        foreach (var spell in ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells)
        {
            r.Table[spell.Id] = r.Table[spell.Id] with { Formula = [spell.Position switch { <= 6 => (uint)spell.Position, 7 => 0x6Eu, _ => 0x70u }, 0, 0, 0, 0, 0, 0, 0] };
        }

        return r;
    }

    /// <summary>The pack holds the War Magic focus and what one cast of each of <paramref name="paid"/> burns (a pack the fight can read: <c>LevelFloor.HeldForAccount</c>).</summary>
    private static void Stock(Rig r, IEnumerable<uint> paid)
    {
        var pack = new Dictionary<uint, int> { [LevelFloor.FocusOf(MagicSchool.WarMagic)] = 1 };
        foreach (var id in paid)
        {
            foreach (var (component, n) in GearKit.FociFormulaCounts(r.Table[id].Formula))
            {
                Assert.True(ComponentWeenies.TryGetByComponent(component, out var weenie), "component " + component);
                pack[weenie.Wcid] = Math.Max(pack.GetValueOrDefault(weenie.Wcid), n);
            }
        }

        var guid = 0x80007300u;
        foreach (var (wcid, count) in pack)
        {
            HoldStack(r, guid++, wcid, count);
        }
    }

    private static void HoldStack(Rig r, uint item, uint wcid, int count)
    {
        var basis = ReducerHarness.Create(item, ReducerHarness.Stamps(), null, name: "Pack stack " + wcid);
        r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize, WeenieClassId = wcid, ContainerId = WorldActionRig.Player, ItemType = 0x1000, StackSize = (ushort)count } }, isUpdate: false);
        r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, WorldActionRig.Player, 0, 0));
    }

    /// <summary>The bolt the fight casts with no pack to judge: the level D10's rule chose.</summary>
    private static uint TopBolt()
    {
        var r = FormulaRig();
        r.Add(SlaveA, TuskerSlave, Den, 30);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(r.Scenario(new FakeWalk())));
        return r.Answered[0];
    }

    [Fact]
    public void A_pack_that_can_pay_picks_exactly_what_it_picked_M6_7_K()
    {
        var top = TopBolt();
        var r = FormulaRig();
        Stock(r, BoltLine);
        r.Add(SlaveA, TuskerSlave, Den, 30);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(r.Scenario(new FakeWalk())));
        Assert.All(r.Answered, id => Assert.Equal(top, id));
        Assert.Equal(0, LinesWith(r, "war pick: the pack does not pay"));
        Assert.Equal(0, LinesWith(r, "is dropped for the fight"));
    }

    [Fact]
    public void A_pack_short_of_the_top_bolts_component_starts_at_the_castable_bolt_and_no_refused_cast_is_sent_M6_7_K()
    {
        var top = TopBolt();
        var below = BoltLine[Array.IndexOf(BoltLine, top) - 1];
        var r = FormulaRig();
        Stock(r, BoltLine.TakeWhile(id => id != top));
        r.RefusedBy[top] = 0x0400; // ACE would refuse it: it is never sent
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.NotEmpty(r.Answered);
        Assert.All(r.Answered, id => Assert.Equal(below, id));
        Assert.Equal(1, LinesWith(r, "war pick: the pack does not pay for level "));
        Assert.Equal(0, scenario.Tally.Misses);
    }

    [Fact]
    public void A_bolt_refused_0x0400_is_dropped_for_the_fight_once_and_the_next_castable_kills_with_no_miss_and_no_walk_M6_7_K()
    {
        var top = TopBolt();
        var at = Array.IndexOf(BoltLine, top);
        var r = FormulaRig(); // no focus: the pack is not judged before the cast, the refusal drops the bolt
        r.RefusedBy[top] = 0x0400;
        r.RefusedBy[BoltLine[at - 1]] = 0x0400;
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, r.Answered.Count(id => id == top));
        Assert.Equal(1, r.Answered.Count(id => id == BoltLine[at - 1]));
        Assert.All(r.Answered.Skip(2), id => Assert.Equal(BoltLine[at - 2], id));
        Assert.Equal(2, LinesWith(r, "is dropped for the fight: ACE refused it for a missing component (0x0400); the next pick is the next castable"));
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Empty(walk.Spots);
    }

    [Fact]
    public void A_pack_that_pays_for_no_bolt_ends_the_fight_at_once_by_name_with_nothing_sent_M6_7_K()
    {
        var r = FormulaRig();
        Stock(r, []);
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));

        Assert.Empty(r.Answered);
        Assert.StartsWith(FightScenario.NoCastableFailure + ": no-components: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("; a logistics trip is due", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_bolt_refused_0x0400_ends_the_fight_by_name_each_refused_once_M6_7_K()
    {
        var r = FormulaRig();
        foreach (var id in BoltLine)
        {
            r.RefusedBy[id] = 0x0400;
        }

        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));

        Assert.Equal(r.Answered.Count, r.Answered.Distinct().Count()); // no refused cast repeats
        Assert.NotEmpty(r.Answered);
        Assert.StartsWith(FightScenario.NoCastableFailure + ": no-components: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("dropped for the fight: ", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_permanent_code_drops_the_bolt_on_its_third_refusal_in_a_row_M6_7_K()
    {
        var top = TopBolt();
        var r = FormulaRig();
        r.RefusedBy[top] = 0x03FC; // ACE's MagicInvalidSpellType
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(FightScenario.RefusalRepeats, r.Answered.Count(id => id == top));
        Assert.Equal(1, LinesWith(r, "is dropped for the fight: ACE refused it 3 times in a row (0x03FC"));
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void A_permanent_code_on_every_bolt_is_a_bounded_named_end_M6_7_K()
    {
        var r = FormulaRig();
        foreach (var id in BoltLine)
        {
            r.RefusedBy[id] = 0x03FC;
        }

        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));

        Assert.True(r.Answered.Count <= BoltLine.Length * FightScenario.RefusalRepeats, r.Answered.Count + " casts");
        Assert.StartsWith(FightScenario.NoCastableFailure + ": no bolt of the line is left; dropped for the fight: ", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_of_the_moment_is_never_a_reason_to_drop_the_bolt_M6_7_K()
    {
        var top = TopBolt();
        var r = FormulaRig();
        r.Script(Shot.MagicOutOfRange, Shot.MagicOutOfRange, Shot.MagicOutOfRange, Shot.MagicOutOfRange); // 0x0406: the target's place, not the spell
        r.Add(SlaveA, TuskerSlave, Den, 30);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(r.Scenario(new FakeWalk())));
        Assert.All(r.Answered, id => Assert.Equal(top, id));
        Assert.Equal(0, LinesWith(r, "is dropped for the fight"));
    }

    [Fact]
    public void The_level_chosen_again_never_goes_back_to_a_dropped_bolt_M6_7_K()
    {
        var top = TopBolt();
        var r = FormulaRig();
        r.RefusedBy[top] = 0x0400;
        r.Add(SlaveA, TuskerSlave, Den, 30, hitsToKill: 4);
        var scenario = r.Scenario(new FakeWalk());
        var buffed = false;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 2000 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario);
            if (!buffed && r.Answered.Count == 2)
            {
                buffed = true;
                r.R.Enchant(2); // the signature changes: the level is chosen again
            }
        }

        Assert.True(buffed);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, r.Answered.Count(id => id == top));
        Assert.Equal(1, LinesWith(r, " is not castable now: "));
    }

    [Fact]
    public void A_war_option_refused_0x0400_leaves_the_options_and_the_bolt_goes_on_M6_7_K()
    {
        const uint ring = 1785;
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, ring], characterLevel: 80, fight: new FightOptions { Kills = 6, Seconds = 600 });
        r.Table[ring] = WorldActionRig.Spell(ring, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Cassius' Ring of Fire", School = MagicSchool.WarMagic, Power = 200, BaseMana = 40, BaseRangeConstant = 4.5f, Formula = [110, 0, 0, 0, 0, 0, 0, 0] };
        r.RefusedBy[ring] = 0x0400;
        HoldStack(r, 0x80007299, 7299, 9); // the ring's Diamond Scarabs, over their floor: the option stands until ACE's refusal
        for (var i = 0u; i < 6; i++)
        {
            r.Add(SlaveA + i, TuskerSlave, Den, PlayerY + 2 + (i * 0.2f));
        }

        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, r.Answered.Count(id => id == ring));
        Assert.DoesNotContain("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(6, scenario.Tally.Kills);
    }
}
