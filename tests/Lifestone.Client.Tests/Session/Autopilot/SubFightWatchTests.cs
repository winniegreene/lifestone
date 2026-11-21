using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 16 E3.1 (docs/automation-engine.md E3, <c>TODO(engine-guard-over-subfight)</c>): one healer in a quest's sub-fight, and the way home kept there too. Over the scripted ACE of <see cref="AutopilotRig"/> with the
/// climber's start quest in the Trip state (the rig of <c>ErrandLapseTests</c>), the quest station's run scripted around a REAL <see cref="FightScenario"/> on the scripted caster of the fight's own tests
/// (<see cref="FightManaTests.Rig"/>, built over the autopilot's world): the vitals guard does not hold a running sub-fight (it is stepped every frame at a health under the guard's line and heals by its own emergency);
/// the way out's reserve is armed in it and she leaves by Lifestone Recall at the line; a lost race and the retreat line end it by name; the health floor holds it for the keeper's heal only with no hostile in reach; the
/// guard still holds the station's walk, a fight whose emergency is off, and a fight that is not the mage's. A leave keeps the quest callable as a ground fight's retreat goes back out: a hunt left for mana is called again
/// from home with what she holds, and the second leave on the fight line in a cycle gives the quest up for the run by name (the ground fight's bound).
/// </summary>
public sealed class SubFightWatchTests(ITestOutputHelper output)
{
    private const string Key = "subfight-test";
    private const string Slave = "Tusker Slave";
    private const string Named = "failed:quest:" + Key + ":retreat:";
    private static readonly string[] Drinks = ["Potion", "Draught", "Tonic", "Tincture", "Elixir", "Brew", "Stew"];

    private static Quest Kills() => new()
    {
        Key = Key,
        Name = "A kill step",
        Giver = null,
        Start = new QuestPlace(0xA9B40019, Vector3.Zero, "Holtburg"),
        Steps = [TrophyQuestTests.Step(0, QuestStepKind.HuntUntil, "the motes") with { Item = new QuestThing(6353, "Pyreal Mote"), Count = 16, Ground = "tusker-lodge" }],
        Done = new QuestDone(null, null, new QuestThing(6353, "Pyreal Mote")),
        TimerSeconds = null,
        Sources = ["the test's own record"],
        Ladder = new QuestLadder(10, 30, true),
    };

    /// <summary>The quest station's run, scripted: it walks (no fight shown) until <see cref="Walking"/> is false, then steps the real fight inside it and shows it (<see cref="IStationSubFight"/>).</summary>
    private sealed class QuestFight(string name, ApWorld w, FightManaTests.Rig rig) : IStationRun, IStationSubFight, IStationWorking, IDisposable
    {
        private ScenarioStatus _status = ScenarioStatus.Running;

        public bool Walking { get; set; }

        public FightScenario? Fight { get; private set; }

        public uint Tusker { get; private set; }

        public int FightSteps { get; private set; }

        public int WalkSteps { get; private set; }

        public string Name => name;

        public string Stage => Walking ? "walk" : "fight";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _status == ScenarioStatus.Succeeded;

        public IStoppableFight? SubFight => _status == ScenarioStatus.Running && !Walking ? Fight : null;

        public double? WorkedAt => _status == ScenarioStatus.Running ? w.Now : null;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_status != ScenarioStatus.Running)
            {
                return _status;
            }

            if (Walking)
            {
                WalkSteps++;
                return ScenarioStatus.Running;
            }

            if (Fight is null)
            {
                w.Trip.Vendor.StandAt(FightManaTests.Den, new Vector3(FightManaTests.PlayerX, FightManaTests.PlayerY, 30)); // the fight rig's floor: the den, the tusker 20 m up the corridor
                Tusker = rig.AddTusker();
                Fight = rig.Scenario();
            }

