using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death loop's new order (M4 3.1 d1, D20 and D22; <see cref="DeathStages.Rebuff"/>, <see cref="DeathStages.Fight"/>) over the same scripted ACE as the rest of this class, with a scripted inner fight scenario (<see cref="ScriptedFight"/>, made by the
/// scenario's <see cref="FightLegFactory"/> seam): the stage order with the rebuff and the fight on and with each off (both off is M4 2.2's loop), the mage's own kit for the inner scenarios, the inner scenario's kills counted, a death during the rebuff or the fight failing the
/// leg with the stage named, the judged measures frozen against a fight with blows, the corpse's own listing classified (one of ordinary items recovered, one that names a gear item, a pyreal stack, a gear item left behind) and the gear stage over the mage's own kit (against the offline
/// stand-in; the decoys are retired, M5 D51). The rig has no inventory, so the rig runs script no gear stage; the stand-in test runs it.
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

    private static DeathScenario Ordered(Rig rig, ScriptedHost host, DeathStages stages, DeathOptions? options = null, GearKit? kit = null, FightOptions? fight = null, BuffOptions? buff = null, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        var o = options ?? new DeathOptions();
        if (o.Fight && o.Travel is null)
        {
            o = o with { Travel = new HandledTravel(rig) }; // M5 3 d1: the handler's corpse leg runs, its way home recalls; the places before the death are the rig's
        }

        return new(kit ?? GearKit.Of(Array.Empty<GearItem>()), spells ?? new Dictionary<uint, SpellInfo>(), o, host.Walk, rig.R.H.Log, endSession: false, stages: stages, buff: buff, fight: fight, fightFactory: host.Make);
    }

    /// <summary>
    /// M5 3 d1: the death handler's travel over the scripted rig: the corpse leg on foot (the player stands in the corpse's cell a few steps on, a move with the session's own next stamps) and the way home as a recall (a teleport to Holtburg with the next
    /// stamps); every other purpose (the bind, the den before the death) is today's place, which the rig answers.
    /// </summary>
    private sealed class HandledTravel(Rig rig) : IJourneyTravel
    {
        private readonly PlaceTravel _place = new();

        public List<TravelRequest> Requests { get; } = [];

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            return request.Purpose switch
            {
                TravelPurpose.Corpse => new Leg(rig, request.Destination.Location.Position.CellId, TravelMeans.Run),
                TravelPurpose.Home => new Leg(rig, HoltburgCell, TravelMeans.Recall),
                _ => _place.GoTo(session, request, now),
            };
        }

        private sealed class Leg(Rig rig, uint cell, TravelMeans means) : ITravelLeg
        {
            private int _steps;

            public TravelMeans Means => means;

            public string? Failure => null;

            public string Fields => string.Empty;

            public TravelStatus Step(IGameSessionView session, double now)
            {
                if (++_steps == 3)
                {
                    Move(rig, cell, teleport: means == TravelMeans.Recall);
                }

                return _steps < 3 ? TravelStatus.Running : TravelStatus.Arrived;
            }

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Moves the player with the session's own next stamps (a teleport when asked: a recall's arrival), so a later stamp of the rig's never reads stale against it.</summary>
    private static void Move(Rig rig, uint cell, bool teleport)
    {
        var h = rig.R.H.R;
        var stamps = rig.R.H.State.Self.Stamps;
        var tele = teleport ? (ushort)(stamps.Teleport + 1) : stamps.Teleport;
        if (teleport)
        {
            h.Reducer.OnPlayerTeleport(h.Context, new Lifestone.Net.Messages.PlayerTeleportMsg(tele));
        }

        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, (ushort)(stamps.Position + 1), tele, 1, cell: cell, x: 50));
        if (teleport)
        {
            rig.R.H.Unhide((ushort)(60 + tele));
        }
    }

    /// <summary>
    /// M5 3 d1: <see cref="Rig.Run"/> with ACE's answer to the handler's Primary Portal Recall (the rig answers the cast's UseDone itself): the spell known, the magic mode while the recall runs (the rig's mage holds no wand: the client's guard would
    /// refuse the mode change), and 3 s after the cast the teleport into the den's arrival.
    /// </summary>
    private static ScenarioStatus Handled(Rig rig, DeathScenario scenario, double maxSeconds = 900, double dt = 0.25)
    {
        rig.R.Server((p, s) => AceM3Writers.MagicUpdateSpell(p, s, (ushort)RecallHome.PrimaryPortalRecallSpell));
        var seen = rig.R.H.Net.Sends.Count;
        var landAt = double.NaN;
        var end = rig.Now + maxSeconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && rig.Now < end)
        {
            status = rig.Frame(scenario, dt);
            var sends = rig.R.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                if (sends[seen].Kind == FakeSendKind.CastUntargetedSpell && sends[seen].Guid == RecallHome.PrimaryPortalRecallSpell)
                {
                    landAt = rig.Now + 3;
                }
            }

            if (scenario.Handler?.CurrentStep == "recall" && rig.R.H.State.Self.Qualities.CombatMode != CombatModes.Magic)
            {
                rig.R.CombatModeIs(CombatModes.Magic);
            }

            if (rig.Now >= landAt)
            {
                landAt = double.NaN;
                Move(rig, DenArrival, teleport: true);
            }
        }

        return status;
    }

    private static string Token(string fields, string name)
    {
        var key = " " + name + "=";
        var at = (" " + fields).IndexOf(key, StringComparison.Ordinal);
        Assert.True(at >= 0, name + " is not in: " + fields);
        var start = at + key.Length - 1;
        var end = fields.IndexOf(' ', start);
        return end < 0 ? fields[start..] : fields[start..end];
    }

    /// <summary>An ordinary carried item of the corpse's listing (neither gear the loops need nor coin): one of <see cref="DeathDropModelTests.Valuables"/>.</summary>
    private static WorldObject Valuable(int index, uint guid) =>
        LootRulesTests.Item(DeathDropModelTests.Valuables[index].ItemType, (uint)DeathDropModelTests.Valuables[index].UnitValue, 50, guid, DeathDropModelTests.Valuables[index].Name, DeathDropModelTests.Valuables[index].Wcid);

    private static WorldObject Gear(uint wcid, uint guid, string name) => LootRulesTests.Item(0x1, 5400, 50, guid, name, wcid);

    /// <summary>M5 3 d1: the death handler's steps as the scenario shows them (the respawn and the rebuff as <c>rebuff</c>, the route, the recall and the corpse leg as <c>corpse-teleporting</c>, the loot, the items back, the item lines as <c>rebuff</c>, the fight), then the way out of the den.</summary>
    private static readonly string[] HandlerStages = ["handler-rebuff", "handler-heal", "handler-topup", "handler-route", "handler-recall", "handler-corpse-walk", "handler-corpse-seek", "handler-corpse-open", "handler-corpse-loot", "handler-corpse-close", "handler-wear", "handler-relines", "handler-fight", "leave-den"];


    // --- the order

    [Fact]
    public void With_the_fight_on_the_order_after_the_respawn_is_the_death_handlers_rebuff_recall_corpse_items_and_fight_with_no_place()
    {
        var rig = TripRig(3);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);
        output.WriteLine("death " + scenario.VerdictFields());

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal([.. M3Stages, .. HandlerStages], StageLog(rig));
        Assert.Equal(2, host.Made); // the rebuff alone, then the fight alone in the den (no item line to re-cast: every line was up and nothing was worn again)
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(3, scenario.ItemsRecovered);
        Assert.Equal(DeathLegVerdict.RebuffedYes, scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        var fields = scenario.VerdictFields();
        Assert.Contains(" trip_abort=none stage_rebuffed=yes den_kills=4 pretrip_heals=", fields, StringComparison.Ordinal);
        EachJudgedKeyOnce(scenario.VerdictFields());
        Assert.Equal("deaths=1 corpse_recovered=yes route=recall items_back=3 vitae_grants=0 rebuffed=yes rebuffed_pretrip=yes relines=0 rearm=none rewear=none replace=none", scenario.HandlerFields);
        Assert.EndsWith(" bound=" + (scenario.Bound ? "yes" : "no") + " deaths=1 corpse_recovered=yes route=recall items_back=3 vitae_grants=0 rebuffed=yes rebuffed_pretrip=yes relines=0 rearm=none rewear=none replace=none", scenario.VerdictFields(), StringComparison.Ordinal); // M5 3 d2: the death line carries the handler fields at its end
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);
        Assert.Equal(2, rig.Places.Count); // the bind and the den before the death; none after it (D51: no place)
        Assert.Empty(rig.XpAsked); // no vitae grant
        Assert.Empty(rig.ItemGrants); // no regear by the item verb

        // The trip ran BEFORE the fight: the corpse came back, then the den's kills.
        var lines = Lines(rig).ToList();
        Assert.True(lines.FindIndex(l => l.StartsWith("handler: corpse_recovered=yes", StringComparison.Ordinal)) < lines.FindIndex(l => l.StartsWith("fight: done; den_kills=4", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("the stages after the respawn: the death handler (", StringComparison.Ordinal));
    }

    /// <summary>M4 P4 g3fix: the rebuff leaves the mage at 10% mana; the scripted fight reports the clock of its first fight step.</summary>
    private (Rig Rig, double? FirstFightStepAt, double RegenAt, DeathScenario Scenario) AfterARebuffAtTenPercentMana(DeathOptions options)
    {
        var rig = TripRig(1);
        double? firstFight = null;
        var regenAt = 0.0;
        var host = new ScriptedHost
        {
            OnBuff = (_, step) =>
            {
                if (step == 1)
                {
                    regenAt = rig.Now + 40;
                    rig.SetMana(10, regenSeconds: 40, regenPercent: 90);
                }
            },
        };
        host.OnFight = (_, step) => firstFight ??= rig.Now;
        var scenario = Ordered(rig, host, Whole, options);
        Handled(rig, scenario);
        Dump(rig, scenario);
        return (rig, firstFight, regenAt, scenario);
    }

    [Fact]
    public void With_a_mana_wait_the_fight_starts_only_after_the_mana_reaches_the_floor_and_logs_both_numbers_P4_g3fix()
    {
        var (rig, firstFight, regenAt, scenario) = AfterARebuffAtTenPercentMana(new DeathOptions { FightManaWaitSeconds = 120 });

        Assert.Null(scenario.Failure);
        Assert.NotNull(firstFight);
        Assert.True(firstFight >= regenAt, $"the first fight step came at {firstFight} s, before the mana regenerated at {regenAt} s");
        var lines = Lines(rig).ToList();
        Assert.Contains(lines, l => l.StartsWith("fight: mana ", StringComparison.Ordinal) && l.Contains("waiting for regeneration before the den, at most 120 s", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("fight: mana ", StringComparison.Ordinal) && l.Contains(" floor after ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mana_wait_that_runs_out_lets_the_fight_go_on_and_warns_with_the_numbers_P4_g3fix()
    {
        var (rig, firstFight, regenAt, scenario) = AfterARebuffAtTenPercentMana(new DeathOptions { FightManaWaitSeconds = 10 });

        Assert.Null(scenario.Failure);
        Assert.True(firstFight < regenAt, $"the fight started at {firstFight} s: the 10 s bound should have ended the wait before the regeneration");
        Assert.Contains(Lines(rig), l => l.Contains("did not reach the", StringComparison.Ordinal) && l.Contains("within 10 s; the fight goes on", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_mana_wait_the_default_the_fight_starts_at_once_and_nothing_is_logged_P4_g3fix()
    {
        var (rig, firstFight, regenAt, scenario) = AfterARebuffAtTenPercentMana(new DeathOptions());

        Assert.Null(scenario.Failure);
        Assert.True(firstFight < regenAt);
        Assert.DoesNotContain(Lines(rig), l => l.StartsWith("fight: mana ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_inner_scenarios_are_the_rebuff_alone_then_the_fight_alone_with_the_death_loops_options_and_the_shared_walk()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost();
        var kit = GearKit.Of(Array.Empty<GearItem>());
        var scenario = Ordered(rig, host, Whole, new DeathOptions { FightKills = 3, FightSeconds = 90 }, kit);

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.Equal(2, host.Made);
        Assert.Equal(FightStages.Mode, host.Stages); // the last one made is the fight: no place stage, the mage stands in the den after the trip
        Assert.Equal(3, host.Fight!.Kills);
        Assert.Equal(90.0, host.Fight.Seconds);
        Assert.Equal(new BuffOptions(), host.Buff);
        Assert.Same(host.Walk, host.PassedWalk); // the fight gets the shared walk (the rebuff gets none)
        Assert.False(host.EndSession); // the death loop owns the session
        Assert.Equal(kit.Items, host.Kit!.Items); // the inner scenario gets the mage's own kit, as built (the decoys are retired, D51)
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

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.Equal((2, 33.0), (host.Fight!.Kills, host.Fight.Seconds)); // the handler's corpse leg makes the run an embedded one: the fight's travel is the seam's
        Assert.Equal(0.5, host.Buff!.CastChance);
        Assert.Equal(2, scenario.DenKills);
    }

    [Fact]
    public void With_the_rebuff_off_only_the_fight_runs_and_the_inner_scenario_has_no_buff_stage()
    {
        var rig = TripRig(2);
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, new DeathOptions { Rebuff = false });

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.Equal([.. M3Stages, .. HandlerStages], StageLog(rig)); // the respawn step is shown as rebuff
        Assert.Equal(1, host.Made);
        Assert.Equal(FightStages.Mode, host.Stages);
        Assert.Equal(DeathLegVerdict.RebuffedSkipped, scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        Assert.Contains(" stage_rebuffed=skipped den_kills=4 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_rebuff_that_ends_with_a_line_down_reads_no_and_the_leg_goes_on()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost() { AllUp = false, BuffsNote = "topped-up:20" };
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.Equal("no", scenario.Rebuffed);
        Assert.Equal(4, scenario.DenKills);
        Assert.Contains(Lines(rig), l => l.StartsWith("rebuff: the buff stage ended with a profile line down", StringComparison.Ordinal));
        Assert.Contains(" stage_rebuffed=no den_kills=4 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_rebuff_reads_no_and_the_trip_goes_on_and_a_failed_fight_fails_the_leg_naming_it()
    {
        var rig = TripRig(1);
        var scenario = new DeathScenario(GearKit.Of(Array.Empty<GearItem>()), new Dictionary<uint, SpellInfo>(), new DeathOptions(), new FakeWalk(), rig.R.H.Log, endSession: false, stages: Whole, fightFactory: (_, _, _, _, _, _, _, _, _) => new FailingFight());

        Assert.Equal(ScenarioStatus.Failed, Handled(rig, scenario));

        Assert.StartsWith("fight: the buff cycle failed", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("no", scenario.Rebuffed);
        Assert.Contains(Lines(rig), l => l.StartsWith("rebuff: the buff stage failed: the buff cycle failed", StringComparison.Ordinal));
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("corpse_recovered=no route=recall", scenario.HandlerFields, StringComparison.Ordinal); // today's place seam has no runner: the corpse is not walked to
    }

    [Fact]
    public void A_fight_that_fails_in_its_kill_loop_fails_the_leg_naming_the_fight_with_the_kills_so_far()
    {
        var rig = TripRig(1);
        var host = new ScriptedHost() { FailInFightAt = 5, FailText = "fight: 180 s passed with kills=2 of 4" };
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Failed, Handled(rig, scenario));

        Assert.Equal("fight: fight: 180 s passed with kills=2 of 4", scenario.Failure);
        Assert.Equal(2, scenario.DenKills);
        Assert.Equal("yes", scenario.Rebuffed);
        Assert.Single(rig.Opened); // the trip came before the fight (D51)
        Assert.Contains(" stage_rebuffed=yes den_kills=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
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
    public void A_death_during_the_rebuff_restarts_the_handler_for_the_newer_corpse_and_never_counts_in_died()
    {
        var rig = TripRig(2);
        var killed = false;
        var host = new ScriptedHost() { OnBuff = (_, step) => { if (step == 2 && !killed) { killed = true; rig.Kill(); } } };
        var scenario = Ordered(rig, host, Whole);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, scenario.Handler!.Deaths);
        Assert.Single(scenario.Handler.OlderCorpses);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal); // the judged measures froze at the first respawn
        Assert.StartsWith("deaths=2 ", scenario.HandlerFields, StringComparison.Ordinal);

        // 3-R2: the death line says deaths=2 once, the handler's (the run's, the corpse trip's second death included); the frozen respawn measures are respawn_deaths=1 and never collide.
        var fields = scenario.VerdictFields();
        EachJudgedKeyOnce(fields);
        Assert.Equal("2", Regex.Match(fields, @"(?<![\w])deaths=(\S+)").Groups[1].Value);
        Assert.Contains(" respawn_deaths=1 ", fields, StringComparison.Ordinal);
    }

    /// <summary>3-R2: each key the smoke summary and the gate judge read from the death station (their pattern, the first match) is on the death line exactly once.</summary>
    private static void EachJudgedKeyOnce(string fields)
    {
        foreach (var key in new[] { "deaths", "corpse_recovered", "route", "items_back", "vitae_grants", "rebuffed", "relines", "trip_abort" })
        {
            Assert.True(Regex.Matches(fields, @"(?<![\w])" + key + "=").Count == 1, key + "= is not once in: " + fields);
        }
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
        RunUntilStage(rig, scenario, "handler-rebuff");
        var before = scenario.VerdictFields();
        var hitsBefore = scenario.Tally.HitsTaken;
        var damageBefore = scenario.Tally.DamageTaken;

        var status = Handled(rig, scenario);
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
        RunUntilStage(rig, scenario, "handler-rebuff");
        var hits = scenario.Tally.HitsTaken;

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.True(scenario.Tally.HitsTaken > hits);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{hits}"), Token(scenario.VerdictFields(), "hits_taken"));
    }

    // --- the gear stage over the mage's own kit

    [Fact]
    public void The_gear_stage_runs_over_the_mages_own_kit_and_logs_no_decoy_check_or_load_line()
    {
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
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

        // The pack holds the kit the way a build leaves it (M5 3 c2a, DoD 6): the gear stage of the death grants nothing, so the build's grant happens here, before the scenario, and the stage only verifies and wields. The kit is the ground's vendor kit
        // (the robe and the wand the supplies list), never the retired Healer's Heart of SmallKit().
        var kit = MagesOwnKit();
        var spawn = session.Tools.SpawnGear(kit, ActionSource.Host);
        Assert.Null(spawn.NotStarted);
        for (var t = 0.0; t < 60 && !spawn.IsDone; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        Assert.True(spawn.IsDone && spawn.Succeeded, spawn.Describe());
        var scenario = new DeathScenario(kit, new Dictionary<uint, SpellInfo>(), new DeathOptions(), null, log, endSession: false, stages: DeathStages.Gear);
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 600 && status == ScenarioStatus.Running; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
            status = scenario.Step(session, session.Clock.Now);
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.True(scenario.Gear!.GearSteps > 0); // the gear stage spawned and wielded the kit's own lines
        Assert.DoesNotContain(log.Entries, e => e.Message.StartsWith("decoy check:", StringComparison.Ordinal) || e.Message.StartsWith("load: ", StringComparison.Ordinal)); // the decoy check and its load line are retired (D51)
        var fields = scenario.VerdictFields();
        Assert.Equal("skipped", Token(fields, "stage_rebuffed"));
        Assert.DoesNotContain("decoy", fields, StringComparison.Ordinal);
    }

    /// <summary>The mage's own kit as a build leaves it, the vendor lines of the default ground's supplies (the wielded wand and the worn robe; the foci and the stock are not wielded and play no part in the gear stage).</summary>
    private static GearKit MagesOwnKit()
    {
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get(FightOptions.DefaultGround);
        var items = new List<GearItem>();
        foreach (var slot in new[] { Lifestone.Client.Session.Grounds.KitSlot.Wielded, Lifestone.Client.Session.Grounds.KitSlot.Worn })
        {
            foreach (var line in ground.Supplies.Kit.Where(k => k.Slot == slot))
            {
                var w = GearKit.SupplyWeenieOf(line.Wcid, ground, "kit");
                items.Add(new GearItem(w.Wcid, line.Name, slot == Lifestone.Client.Session.Grounds.KitSlot.Wielded ? GearKind.Wand : GearKind.Armour, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.UnitBurden));
            }
        }

        return GearKit.Of(items);
    }

    // --- the corpse's own listing

    [Fact]
    public void A_listing_of_ordinary_items_only_is_recovered_and_counted_and_leaves_no_gear_behind()
    {
        var rig = TripRig(0);
        for (var i = 0; i < 4; i++)
        {
            rig.CorpseItems.Add(Valuable(i, 0x80000B20 + (uint)i));
        }

        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(4, scenario.ListingEntries);
        Assert.Equal(0, scenario.GearLeft);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: the listing held 4 entries: gear_listed=0", StringComparison.Ordinal));
        Assert.DoesNotContain("decoy", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>M5 3 d4: the mage's kit as the needed set reads it (a vendor wand line, the kit's wielded line): gear_left= counts the listed items of the CURRENT kit, never the retired loot gear.</summary>
    private static GearKit MageKit => GearKit.Of([new GearItem(2472, "Wand", GearKind.Wand, GearKit.WeenieTypeCaster, GearKit.ItemTypeCaster, EquipMask.Held, 1, null, 50)]);

    [Fact]
    public void A_listing_that_names_a_gear_item_counts_it_and_a_recovered_one_is_no_failure()
    {
        var rig = TripRig(0);
        rig.CorpseItems.Add(Valuable(0, 0x80000B20));
        rig.CorpseItems.Add(Valuable(1, 0x80000B21));
        rig.CorpseItems.Add(Gear(MageKit.WandItem.Wcid, 0x80000B22, "Wand"));
        rig.CorpseItems.Add(Gear(GearKit.Wand.Wcid, 0x80000B23, "The Healer's Heart")); // the retired loot wand: not a line of the kit, so not gear
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, kit: MageKit);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(4, scenario.ListingEntries);
        Assert.Equal(0, scenario.GearLeft); // the listed wand was taken: nothing is left behind
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: the listing held 4 entries: gear_listed=1", StringComparison.Ordinal)); // the kit's wand only
        Assert.Equal("0", Token(scenario.VerdictFields(), "gear_left"));
        Assert.DoesNotContain(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("gear_left=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pyreal_stack_in_the_listing_is_counted_apart_and_is_not_gear_3_1_fix_R20()
    {
        // A pyreal stack on the corpse is listed apart: DeathLiveTests reads ListingEntries less ListingCoins as the listed items, so a coin stack cannot count as one.
        var rig = TripRig(0);
        rig.CorpseItems.Add(Valuable(0, 0x80000B20));
        rig.CorpseItems.Add(Valuable(1, 0x80000B21));
        rig.CorpseItems.Add(Gear(DeathDropModel.CoinWcid, 0x80000B22, "Pyreal"));
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(3, scenario.ListingEntries);
        Assert.Equal(1, scenario.ListingCoins);
        Assert.Equal(0, scenario.GearLeft);
        Assert.Equal(2, scenario.ListingEntries - scenario.ListingCoins); // what the live test calls `listed`
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: the listing held 3 entries: gear_listed=0", StringComparison.Ordinal) && l.Contains("1 of them pyreal stack(s) (wcid 273", StringComparison.Ordinal));
    }

    [Fact]
    public void A_needed_gear_item_that_was_listed_and_not_taken_only_warns_and_counts_gear_left_because_the_handler_spawns_nothing_in_its_place_3_1_fix_R2()
    {
        var rig = TripRig(0);
        rig.CorpseItems.Add(Valuable(0, 0x80000B20));
        var wand = Gear(MageKit.WandItem.Wcid, 0x80000B21, "Wand");
        rig.CorpseItems.Add(wand);
        rig.SilentItems.Add(wand.Guid); // the server never answers this pick-up: it times out
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole, kit: MageKit);

        var status = Handled(rig, scenario);
        Dump(rig, scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure); // a new judge is report-only in its first gate: no failure
        Assert.Null(scenario.Failure);
        Assert.Equal(CorpseLoot.Partial, scenario.CorpseLooted);
        Assert.Equal(1, scenario.GearLeft);
        Assert.Equal("1", Token(scenario.VerdictFields(), "gear_left"));
        Assert.Contains(" gear_left=1", scenario.Outcome().Line, StringComparison.Ordinal);
        var warn = Assert.Single(rig.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("gear_left=1", StringComparison.Ordinal));
        Assert.Contains("(weenie classes " + MageKit.WandItem.Wcid.ToString(CultureInfo.InvariantCulture), warn.Message, StringComparison.Ordinal);
        Assert.Contains("the death handler took what the corpse gave and spawns nothing in its place (D51)", warn.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("lacks them", warn.Message, StringComparison.Ordinal);
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId); // it was placed back, and the run went on to its end
    }

    [Fact]
    public void A_trip_that_never_read_a_listing_leaves_the_listing_fields_n_a()
    {
        var rig = TripRig(2);
        rig.OpenRefused = true;
        var host = new ScriptedHost();
        var scenario = Ordered(rig, host, Whole);

        Assert.Equal(ScenarioStatus.Succeeded, Handled(rig, scenario));

        Assert.Equal("open-refused", scenario.TripAbort);
        Assert.Null(scenario.ListingEntries);
        Assert.Null(scenario.GearLeft);
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
        var scenario = Ordered(rig, host, Whole, (options ?? new DeathOptions()) with { Travel = new PlaceTravel() }, spells: table);
        return (rig, host, scenario);
    }
}
