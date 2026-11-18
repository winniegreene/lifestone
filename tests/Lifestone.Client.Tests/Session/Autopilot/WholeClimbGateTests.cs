using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 AW: the whole climb through the gates, at each band edge from the outskirts' last level to the Cave's first (M6 14, D135: 9, 10, 14, 15, 17, 20, 21, 22), on the production calls: the host's start and move-on, a run's check, its fight read and its way out. Every path that sends her somewhere is walked for every gate state and every state of
/// the buffs. Proved: she never arrives on a rung whose gate is shut, she is never left without a run, and she ends on the highest rung the true state allows. A path that read the level band alone would land her past a shut gate and fail here.
/// </summary>
public sealed class WholeClimbGateTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Burrow = "tusker-burrow";
    private const string Lodge = "tusker-lodge";
    private const string Cave = "tusker-cave";
    private const uint Town = 0xA9B40019;

    private static Ladder L => LadderLoader.WarMage;

    /// <summary>The gates: none met, the Burrow's only, both.</summary>
    public enum Gates
    {
        Shut,
        HalfMet,
        Met,
    }

    /// <summary>What stands on her: a level a run reads, nothing readable, or a line fallen under its level.</summary>
    public enum Buffs
    {
        Known,
        Unknown,
        Lapsed,
    }

    /// <summary>Every combination the test walks.</summary>
    public static TheoryData<int, Gates, Buffs> Climb()
    {
        var data = new TheoryData<int, Gates, Buffs>();
        foreach (var level in new[] { 9, 10, 14, 15, 17, 20, 21, 22 })
        {
            foreach (var gates in Enum.GetValues<Gates>())
            {
                foreach (var buffs in Enum.GetValues<Buffs>())
                {
                    data.Add(level, gates, buffs);
                }
            }
        }

        return data;
    }

    /// <summary>One climber on one world per ground: the state a test scripts.</summary>
    private sealed class Script
    {
        private readonly Dictionary<string, ApWorld> _worlds = [];
        private readonly ITestOutputHelper _output;

        public Script(ITestOutputHelper output, int level, Gates gates, Buffs buffs)
        {
            _output = output;
            (Level, Gate, State) = (level, gates, buffs);
        }

        public int Level { get; }

        public Gates Gate { get; }

        public Buffs State { get; }

        /// <summary>The world of a ground, the climber in it scripted: the level, the skills and the book.</summary>
        public ApWorld World(string ground)
        {
            if (_worlds.TryGetValue(ground, out var known))
            {
                return known;
            }

            var w = new ApWorld(ground, _output, atHome: false);
            w.Level(Level);
            var self = w.Trip.Vendor.State.Self;
            self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, Gate == Gates.Shut ? 1u : 2u, 0, 0u, 0, 0);
            self.Spellbook.IsKnown = true;
            if (Gate != Gates.Shut)
            {
                foreach (var line in L.Gates.Where(g => g.Rung == "burrow" || Gate == Gates.Met).SelectMany(g => g.Lines))
                {
                    self.Spellbook.Learn(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells[line.Level - 1].Id);
                }
            }

            _worlds[ground] = w;
            return w;
        }

        public RungBuffRead Standing => State switch
        {
            Buffs.Known => new RungBuffRead(_ => 8, _ => 4),
            Buffs.Lapsed => new RungBuffRead(name => name == "Armor Self" ? 3 : 8, _ => 4), // one line fallen under its bar: the rest still stand
            _ => new RungBuffRead(_ => 0, _ => 4), // nothing readable: level 0, not met
        };

        public AutopilotScenario Run(string ground)
        {
            var run = World(ground).Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 });
            run.BuffsForTest = Standing;
            run.TrophyQuests = []; // M6 7 AX: this table proves the gates; the trophy job has its own (TrophyJobTests)
            return run;
        }

        public IGameSessionView Stand(string world, uint cell)
        {
            var w = World(world);
            w.Trip.Vendor.StandAt(cell, new Vector3(84, 7.7f, 94));
            return w.Trip.View;
        }

        /// <summary>True when the true state opens a rung's gate: the book and skills the script gave, and every line up.</summary>
        public bool Open(string rung) => State == Buffs.Known && (Gate == Gates.Met || (Gate == Gates.HalfMet && rung == "burrow"));

        /// <summary>The rung she ends on, worked out here without the code under test: from the level's band down, a gate she stands on or that is open stops the walk.</summary>
        public string Expected(string? standsOn)
        {
            if (Level >= 21 && (standsOn == Cave || Open("cave")))
            {
                return Cave; // M6 7 AX: the Cave has its gate: shut, she stays on the Lodge past 20 (M6 14, D135: the Cave from 21)
            }

            if (Level >= 15 && (standsOn == Lodge || Open("lodge")))
            {
                return Lodge; // D135: the Lodge from 15, by the hub
            }

            return Level >= 10 && (standsOn == Burrow || Open("burrow")) ? Burrow : Outskirts; // D135: the Burrow from 10, by the Refuge
        }
    }

    private static uint CellOf(string ground) => (uint)GroundCatalog.Get(ground).Landblocks[0] << 16 | 0x0100;

    /// <summary>
    /// Follows her from a built run to where she hunts, as the dial does: a run's first check, then the way out unless she stands on it; a stop's named rung builds the next run. Each step's choice is judged: the ground it leaves her on is never
    /// behind a shut gate, and there is always a run.
    /// </summary>
    private static string Follow(Script s, IGameSessionView view, string ground, string? standsOn, bool running = false)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            var run = s.Run(ground);
            var check = run.AskRung(view, L, s.Level, "the run's first check");
            if (check.Decision.Kind == LadderStepKind.Moved)
            {
                // A run that is going on moves by the band too; a new run's first check moves only through a gate it read open
                Assert.True(check.Opened || (running && hop == 0), $"a moved first check is a gate read open, never a mismatch: {check.Reason}: {check.Why}");
                ground = Next(s, view, check.Decision.Stop!);
                continue;
            }

            Assert.Equal(LadderStepKind.Hunt, check.Decision.Kind);
            if (ground == standsOn)
            {
                return ground;
            }

            var leaving = run.AskRung(view, L, s.Level, "the way out", leaving: true);
            if (leaving.Decision.Kind == LadderStepKind.Moved)
            {
                ground = Next(s, view, leaving.Decision.Stop!);
                continue;
            }

            Assert.Equal(LadderStepKind.Hunt, leaving.Decision.Kind);
            var rung = leaving.Decision.Rung!;
            Assert.True(L.GateOf(rung.Key) is null || s.Open(rung.Key), $"she left for {rung.Key} with its gate shut (level {s.Level}, {s.Gate}, {s.State}): {leaving.Reason}");
            return ground;
        }

        Assert.Fail($"the rung never settled from {ground} (level {s.Level}, {s.Gate}, {s.State})");
        return ground;
    }

    private static string Next(Script s, IGameSessionView view, string stop)
    {
        var next = AutopilotHostLogic.MoveOnGround(view, stop, out var why);
        Assert.True(next is not null, $"stranded after {stop} (level {s.Level}, {s.Gate}, {s.State}): {why}");
        return next.Key;
    }

    private static string Started(Script s, IGameSessionView view, string? standsOn)
    {
        var ground = AutopilotHostLogic.StartGround(view, out var why);
        Assert.True(ground is not null, $"no run at the start (level {s.Level}, {s.Gate}, {s.State}): {why}");
        var gated = L.Rungs.Single(r => r.GroundKey == ground.Key);
        Assert.True(L.GateOf(gated.Key) is null || ground.Key == standsOn, $"a start read the {gated.Key} gate open with no buff known (level {s.Level}, {s.Gate}, {s.State}): {why}");
        return ground.Key;
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void A_start_in_town_climbs_only_through_open_gates(int level, Gates gates, Buffs buffs)
    {
        var s = new Script(output, level, gates, buffs);
        var view = s.Stand(Outskirts, Town);

        Assert.Equal(s.Expected(null), Follow(s, view, Started(s, view, null), null));
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void A_start_or_a_checkpoint_resume_on_the_ground_climbs_only_through_open_gates(int level, Gates gates, Buffs buffs)
    {
        // A checkpoint holds no rung (AutopilotCheckpoint): a resume is a start where she stands. The town stands in the outskirts' landblocks, so a resume on the outskirts reads the same.
        var s = new Script(output, level, gates, buffs);
        var view = s.Stand(Outskirts, CellOf(Outskirts));

        Assert.Equal(s.Expected(null), Follow(s, view, Started(s, view, null), null));
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void A_start_standing_on_the_Burrow_keeps_her_there_or_moves_her_up_never_stranded(int level, Gates gates, Buffs buffs)
    {
        var s = new Script(output, level, gates, buffs);
        var view = s.Stand(Burrow, CellOf(Burrow));
        var started = Started(s, view, Burrow);

        Assert.Equal(s.Expected(level >= 10 ? Burrow : null), Follow(s, view, started, Burrow));
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void A_restart_after_a_stop_that_names_no_rung_reads_as_a_start(int level, Gates gates, Buffs buffs)
    {
        var s = new Script(output, level, gates, buffs);
        var view = s.Stand(Outskirts, Town);
        foreach (var stop in new[] { "level", "watchdog:travel", "rung-moved:no-such-rung", null })
        {
            var ground = AutopilotHostLogic.MoveOnGround(view, stop, out var why);
            Assert.True(ground is not null, why);
            Assert.True(L.GateOf(L.Rungs.Single(r => r.GroundKey == ground.Key).Key) is null, $"{stop}: a stop with no rung opened a gate at level {level}: {why}");
            Assert.Equal(s.Expected(null), Follow(s, view, ground.Key, null));
        }
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void The_move_on_in_a_fight_goes_only_where_the_gates_let_her(int level, Gates gates, Buffs buffs)
    {
        var s = new Script(output, level, gates, buffs);
        var view = s.Stand(Outskirts, CellOf(Outskirts));
        var run = s.Run(Outskirts);
        var rung = L.Rungs.Single(r => r.GroundKey == Outskirts);
        var due = RungOwner.MoveOnDue(L, rung, run.AskRung(view, L, level, "the fight's read"), Outskirts, static _ => true);

        if (due is null)
        {
            Assert.Equal(Outskirts, s.Expected(null));
            return;
        }

        Assert.Equal(s.Expected(null), Follow(s, view, Next(s, view, due.Stop!), null));
    }

    [Theory]
    [MemberData(nameof(Climb))]
    public void A_death_at_each_rung_sends_her_back_only_through_the_gates(int level, Gates gates, Buffs buffs)
    {
        // She stands in town after the death, on a run still built on the rung she died on.
        foreach (var ground in new[] { Outskirts, Burrow, Lodge })
        {
            var s = new Script(output, level, gates, buffs);
            var view = s.Stand(ground, Town);
            var expected = s.Expected(null);
            var ended = Follow(s, view, ground, ground == Outskirts ? Outskirts : null, running: true);

            if (ground == Outskirts)
            {
                // The town stands in the outskirts: the run hunts on from there, and the fight's read moves her up
                Assert.Contains(ended, new[] { Outskirts, expected });
            }
            else
            {
                Assert.Equal(expected, ended);
            }
        }
    }

    private void GatesOpenAt17(ApWorld w)
    {
        w.Level(17);
        var self = w.Trip.Vendor.State.Self;
        self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        self.Spellbook.IsKnown = true;
        foreach (var line in L.Gates.SelectMany(g => g.Lines))
        {
            self.Spellbook.Learn(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells[line.Level - 1].Id);
        }

        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            self.Spellbook.Learn(spell.SpellId); // the counters ask no learn trip
        }

        output.WriteLine("level 17, every gate's book and skills met");
    }

    private AutopilotScenario LevelUp(ApWorld w, RungBuffRead buffs)
    {
        var run = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 });
        run.BuffsForTest = buffs;
        return run;
    }

    [Fact]
    public void Over_the_scripted_ACE_a_restart_with_the_buffs_still_up_moves_on_and_is_no_mismatch()
    {
        var w = new ApWorld(Outskirts, output, atHome: false);
        GatesOpenAt17(w);
        var run = LevelUp(w, new RungBuffRead(_ => 8, _ => 4));

        run.TrophyQuests = []; // the trophy job has its own tests
        w.Run(run, null, 4000);

        Assert.Equal("rung-moved:lodge", run.StopName);
        Assert.True(run.Ok);
    }

    [Fact]
    public void Over_the_scripted_ACE_a_lodge_run_at_home_does_not_leave_while_a_line_has_lapsed()
    {
        // After a death or a trip she stands at home on a run still built on the Lodge: the same arrow
        var w = new ApWorld(Lodge, output);
        GatesOpenAt17(w);
        var run = LevelUp(w, new RungBuffRead(name => name == "Armor Self" ? 3 : 8, _ => 4));

        w.Run(run, null, 8000);

        Assert.Equal("rung-moved:outskirts", run.StopName); // Armor Self is a line of both gates: the chain drops her to the rung with none
        Assert.True(run.Ok);
        Assert.DoesNotContain(w.Factory.Made, m => m.Kind is StationKinds.RungChange or StationKinds.DenTravel or StationKinds.DenFight);
        Assert.Contains(w.Lines, l => l.Contains("ladder: way out: level 17", StringComparison.Ordinal) && l.Contains("gate-held:lodge", StringComparison.Ordinal));
    }

    [Fact]
    public void Over_the_scripted_ACE_a_lodge_run_at_home_with_every_line_up_leaves_for_it()
    {
        var w = new ApWorld(Lodge, output);
        GatesOpenAt17(w);
        var run = LevelUp(w, new RungBuffRead(_ => 8, _ => 4));

        run.TrophyQuests = []; // the trophy job has its own tests
        w.Run(run, null, 8000);

        Assert.Equal("failed:travel:factory", run.StopName); // the rig has no rung change station: she left for it, which is what is asked
        Assert.DoesNotContain(w.Lines, l => l.Contains("ladder: way out:", StringComparison.Ordinal));
    }
}
