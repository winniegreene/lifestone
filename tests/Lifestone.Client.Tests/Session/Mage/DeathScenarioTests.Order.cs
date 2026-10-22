using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death loop's new order (M4 3.1 d1, D20 and D22; <see cref="DeathStages.Rebuff"/>, <see cref="DeathStages.Fight"/>) over the same scripted ACE as the rest of this class, with a scripted inner fight scenario (<see cref="ScriptedFight"/>, made by the
/// scenario's <see cref="FightLegFactory"/> seam): the stage order with the rebuff and the fight on and with each off (both off is M4 2.2's loop), the plain kit for the regear, the inner scenario's kills counted, a death during the rebuff or the fight failing the
/// leg with the stage named, the judged measures frozen against a fight with blows, the corpse's own listing classified (a decoy-only one recovered, one that names a gear item, <c>--death-decoys off</c>) and the decoy check of the gear stage (against the offline
/// stand-in). The regear is not scripted on the rig (it has no inventory): the rig runs leave the regear flag out, and the stand-in test runs it.
/// </summary>
public sealed partial class DeathScenarioTests
{
    private static DeathStages Whole => Trip | DeathStages.Rebuff | DeathStages.Fight;

    /// <summary>What the factory was asked for, and the scripted inner scenario it made.</summary>
    private sealed class ScriptedHost
    {
        public FakeWalk Walk { get; } = new();

        public ScriptedFight? Inner { get; private set; }

        public int Made { get; private set; }

        public GearKit? Kit { get; private set; }

        public BuffOptions? Buff { get; private set; }

        public FightOptions? Fight { get; private set; }

        public IFightWalk? PassedWalk { get; private set; }

        public FightStages Stages { get; private set; }

        public bool EndSession { get; private set; }

        /// <summary>Called on each step of the scripted fight stage, with the step's number from 1.</summary>
        public Action<ScriptedFight, int>? OnFight { get; set; }

        /// <summary>Called on each step of the scripted buff stage.</summary>
        public Action<ScriptedFight, int>? OnBuff { get; set; }

        public bool AllUp { get; set; } = true;

        /// <summary>When above 0, the scripted fight fails (<see cref="FailText"/>) at this step of its fight stage.</summary>
        public int FailInFightAt { get; set; }

        public string FailText { get; set; } = "the scripted fight failed";

        public string BuffsNote { get; set; } = "topped-up:24";

        public IFightLeg Make(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, BuffOptions buff, FightOptions fight, IFightWalk? walk, ILogSink log, double arrivalSeconds, bool endSession, FightStages stages)
        {
            Made++;
            (Kit, Buff, Fight, PassedWalk, Stages, EndSession) = (kit, buff, fight, walk, stages, endSession);
            return Inner = new ScriptedFight(this, fight, stages);
        }
    }

    /// <summary>
    /// An inner scenario that follows the real one's stage names (<c>start</c>, <c>buffs-cycle</c>, <c>place</c>, <c>teleporting</c>, <c>settle</c>, <c>mode-magic</c>, <c>fight</c>) for a few steps each, counts a kill every second step of the fight until the options' kills
    /// and then succeeds. With only the buff stage on it goes on to the fight, as the real one does: the death loop must stop it at <c>place</c>.
    /// </summary>
    private sealed class ScriptedFight(ScriptedHost host, FightOptions options, FightStages stages) : IFightLeg
    {
        private int _steps;
        private int _phaseSteps;

        public string Name => "fight";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public int Kills { get; private set; }

        public string BuffsNote { get; private set; } = "n/a";

        public bool BuffsAllUp => (stages & FightStages.Buffs) != 0 && host.AllUp;

        public bool Disposed { get; private set; }

        public int Steps => _steps;

        public int FightSteps { get; private set; }

        public FightStages Stages => stages;

        public string VerdictFields() => string.Create(CultureInfo.InvariantCulture, $"buffs={BuffsNote} kills={Kills}");

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            _steps++;
            _phaseSteps++;
            switch (Stage)
            {
                case "start":
                    To((stages & FightStages.Buffs) != 0 ? "buffs-cycle" : "place");
                    break;
                case "buffs-cycle":
                    host.OnBuff?.Invoke(this, _phaseSteps);
                    if (_phaseSteps >= 3)
                    {
                        BuffsNote = host.BuffsNote;
                        To("place");
                    }

                    break;
                case "place":
                    To("teleporting");
                    break;
                case "teleporting":
                    To("settle");
                    break;
                case "settle":
                    To("mode-magic");
                    break;
                case "mode-magic":
                    To("fight");
                    break;
                case "fight":
                    FightSteps++;
                    host.OnFight?.Invoke(this, FightSteps);
                    if (host.FailInFightAt > 0 && FightSteps >= host.FailInFightAt)
                    {
                        Failure = host.FailText;
                        return ScenarioStatus.Failed;
                    }

                    if (FightSteps % 2 == 0)
                    {
                        Kills++;
                    }

                    if (Kills >= options.Kills)
                    {
                        To("done");
                        return ScenarioStatus.Succeeded;
                    }

                    break;
                case "done":
                    return ScenarioStatus.Succeeded;
            }

            return ScenarioStatus.Running;
        }

        public void Dispose() => Disposed = true;

