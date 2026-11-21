using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 fight targeting (run 9, 2026-11-20 19:51 to 2026-11-21 04:58 UTC, the Tusker Holding: 355 Harm Other and drain decisions at Tusker Slaves, 253 of them at Slaves 9.6 to 32.7 m off that no swing order named, 102 at 12
/// Slaves the shot check had dropped 3.9 to 21.5 m off and that stood 1.7 to 11.7 m off by then; the move for a line ran 0 times). Three rules, over the scripted ACE and walk of the other fight tests:
/// (a) <c>m6-blow-unpinned</c>: a blow notice is pinned only on a creature that can have struck her (a swing order, its kind's reach, or the only one of the name); (b) <c>m6-line-first</c>: an attacker with no shot from here is
/// moved to first (a sidestep, then a walk to a spot with a line), and fought with Life Magic only when neither finds a spot or the creature cannot be reached (the owner's rule of 2026-11-16 for the hovering Zefir);
/// (c) <c>m6-shot-drop-place</c>: a shot drop belongs to where it was measured. Nothing here needs the dats or a server, and nothing here was seen live.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const string SlaveName = "Tusker Slave";
    private const uint SlaveNear = 0x80004001;
    private const uint SlaveFar = 0x80004002;

    /// <summary>The bolts and Harm Other I to IV: the life rule has a spell to cast, so a rule that comes before it is seen to.</summary>
    private static IReadOnlyCollection<uint> BoltsAndHarm() => [.. ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id), .. HarmOneToFour];

    /// <summary>The server moves the mage to (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="cell"/>; <paramref name="stamp"/> above the rig's last.</summary>
    private static void MoveMage(Rig r, uint cell, float x, float y, ushort stamp) =>
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, new UpdatePositionMsg(Lifestone.Client.Tests.Session.ActionHarness.Amy, PositionFlags.Grounded, ReducerHarness.Position(cell, x, y, 30), null, null, 1, stamp, 2, 1));

    /// <summary>The server moves creature <paramref name="guid"/> to <paramref name="y"/>; <paramref name="stamp"/> above its last (<see cref="Rig.Move"/> uses 50).</summary>
    private static void MoveCreature(Rig r, uint guid, uint cell, float y, ushort stamp) =>
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, new UpdatePositionMsg(guid, PositionFlags.Grounded, ReducerHarness.Position(cell, PlayerX, y, 30), null, null, 1, stamp, 1, 1));

    /// <summary>A shot check that answers from a list, the last answer standing.</summary>
    private sealed class ScriptedShots(params ShotAnswer[] answers) : IShotCheck
    {
        public int Asked { get; private set; }

        public ShotVerdict Check(IGameSessionView session, uint spellId, uint target)
        {
            var answer = answers[Math.Min(Asked++, answers.Length - 1)];
            return new ShotVerdict(answer, answer == ShotAnswer.Blocked ? "a ledge" : "open air");
        }
    }

    // ---- (a) a blow is pinned only on a creature that can have struck her ----

    [Fact]
    public void M6_targeting_a_blow_with_no_swing_order_is_not_pinned_on_a_far_creature_of_that_name_and_no_life_spell_goes_at_it()
    {
        // Run 9's far case: Slaves stuck on a lower floor 12 and 16 m off, out of line; a blow notice names "Tusker Slave" alone, and none of the two can have struck her.
        var r = new Rig(known: BoltsAndHarm(), fight: Unending(), life: true);
        r.Add(SlaveNear, TuskerSlave, FloorCell, PlayerY + 12, hitsToKill: 100);
        r.Add(SlaveFar, TuskerSlave, FloorCell, PlayerY + 16, hitsToKill: 100);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true };
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 40; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r, SlaveName);
            }

            r.Frame(scenario);
        }

        Assert.Equal(0, scenario.AttackersNow);
        Assert.Equal(5, scenario.BlowsUnpinned);
        Assert.Equal(5, LinesWith(r, "self-defence: a blow of Tusker Slave (damage notice) came with no swing order of that name in the last 3 s, and the nearest Tusker Slave 0x80004001 stands 12.0 m off by the fight's record, past a melee blow's reach 4 m (the far-blow measure's, FightScenario.LivePositionBlowMetres"));
        Assert.Equal(0, LinesWith(r, "attacked (damage notice)"));
        Assert.Equal(0, scenario.LifeThroughCasts);
        Assert.DoesNotContain(HarmFour, r.CastSpells);
        Assert.Equal(0, scenario.Defended);
    }

    [Fact]
    public void M6_targeting_a_blow_is_pinned_on_the_one_within_melee_reach_and_a_swing_order_pins_a_far_one_by_its_guid()
    {
        var r = new Rig(fight: Unending());
        var near = r.Add(SlaveNear, TuskerSlave, Den, PlayerY + (float)FightScenario.LivePositionBlowMetres - 0.5f, hitsToKill: 100); // within the far-blow measure's reach
        r.Add(SlaveFar, TuskerSlave, FloorCell, PlayerY + 12, hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, SlaveName);
        r.Frame(scenario);

        Assert.Equal(1, scenario.AttackersNow);
        Assert.Equal(0, scenario.BlowsUnpinned);
        Assert.Equal(1, LinesWith(r, "self-defence: Tusker Slave 0x80004001 attacked (damage notice); fighting it before the targets"));

        // The far one's swing order names it by guid: its blow is its own, wherever its record stands (the existing rule).
        Swing(r, SlaveFar, 2);
        MeleeHit(r, SlaveName);
        r.Frame(scenario);

        Assert.Equal(2, scenario.AttackersNow);
        Assert.Equal((1, 1), (scenario.SwingOrders, scenario.SwingBlows));
        Assert.Equal(0, scenario.BlowsUnpinned);
        Assert.False(near.Dead);
    }

    [Fact]
    public void M6_targeting_the_only_creature_of_its_name_is_pinned_wherever_its_record_stands_and_the_line_says_why()
    {
        var r = new Rig(fight: Unending());
        r.Add(VirindiGuid, Virindi, Den, PlayerY + 20, "Virindi", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r);
        r.Frame(scenario);

        Assert.Equal(1, scenario.AttackersNow);
        Assert.Equal(0, scenario.BlowsUnpinned);
        Assert.Equal(1, LinesWith(r, "self-defence: a blow of Virindi (damage notice) with no swing order: 0x80002001, 20.0 m off by the fight's record, past the 4 m a blow of its kind comes from, is the only living Virindi in the table: the notice is its (m6-blow-unpinned: the name is an identity)"));
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 attacked (damage notice)"));
    }

    [Fact]
    public void M6_targeting_a_thrower_is_pinned_within_its_recorded_range_and_a_launcher_within_ACEs_missile_cap()
    {
        // The bestiary's own records, not hand-typed: a kind whose ground profile records a thrown attack (the Tusker Holding's chitticks), and one the world database's table says wields a launcher.
        var thrower = Bestiary.All.First(k => k.Offence.Ranged is { Spell: false, RangeMetres: > 10 } && k.Timing is not { Launcher: true });
        var range = thrower.Offence.Ranged!.RangeMetres;
        var archer = Bestiary.All.First(k => k.Offence.Ranged is null && k.Timing is { Launcher: true });
        Assert.Equal(77.7, FightScenario.AceMissileRangeCapMetres, 1); // ACE: 85 yards over 1.094 (Creature_Missile.cs:402-403)

        var r = new Rig(fight: Unending());
        r.Add(SlaveNear, thrower.Wcid, Den, PlayerY + (float)(range - 5), "Thrower", hitsToKill: 100);
        r.Add(SlaveFar, thrower.Wcid, Den, PlayerY + (float)(range + 5), "Thrower", hitsToKill: 100);
        r.Add(VirindiGuid, archer.Wcid, Den, PlayerY + 50, "Archer", hitsToKill: 100);
        r.Add(VirindiGuid + 1, archer.Wcid, Den, PlayerY + 60, "Archer", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, "Thrower");
        MeleeHit(r, "Archer");
        r.Frame(scenario);

        Assert.Equal(2, scenario.AttackersNow);
        Assert.Equal(0, scenario.BlowsUnpinned);
        Assert.Equal(1, LinesWith(r, "self-defence: Thrower 0x80004001 attacked (damage notice)"));
        Assert.Equal(1, LinesWith(r, "self-defence: Archer 0x80002001 attacked (damage notice)"));
    }

    // ---- (b) a spot with a line first; Life Magic when none, or no path ----

    [Fact]
    public void M6_targeting_an_attacker_at_her_with_something_between_them_is_sidestepped_for_before_any_Harm_Other()
    {
        // Before: Harm Other came first whenever it was known, in range and paid for (run 9: 0 moves for a line in 9 h).
        var r = new Rig(known: BoltsAndHarm(), life: true);
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 23, "Virindi"); // 3 m off, something between them from where she stands
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell] };
        walk.ClearFromCells.Add(OtherFloorCell); // from the cell a step to the side ends in, the line is clear
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellAttack(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(1, scenario.Sidesteps);
        Assert.Single(walk.Spots);
        Assert.Equal(0, scenario.LifeThroughCasts);
        Assert.DoesNotContain(HarmFour, r.CastSpells);
        Assert.Equal(1, scenario.LineSpotsFound);
        Assert.Equal(1, LinesWith(r, "self-defence: the walk is dropped; 0x80002001 is in range with a clear line") + LinesWith(r, "self-defence: the leg for a line to Virindi 0x80002001 arrived at 0x01050375")); // checked after: the line came
    }

    [Fact]
    public void M6_targeting_an_attacker_10_m_off_with_no_line_is_walked_to_a_spot_with_a_line_before_any_Harm_Other()
    {
        var r = new Rig(known: BoltsAndHarm(), life: true);
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // 10 m off, behind a wall, within Harm Other's 20 m
        var spot = SpotAt(OtherFloorCell, 26);
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(spot);
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellAttack(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(virindi.Dead);
        Assert.Equal([spot], walk.Spots);
        Assert.Equal(0, scenario.LifeThroughCasts);
        Assert.DoesNotContain(HarmFour, r.CastSpells);
        Assert.Equal(1, LinesWith(r, "self-defence: a walk toward Virindi 0x80002001 at 0x01050303, 10 m off with no shot from here: to 0x010502E1 [100 26 30]"));
    }

    [Fact]
    public void M6_targeting_a_spot_no_walkable_path_reaches_leaves_the_attacker_to_Harm_Other_where_she_stands()
    {
        var r = new Rig(known: BoltsAndHarm(), fight: Unending(), life: true);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 100, health: 60);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TraceCells = [Den, OtherFloorCell, FloorCell], RefuseSpotLegs = true };
        walk.Floor.Add(SpotAt(OtherFloorCell, 26));
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellAttack(r);
        for (var i = 0; i < 200 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Empty(walk.Spots);
        Assert.Equal(0, walk.Legs);
        Assert.All(r.CastSpells, id => Assert.Equal(HarmFour, id));
        Assert.True(scenario.LifeThroughCasts >= 1);
        Assert.Equal(2, LinesWith(r, "self-defence: the leg to the spot with a line to Virindi 0x80002001 could not be planned (no walkable path: the fake walk finds no walkable path); a life spell is asked where she stands (m6-line-first)")); // each of the two searches, then the bound
    }

    [Fact]
    public void M6_targeting_like_the_hovering_Zefir_a_creature_no_spot_has_a_line_to_is_harmed_where_she_stands_not_run_past()
    {
        // The owner's rule of 2026-11-16 (the hovering Kilif Zefir of the Crater Pathway, the travel fight-back's own tests in RungChangeTests.LifeStrike): a creature no move reaches is fought with Life Magic.
        // Here no spot on the line to it, nor any point to the side, has a line to it.
        var r = new Rig(known: BoltsAndHarm(), fight: Unending(), life: true);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 100, health: 60);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(SpotAt(OtherFloorCell, 26)); // on the line and in range, but the wall hides it from there too
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellAttack(r);
        for (var i = 0; i < 200 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Empty(walk.Spots);
        Assert.All(r.CastSpells, id => Assert.Equal(HarmFour, id));
        Assert.Equal((1, 1), (scenario.LifeThroughKills, scenario.Defended));
        Assert.Equal(2, scenario.LineSpotsNone); // two searches, then the bound: no third
        Assert.Equal(1, LinesWith(r, "self-defence: no spot with a line to Virindi 0x80002001 (10 m off, no shot from here; search 1 of 2): no sidestep: it stands 10 m off, past the sidestep's reach 6 m; no floor spot in the 3 cell(s) on the line to it is within the cast range 30 m with a clear line (1 line(s) swept); a life spell is asked next (m6-line-first)"));
        Assert.Equal(0, LinesWith(r, "falls back to the tour's next leg"));
    }

    [Fact]
    public void M6_targeting_two_legs_that_brought_no_line_hand_the_attacker_to_Harm_Other_not_the_tours_leg()
    {
        // The spot has a line by the test, but the legs never bring the mage there (the fake walk does not move her): each arrival is checked, then the bound, then Life Magic.
        var r = new Rig(known: BoltsAndHarm(), fight: Unending(), life: true);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 100, health: 60);
        var spot = SpotAt(OtherFloorCell, 26);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 2, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(spot);
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellAttack(r);
        for (var i = 0; i < 300 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal([spot, spot], walk.Spots);
        Assert.Equal(2, LinesWith(r, "self-defence: the leg for a line to Virindi 0x80002001 arrived at 0x01050375 [100 20 30], 10 m from it: the line test finds it still blocked: the next search or the life rule (m6-line-first)"));
        Assert.All(r.CastSpells, id => Assert.Equal(HarmFour, id));
        Assert.True(scenario.LifeThroughCasts >= 1);
        Assert.Equal(0, LinesWith(r, "falls back to the tour's next leg"));
    }

    // ---- (c) a shot drop belongs to where it was measured ----

    [Fact]
    public void M6_targeting_a_drop_by_the_check_ends_when_she_or_it_moves_from_where_it_was_measured()
    {
        var lines = new List<string>();
        var shots = new ShotLedger(lines.Add);
        var me = SpotAt(Den, 20);
        var it = SpotAt(Den, 30);
        const double moved = FightScenario.ShotDropMovedMetres;
        Assert.Equal(FightScenario.LivePositionGapMetres, moved); // the fight's own measure of two places, no new number

        shots.Drop(7, "Tusker Slave", 0, "the shot is blocked: a ledge", ShotDropKind.Blocked, new ShotPlace(me, it));
        Assert.True(shots.IsDropped(7, 1, in me, in it, moved));
        Assert.True(shots.IsDropped(7, 1, SpotAt(Den, 21), in it, moved)); // 1 m: the same place
        Assert.False(shots.IsDropped(7, 1, SpotAt(Den, 22), in it, moved)); // 2 m: a new place
        Assert.Contains(lines, l => l.Contains("shot drop: 0x00000007 is back: she has moved 2.0 m from where it was measured (more than 1.5 m); it stands 8.0 m away now, the check belonged to the place it was measured at (m6-shot-drop-place)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("10.0 m apart: it ends sooner when she or it moves from there or it comes within 3 m (m6-shot-drop-place)", StringComparison.Ordinal));

        shots.Drop(7, "Tusker Slave", 2, "the shot is blocked: a ledge", ShotDropKind.Blocked, new ShotPlace(me, it));
        Assert.False(shots.IsDropped(7, 3, in me, SpotAt(Den, 28), moved)); // it came down off its ledge: 2 m
        Assert.Contains(lines, l => l.Contains("is back: it has moved 2.0 m from where it was measured", StringComparison.Ordinal));
        Assert.False(shots.DropKindOf(7, out _));
    }

    [Fact]
    public void M6_targeting_a_drop_measured_close_stays_while_neither_moves_and_one_measured_far_ends_when_it_comes_within_3_m()
    {
        var lines = new List<string>();
        var shots = new ShotLedger(lines.Add);
        var me = SpotAt(Den, 20);
        const double moved = FightScenario.ShotDropMovedMetres;

        // Measured 2.4 m off: taking it back each frame would ask the same blocked check again and cast nothing. It stands until one of them moves.
        shots.Drop(7, "Tusker Slave", 0, "the shot is blocked: a ledge", ShotDropKind.Blocked, new ShotPlace(me, SpotAt(Den, 22.4f)));
        for (var t = 1; t < 100; t += 10)
        {
            Assert.True(shots.IsDropped(7, t, in me, SpotAt(Den, 22.4f), moved));
        }

        // Run 9's 0x80006255: measured 5.4 m off by the check, then within 3 m. Moved 1 m here: it is the coming close that ends it.
        shots.Drop(8, "Tusker Slave", 0, "the shot is blocked: a ledge", ShotDropKind.Blocked, new ShotPlace(me, SpotAt(Den, 23.4f)));
        Assert.True(shots.IsDropped(8, 1, in me, SpotAt(Den, 23.1f), moved));
        Assert.False(shots.IsDropped(8, 1, in me, SpotAt(Den, 22.4f), moved));
        Assert.Contains(lines, l => l.Contains("shot drop: 0x00000008 is back: it has come within 3 m (measured 3.4 m off): an attacker this close is fought (M5 D88)", StringComparison.Ordinal));

        // The time still ends any drop.
        Assert.False(shots.IsDropped(7, ShotLedger.DropSeconds + 1, in me, SpotAt(Den, 22.4f), moved));
    }

    [Fact]
    public void M6_targeting_a_drop_for_wasted_casts_ends_when_she_moves_but_not_when_its_drawn_body_shifts_and_one_with_no_place_keeps_the_old_rule()
    {
        var lines = new List<string>();
        var shots = new ShotLedger(lines.Add);
        var me = SpotAt(Den, 20);
        var it = SpotAt(Den, 25);
        const double moved = FightScenario.ShotDropMovedMetres;

        Assert.False(shots.NoteWasted(7, "Tusker Slave", 0, "no bolt", new ShotPlace(me, it)));
        Assert.True(shots.NoteWasted(7, "Tusker Slave", 1, "no bolt", new ShotPlace(me, it)));
        Assert.True(shots.DropKindOf(7, out var kind) && kind == ShotDropKind.Wasted);
        Assert.True(shots.IsDropped(7, 2, in me, SpotAt(Den, 27), moved)); // its body shifted 2 m: a sticky creature's drawn body does, and each take-back would pay two more casts
        Assert.False(shots.IsDropped(7, 2, SpotAt(Den, 18), in it, moved)); // she stepped 2 m: a new place to cast from
        Assert.Contains(lines, l => l.Contains("the wasted casts belonged to the place it was measured at", StringComparison.Ordinal));

        // A drop with no place (the travel fight-back's): the time and the close rule, as before.
        shots.Drop(9, "Tusker Slave", 0, "the shot is blocked: a ledge");
        Assert.True(shots.IsDropped(9, 1, SpotAt(Den, 0), in it, moved));
        Assert.False(shots.IsDropped(9, 1, in me, SpotAt(Den, 22), moved));
        Assert.Contains(lines, l => l.Contains("shot drop: 0x00000009 is back: it stands 2.0 m away, within 3 m", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_targeting_run_9s_near_case_a_dropped_attacker_that_came_close_gets_the_shot_check_again_and_the_bolt_and_the_life_line_names_the_check()
    {
        // Run 9, Tusker Slave 0x80006269: the check before the cast blocked it 5 m off (02:11:56.966 UTC), she and it moved, and it was harmed 3.8 m off for 120 s labelled "its bolts came to nothing"; the bolt the drop's end
        // let go hit at once (02:14:03). Here: blocked from where it stood, harmed while the drop holds from there (no spot: this walk offers none), then it steps in and the drop is the check's to take again.
        var r = CaveRig(HarmOneToFour, Unending());
        var shots = new ScriptedShots(ShotAnswer.Blocked, ShotAnswer.Clear);
        r.R.Api.UseShots(shots);
        var tusker = r.Add(CrimsonGuid, Crimsonback, CaveThere, PlayerY + 3.5f, CrimsonName, hitsToKill: 2, health: 110);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, CrimsonName); // 3.5 m off: within a melee blow's reach
        for (var i = 0; i < 200 && !r.CastSpells.Contains(HarmFour); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, shots.Asked);
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80003001 (Tusker Crimsonback) is left alone for 120 s: the shot is blocked: a ledge; measured with her at 0x5B4601FC"));
        Assert.Equal(1, LinesWith(r, LifeAt + "3.5 m off, cannot be reached by the bolt (the shot rule dropped it: the shot check before the cast found the bolt's path blocked from where she stood, and no bolt was sent): Harm Other IV (1174)"));
        Assert.Equal(0, LinesWith(r, "its bolts came to nothing"));

        MoveCreature(r, CrimsonGuid, CaveThere, PlayerY + 2.5f, 60); // 1 m nearer: within 3 m of her
        for (var i = 0; i < 400 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r, CrimsonName);
            }

            r.Frame(scenario);
        }

        var bolt = scenario.SpellId;
        Assert.True(tusker.Dead);
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80003001 is back: it has come within 3 m (measured 3.5 m off)"));
        Assert.Equal(3, shots.Asked); // blocked once, then asked again before each of the two bolts that kill it
        Assert.Equal([bolt, bolt], r.CastSpells.Where(id => id != HarmFour).ToArray());
        Assert.True(r.CastSpells.LastIndexOf(HarmFour) < r.CastSpells.IndexOf(bolt), "no Harm Other once the drop was the check's to take again");
    }

    [Fact]
    public void M6_targeting_a_drop_measured_close_ends_when_she_steps_and_the_bolt_goes_after_the_check()
    {
        var r = CaveRig(HarmOneToFour, Unending());
        var shots = new ScriptedShots(ShotAnswer.Blocked, ShotAnswer.Clear);
        r.R.Api.UseShots(shots);
        var tusker = r.Add(CrimsonGuid, Crimsonback, CaveThere, PlayerY + 2.4f, CrimsonName, hitsToKill: 2, health: 110);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, CrimsonName);
        for (var i = 0; i < 200 && !r.CastSpells.Contains(HarmFour); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(0, LinesWith(r, "is back")); // measured 2.4 m off: no take-back while neither moves
        MoveMage(r, CaveHere, PlayerX + 2, PlayerY, 4); // she steps 2 m to the side
        for (var i = 0; i < 400 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r, CrimsonName);
            }

            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80003001 is back: she has moved 2.0 m from where it was measured (more than 1.5 m)"));
        Assert.Equal(3, shots.Asked); // blocked once, then asked again before each of the two bolts that kill it
        Assert.Equal(scenario.SpellId, r.CastSpells[^1]);
    }
}
