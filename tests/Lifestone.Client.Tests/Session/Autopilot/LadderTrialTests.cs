using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14, the trial switch (the owner, 2026-11-13: the climber at level 49, on the Abode's rung, tries the Tusker Quarters early, as a test, then goes back; M6 14 D135 moved the Quarters' band to 42, so the
/// tests stand at 41, the Abode's last level): <c>--autopilot-trial-ground</c> with <c>--autopilot-trial-minutes</c>. Proved over the scripted ACE: at level 41 the trial of the Quarters starts at the dial's start, the first run is built on the Quarters, its way out reads the Abode's gate lines open and she
/// fights there; after its minutes of fight time from her first kill the trial ends by name and the ladder's own rung, the Abode, is taken by a normal rung change; a death during the trial ends it and the ladder resumes the
/// Abode after the recovery. By the rules: the Holding (entry portal 60) and a ground of her own rung are refused at level 41 by name and the ladder goes on as usual; each rung's own gate is read; a corpse on
/// the trial's ground is the trial's gate's. And the options, the cycle line's <c>trial=</c>, and the lines' names.
/// </summary>
public sealed class LadderTrialTests(ITestOutputHelper output)
{
    private const string Quarters = "tusker-quarters";
    private const string Holding = "tusker-holding";
    private const string Abode = "tusker-abode";

    private static Ladder L => LadderLoader.WarMage;

    /// <summary>The climber at <paramref name="level"/> over the scripted world (<see cref="AutopilotMoveOnLoopTests.Climb"/>: every gate line up, the bot's hour standing) with the trial in her options, and the Quarters' and the Abode's stock in the pack, so no supply trip is due.</summary>
    private AutopilotMoveOnLoopTests.Climb Climb(LadderTrial trial, bool death = false, int tripAfterKills = 0)
    {
        var c = new AutopilotMoveOnLoopTests.Climb(output, tripFirst: false, level: 41, withOptions: o => o with { Trial = trial, TestDeath = death, TripAfterKills = tripAfterKills });
        foreach (var key in new[] { Quarters, Abode })
        {
            foreach (var line in GroundCatalog.Get(key).Supplies.Stock)
            {
                c.World.Trip.Own(line.Name, line.Wcid, VendorRig.MiscType, 1, (ushort)line.Keep);
            }
        }

        return c;
    }

    private static IReadOnlyList<string> Trial(AutopilotMoveOnLoopTests.Climb c) => [.. c.Lines.Where(l => l.StartsWith(LadderTrial.LinePrefix, StringComparison.Ordinal))];

    private static IReadOnlyList<string> Cycles(AutopilotMoveOnLoopTests.Climb c) => [.. c.Lines.Where(l => l.StartsWith(LadderLine.Prefix + " at=cycle ", StringComparison.Ordinal))];

    [Fact]
    public void At_level_41_the_trial_of_the_Quarters_starts_hunts_there_and_returns_to_the_Abode_after_its_minutes()
    {
        var trial = new LadderTrial(Quarters, minutes: 2);
        var c = Climb(trial);

        c.Press();
        c.Run(x => x.FightsOn(Quarters));
        Assert.True(c.FightsOn(Quarters), "she fights on the Quarters");
        Assert.Equal(LadderTrialPhase.On, trial.Phase);
        Assert.Equal("quarters", trial.Rung);
        Assert.Equal([Quarters], c.Grounds); // the dial's start built the first run on the trial's ground: no run on the Abode first

        c.Run(x => x.FightsOn(Abode), frames: 4000);
        c.Print();

        Assert.True(c.FightsOn(Abode), "she fights on the Abode after the trial");
        Assert.Equal([Quarters, Abode], c.Grounds);
        Assert.Equal((1, 1), (c.Asked, c.Logic.MoveOns)); // one normal rung change back
        Assert.Empty(c.Stopped);
        Assert.Equal(LadderTrialPhase.Ended, trial.Phase);
        Assert.StartsWith("time: 2 minutes of fight time", trial.Why, StringComparison.Ordinal);
        Assert.InRange(trial.FightSeconds, 120, 140); // fight time only, from the first kill, ended in the first lull past it
        Assert.True(trial.Kills > 0);

        // the lines, by name: the start (the dial's), the return (the way out read the Abode's lines open), the first kill, the end
        var lines = Trial(c);
        Assert.StartsWith("ladder: trial: start: ground=tusker-quarters rung=quarters level=41 minutes=2 (start): ", lines[0], StringComparison.Ordinal);
        Assert.Single(lines, l => l.StartsWith("ladder: trial: return n=1: ground=tusker-quarters level=41 (the way out): the gate quarters's lines are read open", StringComparison.Ordinal));
        Assert.Single(lines, l => l.StartsWith("ladder: trial: first kill: ground=tusker-quarters", StringComparison.Ordinal));
        var end = Assert.Single(lines, l => l.StartsWith("ladder: trial: end: ", StringComparison.Ordinal));
        Assert.Contains("ground=tusker-quarters rung=quarters level=41 fought_min=2.", end, StringComparison.Ordinal);
        Assert.EndsWith(": time: 2 minutes of fight time fought from her first kill there; the ladder's own rung next, through a normal rung change", end, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("refused", StringComparison.Ordinal));

        // the choice and the rung change, both ways
        Assert.Single(c.Lines, l => l.StartsWith("ladder: rung: asked by start: level=41", StringComparison.Ordinal) && l.EndsWith("-> rung=quarters ground=tusker-quarters reason=trial:tusker-quarters", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:abode", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("ladder: move-on: level 41: the trial of tusker-quarters is over (time: ", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: rung: asked by move-on: level=41", StringComparison.Ordinal) && l.EndsWith("named=abode -> rung=abode ground=tusker-abode reason=stop-named:abode", StringComparison.Ordinal));

        // the cycle lines: the trial's carry trial=tusker-quarters beside its rung; the Abode's do not
        var cycles = Cycles(c);
        var trialCycles = cycles.Where(l => l.Contains(" ground=tusker-quarters ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(trialCycles);
        Assert.All(trialCycles, l => Assert.Contains(" rung=quarters ", l, StringComparison.Ordinal));
        Assert.Contains(trialCycles, l => l.Contains(" buffbot=on trial=tusker-quarters", StringComparison.Ordinal));
        Assert.DoesNotContain(cycles, l => l.Contains(" ground=tusker-abode ", StringComparison.Ordinal) && l.Contains(" trial=", StringComparison.Ordinal));

        // and the Abode's run hunts on, the trial over: no second rung change
        c.Run(frames: 400);
        Assert.Equal([Quarters, Abode], c.Grounds);
        Assert.Equal(LadderTrialPhase.Ended, trial.Phase);
    }

    [Fact]
    public void A_supply_trip_during_the_trial_comes_back_to_the_trials_ground()
    {
        var trial = new LadderTrial(Quarters, minutes: 30);
        var c = Climb(trial, tripAfterKills: 3); // a supply trip forced after the third kill, as the counters call one (--autopilot-trip-after-kills)
        var made = c.World.Factory.Script!;
        var trips = 0;
        c.World.Factory.Script = spec => spec.Kind == StationKinds.Logistics && c.Ground.Key == Quarters
            ? new AutopilotRig.ScriptedRun(spec.Name, step =>
            {
                if (step == 0)
                {
                    trips++;
                    c.World.Trip.Vendor.StandAt(c.Ground.Home.LifestoneCell, c.Ground.Home.LifestonePoint); // the trip's recall home, as the real station's
                }

                return step >= 12 ? ScenarioStatus.Succeeded : null;
            }, () => "sold=1 bought=1")
            : made(spec);

        c.Press();
        c.Run(x => trial.Returns >= 2 && x.FightsOn(Quarters));
        c.Print();

        Assert.Equal(1, trips);
        Assert.True(c.FightsOn(Quarters), "she is back on the trial's ground after the trip");
        Assert.Equal([Quarters], c.Grounds); // no rung change: the run's ground stays the trial's
        Assert.Equal(LadderTrialPhase.On, trial.Phase);
        Assert.Single(Trial(c), l => l.StartsWith("ladder: trial: return n=2: ground=tusker-quarters level=41 (the way out): the gate quarters's lines are read open; 0.", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.StartsWith("autopilot stop=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_trial_of_the_Holding_is_refused_at_level_41_by_its_entry_portal_and_the_ladder_goes_on_as_usual()
    {
        Assert.StartsWith("portal: the entry portal 22680 of tusker-holding admits levels 60 and up; level 41 is not admitted", RungOwner.TrialRefusal(L, Holding, 41, out var rung), StringComparison.Ordinal);
        Assert.Equal("holding", rung?.Key);
        Assert.StartsWith("not-later: ", RungOwner.TrialRefusal(L, Holding, 60, out _), StringComparison.Ordinal); // M6 holding at 60 (the owner's decision, 2026-11-20): from 60 the Holding is her own rung, never a trial

        var trial = new LadderTrial(Holding);
        var c = Climb(trial);
        c.Press();
        c.Run(x => x.FightsOn(Abode));
        c.Print();

        Assert.True(c.FightsOn(Abode), "she fights on the Abode, her own rung");
        Assert.Equal([Abode], c.Grounds);
        Assert.Equal(LadderTrialPhase.Refused, trial.Phase);
        var refused = Assert.Single(Trial(c));
        Assert.StartsWith("ladder: trial: refused: ground=tusker-holding rung=holding level=41 (start): portal: the entry portal 22680 of tusker-holding admits levels 60 and up; level 41 is not admitted; the ladder goes on as usual", refused, StringComparison.Ordinal);
        Assert.DoesNotContain(Cycles(c), l => l.Contains(" trial=", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains("reason=trial:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_death_during_the_trial_ends_it_and_the_ladder_resumes_its_own_rung_after_the_recovery()
    {
        var trial = new LadderTrial(Quarters, minutes: 30);
        var c = Climb(trial, death: true); // the scripted death after the first kill on the Quarters (--autopilot-test-death)

        c.Press();
        c.Run(x => x.FightsOn(Abode));
        c.Print();

        Assert.True(c.FightsOn(Abode), "she fights on the Abode after the death's recovery");
        Assert.Equal([Quarters, Abode], c.Grounds);
        Assert.Equal(LadderTrialPhase.Ended, trial.Phase);
        Assert.Equal("death: a death during the trial (deaths=1)", trial.Why);
        var end = Assert.Single(Trial(c), l => l.StartsWith("ladder: trial: end: ", StringComparison.Ordinal));
        Assert.EndsWith(": death: a death during the trial (deaths=1); the ladder resumes its own rung after the recovery, through a normal rung change", end, StringComparison.Ordinal);
        Assert.True(trial.FightSeconds < 30 * 60);

        // the death's cycle was the trial's; the recovery came first (the death station), then the check that took the Abode
        var died = Assert.Single(c.Lines, l => l.StartsWith("autopilot cycle n=", StringComparison.Ordinal) && l.Contains(" state=death ", StringComparison.Ordinal));
        Assert.Contains(Cycles(c), l => l.Contains(" deaths=1 ", StringComparison.Ordinal) && l.Contains(" trial=tusker-quarters", StringComparison.Ordinal));
        var order = c.Lines.ToList();
        Assert.True(order.IndexOf(died) < order.FindIndex(l => l.StartsWith("autopilot stop=rung-moved:abode", StringComparison.Ordinal)), "the rung change follows the recovery");
        Assert.Single(c.Lines, l => l.StartsWith("ladder: move-on: level 41: the trial of tusker-quarters is over (death: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tusker-habitat", "not-later: tusker-habitat is no ground of a rung after her own (abode, levels 25-41)")] // the Abode's own alternate
    [InlineData("tusker-cave", "not-later: tusker-cave is no ground of a rung after her own (abode, levels 25-41)")]
    [InlineData("tusker-abode", "not-later: tusker-abode is no ground of a rung after her own (abode, levels 25-41)")]
    [InlineData("tusker-barracks", null)] // the Quarters' alternate, its portal 40
    [InlineData("tusker-temple-fort-tethana", null)]
    public void At_level_41_a_trial_needs_a_ground_of_a_later_rung_or_its_alternate(string ground, string? refusal)
    {
        var why = RungOwner.TrialRefusal(L, ground, 41, out var rung);
        if (refusal is null)
        {
            Assert.Null(why);
            Assert.Equal("quarters", rung?.Key);
        }
        else
        {
            Assert.StartsWith(refusal, why, StringComparison.Ordinal);
            Assert.Null(rung);
        }
    }

    [Fact]
    public void The_Quarters_and_the_Holding_have_their_own_gates_and_the_trial_reads_them()
    {
        // M6 14 (D135): the Quarters' gate is the Abode's lines; the Holding's asks the buff bot's level VII armour lines (before D135 neither had one and the trial read the Abode's)
        Assert.Null(RungOwner.TrialRefusal(L, Quarters, 41, out var rung));
        Assert.Equal("quarters", rung!.Key);
        Assert.Same(L.GateOf("quarters"), RungOwner.TrialGate(L, rung));
        Assert.Same(L.GateOf("holding"), RungOwner.TrialGate(L, L.Rungs.Single(r => r.Key == "holding")));
        Assert.Equal(L.GateOf("abode")!.Lines, L.GateOf("quarters")!.Lines);
        Assert.Equal(L.GateOf("cave")!.Lines, L.GateOf("abode")!.Lines);
        Assert.Same(L.GateOf("lodge"), RungOwner.TrialGate(L, L.Rungs.Single(r => r.Key == "lodge"))); // a rung with a gate is read by its own
    }

    [Fact]
    public void The_trials_gate_is_read_whole_at_the_way_out_and_shut_it_refuses_the_trial_with_the_ladders_own_choice()
    {
        var q = Trained();
        var knows = static (string _, int _) => true;
        RungAsk Ask(LadderTrial trial, Func<string, int> active, bool leaving, string? run = Quarters) => new(41, null, q, knows, new RungBuffRead(active, static _ => 4)) { RunGround = run, Leaving = leaving, Trial = trial };

        var on = new LadderTrial(Quarters);
        var open = RungOwner.Decide(L, Ask(on, static _ => 8, leaving: true));
        Assert.Equal((LadderStepKind.Hunt, "quarters", Quarters, "trial:tusker-quarters"), (open.Decision.Kind, open.Decision.Rung?.Key, open.Decision.Rung?.GroundKey, open.Reason));
        Assert.True(open.Trial is { Steers: true, Refused: null, Gate.Open: true });
        Assert.Equal("quarters", open.Trial!.Gate!.Gate.Rung); // M6 14 (D135): its own gate
        Assert.Equal(L.Rungs.Single(r => r.Key == "quarters").TargetMargin, open.Decision.Rung!.TargetMargin); // the fight takes the trial rung's plan

        var downAsk = Ask(on, static name => name == "Impenetrability" ? 0 : 8, leaving: true);
        var shut = RungOwner.Decide(L, downAsk);
        var own = RungOwner.Decide(L, downAsk with { Trial = null });
        Assert.Equal(LadderStepKind.Moved, shut.Decision.Kind);
        Assert.Equal((own.Decision, own.Reason, own.HeldLevel), (shut.Decision, shut.Reason, shut.HeldLevel)); // the ladder's own choice: she does not leave for the Quarters
        Assert.StartsWith("gate-held:", shut.Reason, StringComparison.Ordinal);
        Assert.True(shut.Trial is { Steers: false, Refused: not null });
        Assert.Contains(shut.Gates, g => ReferenceEquals(g, shut.Trial!.Gate) && !g.Open); // the trial's read is on the choice, for the log
        Assert.StartsWith("gate: the quarters gate's lines are read shut at the way out to tusker-quarters: Impenetrability up (level 0 of 3)", shut.Trial!.Refused, StringComparison.Ordinal);

        // a read that is no way out does not read the gate: from another ground the trial's rung change, on its ground the hunt
        var moved = RungOwner.Decide(L, Ask(on, static _ => 0, leaving: false, run: Abode));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:quarters", true, "trial:tusker-quarters"), (moved.Decision.Kind, moved.Decision.Stop, moved.Decision.Ok, moved.Reason));
        Assert.Null(moved.Trial!.Gate);
        Assert.Equal(LadderStepKind.Hunt, RungOwner.Decide(L, Ask(on, static _ => 0, leaving: false)).Decision.Kind);

        // the trial moves by the reads (LadderTrial.Note): the start, then a gate shut at the way out before any kill there refuses it
        var log = new MemoryLog();
        on.Note(moved.Trial, 41, "the cycle's check", log);
        Assert.Equal(LadderTrialPhase.On, on.Phase);
        on.Note(shut.Trial, 41, "the way out", log);
        Assert.Equal(LadderTrialPhase.Refused, on.Phase);
        Assert.Contains(log.Lines, l => l.StartsWith("ladder: trial: refused: ground=tusker-quarters rung=quarters level=41 (the way out): gate: the quarters gate's lines are read shut", StringComparison.Ordinal));
        Assert.False(on.Steers);
        Assert.Null(RungOwner.Decide(L, Ask(on, static _ => 8, leaving: true)).Trial); // refused: the ladder's own, as usual

        // after a kill there, a gate shut at a return ends it instead
        var later = new LadderTrial(Quarters);
        later.Note(moved.Trial, 41, "the cycle's check", log);
        later.Kill(1, log);
        later.Note(shut.Trial, 41, "the way out", log);
        Assert.Equal(LadderTrialPhase.Ended, later.Phase);
        Assert.StartsWith("gate-shut: gate: ", later.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corpse_on_the_trials_ground_is_the_trials_gates_and_without_the_trial_the_Quarters_band_holds_it_shut()
    {
        var q = Trained();
        var knows = static (string _, int _) => true;
        var up = new RungBuffRead(static _ => 8, static _ => 4);
        var log = new MemoryLog();
        var trial = new LadderTrial(Quarters);
        trial.Note(RungOwner.Decide(L, new RungAsk(41, null, q, knows, up) { RunGround = Abode, Trial = trial }).Trial, 41, "the cycle's check", log);
        Assert.Equal(LadderTrialPhase.On, trial.Phase);

        var plain = RungOwner.CorpseRun(L, new RungAsk(41, null, q, knows, up), Quarters);
        Assert.False(plain.Open); // the Quarters' band (42) is above her: no corpse run there, as before
        var open = RungOwner.CorpseRun(L, new RungAsk(41, null, q, knows, up) { Trial = trial }, Quarters);
        Assert.Equal((true, "trial:tusker-quarters", "quarters"), (open.Open, open.Reason, open.Rung));
        var shut = RungOwner.CorpseRun(L, new RungAsk(41, null, q, knows, new RungBuffRead(static n => n == "Bludgeon Bane" ? 0 : 8, static _ => 4)) { Trial = trial }, Quarters);
        Assert.Equal((false, "trial-gate-held:tusker-quarters"), (shut.Open, shut.Reason));

        trial.EndByDeath(1, 41, log); // the death is counted after the recovery's corpse run; once over, a corpse there is still the trial's
        Assert.True(RungOwner.CorpseRun(L, new RungAsk(41, null, q, knows, up) { Trial = trial }, Quarters).Open);
        Assert.Equal("no-rung", RungOwner.CorpseRun(L, new RungAsk(41, null, q, knows, up) { Trial = trial }, null).Reason); // a corpse on a road: no rung's matter
    }

    [Fact]
    public void The_options_name_the_ground_and_the_minutes_and_refuse_what_cannot_be_a_trial()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        var off = Parse("--autopilot-job=level-up");
        Assert.Null(off.Trial);
        Assert.DoesNotContain("trial", off.ToString(), StringComparison.Ordinal);

        var on = Parse("--autopilot-job=level-up", "--autopilot-trial-ground=tusker-quarters");
        Assert.Empty(on.Errors);
        Assert.Equal((Quarters, 30.0, LadderTrialPhase.Asked), (on.Trial!.Ground, on.Trial.Minutes, on.Trial.Phase));
        Assert.EndsWith(" trial=tusker-quarters trial_minutes=30", on.ToString(), StringComparison.Ordinal);
        Assert.Same(on.Trial, (on with { Minutes = 5 }).Trial); // every run's options are copies: one trial a process

        var minutes = Parse("--autopilot-job=level-up", "--autopilot-trial-ground=tusker-quarters", "--autopilot-trial-minutes=45");
        Assert.Equal(45, minutes.Trial!.Minutes);

        Assert.Contains(Parse("--autopilot-job=level-up", "--autopilot-trial-ground=tusker-nowhere").Errors, e => e.StartsWith("--autopilot-trial-ground: 'tusker-nowhere' is not a hunting ground", StringComparison.Ordinal));
        Assert.Contains(Parse("--autopilot-job=level-up", "--autopilot-trial-minutes=45").Errors, e => e == "--autopilot-trial-minutes: is read only with --autopilot-trial-ground");
        Assert.Contains(Parse("--autopilot-job=level-up", "--autopilot-trial-ground=tusker-quarters", "--autopilot-trial-minutes=0").Errors, e => e.StartsWith("--autopilot-trial-minutes: '0' is not more than 0", StringComparison.Ordinal));
        Assert.Contains(Parse("--autopilot-trial-ground=tusker-quarters").Errors, e => e.StartsWith("--autopilot-trial-ground: the trial is the level up job's", StringComparison.Ordinal)); // the den's job
        Assert.Contains(Parse("--autopilot-job=level-up", "--autopilot-den-run", "--autopilot-trial-ground=tusker-quarters").Errors, e => e.StartsWith("--autopilot-trial-ground: the trial is the level up job's own loop", StringComparison.Ordinal));
        Assert.Null(Parse("--autopilot-job=level-up", "--autopilot-task=errand", "--autopilot-trial-ground=tusker-quarters").Trial);

        var catalog = string.Concat(Directory.GetFiles(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options"), "OptionCatalog*.cs").Select(File.ReadAllText));
        Assert.Contains("new(\"" + AutopilotOptions.TrialGroundOption + "\"", catalog, StringComparison.Ordinal);
        Assert.Contains("new(\"" + AutopilotOptions.TrialMinutesOption + "\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ladder_line_says_trial_after_buffbot_and_before_reason_and_only_under_a_trial()
    {
        var tally = new AutopilotTally();
        var plain = new LadderLine("cycle", 3, 41, "abode", Abode, tally, 0, null, null, BuffBot: true).Text();
        Assert.EndsWith(" buffbot=on", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("trial=", plain, StringComparison.Ordinal);

        var trial = new LadderLine("cycle", 4, 41, "quarters", Quarters, tally, 0, null, null, Reason: "x", BuffBot: true, Trial: Quarters).Text();
        Assert.EndsWith(" buffbot=on trial=tusker-quarters reason=x", trial, StringComparison.Ordinal);
        Assert.Equal(LadderLine.FieldNames, trial.Split(' ').Skip(1).Take(LadderLine.FieldNames.Count).Select(t => t[..t.IndexOf('=', StringComparison.Ordinal)]));
    }

    private static PlayerQualities Trained()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    /// <summary>A log that keeps its messages.</summary>
    private sealed class MemoryLog : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }
}