        private void To(string stage)
        {
            Stage = stage;
            _phaseSteps = 0;
        }
    }

    private static DeathScenario Ordered(Rig rig, ScriptedHost host, DeathStages stages, DeathOptions? options = null, GearKit? kit = null, FightOptions? fight = null, BuffOptions? buff = null, IReadOnlyDictionary<uint, SpellInfo>? spells = null) =>
        new(kit ?? GearKit.Of(Array.Empty<GearItem>()), spells ?? new Dictionary<uint, SpellInfo>(), options ?? new DeathOptions(), host.Walk, rig.R.H.Log, endSession: false, stages: stages, buff: buff, fight: fight, fightFactory: host.Make);

    private static string Token(string fields, string name)
    {
        var key = " " + name + "=";
        var at = (" " + fields).IndexOf(key, StringComparison.Ordinal);
        Assert.True(at >= 0, name + " is not in: " + fields);
        var start = at + key.Length - 1;
        var end = fields.IndexOf(' ', start);
        return end < 0 ? fields[start..] : fields[start..end];
    }

    private static WorldObject Decoy(int index, uint guid) =>
        LootRulesTests.Item(DecoyKit.All[index].ItemType, (uint)DecoyKit.All[index].UnitValue, (ushort)DecoyKit.All[index].Burden, guid, DecoyKit.All[index].Name, DecoyKit.All[index].Wcid);

    private static WorldObject Gear(uint wcid, uint guid, string name) => LootRulesTests.Item(0x1, 5400, 50, guid, name, wcid);

    /// <summary>The mage carries the item: its object exists and the server's 0022 puts it in the main pack (what a pick-up does).</summary>
    private static void Carry(Rig rig, WorldObject item)
    {
        var h = rig.R.H.R;
        h.Reducer.OnCreateObject(h.Context, item.Created, isUpdate: false);
        rig.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item.Guid, WorldActionRig.Player, 0, 0));
    }

    private static readonly string[] AfterVitals = ["rebuff", "fight"];

    private static readonly string[] TripStages = ["corpse-teleporting", "corpse-settle", "corpse-seek", "corpse-opening", "corpse-looting", "corpse-closing", "corpse-return", "corpse-return-settle"];

    // --- the order

    [Fact]
    public void With_the_rebuff_and_the_fight_on_the_stages_after_the_respawn_are_rebuff_fight_the_trip_then_the_end()
    {
        var rig = TripRig(3);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal([.. M3Stages, .. AfterVitals, .. TripStages], StageLog(rig));
        Assert.Equal(1, host.Made); // ONE inner scenario for the rebuff and the fight
        Assert.True(host.Inner!.Disposed);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(DeathLegVerdict.RebuffedYes, scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        var fields = scenario.VerdictFields();
        Assert.Equal("yes", Token(fields, "rebuffed"));
        Assert.Equal("4", Token(fields, "den_kills"));
        Assert.Contains(" trip_abort=none rebuffed=yes den_kills=4 decoys=", fields, StringComparison.Ordinal);
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);

        // The fight ran BEFORE the trip: the inner scenario's last step is before the corpse's open.
        var lines = Lines(rig).ToList();
        Assert.True(lines.FindIndex(l => l.StartsWith("fight: done; den_kills=4", StringComparison.Ordinal)) < lines.FindIndex(l => l.StartsWith("corpse: going back for", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("the stages after the respawn: rebuff, fight, corpse (", StringComparison.Ordinal));
    }

    [Fact]
    public void The_inner_scenario_is_made_once_with_the_buff_stage_and_the_den_stages_the_death_loops_options_and_the_shared_walk()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost();
        var kit = GearKit.Of(Array.Empty<GearItem>()).WithDecoys(DecoyKit.All);
        var scenario = Ordered(rig, host, Whole, new DeathOptions { FightKills = 3, FightSeconds = 90 }, kit);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(FightStages.Buffs | FightStages.Place | FightStages.Mode, host.Stages);
        Assert.Equal(3, host.Fight!.Kills);
        Assert.Equal(90.0, host.Fight.Seconds);
        Assert.Equal(new BuffOptions(), host.Buff);
        Assert.Same(host.Walk, host.PassedWalk);
        Assert.False(host.EndSession); // the death loop owns the session
        Assert.False(host.Kit!.HasDecoys); // the inner scenario never meets a decoy: it gets the plain kit
        Assert.Equal(3, scenario.DenKills);
    }

    [Fact]
    public void The_constructors_options_for_the_inner_scenario_override_the_defaults()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost();
        var fight = new FightOptions { Kills = 2, Seconds = 33 };
        var buff = new BuffOptions { CastChance = 0.5 };
        var scenario = Ordered(rig, host, Whole, fight: fight, buff: buff);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(fight, host.Fight);
        Assert.Equal(0.5, host.Buff!.CastChance);
        Assert.Equal(2, scenario.DenKills);
    }

    [Fact]
    public void With_the_rebuff_off_only_the_fight_runs_and_the_inner_scenario_has_no_buff_stage()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Rebuff = false });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal([.. M3Stages, "fight", .. TripStages], StageLog(rig));
        Assert.Equal(FightStages.Place | FightStages.Mode, host.Stages);
        Assert.Equal(DeathLegVerdict.RebuffedSkipped, scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        Assert.Contains(" rebuffed=skipped den_kills=4 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_fight_off_the_order_is_2_2s_the_trip_straight_after_the_vitals_then_the_rebuff_alone()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Fight = false });

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal([.. M3Stages, .. TripStages, "rebuff"], StageLog(rig)); // the mage was in non-combat when the scripted cycle ended: no rebuff-peace
        Assert.Equal(FightStages.Buffs, host.Stages);
        Assert.Equal(DeathLegVerdict.RebuffedYes, scenario.Rebuffed);
        Assert.Equal(0, scenario.DenKills);
        Assert.Equal(0, host.Inner!.FightSteps); // the scripted scenario would go on to fight: the death loop dropped it at place
        Assert.Equal("place", host.Inner.Stage);
        Assert.True(host.Inner.Disposed);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Contains(" rebuffed=yes den_kills=0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rebuff_alone_leaves_the_magic_mode_the_cycle_entered()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost() { OnBuff = (_, step) => { if (step == 1) { rig.R.CombatModeIs(CombatModes.Magic); } } };
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Fight = false });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal([.. M3Stages, .. TripStages, "rebuff", "rebuff-peace"], StageLog(rig));
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
        Assert.True(rig.ModeChanges >= 1);
    }

    [Fact]
    public void With_the_fight_and_the_rebuff_both_off_the_loop_is_exactly_the_one_that_merged_in_2_2()
    {
        var rig = TripRig(3);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Fight = false, Rebuff = false });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(0, host.Made);
        Assert.Equal([.. M3Stages, .. TripStages], StageLog(rig));
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Contains(" trip_abort=none rebuffed=skipped den_kills=0 ", scenario.VerdictFields(), StringComparison.Ordinal);

        // Without the two flags in the mask it is the same run.
        var masked = TripRig(3);
        var maskedHost = new ScriptedHost();
        Assert.Equal(ScenarioStatus.Succeeded, masked.Run(Ordered(masked, maskedHost, Trip)));
        Assert.Equal(0, maskedHost.Made);
        Assert.Equal(StageLog(rig), StageLog(masked));
    }

    [Fact]
    public void A_rebuff_that_ends_with_a_line_down_reads_no_and_the_leg_goes_on()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost() { AllUp = false, BuffsNote = "topped-up:20" };
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("no", scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        Assert.Contains(Lines(rig), l => l.StartsWith("rebuff: the buff stage ended with a profile line down", StringComparison.Ordinal));
        Assert.Contains(" rebuffed=no den_kills=4 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_inner_scenario_fails_the_leg_naming_the_stage_and_the_rebuff_reads_no()
    {
        var rig = TripRig(1);
        var scenario = new DeathScenario(GearKit.Of(Array.Empty<GearItem>()), new Dictionary<uint, SpellInfo>(), new DeathOptions(), new FakeWalk(), rig.R.H.Log, endSession: false, stages: Whole, fightFactory: (_, _, _, _, _, _, _, _, _) => new FailingFight());

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.StartsWith("rebuff: the buff cycle failed", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("no", scenario.Rebuffed);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_that_fails_in_its_kill_loop_fails_the_leg_naming_the_fight_with_the_kills_so_far()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost() { FailInFightAt = 5, FailText = "fight: 180 s passed with kills=2 of 4" };
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Equal("fight: fight: 180 s passed with kills=2 of 4", scenario.Failure);
        Assert.Equal(2, scenario.DenKills);
        Assert.Equal("yes", scenario.Rebuffed);
        Assert.Empty(rig.Opened);
        Assert.Contains(" rebuffed=yes den_kills=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    private sealed class FailingFight : IFightLeg
    {
        public string Name => "fight";

        public string Stage => "buffs-cycle";

        public string? Failure => "the buff cycle failed";

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public int Kills => 0;

        public string BuffsNote => "n/a";

        public bool BuffsAllUp => false;

        public string VerdictFields() => "buffs=n/a";

        public ScenarioStatus Step(IGameSessionView session, double now) => ScenarioStatus.Failed;

        public void Dispose()
        {
        }
    }

    // --- a death in the rebuff or the fight

    [Fact]
    public void A_death_during_the_fight_fails_the_leg_naming_the_stage_and_never_counts_in_died()
    {
        var rig = TripRig(2);
        rig.Lethal = true;
        var host = new ScriptedHost() { OnFight = (_, step) => { if (step == 3) { rig.Kill(); } } };
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("fight: the mage died during the fight", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("fight", scenario.Stage);
        Assert.True(host.Inner!.Disposed);
        Assert.Empty(rig.Opened); // no trip: the leg failed first
        var outcome = scenario.Outcome();
        Assert.False(outcome.Ok);
        Assert.Contains(" died=1 ", outcome.Line, StringComparison.Ordinal); // the second death is not a second count
        Assert.Contains(outcome.Problems, p => p.StartsWith("the death loop failed: fight: the mage died during the fight", StringComparison.Ordinal));
        Assert.Contains(" rebuffed=yes den_kills=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_death_during_the_rebuff_fails_the_leg_naming_the_stage_and_reads_rebuffed_no()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost() { OnBuff = (_, step) => { if (step == 2) { rig.Kill(); } } };
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.StartsWith("rebuff: the mage died during the rebuff", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("no", scenario.Rebuffed);
        Assert.Equal(0, host.Inner!.FightSteps);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    // --- the measures

    [Fact]
    public void A_fight_with_blows_and_a_health_drop_never_moves_the_death_loops_judged_fields()
    {
        var rig = TripRig(3);
        var host = new ScriptedHost()
        {
            OnFight = (_, step) =>
            {
                rig.Lethal = false; // the death the loop waits for came before; these blows must not kill
                rig.Damage = 2;
                rig.Hit();
                if (step == 2)
                {
                    rig.SetHealth(35);
                }
            },
        };
        var scenario = Ordered(rig, host, Whole);
        RunUntilStage(rig, scenario, "rebuff");
        var before = scenario.VerdictFields();
        var hitsBefore = scenario.Tally.HitsTaken;
        var damageBefore = scenario.Tally.DamageTaken;

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.Tally.HitsTaken > hitsBefore, "the fight's blows landed in the tally");
        Assert.True(scenario.Tally.DamageTaken > damageBefore);
        var after = scenario.VerdictFields();
        foreach (var name in new[] { "died", "suicides", "vitae", "respawn", "corpse", "purged", "protected", "vitals_pct", "hits_taken", "damage_taken", "evades", "health_min_pct", "deaths", "corpses_stale", "protection" })
        {
            Assert.Equal(Token(before, name), Token(after, name));
        }

        Assert.NotEqual("0.0", Token(after, "vitals_pct"));
        Assert.Equal("1", Token(after, "died"));
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{hitsBefore}"), Token(after, "hits_taken"));
    }

    [Fact]
    public void The_measures_freeze_at_the_end_of_the_respawns_vitals_even_without_a_trip_or_a_fight()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost() { OnBuff = (_, _) => { rig.Damage = 2; rig.Lethal = false; rig.Hit(); } };
        var scenario = Ordered(rig, host, Core | DeathStages.Rebuff, new DeathOptions { Fight = false });
        RunUntilStage(rig, scenario, "rebuff");
        var hits = scenario.Tally.HitsTaken;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.True(scenario.Tally.HitsTaken > hits);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{hits}"), Token(scenario.VerdictFields(), "hits_taken"));
    }

    // --- the regear's kit

    [Fact]
    public void The_regear_kit_is_the_kit_without_its_decoys_and_a_plain_kit_is_left_as_it_is()
    {
        var rig = TripRig(1);
        var plain = GearKit.Of(Array.Empty<GearItem>());
        var withDecoys = plain.WithDecoys(DecoyKit.All);
        var host = new ScriptedHost();

        var decoyScenario = Ordered(rig, host, Whole, kit: withDecoys);
        var plainScenario = Ordered(rig, host, Whole, kit: plain);

        Assert.True(withDecoys.HasDecoys);
        Assert.False(decoyScenario.RegearKit.HasDecoys);
        Assert.Empty(decoyScenario.RegearKit.DecoyItems);
        Assert.Equal(withDecoys.Items.Count - DecoyKit.All.Count, decoyScenario.RegearKit.Items.Count);
        Assert.Same(plain, plainScenario.RegearKit);
    }

    [Fact]
    public void The_gear_stage_checks_the_decoys_once_and_the_regear_with_the_plain_kit_spawns_none()
    {
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Items = DecoyKit.All.ToDictionary(d => d.Wcid, d => new StandInItemKind(d.Name, null)) };
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit);
            },
            time,
            log);
        for (var t = 0.0; t < 2.0; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        var kit = SmallKit().WithDecoys(DecoyKit.All);
        var scenario = new DeathScenario(kit, new Dictionary<uint, SpellInfo>(), new DeathOptions(), null, log, endSession: false, stages: DeathStages.Gear | DeathStages.Regear);
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 600 && status == ScenarioStatus.Running; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
            status = scenario.Step(session, session.Clock.Now);
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(8, scenario.Gear!.DecoyGrants); // the gear stage spawned the eight decoys
        Assert.Equal(0, scenario.Regear!.DecoyGrants); // the regear spawned none
        Assert.False(scenario.RegearKit.HasDecoys);
        Assert.Single(log.Entries, e => e.Message.StartsWith("decoy check:", StringComparison.Ordinal)); // once, after the gear steps
        Assert.Single(log.Entries, e => e.Message.StartsWith("load: ", StringComparison.Ordinal) && e.Message.Contains("of the capacity", StringComparison.Ordinal)); // 3.1 fix R7: the live load share, once, after the check (the stand-in mage is far from 95%)
        var check = Assert.NotNull(scenario.DecoyCheckResult);
        Assert.Equal(check.Field, scenario.DecoySafeField);
        var fields = scenario.VerdictFields();
        Assert.Equal(check.Field, Token(fields, "decoy_safe"));
        Assert.Equal("n/a", Token(fields, "decoys")); // no Death stage: the pack was never counted at a death
        Assert.Equal("skipped", Token(fields, "rebuffed"));
    }

    [Fact]
    public void With_decoys_off_the_gear_stage_makes_no_check_and_every_decoy_field_reads_n_a()
    {
        var rig = TripRig(0);
        rig.CorpseItems.Add(Decoy(0, 0x80000B20));
        rig.CorpseItems.Add(Gear(GearKit.Wand.Wcid, 0x80000B21, "The Healer's Heart"));
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Decoys = false });

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Null(scenario.DecoysCarried);
        Assert.Null(scenario.DecoysDropped);
        Assert.Null(scenario.GearDropped);
        Assert.Null(scenario.DecoysRecovered);
        Assert.Equal("n/a", scenario.DecoySafeField);
        Assert.Contains(" decoys=n/a decoy_safe=n/a decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("corpse: the listing held", StringComparison.Ordinal));
    }

    // --- the decoys carried, and the corpse's own listing

    [Fact]
    public void The_pack_is_counted_for_decoys_by_weenie_class_as_the_wait_among_the_tuskers_begins()
    {
        var rig = TripRig(1);
        Carry(rig, Decoy(0, 0x80000C10));
        Carry(rig, Decoy(3, 0x80000C11));
        Carry(rig, Decoy(5, 0x80000C12));
        Carry(rig, LootRulesTests.Item(0x80, 10, 5, 0x80000C13, "Not a decoy", 141));
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(3, scenario.DecoysCarried);
        Assert.Equal("3", Token(scenario.VerdictFields(), "decoys"));
        Assert.Contains(Lines(rig), l => l.StartsWith("decoys: 3 in the pack as the wait among the tuskers begins;", StringComparison.Ordinal));
    }

    [Fact]
    public void A_listing_of_decoys_only_is_recovered_and_counted()
    {
        var rig = TripRig(0);
        for (var i = 0; i < 4; i++)
        {
            rig.CorpseItems.Add(Decoy(i, 0x80000B20u + (uint)i));
        }

        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(4, scenario.DecoysDropped);
        Assert.Equal(0, scenario.GearDropped);
        Assert.Equal(4, scenario.DecoysRecovered);
        Assert.Contains(" decoys_dropped=4 gear_dropped=0 decoys_recovered=4", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: the listing held 4 entries: decoys_dropped=4 gear_dropped=0 decoys_recovered=4", StringComparison.Ordinal));
    }

    [Fact]
    public void A_listing_that_names_a_gear_item_counts_it_and_a_recovered_one_is_no_failure()
    {
        var rig = TripRig(0);
        rig.CorpseItems.Add(Decoy(0, 0x80000B20));
        rig.CorpseItems.Add(Decoy(1, 0x80000B21));
        rig.CorpseItems.Add(Gear(GearKit.Wand.Wcid, 0x80000B22, "The Healer's Heart"));
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(2, scenario.DecoysDropped);
        Assert.Equal(1, scenario.GearDropped);
        Assert.Equal(2, scenario.DecoysRecovered);
        Assert.Equal(0, scenario.GearLeft); // the listed wand was taken: nothing is left behind
        Assert.Contains(" decoys_dropped=2 gear_dropped=1 decoys_recovered=2", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal("0", Token(scenario.VerdictFields(), "gear_left"));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("gear_left=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pyreal_stack_in_the_listing_is_counted_apart_and_is_neither_a_decoy_nor_gear_3_1_fix_R20()
    {
        // ACE puts half the pyreals on the corpse (Player_Death.cs): a mage that carries coins lists them beside its decoys. DeathLiveTests (k) reads ListingEntries less ListingCoins as `listed`, so the coins cannot fail decoys_dropped >= listed - 1.
        var rig = TripRig(0);
        rig.CorpseItems.Add(Decoy(0, 0x80000B20));
        rig.CorpseItems.Add(Decoy(1, 0x80000B21));
        rig.CorpseItems.Add(Gear(DeathDropModel.CoinWcid, 0x80000B22, "Pyreal"));
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(3, scenario.ListingEntries);
        Assert.Equal(1, scenario.ListingCoins);
        Assert.Equal(2, scenario.DecoysDropped);
        Assert.Equal(0, scenario.GearDropped);
        Assert.Equal(0, scenario.GearLeft);
        var listed = scenario.ListingEntries - scenario.ListingCoins; // what the live test calls `listed`
        Assert.Equal(2, listed);
        Assert.True(scenario.DecoysDropped >= listed - 1);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: the listing held 3 entries: decoys_dropped=2 gear_dropped=0", StringComparison.Ordinal) && l.Contains("1 of them pyreal stack(s) (wcid 273", StringComparison.Ordinal));
    }

    [Fact]
    public void A_needed_gear_item_that_was_listed_and_not_taken_only_warns_and_counts_gear_left_because_the_regear_already_replaced_it_3_1_fix_R2()
    {
        var rig = TripRig(0);
        rig.CorpseItems.Add(Decoy(0, 0x80000B20));
        var wand = Gear(GearKit.Wand.Wcid, 0x80000B21, "The Healer's Heart");
        rig.CorpseItems.Add(wand);
        rig.SilentItems.Add(wand.Guid); // the server never answers this pick-up: it times out
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure); // a new judge is report-only in its first gate: no failure
        Assert.Null(scenario.Failure);
        Assert.Equal(CorpseLoot.Partial, scenario.CorpseLooted);
        Assert.Equal(1, scenario.DecoysDropped);
        Assert.Equal(1, scenario.GearDropped);
        Assert.Equal(1, scenario.DecoysRecovered);
        Assert.Equal(1, scenario.GearLeft);
        Assert.Equal("1", Token(scenario.VerdictFields(), "gear_left"));
        Assert.Contains(" decoys_dropped=1 gear_dropped=1 decoys_recovered=1 ", scenario.Outcome().Line, StringComparison.Ordinal);
        Assert.Contains(" gear_left=1", scenario.Outcome().Line, StringComparison.Ordinal);
        var warn = Assert.Single(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("gear_left=1", StringComparison.Ordinal));
        Assert.Contains("(weenie classes " + GearKit.Wand.Wcid.ToString(CultureInfo.InvariantCulture), warn.Message, StringComparison.Ordinal);
        Assert.Contains("the regear before the trip already replaced them, so what is left behind is a duplicate on the corpse, not a missing item", warn.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("lacks them", warn.Message, StringComparison.Ordinal);
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId); // it was placed back, and the run went on to its end
    }

    [Fact]
    public void In_2_2s_order_the_same_listing_only_warns_because_the_regear_after_the_trip_spawns_the_gear()
    {
        var rig = TripRig(0);
        var wand = Gear(GearKit.Wand.Wcid, 0x80000B21, "The Healer's Heart");
        rig.CorpseItems.Add(wand);
        rig.SilentItems.Add(wand.Guid);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Fight = false, Rebuff = false });

        var status = rig.Run(scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, scenario.GearDropped);
        Assert.Equal(1, scenario.GearLeft);
        Assert.Contains(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("gear_left=1", StringComparison.Ordinal) && e.Message.Contains("not a failure in M4 2.2's order", StringComparison.Ordinal));
    }

    [Fact]
    public void A_trip_that_never_read_a_listing_leaves_the_listing_fields_n_a()
    {
        var rig = TripRig(2);
        rig.OpenRefused = true;
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal("open-refused", scenario.TripAbort);
        Assert.Null(scenario.DecoysDropped);
        Assert.Null(scenario.GearLeft);
        Assert.Contains(" decoys_dropped=n/a gear_dropped=n/a decoys_recovered=n/a", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal("n/a", Token(scenario.VerdictFields(), "gear_left"));
    }

    // --- the pre-trip recovery (M4 3.1 d1 follow-up F1): Heal Self VII, then regeneration, before the trip's health precondition is judged

    private static Dictionary<uint, SpellInfo> HealTable(uint baseMana = 10) => new()
    {
        [DeathScenario.HealSelfVii] = WorldActionRig.Spell(DeathScenario.HealSelfVii, DatReaderWriter.Enums.SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Heal Self VII", BaseMana = baseMana },
    };

    /// <summary>A mage that left the fight at <paramref name="percent"/> health (the scripted fight's first step) with Heal Self VII in the table and in the action API.</summary>
    private static (Rig Rig, ScriptedHost Host, DeathScenario Scenario) AfterAFightAt(uint percent, uint healPoints, uint baseMana = 10, DeathOptions? options = null, double regenSeconds = 0, uint regenPercent = 0)
    {
        var rig = TripRig(2);
        rig.HoldsWand = true; // the guard needs a wielded caster for the magic mode the pre-trip heal enters
        rig.Describe();
        rig.R.CombatModeIs(CombatModes.NonCombat); // a fresh description forgets the rig's mode: the scenario would send a change to peace
        WieldedSetups.Create(rig.R.H.R, WieldedSetups.Wand);
        rig.HealPoints = healPoints;
        var table = HealTable(baseMana);
        rig.R.Spells(table[DeathScenario.HealSelfVii]);
        var host = new ScriptedHost
        {
            OnFight = (_, step) =>
            {
                if (step == 1)
                {
                    rig.SetHealth(percent);
                    if (regenSeconds > 0)
                    {
                        rig.RegenerateTo(regenSeconds, regenPercent); // the regeneration the wait waits for, counted from the end of the fight
                    }
                }
            },
        };
        return (rig, host, Ordered(rig, host, Whole, options, spells: table));
    }

    [Fact]
    public void Under_the_minimum_after_the_fight_one_cast_of_Heal_Self_restores_the_health_and_the_trip_goes()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 14);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal([DeathScenario.HealSelfVii], rig.Casts);
        Assert.Equal(1, scenario.PretripHeals);
        Assert.Equal([.. M3Stages, .. AfterVitals, "pretrip-heal", .. TripStages], StageLog(rig));
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(2, rig.ModeChanges); // into the magic mode the cast needs (the fight left the mage in non-combat), and out again
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("corpse: the health share is 30% after the fight, under --death-recover-min-health 60%: recovering before the trip", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: heal 1 of 4: Heal Self VII, health 30% before", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: heal 1 landed (Sent): health 30% to 81%", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: the health share is 81%, at the minimum 60% after 1 cast(s)", StringComparison.Ordinal));
        Assert.Contains(" pretrip_heals=1", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_casts_when_one_is_not_enough_and_the_casts_are_logged_one_by_one()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 6);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal([DeathScenario.HealSelfVii, DeathScenario.HealSelfVii], rig.Casts);
        Assert.Equal(2, scenario.PretripHeals);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(2, rig.ModeChanges); // the magic mode once for both casts
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("pretrip: heal 1 of 4:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: heal 2 of 4:", StringComparison.Ordinal) && l.Contains("health 52% before", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: heal 2 landed", StringComparison.Ordinal) && l.Contains("health 52% to 74%", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("pretrip: heal 3", StringComparison.Ordinal));
        Assert.Contains(" pretrip_heals=2", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_cast_is_skipped_when_the_mana_is_under_the_spells_cost_and_regeneration_brings_the_health_back()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 14, baseMana: 100, regenSeconds: 40, regenPercent: 80);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Empty(rig.Casts);
        Assert.Equal(0, scenario.PretripHeals);
        Assert.Equal(0, rig.ModeChanges); // no cast, so the mage never left non-combat
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("pretrip: the mana ", StringComparison.Ordinal) && l.Contains(" is under the 100 Heal Self VII costs: no cast, regeneration only", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: health 30% is under the minimum 60% with no cast to make; waiting up to 60 s for regeneration", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: regeneration: health 78%", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("pretrip: the health share is 78%, at the minimum 60% after ", StringComparison.Ordinal) && l.Contains("s of regeneration", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mage_still_under_the_minimum_after_the_casts_and_sixty_seconds_skips_the_trip_with_the_TODO()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 1); // each cast heals a point of 28: four casts, still far under

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(4, rig.Casts.Count); // at most four
        Assert.Equal(4, scenario.PretripHeals);
        Assert.Equal(CorpseLoot.Skipped, scenario.CorpseLooted);
        Assert.Empty(rig.Opened);
        Assert.Equal(2, rig.Places.Count); // the bind's and the den's: no trip
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("pretrip: 60 s of regeneration left the health share at", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("corpse: skipped (corpse_looted=skipped): the health share is ", StringComparison.Ordinal) && l.Contains("TODO(m4-death-trip-health)", StringComparison.Ordinal) && l.Contains("4 heal casts", StringComparison.Ordinal));
        Assert.Equal([.. M3Stages, .. AfterVitals, "pretrip-heal"], StageLog(rig));
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
        Assert.Contains(" pretrip_heals=4", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_spell_the_table_lacks_is_no_cast_and_the_wait_still_runs()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost { OnFight = (_, step) => { if (step == 1) { rig.SetHealth(30); rig.RegenerateTo(40, 75); } } };
        var scenario = Ordered(rig, host, Whole);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);

        Assert.Empty(rig.Casts);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Contains(Lines(rig), l => l.Contains("Heal Self VII (spell 2073) is not in the SpellTable: no cast", StringComparison.Ordinal));
    }

    [Fact]
    public void At_or_above_the_minimum_after_the_fight_there_is_no_cast_and_no_recovery_stage()
    {
        var (rig, _, scenario) = AfterAFightAt(75, 14);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.Casts);
        Assert.Equal(0, scenario.PretripHeals);
        Assert.Equal(0, rig.ModeChanges);
        Assert.DoesNotContain("pretrip-heal", StageLog(rig));
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);

        // Exactly at the minimum is at it: 13 of the vitae-reduced maximum 27, the minimum set to that very share.
        var (atRig, _, atScenario) = AfterAFightAt(49, 14, options: new DeathOptions { RecoverMinHealthPercent = 13 * 100.0 / 27 });
        Assert.Equal(ScenarioStatus.Succeeded, atRig.Run(atScenario));
        Assert.Empty(atRig.Casts);
        Assert.DoesNotContain("pretrip-heal", StageLog(atRig));
        Assert.Equal(CorpseLoot.Yes, atScenario.CorpseLooted);
    }

    [Fact]
    public void A_mage_that_is_already_in_the_magic_mode_casts_without_a_mode_change_and_still_leaves_it_after()
    {
        var (rig, host, scenario) = AfterAFightAt(30, 14);
        host.OnFight = (_, step) =>
        {
            if (step == 1)
            {
                rig.SetHealth(30);
                rig.R.CombatModeIs(CombatModes.Magic);
            }
        };

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(1, scenario.PretripHeals);
        Assert.Equal(1, rig.ModeChanges); // only the way out
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_death_during_the_recovery_fails_the_leg_naming_the_stage()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 0);
        RunUntilStage(rig, scenario, "pretrip-heal");
        rig.Kill();

        var status = rig.Run(scenario);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("pretrip-heal: the mage died during the pretrip-heal", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void In_2_2s_order_the_recovery_never_runs_and_a_low_health_share_skips_the_trip_as_before()
    {
        var rig = TripRig(2);
        rig.HealPoints = 14;
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Fight = false, RecoverMinHealthPercent = 90 }, spells: HealTable());

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.Casts);
        Assert.Equal(0, scenario.PretripHeals);
        Assert.Equal(CorpseLoot.Skipped, scenario.CorpseLooted);
        Assert.DoesNotContain("pretrip-heal", StageLog(rig));
        Assert.DoesNotContain(Lines(rig), l => l.Contains("TODO(m4-death-trip-health)", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_failing_precondition_is_named_without_a_recovery()
    {
        var (rig, _, scenario) = AfterAFightAt(30, 14);
        rig.NoCorpse = true; // no corpse was remembered: healing would not bring the trip back

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.Casts);
        Assert.DoesNotContain("pretrip-heal", StageLog(rig));
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: skipped (corpse_looted=skipped): no corpse of the mage was remembered", StringComparison.Ordinal));
    }

    // --- the decoy top-up after the unbuff suicide (M4 3.1 d1 follow-up F2)

    private static GearKit DecoyKitFull() => GearKit.Of(Array.Empty<GearItem>()).WithDecoys(DecoyKit.All);

    private static Rig BuffedRig(int carried)
    {
        var rig = new Rig(buffed: true);
        rig.Damage = 3; // the tuskers' blows do not kill the buffed mage within the suicide's 15 s
        rig.R.PrivateInt(PlayerQualities.LevelProperty, 126); // the decoy check needs the level
        for (var i = 0; i < carried; i++)
        {
            Carry(rig, Decoy(i, 0x80000C20u + (uint)i));
        }

        rig.CorpseItems.Add(LootRulesTests.Item(0x80, 10, 5, 0x80000B10, "Thing 0", 141));
        return rig;
    }

    [Fact]
    public void After_the_unbuff_suicide_the_decoys_the_pack_lacks_are_spawned_before_the_den_stages_run_again()
    {
        var rig = BuffedRig(carried: 5);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Trip, kit: DecoyKitFull());

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, rig.Suicides);
        Assert.Equal([DecoyKit.All[5].Wcid, DecoyKit.All[6].Wcid, DecoyKit.All[7].Wcid], rig.ItemGrants); // the kit's order, only the three the pack lacks
        Assert.Equal(3, scenario.DecoyTopUpGrants);
        var stages = StageLog(rig);
        var at = stages.IndexOf("unbuff-vitals");
        Assert.Equal(["unbuff-vitals", "unbuff-decoys", "den-place"], stages.GetRange(at, 3)); // after the suicide's vitals, before the den stages again
        Assert.Equal(8, scenario.DecoysCarried); // counted when the wait among the tuskers begins: the pack is whole again
        var lines = Lines(rig).ToList();
        Assert.Single(lines, l => l.StartsWith("decoy check:", StringComparison.Ordinal)); // the re-run: the gear stage did not run here
        Assert.Contains(lines, l => l.StartsWith("unbuff: the decoy top-up is done (3 granted); the decoy check runs again", StringComparison.Ordinal));
        var check = Assert.NotNull(scenario.DecoyCheckResult);
        Assert.Equal(check.Field, scenario.DecoySafeField);
        Assert.Equal("yes", scenario.DecoySafeField);
        Assert.Equal(check.Field, Token(scenario.VerdictFields(), "decoy_safe"));
        Assert.Equal("8", Token(scenario.VerdictFields(), "decoys"));
    }

    [Fact]
    public void The_top_up_says_once_that_the_suicides_corpse_stays_in_the_den_and_the_trip_goes_for_the_deaths_own()
    {
        var rig = BuffedRig(carried: 6);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Trip, kit: DecoyKitFull());

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Single(Lines(rig), l => l.StartsWith("unbuff: the suicide is a death too", StringComparison.Ordinal) && l.Contains("the suicide's own corpse stays in the den", StringComparison.Ordinal) && l.Contains("is not gone back for", StringComparison.Ordinal) && l.Contains("(2 missing, 6 held)", StringComparison.Ordinal));
        Assert.Equal(1, scenario.Suicides);
        Assert.Equal([rig.CorpseGuid], rig.Opened); // one open: the tuskers' corpse, the last one the rig made, not the suicide's
        Assert.Equal(rig.CorpseGuid, scenario.Tally.LastCorpseGuid);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
    }

    [Fact]
    public void A_pack_that_lacks_nothing_grants_nothing_but_the_check_still_runs_again()
    {
        var rig = BuffedRig(carried: 8);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Core, kit: DecoyKitFull());

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Empty(rig.ItemGrants);
        Assert.Equal(0, scenario.DecoyTopUpGrants);
        Assert.Contains("unbuff-decoys", StageLog(rig));
        Assert.Contains(Lines(rig), l => l.Contains("(0 missing, 8 held)", StringComparison.Ordinal));
        Assert.Single(Lines(rig), l => l.StartsWith("decoy check:", StringComparison.Ordinal));
        Assert.Equal(8, scenario.DecoysCarried);
    }

    [Fact]
    public void Without_a_suicide_there_is_no_top_up()
    {
        var rig = new Rig(buffed: false); // the mage holds no enchantment a death purges: the unbuff stage sends no suicide
        rig.R.PrivateInt(PlayerQualities.LevelProperty, 126);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Core, kit: DecoyKitFull());

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(0, rig.Suicides);
        Assert.Empty(rig.ItemGrants);
        Assert.DoesNotContain("unbuff-decoys", StageLog(rig));
        Assert.Equal(0, scenario.DecoyTopUpGrants);

        // A run without the unbuff stage at all: the same.
        var noStage = new Rig(buffed: true);
        noStage.Damage = 3;
        var noStageScenario = Ordered(noStage, new ScriptedHost(), DeathStages.Bind | DeathStages.Den | DeathStages.Death, kit: DecoyKitFull());
        Assert.Equal(ScenarioStatus.Succeeded, noStage.Run(noStageScenario));
        Assert.Empty(noStage.ItemGrants);
        Assert.DoesNotContain("unbuff-decoys", StageLog(noStage));
    }

    [Fact]
    public void A_kit_without_decoys_has_no_top_up_stage_even_after_a_suicide()
    {
        var rig = new Rig(buffed: true);
        rig.Damage = 3;
        var scenario = Ordered(rig, new ScriptedHost(), Core); // the plain kit the other tests use

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(1, rig.Suicides);
        Assert.Empty(rig.ItemGrants);
        Assert.DoesNotContain("unbuff-decoys", StageLog(rig));
    }

    [Fact]
    public void With_decoys_off_the_suicide_is_followed_by_the_den_stages_at_once_and_nothing_is_spawned()
    {
        var rig = BuffedRig(carried: 3);
        var scenario = Ordered(rig, new ScriptedHost(), Trip, new DeathOptions { Decoys = false }, DecoyKitFull());

        var status = rig.Run(scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(1, rig.Suicides);
        Assert.Empty(rig.ItemGrants);
        Assert.DoesNotContain("unbuff-decoys", StageLog(rig));
        Assert.Equal(0, scenario.DecoyTopUpGrants);
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("decoy check:", StringComparison.Ordinal) || l.StartsWith("decoys:", StringComparison.Ordinal));
        Assert.Contains(" decoys=n/a decoy_safe=n/a ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_up_the_server_never_answers_fails_the_leg_naming_the_stage()
    {
        var rig = BuffedRig(carried: 0);
        rig.GrantsSilent = true;
        var scenario = Ordered(rig, new ScriptedHost(), Core, kit: DecoyKitFull());

        var status = rig.Run(scenario);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("unbuff: the decoy top-up", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("unbuff-decoys", scenario.Stage);
        Assert.Equal(0, scenario.DecoyTopUpGrants);
    }
}
