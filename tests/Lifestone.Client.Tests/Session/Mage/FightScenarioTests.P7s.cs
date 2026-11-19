using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 P7s (D148), two rows of the long run of 2026-10-30 (cell 0x01050244 of the den). <c>m5-8-loot-no-go-corpse</c>: both no-go blips were the loot step's Use of a Virindi Servant's corpse beyond reach, which let ACE's own MoveTo
/// walk the mage into the no-go cell; now, before such a Use (D141), the corpse's cell and ACE's straight approach are checked against the ground's no-go cells: a corpse in one, or an approach crossing one that the fight's own walk
/// cannot get round, is skipped with no Use, named once; a clear corpse is opened as before. <c>m5-8-no-go-attacker</c>: an attacker shooting from a no-go cell with no line from the mage leads to a named step to a spot with a line, or out
/// of its reach, not a hold (D88). Over the same scripted ACE and walk as the other fight tests; nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static FightOptions NoGoLootFight() => new() { Kills = 1, Seconds = 1000, Loot = true, LootThresholds = RigLoot, LootAggroMetres = 0 };

    [Fact]
    public void A_corpse_in_a_no_go_cell_is_skipped_by_name_with_no_use_and_the_fight_goes_on()
    {
        var r = new Rig(fight: NoGoLootFight()) { Corpses = true, MoveToSeconds = 3 };
        StockCorpse(r);
        var virindi = r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi"); // 10 m off, in the wing's no-go cell: fought back at from where the mage stands (a line)
        var walk = new FakeWalk { TraceCells = [Den, NoGoCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(virindi.Dead);
        Assert.Empty(r.Opened); // no Use: ACE's MoveTo would have walked the mage into the no-go cell
        Assert.Empty(walk.Spots); // and the fight's own walk never goes there either
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80002101 of 'Virindi' is skipped, no Use: it lies in the no-go cell 0x0105010D of "));
        Assert.Equal(0, LinesWith(r, "ACE's own MoveTo walks the mage"));
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains("loot_skipped_unreached=1", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_approach_crossing_a_no_go_cell_that_the_fights_own_walk_cannot_get_round_is_skipped_with_no_use()
    {
        var r = new Rig(fight: NoGoLootFight()) { Corpses = true, MoveToSeconds = 3 };
        StockCorpse(r);
        r.Add(SlaveA, TuskerSlave, FloorCell, 30);
        var walk = new FakeWalk { TraceCells = [Den, NoGoCell, FloorCell], TicksPerLeg = 2 }; // ACE's straight line runs through the wing
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Opened);
        Assert.Single(walk.Spots); // the fight's own walk went toward it once, over the walkable cells
        Assert.Equal(FloorCell, walk.Spots[0].CellId);
        Assert.Equal(1, LinesWith(r, ": no Use from here; the fight's own walk, planned over the walkable cells only, goes toward it first (once)"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=loot (the corpse 0x80001101 of 'Tusker Slave' at 0x01050303 "));
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 of 'Tusker Slave' is skipped, no Use: ACE's straight approach from 0x01050375 crosses the no-go cell 0x0105010D of "));
        Assert.Equal(1, LinesWith(r, ", still, after the fight's own walk toward it"));
        Assert.Equal(1, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void An_approach_crossing_a_no_go_cell_is_walked_round_by_the_fights_own_walk_and_the_corpse_is_then_opened()
    {
        var r = new Rig(fight: NoGoLootFight()) { Corpses = true, MoveToSeconds = 3 };
        StockCorpse(r);
        r.Add(SlaveA, TuskerSlave, FloorCell, 30);
        var walk = new FakeWalk { TraceCells = [Den, NoGoCell, FloorCell], TraceCellsAfterSpotLeg = [FloorCell], TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Single(walk.Spots);
        Assert.Equal([CorpseA], r.Opened); // opened from the far side of the wing
        Assert.Equal([CorpseA], r.Closed);
        Assert.Equal(0, LinesWith(r, "is skipped, no Use:"));
        Assert.Equal(1, LinesWith(r, "loot: the fight's own walk toward the corpse 0x80001101 of 'Tusker Slave' ended; it is looked at again"));
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_clear_corpse_beyond_reach_is_still_opened_through_aces_move_to()
    {
        var r = new Rig(fight: NoGoLootFight()) { Corpses = true, MoveToSeconds = 3 };
        StockCorpse(r);
        r.Add(SlaveA, TuskerSlave, FloorCell, 30);
        var walk = new FakeWalk { TraceCells = [Den, FloorCell] };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([CorpseA], r.Opened);
        Assert.Empty(walk.Spots);
        Assert.Equal(1, walk.Traces);
        Assert.Equal(1, LinesWith(r, "loot: ACE's straight approach to the corpse 0x80001101 of 'Tusker Slave' crosses no no-go cell (2 cell(s), 0x01050375 to 0x01050303); the Use may walk"));
        Assert.Equal(1, LinesWith(r, "ACE's own MoveTo walks the mage"));
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void M6_sprint_a_failed_ACE_walk_to_a_clear_corpse_sends_the_fights_own_walk_once_and_never_loops()
    {
        // Run 5 (cache/m6/sprint/run5/step3-levelup.log 14:51:06 UTC): the trace crossed walkable cells only, ACE's straight walk ran her into a wall, the open failed and the corpse was left. Now the fight's own walk goes
        // toward it once and the corpse is looked at again; here ACE's walk fails every time and the fake walk does not move her, so the second try fails too and the corpse is left, named, with no third.
        var r = new Rig(fight: NoGoLootFight()) { Corpses = true, MoveToSeconds = 3, MoveToFailsAfter = 3 };
        StockCorpse(r);
        r.Add(SlaveA, TuskerSlave, FloorCell, 30);
        var walk = new FakeWalk { TraceCells = [Den, FloorCell], TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([CorpseA, CorpseA], r.Opened); // ACE's walk, the own walk, ACE's walk again from where it ended
        Assert.Single(walk.Spots); // the own walk went once
        Assert.Equal(FloorCell, walk.Spots[0].CellId);
        Assert.Equal(1, LinesWith(r, "the fight's own walk, planned over the walkable cells, goes toward it once; then it is looked at again (m6-loot-walk-after-stall)"));
        Assert.Equal(1, LinesWith(r, "loot: the fight's own walk toward the corpse 0x80001101 of 'Tusker Slave' ended; it is looked at again"));
        Assert.Equal(1, LinesWith(r, "not reached: the open was not confirmed"));
        Assert.Equal(1, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void An_attacker_in_a_no_go_cell_with_no_line_leads_to_a_named_step_to_a_spot_with_a_line_and_is_fought_back_not_held()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi");
        var spot = new CellFrame(OtherFloorCell, new AcFrame(new Vector3(PlayerX, 26, 30), Quaternion.Identity)); // round the corner: 6 m off, 4 m from the Virindi
        var walk = new FakeWalk { BlockedCell = NoGoCell, TicksPerLeg = 3 };
        walk.ClearFromCells.Add(OtherFloorCell);
        walk.Floor.Add(spot);
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([spot], walk.Spots);
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 stands in the no-go cell 0x0105010D with no line from here; a step to a spot with a line to Virindi 0x80002001 in the no-go cell 0x0105010D: 0x010502E1 [100 26 30], 6 m off, 4 m from it"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a step to a spot with a line to Virindi 0x80002001"));
        Assert.Equal(0, LinesWith(r, "holding, nothing is walked into a no-go cell"));
        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void An_attacker_in_a_no_go_cell_with_no_spot_in_line_leads_to_a_step_out_of_its_reach()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi");
        var away = new CellFrame(OtherFloorCell, new AcFrame(new Vector3(PlayerX, -15, 30), Quaternion.Identity)); // 35 m off, 45 m from the Virindi: past the 30 m cast range that stands in for its reach
        var walk = new FakeWalk { BlockedCell = NoGoCell, TicksPerLeg = 3 };
        walk.Floor.Add(away);
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 12; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([away], walk.Spots);
        Assert.Equal(1, LinesWith(r, "with no line from here; a step out of the reach of Virindi 0x80002001 in the no-go cell 0x0105010D: 0x010502E1 [100 -15 30], 35 m off, 45 m from it, past its reach"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a step out of the reach of Virindi"));
        Assert.Equal(0, LinesWith(r, "holding, nothing is walked into a no-go cell"));
    }

    [Fact]
    public void The_steps_for_one_attacker_are_bounded_then_the_hold_and_its_bound_stand_as_before()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi", hitsToKill: 100);
        var spot = new CellFrame(OtherFloorCell, new AcFrame(new Vector3(PlayerX, 26, 30), Quaternion.Identity));
        var walk = new FakeWalk { BlockedCell = NoGoCell, KeepBlockedCell = true, TicksPerLeg = 2 }; // the spot's line is clear by the sweep, but the step never brings one (the approximation's gap)
        walk.ClearFromCells.Add(OtherFloorCell);
        walk.Floor.Add(spot);
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 60 && scenario.Stage == "fight"; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(FightScenario.MaxDefenceSteps, walk.Spots.Count);
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a step to a spot with a line to Virindi 0x80002001 in the no-go cell 0x0105010D: 0x010502E1 [100 26 30], 6 m off, 4 m from it (the cast range 30 m; step 1 of 2)"));
        Assert.Equal(1, LinesWith(r, "walking: purpose=self-defence (a step to a spot with a line to Virindi 0x80002001 in the no-go cell 0x0105010D: 0x010502E1 [100 26 30], 6 m off, 4 m from it (the cast range 30 m; step 2 of 2)"));
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 stands in the no-go cell 0x0105010D with no line from here; holding"));
        Assert.DoesNotContain(r.CastTargets, t => t == VirindiGuid);
    }

    [Fact]
    public void At_the_diamond_scarabs_floor_the_ring_is_left_out_of_the_war_pick_the_bolt_goes_on_and_more_scarabs_bring_it_back()
    {
        const uint ring = 1785;
        const uint scarabs = 0x80007299;
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, ring], characterLevel: 80);
        r.Table[ring] = WorldActionRig.Spell(ring, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Cassius' Ring of Fire", School = MagicSchool.WarMagic, Power = 200, BaseMana = 40, BaseRangeConstant = 4.5f, Formula = [110, 0, 0, 0, 0, 0, 0, 0] };
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40, hitsToKill: 1000);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.NotEmpty(r.CastTargets); // the bolt goes on
        Assert.DoesNotContain("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(1, LinesWith(r, "components: Diamond Scarab at its floor (0 held, under its stop line 2; carried, no shop sells it): Cassius' Ring of Fire is left out of the war pick until more turn up"));
        Assert.Equal(1, LinesWith(r, "war pick: ring: left out at a carried component's floor (Diamond Scarab 0 held, under its stop line 2)"));

        var basis = ReducerHarness.Create(scarabs, ReducerHarness.Stamps(), null, name: "Diamond Scarab"); // three turn up (loot, a trade)
        r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize, WeenieClassId = 7299, ContainerId = WorldActionRig.Player, StackSize = 3 } }, isUpdate: false);
        r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, scarabs, WorldActionRig.Player, 0, 0));
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.Contains("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(1, LinesWith(r, "components: Diamond Scarab back over its floor (3 held, its stop line 2): Cassius' Ring of Fire returns to the war pick"));
    }
}