            FightSteps++;
            var status = Fight.Step(session, now);
            return status == ScenarioStatus.Running ? status : _status = status;
        }

        public string VerdictFields() => $"key={Key} from=home steps=0/1 done=no xp=+0 reward_line=0 timer=none ok=no fail=none";

        public void Dispose() => Fight?.Dispose();
    }

    private sealed class World
    {
        public required ApWorld W { get; init; }

        public required AutopilotScenario Autopilot { get; init; }

        public required FightManaTests.Rig Rig { get; init; }

        public QuestFight? Run { get; set; }

        public List<uint> Casts { get; } = [];

        public bool Walk { get; set; }

        /// <summary>True: an appraisal asked is answered (the armour ledger's reads of her worn pieces: unanswered, each holds a lane for its 5 s).</summary>
        public bool AnswersIdentify { get; set; }

        public int Seen { get; set; }
    }

    /// <summary>The climber at the Lodge's level with the start quest due, the fight rig's caster over the same world, Lifestone Recall in her book and the table (50 mana, as the way out's own tests).</summary>
    private World Climber(FightOptions? fight = null, bool walk = false, bool keeper = false)
    {
        FightManaTests.Rig? rig = null;
        World? world = null;
        var quest = Kills();
        var timers = new QuestTimers("Winnie");
        var options = new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key };
        var (w, _) = TrophyQuestTests.MakeClimber(
            output,
            tusk: false,
            spec => world!.Run = new QuestFight(spec.Name, world.W, rig!) { Walking = world.Walk },
            timers,
            prepare: made =>
            {
                // the caster's description replaces the player's: the pack the logistics world gave her is put back, without its drinks (the scripted caster answers only its own potion stacks, and these fights are about her spells)
                var state = made.Trip.View.State;
                var held = state.Self.Inventory.Pack.Select(e => e.Guid).Where(g => !state.Objects.TryGet(g, out var o) || !Drinks.Any(d => (o.Name ?? string.Empty).Contains(d, StringComparison.Ordinal))).ToList();
                rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true, fight: fight, world: made.Trip.Vendor.R) { HitsToKill = 400, TuskerFlags = WorldObjectTable.AttackableFlag };
                foreach (var guid in held)
                {
                    made.Trip.Vendor.Put(guid);
                }

                made.Trip.Vendor.R.Tick();
            },
            options: options);
        rig!.Caster.HealthPerCast = 18;
        rig.Caster.AnswerWields = true;
        w.Spells = new Dictionary<uint, SpellInfo>(rig.Caster.Table)
        {
            [RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Lifestone Recall", BaseMana = 50 },
        };
        if (keeper)
        {
            w.KeeperMaker = c => new VitalsKeeper(w.Trip.View, c.Spells, new VitalsOptions(), c.Log);

            // with a keeper a ladder errand starts ready: an enchantment stands on her (AutopilotScenario.ErrandWaitsForBuffs)
            var buff = BuffProfiles.Climber.LineNamed("Focus Self")!.Spells[5];
            w.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)buff.Id, 2, 2000, 0, buff.Power, 0.0, 3600, w.Trip.Vendor.State.Self.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
            w.Trip.Vendor.R.Tick();
        }

        w.Trip.Vendor.State.Self.Spellbook.Learn(RecallHome.LifestoneRecallSpell);
        var autopilot = w.Autopilot(options);
        autopilot.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4);
        autopilot.TrophyQuests = [quest];
        world = new World { W = w, Autopilot = autopilot, Rig = rig, Walk = walk };
        rig.Caster.TargetedCastHook = (spell, _) =>
        {
            world.Casts.Add(spell);
            return false;
        };
        rig.Caster.CastHook = spell =>
        {
            world.Casts.Add(spell);
            return false;
        };
        return world;
    }

    /// <summary>One frame's script: the caster answers what was sent.</summary>
    private static void Pump(World x)
    {
        var sends = x.W.Trip.Vendor.H.Net.Sends;
        while (x.Seen < sends.Count)
        {
            var send = sends[x.Seen++];
            if (x.AnswersIdentify && send.Kind == FakeSendKind.Identify)
            {
                x.W.Trip.Vendor.R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, send.Guid, new AceAppraisal { Success = true }));
            }
        }

        x.Rig.Caster.Pump();
        x.W.Trip.Vendor.R.Tick();
    }

    private static void Hit(World x, uint damage) => x.W.Trip.Vendor.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, Slave, 4, 0.1, damage, 4, false, 0));

    private static void TuskerHealth(World x, float fraction)
    {
        var h = x.W.Trip.Vendor.H;
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(x.Run!.Tusker, fraction));
    }

    /// <summary>Runs to the quest station's fight with its first bolt answered.</summary>
    private void ToTheFight(World x)
    {
        x.W.Run(x.Autopilot, a => a.State == AutopilotState.Trip && x.Run is { Fight: not null } && x.Rig.BoltAnswers.Any(), 20000, _ => Pump(x));
        Assert.True(x.Autopilot.State == AutopilotState.Trip && x.Run is { Fight: not null }, $"state {x.Autopilot.State}, stop {x.Autopilot.StopName}: {string.Join(" | ", x.W.Lines.TakeLast(12))}");
    }

    /// <summary>The fight on a copy of its ground whose retreat line is <paramref name="percent"/> (the den's own is 0, never: D91).</summary>
    private static FightOptions OnALine(int percent) => new() { GroundForTest = GroundCatalog.Get(FightOptions.DefaultGround) with { FightRetreatHealthPercent = percent } };

    private static string Tail(World x, int from) => string.Join(" | ", x.W.Lines.Skip(from).Where(l => l.StartsWith("survival", StringComparison.Ordinal) || l.StartsWith("guard:", StringComparison.Ordinal) || l.StartsWith("retreat", StringComparison.Ordinal) || l.StartsWith("way out", StringComparison.Ordinal) || l.StartsWith("emergency", StringComparison.Ordinal)).TakeLast(14));

    private static bool GuardLine(string l) => l.StartsWith("guard: trip:", StringComparison.Ordinal);

    [Fact]
    public void The_vitals_guard_does_not_hold_a_running_sub_fight_which_is_stepped_every_frame_and_heals_by_its_own_emergency()
    {
        var x = Climber();
        ToTheFight(x);
        var from = x.W.Lines.Count();
        Hit(x, 3);
        TuskerHealth(x, 1f);
        x.Rig.Caster.SetHealth(80); // 47% of her 170: under the guard's line (the next test's state) and under the fight's own emergency line (60%)
        var steps = x.Run!.FightSteps;
        var casts = x.Casts.Count;

        x.W.Run(x.Autopilot, null, 40, _ =>
        {
            Hit(x, 3);
            Pump(x);
        });

        Assert.Equal(40, x.Run.FightSteps - steps); // stepped every frame: nothing froze it
        Assert.Equal(AutopilotState.Trip, x.Autopilot.State);
        Assert.Equal((1, 0, 0, 0), (x.Autopilot.SubFightsKept, x.Autopilot.GuardOverSubFights, x.Autopilot.SurvivalHolds, x.Autopilot.SubFightLeaves));
        Assert.DoesNotContain(x.W.Lines, GuardLine); // no heal pick, no cast and no hold of the guard's in the trip
        Assert.DoesNotContain(x.W.Lines, l => l.Contains("guard-over-subfight", StringComparison.Ordinal));
        Assert.DoesNotContain(x.W.Lines, l => l.StartsWith("survival: trip: wait:", StringComparison.Ordinal));
        var stood = Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: stand-down: the quest station steps a fight on " + FightOptions.DefaultGround + " that keeps her bars (its emergency line is ", StringComparison.Ordinal));
        Assert.Contains("the vitals guard and the layer's wait stand down over it as over a ground fight", stood, StringComparison.Ordinal);
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("emergency: Drain Health Other ", StringComparison.Ordinal) && l.Contains(" at health 80/170 ", StringComparison.Ordinal)); // the fight's own emergency chose the heal
        Assert.Contains(x.Casts.Skip(casts), c => ScriptedCaster.DrainLine.Spells.Any(s => s.Id == c) || VitalsMeans.HealSelf.Contains(c));
        Assert.True(x.Rig.Caster.Health > 80, $"health {x.Rig.Caster.Health}: {Tail(x, from)}");
    }

    [Fact]
    public void A_sub_fight_whose_emergency_is_off_is_still_the_guards_and_the_evidence_line_says_so_once()
    {
        // The state that remains where the guard acts above a fight: no heal decider of the fight's own would answer (--fight-emergency-floor 0), so the stand-down does not apply and E3.0's line is its evidence.
        var x = Climber(new FightOptions { EmergencyFloorPercent = 0 });
        ToTheFight(x);
        var from = x.W.Lines.Count();
        Assert.False(x.Run!.Fight!.EmergencyOn);
        Hit(x, 3);
        TuskerHealth(x, 1f);
        x.Rig.Caster.SetHealth(80);

        x.W.Run(x.Autopilot, a => a.GuardOverSubFights > 0, 40, _ => Pump(x));
        var steps = x.Run.FightSteps;
        x.W.Run(x.Autopilot, null, 8, _ =>
        {
            x.Rig.Caster.SetHealth(80); // held under the line: the guard goes on holding
            Pump(x);
        });

        Assert.True(x.Autopilot.GuardOverSubFights == 1, Tail(x, from));
        Assert.Equal(0, x.Autopilot.SubFightsKept);
        Assert.True(x.Autopilot.SurvivalHolds >= 1);
        Assert.Contains(x.W.Lines.Skip(from), GuardLine); // 80 of 170 is under the guard's line: it picks and casts
        Assert.Single(x.W.Lines, l => l.Contains("survival-shadow: guard-over-subfight: ", StringComparison.Ordinal));
        Assert.Equal(steps, x.Run.FightSteps); // not stepped while the guard holds
        Assert.DoesNotContain(x.W.Lines, l => l.StartsWith("survival: trip: stand-down: the quest station steps a fight", StringComparison.Ordinal));
    }

    [Fact]
    public void The_guard_still_holds_the_stations_walk()
    {
        var x = Climber(walk: true);
        x.W.Run(x.Autopilot, a => a.State == AutopilotState.Trip && x.Run is { WalkSteps: > 2 }, 20000, _ => Pump(x));
        Assert.True(x.Autopilot.State == AutopilotState.Trip && x.Run is { WalkSteps: > 2 }, $"state {x.Autopilot.State}");
        var from = x.W.Lines.Count();
        Assert.Null(x.Run!.SubFight); // the station walks: no fight is shown
        x.Rig.Caster.SetHealth(80);
        x.W.Run(x.Autopilot, a => a.SurvivalHolds > 0, 40, _ => Pump(x));
        var walked = x.Run.WalkSteps;

        x.W.Run(x.Autopilot, null, 4, _ =>
        {
            x.Rig.Caster.SetHealth(80);
            Pump(x);
        });

        Assert.True(x.Autopilot.SurvivalHolds == 1, Tail(x, from));
        Assert.Equal(walked, x.Run.WalkSteps); // the walk is held while the guard heals
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("survival: trip: wait: heal: the vitals guard's heal pick is under way; the quest station is held", StringComparison.Ordinal));
        Assert.Contains(x.W.Lines.Skip(from), GuardLine);
        Assert.Equal((0, 0), (x.Autopilot.SubFightsKept, x.Autopilot.GuardOverSubFights));
    }

    [Fact]
    public void The_way_outs_reserve_is_armed_in_a_sub_fight_a_heal_that_would_spend_the_recall_is_refused_and_she_leaves_by_the_recall()
    {
        var x = Climber();
        var arrows = new List<AutopilotTransition>();
        x.Autopilot.StateChanged = arrows.Add;
        ToTheFight(x);
        var from = x.W.Lines.Count();

        // The line is the outer loop's own sum, armed in the fight each frame as in a ground fight: Lifestone Recall needs 86 here (the station's 20% of her 426 mana, over the spell's 50), and one Drain Health Other I 10.
        Assert.Contains(x.W.Lines, l => l.StartsWith("way out: armed: line=96 (Lifestone Recall 86 + Drain Health Other I 10)", StringComparison.Ordinal));
        Hit(x, 3);
        TuskerHealth(x, 1f);
        x.Rig.Caster.ManaRegenPerSecond = 0;
        x.Rig.Caster.StaminaRegenPerSecond = 0;
        x.Rig.Caster.SetVitals(100, 0); // over the line by 4: a heal dearer than that spends into the reserve
        x.Rig.Caster.SetHealth(93); // 55% of 170: under the fight's emergency line

        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip, 120, _ =>
        {
            Hit(x, 3);
            Pump(x);
        });

        Assert.True(x.Autopilot.State == AutopilotState.Travel, $"state {x.Autopilot.State}: {Tail(x, from)}");
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("way out: the heal is refused: mana ", StringComparison.Ordinal) && l.Contains("would leave the recall unpaid (line=96 (Lifestone Recall 86 + Drain Health Other I 10))", StringComparison.Ordinal));
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("retreat: mana: decide mana=", StringComparison.Ordinal) && l.EndsWith("-> retreat (mana-line)", StringComparison.Ordinal));
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("retreat: mana: check before the recall: mana ", StringComparison.Ordinal) && l.Contains(" of 86 needed", StringComparison.Ordinal));
        var leave = Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: leave: mana-line: mana-line at mana ", StringComparison.Ordinal));
        Assert.Contains("(sub-fight)", leave, StringComparison.Ordinal);
        Assert.EndsWith($"the quest station ends by name {Named}mana-line; she goes home", leave, StringComparison.Ordinal);
        var arrow = arrows.Last();
        Assert.Equal((AutopilotState.Trip, AutopilotState.Travel), (arrow.From, arrow.To));
        Assert.StartsWith($"retreat: {Named}mana-line: mana-line at mana ", arrow.Why, StringComparison.Ordinal);
        Assert.Equal(RecallHomeStation.StationName, Kinds(x.W).Last()); // by Lifestone Recall, by name
        Assert.Equal((1, 1, 1, 0), (x.Autopilot.SubFightLeaves, x.Autopilot.SurvivalLeaves, x.Autopilot.Retreats, x.Autopilot.GuardOverSubFights));
        Assert.DoesNotContain(x.W.Lines, GuardLine);
        Assert.True(x.Rig.Mana >= 86, $"mana {x.Rig.Mana}: the recall is still paid when she leaves");
    }

    [Fact]
    public void A_lost_race_ends_a_sub_fight_by_name_by_lifestone_recall()
    {
        var x = Climber(OnALine(30)); // a ground whose line is on (the retreat rule is off where it is 0), and her health stays over it: only the race leaves
        var arrows = new List<AutopilotTransition>();
        x.Autopilot.StateChanged = arrows.Add;
        ToTheFight(x);
        var from = x.W.Lines.Count();
        TuskerHealth(x, 1f);

        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip, 120, _ =>
        {
            Hit(x, 10); // 40 a second: more than any heal she can sustain
            x.Rig.Caster.SetHealth(70); // 41% of 170, over the 30% line
            Pump(x);
        });

        Assert.True(x.Autopilot.State == AutopilotState.Travel, $"state {x.Autopilot.State}: {Tail(x, from)}");
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("retreat: decide health=41.2% ", StringComparison.Ordinal) && l.Contains(" losing-race -> retreat (sub-fight; why losing-race; ", StringComparison.Ordinal));
        var leave = Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: leave: losing-race: losing-race at health 70/170 (sub-fight): ", StringComparison.Ordinal));
        Assert.EndsWith($"the quest station ends by name {Named}losing-race; she goes home", leave, StringComparison.Ordinal);
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("survival: trip: leave: losing-race: Lifestone Recall, check before: ", StringComparison.Ordinal));
        var arrow = arrows.Last();
        Assert.Equal((AutopilotState.Trip, AutopilotState.Travel), (arrow.From, arrow.To));
        Assert.StartsWith($"retreat: {Named}losing-race: ", arrow.Why, StringComparison.Ordinal);
        Assert.Equal(RecallHomeStation.StationName, Kinds(x.W).Last());
        Assert.Equal((1, 1, 1), (x.Autopilot.SubFightLeaves, x.Autopilot.SurvivalLeaves, x.Autopilot.Retreats));
        Assert.DoesNotContain(x.W.Lines, l => GuardLine(l) && !l.Contains("holds off", StringComparison.Ordinal));
    }

    [Fact]
    public void The_retreat_line_of_the_sub_fights_own_ground_ends_it_by_name()
    {
        var x = Climber(OnALine(50));
        var arrows = new List<AutopilotTransition>();
        x.Autopilot.StateChanged = arrows.Add;
        ToTheFight(x);
        var from = x.W.Lines.Count();
        TuskerHealth(x, 1f);
        x.Rig.Caster.ManaRegenPerSecond = 0;
        x.Rig.Caster.StaminaRegenPerSecond = 0;
        x.Rig.Caster.SetVitals(60, 0); // under Lifestone Recall's 86: the race has no road and the way out is gone, so the line's own arm decides

        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip, 200, _ =>
        {
            Hit(x, 10);
            x.Rig.Caster.SetHealth(70); // 41%: under the 50% line of the fight's own ground
            // M6 stamina dance (b): the mana is held at 60 each tick, as the health is, so the heal stays payable whatever the bolts' Mana Conversion rolls take. Before, the mana ran down from 60 with the bolts, and
            // whether the heal was still payable at the end hung on the scripted caster's one seeded roll stream (ScriptedCaster.cs:116-117): the old refill's Revitalize Self VI before this block (at 213 of 275 stamina,
            // 62 missing, under VI's top roll of 160; rule b drops it) took one roll, and without it the bolts' rolls shifted, the mana fell under the heal's cost and the retreat came as nothing-payable.
            x.Rig.Caster.SetVitals(60, 0);
            Pump(x);
        });

        Assert.True(x.Autopilot.State == AutopilotState.Travel, $"state {x.Autopilot.State}: {Tail(x, from)}");
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("retreat: decide health=41.2% ", StringComparison.Ordinal) && l.Contains(" line=50% -> retreat (sub-fight; why incoming-beats-heal; line: " + FightOptions.DefaultGround + "'s fight.retreat_health_percent", StringComparison.Ordinal)); // the fight's own ground, not the rung's
        var leave = Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: leave: incoming-beats-heal: incoming-beats-heal at health 70/170 under the 50% line (sub-fight)", StringComparison.Ordinal));
        Assert.EndsWith($"the quest station ends by name {Named}incoming-beats-heal; she goes home", leave, StringComparison.Ordinal);
        var arrow = arrows.Last();
        Assert.Equal((AutopilotState.Trip, AutopilotState.Travel), (arrow.From, arrow.To));
        Assert.StartsWith($"retreat: {Named}incoming-beats-heal: ", arrow.Why, StringComparison.Ordinal);
        Assert.Equal((1, 1, 1), (x.Autopilot.SubFightLeaves, x.Autopilot.SurvivalLeaves, x.Autopilot.Retreats));
        Assert.DoesNotContain(x.W.Lines, l => l.Contains(AutopilotScenario.RetreatAgainPrefix, StringComparison.Ordinal)); // never toward retreat:again: she was not on the rung's ground
    }

    [Fact]
    public void The_health_floor_holds_a_sub_fight_for_the_keepers_heal_only_with_no_hostile_in_reach()
    {
        static bool UnderAttack(string l) => l.StartsWith("survival: trip: sub-fight: health ", StringComparison.Ordinal) && l.Contains(" of 170 is under the 50% floor, and a hostile is in reach (", StringComparison.Ordinal);

        var x = Climber(OnALine(50), keeper: true);
        x.AnswersIdentify = true;
        ToTheFight(x);
        var from = x.W.Lines.Count();
        TuskerHealth(x, 1f);

        // A hostile on her, her health under the floor (the ground's 50%): the fight is not held; said once.
        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip || x.W.Lines.Skip(from).Any(l => UnderAttack(l)), 60, _ =>
        {
            Hit(x, 1); // 4 a second: under the heal she can pay, so the retreat rule holds
            x.Rig.Caster.SetHealth(60); // 35% of 170, and 46% after one heal of the fight's own: under the floor either way
            Pump(x);
        });
        Assert.True(x.Autopilot.State == AutopilotState.Trip && x.Autopilot.SubFightFloorHolds == 0, $"state {x.Autopilot.State}: {Tail(x, from)}");
        Assert.Single(x.W.Lines, l => UnderAttack(l));

        // No hit for the watch's quiet seconds: the hold, the keeper's pass to the share a fight starts at, and the fight goes on where it stopped.
        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip || a.SubFightFloorHolds > 0, 200, _ =>
        {
            x.Rig.Caster.SetHealth(60); // 35% of 170, and 46% after one heal of the fight's own: under the floor either way
            Pump(x);
        });
        Assert.True(x.Autopilot.State == AutopilotState.Trip && x.Autopilot.SubFightFloorHolds == 1, $"state {x.Autopilot.State}: {Tail(x, from)}");
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("survival: trip: wait: heal: health ", StringComparison.Ordinal) && l.Contains(" of 170 is under the 50% floor of the station's fight with no hostile in reach: the keeper's heal in place to 80%", StringComparison.Ordinal) && l.Contains("the quest station is held", StringComparison.Ordinal));
        var steps = x.Run!.FightSteps;
        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip || x.W.Lines.Skip(from).Any(l => l.StartsWith("survival: trip: go: ", StringComparison.Ordinal)), 400, _ =>
        {
            x.Rig.Caster.SetHealth(Math.Min(170, x.Rig.Caster.Health + 4)); // the keeper rests her in peace: the server's regeneration, scripted
            Pump(x);
        });
        var held = x.Run.FightSteps - steps;
        x.W.Run(x.Autopilot, null, 8, _ => Pump(x));

        Assert.True(held <= 1, $"the fight was stepped {held} times while the keeper healed: {Tail(x, from)}");
        Assert.True(x.Run.FightSteps - steps >= 8, Tail(x, from)); // the same fight goes on
        Assert.Contains(x.W.Lines.Skip(from), l => l.StartsWith("survival: trip: go: ", StringComparison.Ordinal) && l.Contains("held ", StringComparison.Ordinal));
        Assert.True(x.Rig.Caster.Health >= 136, $"health {x.Rig.Caster.Health} of 170: {Tail(x, from)}"); // 80%
        Assert.Equal((AutopilotState.Trip, 1, 0), (x.Autopilot.State, x.Autopilot.SurvivalHolds, x.Autopilot.GuardOverSubFights));
        Assert.DoesNotContain(x.W.Lines, GuardLine); // the keeper healed; the guard did not act above the fight
    }

    /// <summary>At home after a leave the server's bars are back (the retreat's keeper's pass has no keeper in this world); in the trip nothing is touched.</summary>
    private static void HomeBars(World x)
    {
        if (x.Autopilot.State != AutopilotState.Trip)
        {
            x.Rig.Caster.SetVitals(400, 200);
            x.Rig.Caster.SetHealth(170);
        }
    }

    private static int Held(World x, uint wcid) => x.W.Trip.View.State.Self.Inventory.Pack.Count(e => x.W.Trip.View.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

    [Fact]
    public void A_hunt_left_for_mana_is_called_again_from_home_after_the_keepers_pass_and_what_she_holds_is_kept()
    {
        var x = Climber();
        var arrows = new List<AutopilotTransition>();
        x.Autopilot.StateChanged = arrows.Add;
        ToTheFight(x);
        var first = x.Run!;
        for (var i = 0; i < 5; i++)
        {
            x.W.Trip.Own("Pyreal Mote", 6353, VendorRig.MiscType, 1); // what the hunt has gained so far
        }

        Hit(x, 3);
        TuskerHealth(x, 1f);
        x.Rig.Caster.ManaRegenPerSecond = 0;
        x.Rig.Caster.StaminaRegenPerSecond = 0;
        x.Rig.Caster.SetVitals(100, 0);
        x.Rig.Caster.SetHealth(93);
        x.W.Run(x.Autopilot, a => a.State != AutopilotState.Trip, 120, _ =>
        {
            Hit(x, 3);
            Pump(x);
        });
        Assert.True(x.Autopilot.State == AutopilotState.Travel && x.Autopilot.SubFightLeaves == 1, $"state {x.Autopilot.State}: {Tail(x, 0)}");
        var left = arrows.Count;

        x.W.Run(x.Autopilot, a => a.StopName is not null || (a.State == AutopilotState.Trip && !ReferenceEquals(x.Run, first) && x.Run is { Fight: not null }), 4000, _ =>
        {
            HomeBars(x);
            Pump(x);
        });

        Assert.True(x.Autopilot.State == AutopilotState.Trip && !ReferenceEquals(x.Run, first), $"state {x.Autopilot.State}, stop {x.Autopilot.StopName}: {string.Join(" | ", arrows.Skip(left - 1).Select(a => $"{a.From}->{a.To}: {a.Why}"))}");
        Assert.Contains(x.W.Lines, l => l.StartsWith($"autopilot quest: key={Key}: the leave of its fight (mana-line) does not fail it for the run, as a retreat from a ground fight goes home and back out: it is called again before she next leaves home, after the keeper's pass and her buffs; a leave for mana is not counted", StringComparison.Ordinal));
        Assert.Contains(x.W.Lines, l => l.StartsWith($"autopilot quest: key={Key}: ", StringComparison.Ordinal) && l.Contains($"the quest {Key} again after the leave of its fight: it goes on from what she holds", StringComparison.Ordinal));
        Assert.Equal(2, Kinds(x.W).Count(k => k == "quest")); // the same quest, called again this run
        var after = arrows.Skip(left - 1).ToList();
        Assert.Equal(AutopilotState.Travel, after[0].To); // the leave: Lifestone Recall
        Assert.Equal(AutopilotState.Upkeep, after[1].To); // home: the retreat's keeper's pass
        Assert.StartsWith("retreat: home (", after[1].Why, StringComparison.Ordinal);
        Assert.Equal(AutopilotState.KnownState, after[2].To);
        Assert.Equal(AutopilotState.Trip, after[^1].To); // and the quest again, from home
        Assert.Contains("again after the leave of its fight", after[^1].Why, StringComparison.Ordinal);
        x.W.Run(x.Autopilot, null, 3, _ => Pump(x)); // the second fight's first frames: the layer stands down over it too
        Assert.Equal(5, Held(x, 6353)); // what the hunt gained is still hers: the step counts it (QuestStationLapseTests: a hunt-until goes on from what she holds)
        Assert.Equal((2, 1, 1), (x.Autopilot.SubFightsKept, x.Autopilot.SubFightLeaves, x.Autopilot.Retreats));
        Assert.Null(x.Autopilot.StopName);
        Assert.DoesNotContain(x.W.Lines, l => l.Contains("given up for the run by name", StringComparison.Ordinal));
    }

    [Fact]
    public void The_second_leave_on_the_fight_line_in_a_cycle_gives_the_quest_up_for_the_run_by_name_as_a_ground_is_given_up()
    {
        var x = Climber(OnALine(50));
        ToTheFight(x);
        TuskerHealth(x, 1f);
        x.Rig.Caster.ManaRegenPerSecond = 0;
        x.Rig.Caster.StaminaRegenPerSecond = 0;

        // Under the 50% line of the fight's ground with the incoming over the heal she can pay, the recall unpaid: the line's own arm leaves, each time she is in the quest's fight.
        void Beat()
        {
            if (x.Autopilot.State == AutopilotState.Trip && x.Run is { Fight: not null, FightSteps: > 4 })
            {
                x.Rig.Caster.SetVitals(60, 0);
                x.Rig.Caster.SetHealth(70);
                Hit(x, 10);
            }
            else
            {
                HomeBars(x);
            }

            Pump(x);
        }

        x.W.Run(x.Autopilot, a => a.SubFightLeaves == 1, 400, _ => Beat());
        Assert.True(x.Autopilot.SubFightLeaves == 1, $"state {x.Autopilot.State}: {Tail(x, 0)}");
        Assert.Contains(x.W.Lines, l => l.StartsWith($"autopilot quest: key={Key}: the leave of its fight (incoming-beats-heal) does not fail it for the run", StringComparison.Ordinal) && l.Contains("leave 1 of 1 on the fight line for this quest in cycle ", StringComparison.Ordinal));
        Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: leave: incoming-beats-heal: ", StringComparison.Ordinal) && l.EndsWith($"the quest station ends by name {Named}incoming-beats-heal; she goes home", StringComparison.Ordinal));

        x.W.Run(x.Autopilot, a => a.StopName is not null || a.SubFightLeaves == 2, 6000, _ => Beat());
        Assert.True(x.Autopilot.SubFightLeaves == 2, $"state {x.Autopilot.State}, stop {x.Autopilot.StopName}: {Tail(x, 0)}");
        Assert.Contains(x.W.Lines, l => l.StartsWith($"autopilot quest: key={Key}: the leave 2 of its fight on the fight line in cycle ", StringComparison.Ordinal) && l.Contains("the quest is given up for the run by name, the ground fight's bound (retreat:again:<ground> at the retreat 2 from one ground in one cycle)", StringComparison.Ordinal));
        Assert.Single(x.W.Lines, l => l.StartsWith("survival: trip: leave: incoming-beats-heal: ", StringComparison.Ordinal) && l.EndsWith($"the quest station ends by name {Named}again:incoming-beats-heal; she goes home", StringComparison.Ordinal));

        // Given up: home, and the ladder goes on without the quest (its fight on the rung's ground), with no third call and no stop of the run.
        x.W.Run(x.Autopilot, a => a.StopName is not null || a.State == AutopilotState.Fight, 6000, _ => Beat());
        Assert.True(x.Autopilot.State == AutopilotState.Fight && x.Autopilot.StopName is null, $"state {x.Autopilot.State}, stop {x.Autopilot.StopName}");
        Assert.Equal(2, Kinds(x.W).Count(k => k == "quest"));
        Assert.Equal((2, 2), (x.Autopilot.SubFightLeaves, x.Autopilot.Retreats));
        Assert.DoesNotContain(x.W.Lines, l => l.Contains(AutopilotScenario.RetreatAgainPrefix + x.W.Ground.Key, StringComparison.Ordinal)); // the rung's own count is untouched
    }
}
