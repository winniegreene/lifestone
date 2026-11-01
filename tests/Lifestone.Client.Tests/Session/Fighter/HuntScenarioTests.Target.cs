using System.Numerics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// Group 4 of the hunt rows over the scripted rig (M4 4.1 fix2a): the target choice, the approach and the ground (M4 1.2, 1.3, 3.1 s0). The scripted ACE walks the player to a far target as the stand-in did (the player's MoveToObject and an F748 every 200 ms, cancelled
/// with YouChargedTooFar 15 m from where the walk began); the rows that play ACE's live round 3 (the MoveToObject alone, the client's body carrying it out) turn <c>ApproachSendsPositions</c> off.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private const string TuskerName = "Tusker Slave";
    private const uint TuskerWcid = 1628;

    [Fact]
    public void A_melee_hunt_binds_goes_to_the_belt_kills_the_nearest_listed_creature_and_comes_back()
    {
        using var rig = Belt12(Hunt());

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Null(scenario.Failure);
        var fields = scenario.VerdictFields();
        Assert.Matches(Verdict, fields);
        Assert.StartsWith("fighter=ok kills=1 ", fields, StringComparison.Ordinal);
        Assert.True(scenario.Tally.Swings >= 1, fields);
        Assert.True(rig.Session.Attack.Swings >= 1, fields);
        Assert.True(scenario.Tally.Hits >= 1 || scenario.Tally.Kills == 1, fields);
        Assert.Equal(0, scenario.Tally.Shots);
        Assert.EndsWith(" returned=yes", fields, StringComparison.Ordinal);
        Assert.True(scenario.Bound);
        Assert.Equal(1, scenario.CellsVisited);
        Assert.Equal(1, scenario.Attacks);
        Assert.Equal(0xA9B4u, rig.Session.State.Self.Position!.Value.CellId >> 16);
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var maceWielded) && maceWielded, "the Mace stays wielded (the fighter-build scenario reads wielded=331)");
        Assert.True(rig.Logged("[hunt] verdict fighter=ok") || rig.Logged("verdict fighter=ok"), "the verdict line");
    }

    [Fact]
    public void A_target_the_server_refuses_is_excluded_and_the_next_one_is_fought()
    {
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) =>
        {
            r.PlaceCreature(FirstCreature, Belt, 8, refuses: true); // the nearest: a creature to the client, none to the script (a refusal)
            r.PlaceCreature(FirstCreature + 1, Belt, 12);
        };

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(2, scenario.Attacks);
        Assert.True(rig.Logged("was refused"), "the refusal is logged by name");
        Assert.True(rig.Logged("excluding it and picking the next"));
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_with_no_listed_creature_sends_the_hunt_to_the_next_cell()
    {
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) =>
        {
            if (n == 2)
            {
                r.PlaceCreature(FirstCreature, Belt, 12);
            }
        };

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(2, scenario.CellsVisited);
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void A_nearer_listed_level_30_creature_is_skipped_for_a_farther_one_in_the_band()
    {
        // Review 1.2-R1: live round 1 fought the Tumerok Warrior (level 30) for the retreat. It is listed (wcid 233) and the band 8-20 filters it; the Field Ursuin (level 8) 20 m away is the target.
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) =>
        {
            r.PlaceCreature(FirstCreature, "Tumerok Warrior", 6, health: 1_000_000, wcid: TumerokWcid);
            r.PlaceCreature(FirstCreature + 1, Belt, 20);
        };

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(rig.Logged("acquire: target 'Field Ursuin' wcid 7990 level 8"), "the target's wcid and level are logged");
        Assert.False(rig.Logged("acquire: target 'Tumerok Warrior'"), "the level-30 creature is never the target");
    }

    [Fact]
    public void A_creature_that_is_not_in_the_table_is_never_picked_not_even_as_a_fallback()
    {
        // Review 1.2-R1: the unlisted fallback (any creature within 40 m on the last cell) is gone. The Tusker Slave is not a belt creature.
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, TuskerName, 3, wcid: TuskerWcid);

        var status = rig.Run(1200);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("an unlisted creature is never a target", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(HuntScenario.MaxCells, scenario.CellsVisited);
        Assert.Equal(0, scenario.Attacks);
        Assert.False(rig.Logged("fallback"));
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void A_sequence_that_never_ends_is_cancelled_after_the_bounded_wait_and_its_target_excluded()
    {
        // Review 1.2-R4: a sequence left Active (a lost final 01A7) stalled the hunt silently until its 300 s. Here the sequence is made Active by hand just before the send: no 01A7 will ever come.
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        var scenario = rig.Scenario;
        var forced = false;
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.05)
        {
            if (!forced && scenario.Stage == "fight-send")
            {
                forced = true;
                rig.Session.Reducer.Attack.OnCommence();
                Assert.True(rig.Session.Attack.Active);
            }

            status = rig.Run(0.05);
        }

        Assert.True(forced, "the hunt reached the first send");
        Assert.Equal(1, scenario.Stalls);
        Assert.Equal(0, scenario.Attacks);
        Assert.True(rig.Logged("the final 01A7 never came"));
        Assert.True(rig.Logged("sending CancelAttack and excluding"));
        Assert.Equal(ScenarioStatus.Failed, status); // the only creature is excluded: the run ends loudly, not at the 300 s
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void A_target_thirty_metres_away_is_walked_to_in_fifteen_metre_approaches_retried_after_YouChargedTooFar()
    {
        // Live round 2: ACE cancels a melee approach once the player is 15 m from where it began (028A 0x003D then 01A7 0x0036); the band's targets are 15-60 m away.
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(scenario.ChargeRetries >= 1, "the first approach is cancelled at 15 m");
        Assert.True(rig.Logged("YouChargedTooFar (0x003D)"), "the WeenieError is logged by name");
        Assert.True(rig.Logged("retry 1 of"));
        Assert.False(rig.Logged("no sequence began within"), "the hunt reacts to the 01A7 at once, not after 15 s");
    }

    /// <summary>Takes the client's reported positions to the script (the headless run sends no movement), as ACE takes the client's autonomous positions.</summary>
    private sealed class ClientToScript(HuntScriptRig rig) : IMovementSink
    {
        public int Positions { get; private set; }

        public void MoveToState(Lifestone.Data.Motion.RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) => Report(position);

        public void AutonomousPosition(in CellFrame position, bool contact) => Report(position);

        public void Jump(float extent, Vector3 velocity, in CellFrame position) => Report(position);

        private void Report(in CellFrame position)
        {
            Positions++;
            rig.ClientPosition(position);
        }
    }

    [RequiresDatsFact]
    public void A_target_thirty_metres_away_is_walked_to_by_the_clients_own_body_when_the_server_sends_only_the_MoveToObject()
    {
        // Live round 3: ACE's approach is its MoveToObject F74C and no F748 for the player; the client's physics carries it out and reports its positions. Headless, the hunt's own body (HuntFollow) does.
        var dats = datsFixture.Dats;
        ClientToScript? sink = null;
        HuntFollow? follow = null;
        using var rig = HuntScriptRig.Create(log =>
        {
            sink = new ClientToScript(null!);
            follow = new HuntFollow(dats, log, _ => sink);
            return new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Melee, Kills = 1, Seconds = 600 }, log, cell => HuntGround.TerrainHeight(dats, cell), endSession: false, follow: follow);
        });
        using var disposeFollow = follow;
        sink = new ClientToScript(rig);
        rig.ApproachSendsPositions = false;
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(sink.Positions > 10, "the client's body reported its positions: " + sink.Positions);
        Assert.True(scenario.ChargeRetries >= 1, "the first approach is cancelled at 15 m and retried from where the client's body stands");
        Assert.True(rig.Logged("the server moves the player: the hunt's own body follows"), "the body followed the MoveToObject");
        Assert.True(rig.Logged("the client is 3"), "the client's distance is logged at each send");
        Assert.False(rig.Logged("did not close in"), "no target excluded for a walk that did not close");
        Assert.True(rig.Logged("movement intents: controller bound"), "HuntFollow binds its walker's controller to IActionApi.Move right after OfflineWalker.Create (P5a S4)");
        Assert.True(rig.Logged("movement intents: controller unbound"), "and unbinds it when the hunt disposes the follower");
    }

    [Fact]
    public void Without_the_clients_own_body_a_MoveToObject_with_no_F748_moves_nothing_and_the_far_target_is_excluded()
    {
        // The live round 3 failure, kept as the control for the test above: the hunt measured the closure from a position that never moved.
        using var rig = HuntScriptRig.Create(Hunt());
        rig.ApproachSendsPositions = false;
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 30);

        rig.Run(300);

        Assert.True(rig.Logged("did not close in") || rig.Logged("no sequence began within"), "no body walked the MoveTo");
        Assert.Equal(0, rig.Scenario.Tally.Kills);
    }

    [Fact]
    public void A_target_near_the_edge_of_the_band_range_takes_several_retries()
    {
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 58);

        var status = rig.Run(1200);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.True(scenario.ChargeRetries >= 3, scenario.ChargeRetries.ToString());
    }

    [Fact]
    public void A_health_fall_under_thirty_percent_mid_fight_cancels_the_attack_and_returns_with_the_run_failed()
    {
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        rig.SetPlayerHealthLater(45, 4);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("retreat", scenario.Failure, StringComparison.Ordinal);
        Assert.True(scenario.Returned);
        Assert.Equal(0, scenario.Tally.Kills);
        Assert.False(rig.Session.Attack.Active);
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_forest_ground_is_accepted_the_log_names_it_and_a_belt_creature_is_never_its_target_3_1()
    {
        // M4 3.1 s0: the forest row is in use (--hunt-ground=forest). The scripted creature is a belt creature (Field Ursuin, wcid 7990, level 8), not in the forest table, so the run ends at the "nothing near" path
        // of every cell: the forest target rule is the belt's (nearest creature whose wcid is in the ground's table and whose table level is in its band), and the hunt attacks nothing.
        var options = new HuntOptions { Ground = HuntGroundChoice.Forest };
        using var rig = HuntScriptRig.Create(log => new HuntScenario(options, log, _ => 40f, options.GroundRow, endSession: false));
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);

        var status = rig.Run(1200);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.True(rig.Logged("ground Low North Forest Mix:"), "the log names the ground");
        Assert.True(rig.Logged($"{HuntGround.LowNorthForestMix.UsableCells.Count()} usable cell(s) of {HuntGround.LowNorthForestMix.Cells.Count}"));
        Assert.Contains("no creature of the table in the band 30-40", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Attacks);
        Assert.Equal(HuntScenario.MaxCells, scenario.CellsVisited);
        Assert.True(scenario.Returned);
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void On_foot_the_forest_hunt_walks_to_all_33_spots_in_the_loop_each_to_the_spots_point_and_says_the_expected_yield_first_M5_2_g3()
    {
        // D42 (m5-hunt-band-empty): the archer's journey hunt could fail on eight empty spots while 25 more stood unvisited. The forest's cap is the profile's 33, each walk goes to the encounter spot (the generator's corner,
        // 17 m from the cell's centre) and one INFO line before the walk says what to expect. The scripted rig holds no creature at all, so every spot is empty and the search ends by the acquire's own name.
        var options = new HuntOptions { Ground = HuntGroundChoice.Forest, Kills = 1, Seconds = 100000, Sell = false, BuyKits = 0 };
        var travel = new J2aRecordingTravel();
        using var rig = HuntScriptRig.Create(log => new HuntScenario(options with { Travel = travel }, log, _ => 40f, options.GroundRow, endSession: false) { SeekHeight = (_, _, _) => 40f }, quitAfter: 100000);
        travel.Rig = rig;

        var status = rig.Run(100000);

        var forest = HuntGround.LowNorthForestMix;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(33, rig.Scenario.CellLimit);
        Assert.Equal(33, rig.Scenario.CellsVisited); // not OnFootMaxCells' eight
        var walks = travel.Requests.Where(r => r.Purpose == TravelPurpose.Cell).ToArray();
        Assert.Equal(33, walks.Length);
        Assert.Equal(forest.Cells.Select(c => c.CellId), walks.Select(r => r.Destination.Location.Position.CellId)); // the loop's order
        Assert.Equal(forest.Cells.Select(c => new Vector2(c.StopX, c.StopY)), walks.Select(r => new Vector2(r.Destination.Location.Position.Local.X, r.Destination.Location.Position.Local.Y))); // the spots, not the centres
        Assert.All(walks, r => Assert.Equal(40.5f, r.Destination.Location.Position.Local.Z)); // the terrain height at the spot plus half a metre
        Assert.StartsWith("acquire: no creature of the table in the band 30-40 within 60 m of the player after 33 cell(s) (38 creatures are listed", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.True(rig.Logged("(cell 33 of at most 33)"), rig.Tail());
        Assert.True(rig.Logged("place loc at (168, 0, 40.5) (cell 1 of at most 33)"), rig.Tail()); // the first spot: cell 0x9FB50039's corner, not its centre (180, 12)

        var yield = rig.Log.Lines.Single(l => l.Contains("ground: expected yield before the walk", StringComparison.Ordinal));
        Assert.Contains("33 spot(s) of Low North Forest Mix to visit, 0.342 usable creature(s) a spot for the band 30-40 (13 of the 38 listed creatures; a camp counts once)", yield, StringComparison.Ordinal);
        Assert.EndsWith("the chance no spot holds one 0 (report-only)", yield, StringComparison.Ordinal);
        Assert.True(rig.LineAt("ground: expected yield before the walk") < rig.LineAt("ground: cell "), "the yield is said before the first walk");
    }

    [Fact]
    public void The_archers_expected_yield_is_0_167_a_spot_over_the_forest_and_its_hunt_walks_up_to_33_spots_M5_2_g3()
    {
        var options = new HuntOptions { Weapon = HuntWeapon.Missile, Ground = HuntGroundChoice.Forest, Kills = 1, Seconds = 100000, Sell = false, BuyKits = 0 };
        var travel = new J2aRecordingTravel();
        using var rig = HuntScriptRig.Create(log => new HuntScenario(options with { Travel = travel }, log, _ => 40f, options.GroundRow, endSession: false, template: FighterTemplate.Archer) { SeekHeight = (_, _, _) => 40f }, FighterTemplate.Archer.Name, quitAfter: 100000);
        travel.Rig = rig;

        Assert.True(rig.RunUntil(() => rig.Logged("ground: expected yield"), 600), rig.Tail());

        Assert.Equal(33, rig.Scenario.CellLimit);
        var yield = rig.Log.Lines.Single(l => l.Contains("ground: expected yield before the walk", StringComparison.Ordinal));
        Assert.Contains("33 spot(s) of Low North Forest Mix to visit, 0.167 usable creature(s) a spot for the band 20-30 (6 of the 33 listed creatures; a camp counts once), 5.51 over the 33 spot(s)", yield, StringComparison.Ordinal);
        Assert.Contains("the chance no spot holds one 0.0024", yield, StringComparison.Ordinal); // 0.833^33; with the old eight cells it was 0.23
    }

    [Fact]
    public void A_row_built_by_hand_without_spawn_masses_says_its_yield_is_not_known_and_keeps_the_hunts_own_cap_M5_2_g3()
    {
        var row = HuntGround.LowB with { Table = [new HuntCreature(7990, "Field Ursuin", 8)], CellCap = null };
        var travel = new J2aRecordingTravel();
        using var rig = HuntScriptRig.Create(log => new HuntScenario(new HuntOptions { Travel = travel }, log, _ => 40f, row, endSession: false), quitAfter: 100000);
        travel.Rig = rig;

        Assert.True(rig.RunUntil(() => rig.Logged("ground: expected yield"), 600), rig.Tail());

        Assert.Equal(HuntScenario.OnFootMaxCells, rig.Scenario.CellLimit); // no cap in the row: the hunt's own
        Assert.True(rig.Logged("ground: expected yield: not known (Low B belt has 8 spot(s) to visit but its table has no spawn mass); report-only"), rig.Tail());
    }

    [Fact]
    public void A_row_that_is_not_in_use_is_refused_and_the_options_parse()
    {
        var log = new RecordingLogSink();
        var unused = HuntGround.LowNorthForestMix with { InUse = false };
        var refused = Assert.Throws<ArgumentException>(() => new HuntScenario(new HuntOptions(), log, _ => 40f, unused));
        Assert.Contains("TODO(m4-hunt-ground-unused)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Low North Forest Mix", refused.Message, StringComparison.Ordinal);
        Assert.NotNull(new HuntScenario(new HuntOptions(), log, _ => 40f, HuntGround.LowNorthForestMix)); // the forest row itself is in use now
        var o = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=3", "--hunt-seconds=90", "--hunt-weapon=missile"]));
        Assert.Empty(o.Errors);
        Assert.Equal((3, 90.0, HuntWeapon.Missile), (o.Kills, o.Seconds, o.Weapon));
        Assert.True(o.OpenCorpse, "--hunt-open-corpse is on by default");
        Assert.False(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-open-corpse=off"])).OpenCorpse);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-open-corpse=maybe"])).Errors);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=0"])).Errors);
        Assert.NotEmpty(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-weapon=spear"])).Errors);
        Assert.Matches(Verdict, new HuntScenario(new HuntOptions(), log, _ => 40f).VerdictFields());
    }
}
