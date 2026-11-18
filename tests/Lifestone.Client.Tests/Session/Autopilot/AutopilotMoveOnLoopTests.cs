using System.Globalization;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14, the live loop of 2026-11-12 replayed over the scripted ACE: the climber at the Burrow's first level (16 live; 10 since M6 14 D135), her run on Holtburg's outskirts, the Burrow's gate open to the run because the buff bot's spells stand in the place of scrolls
/// she never learned (the gates' second form), and the dial choosing the next run's ground with no run to read a standing spell with. Live, the dial read the gate on the book alone, kept her on the outskirts, the restarted run's first
/// check moved on again, and the dial restarted 33 times in 15 s. Proved: with the ended run's hand-over the dial reads the gate as the run did and the one restart is on the Burrow, which she travels to (with and without a stale stock
/// line that calls a trip first, as live); a dial that still cannot read it refuses the move-on at once by <c>failed:move-on-loop</c>; a host that restarts on the same ground anyway is stopped by that name at the second move-on,
/// trip or no trip; and a stop asked of the restarted run ends it with no further restart.
/// </summary>
public sealed class AutopilotMoveOnLoopTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Burrow = "tusker-burrow";
    internal const string StartOption = "--autopilot-start";

    /// <summary>What the run reads standing in a gate line's place: the bot's level VII spell, as the live log read it.</summary>
    private static string? Stands(string line, int level) =>
        level <= 7 ? string.Create(CultureInfo.InvariantCulture, $"not in the book; stands by another's spell 2066 at level VII, 3510 s left: no scroll of her own is needed while it stands") : null;

    /// <summary>
    /// The dial over the scripted world as the play host wired it before M6 16 E0b (since then the steps are the client's, <c>AutopilotRungKits</c>, proved over this same world by <c>HeadlessClimbTests</c>): each scenario is built on the ground the move-on chose, with the carry's hand-over; the hook asks the
    /// logic for the next ground (<see cref="AutopilotHostLogic.MoveOnGroundFor"/>) and refuses by its reason. <paramref name="keepsTheGround"/> is the host of the live loop in its effect: every restart on the ground of the run before.
    /// </summary>
    /// <remarks>
    /// The start's rung (<c>AutopilotStartRungTests</c>) uses it too: <paramref name="level"/> is the climber's, <paramref name="active"/> the level each line stands at as every run reads it (null: every line up), and
    /// <see cref="Press"/> is the first press as the play host makes it, the dial's start choosing the first run's ground.
    /// </remarks>
    internal sealed class Climb
    {
        public Climb(ITestOutputHelper output, bool tripFirst, bool keepsTheGround = false, int level = 10, Func<string, int>? active = null, Func<AutopilotOptions, AutopilotOptions>? withOptions = null, IReadOnlyList<Lifestone.Client.Session.Quests.Quest>? trophyQuests = null)
        {
            Options = withOptions?.Invoke(Options) ?? Options; // M6 item 14: a test's own options (the trial switch's tests: LadderTrialTests)
            World = ClimberWorld(output, tripFirst, level);
            Ground = World.Ground;
            var home = World.Ground.Home;
            World.Factory.Script = spec => spec.Kind switch
            {
                StationKinds.Logistics => new ScriptedRun(spec.Name, step => { Trips += step == 0 && Ground.Key == Outskirts ? 1 : 0; return step >= 12 ? ScenarioStatus.Succeeded : null; }, () => "sold=0 bought=0"), // a trip of three seconds that restocks nothing
                StationKinds.RungChange or StationKinds.DenTravel => new ScriptedRun(spec.Name, _ =>
                {
                    var goesHome = spec.Arguments.TryGetValue(RungChangeStation.DirectionArgument, out var direction) && direction == "home";
                    var arrival = Ground.ArrivalDestination().Location.Position;
                    World.Trip.Vendor.StandAt(goesHome ? home.LifestoneCell : arrival.CellId, goesHome ? home.LifestonePoint : arrival.Local);
                    return ScenarioStatus.Succeeded;
                }, () => $"ground={Ground.Key} ok=yes fail=none route=recall"),
                _ => null,
            };
            var options = Options; // as the live run: ladder_quests=off (or the test's own)
            Logic = new AutopilotHostLogic(
                carry =>
                {
                    HandOvers.Add(carry.HandOver);
                    Grounds.Add(Ground.Key);
                    var scenario = new AutopilotScenario(carry.Remaining(options), World.Factory, World.Context().WithDungeon(Ground), null, null, carry.Seed, carry.HandOver)
                    {
                        BuffsForTest = new RungBuffRead(active ?? (static _ => 8), static _ => 4), // every line up, as after the bot's pass and her own (or as the test says)
                        GateStandsForTest = Stands,
                        TrophyQuests = trophyQuests ?? [], // no trophy errand: the scripted world makes no quest station (M6 14 guard task: a test may give its own)
                    };
                    carry.Apply(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground);
            Logic.MoveOn = view =>
            {
                Asked++;
                if (keepsTheGround)
                {
                    return MoveOnAnswer.Go; // the next run on the ground of the run before, whatever the stop said
                }

                var next = Logic.MoveOnGroundFor(view, out var why);
                if (next is null)
                {
                    return MoveOnAnswer.Refused(why.StartsWith(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal) ? why : "failed:rung-restart-refused: the next rung has no run: " + why);
                }

                Ground = next;
                Logic.UseGround(next);
                return MoveOnAnswer.Go;
            };
            Logic.Stopped += Stopped.Add;
        }

        /// <summary>
        /// The scripted world of the climb (shared with <c>HeadlessClimbTests</c>, M6 16 E0b): the climber at <paramref name="level"/> on Holtburg's outskirts with her kit, a caster in the hands and a spare, Item Enchantment trained,
        /// every scrivener's line in her book but two of the Burrow's gate (Focus Self, Willpower Self), and the buff bot's hour standing on her. <paramref name="tripFirst"/>: a stale stock line calls a trip first. <paramref name="onKill"/>: called at each kill of a scripted fight (a test levels her there).
        /// </summary>
        internal static ApWorld ClimberWorld(ITestOutputHelper output, bool tripFirst, int level = 10, Action<ApWorld, int>? onKill = null)
        {
            var stale = GroundCatalog.Get(Outskirts).Supplies.Stock.First(l => l.TripUnder > 1);
            var world = new ApWorld(Outskirts, output, held: tripFirst ? l => l.Wcid == stale.Wcid ? l.TripUnder / 2 : l.Keep : null, atHome: false, onKill: onKill);
            world.Level(level);
            world.OwnKit();
            var wielded = world.Trip.Own("Wand", 31336, 0x8000, 100); // a caster in the hands and a spare, as TrophyQuestTests' climber
            world.Trip.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, wielded, 0x01000000));
            world.Trip.Vendor.R.Tick();
            world.Trip.Own("Spare Wand", 31336, 0x8000, 100);

            var self = world.Trip.Vendor.State.Self;
            self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0); // the gate's skill: trained
            self.Spellbook.IsKnown = true;
            foreach (var spell in ScrivenerCatalog.Default.Spells.Where(s => s.Line is not ("Focus Self" or "Willpower Self")))
            {
                self.Spellbook.Learn(spell.SpellId); // every scrivener's line but two of the gate's: those stand by the bot's spell, never by her book
            }

            foreach (var line in BuffBotWire.Tusker.Lines)
            {
                var category = (ushort)(1000 + BuffBotWire.Tusker.Lines.ToList().IndexOf(line));
                world.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)line.SpellId, 1, category, 0, 300, 0.0, 3600, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0))); // the bot's hour stands: no stop at the bot is due
            }

            world.Trip.Vendor.R.Tick();
            return world;
        }

        public ApWorld World { get; }

        public AutopilotOptions Options { get; } = new() { Job = AutopilotJob.LevelUp, LadderQuests = false };

        public AutopilotHostLogic Logic { get; }

        /// <summary>
        /// The first press as the play host makes it (<c>AutopilotRungKits.PrepareRung</c>): the dial's start chooses the ground of the first run (<see cref="AutopilotHostLogic.StartGround"/> with the run's options), the
        /// parts are built over it, and the dial starts unattended.
        /// </summary>
        public void Press()
        {
            var ground = AutopilotHostLogic.StartGround(World.Trip.View, out var why, World.Trip.Context().Log, Options) ?? throw new InvalidOperationException("the start has no ground: " + why);
            Ground = ground;
            Logic.UseGround(ground);
            Logic.Toggle(World.Trip.View, startedBy: StartOption);
        }

        public HuntingGround Ground { get; private set; }

        public int Asked { get; private set; }

        /// <summary>The trips begun by a run on the outskirts (the Burrow's own stock lines call a trip of their own at its start: not counted).</summary>
        public int Trips { get; private set; }

        public List<string> Stopped { get; } = [];

        public List<string> Grounds { get; } = [];

        public List<AutopilotHandOver?> HandOvers { get; } = [];

        public IEnumerable<string> Lines => World.Lines;

        public int Restarts => World.Lines.Count(l => l == "autopilot: started by rung-moved" || l.Contains("the dial starts the next run now", StringComparison.Ordinal));

        public void Run(Func<Climb, bool>? until = null, int frames = 6000)
        {
            for (var i = 0; i < frames && Logic.Mode != AutopilotMode.Done && until?.Invoke(this) != true; i++)
            {
                var t = World.Trip;
                t.Vendor.R.Tick(0.25);
                t.Shops.Pump();
                t.Xp.Pump();
                t.Vendor.R.Tick();
                AnswerWieldsAndModes(World);
                Logic.Tick(World.Trip.View, World.Now);
            }
        }

        public void Print() => World.Output.WriteLine(string.Join(Environment.NewLine, World.Lines.Where(l => (l.StartsWith("autopilot", StringComparison.Ordinal) || l.StartsWith("ladder", StringComparison.Ordinal)) && !l.StartsWith("autopilot audit ", StringComparison.Ordinal))));

        public bool FightsOn(string ground) => Logic.Scenario is { State: AutopilotState.Fight, CurrentRun: IStoppableFight { Kills: >= 1 } } && Ground.Key == ground;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a stale stock line calls a trip before the first check, as live at 05:40:44
    public void At_level_10_with_the_Burrows_gate_open_by_the_bots_spells_the_one_restart_is_on_the_Burrow_and_she_travels_there(bool tripFirst)
    {
        var c = new Climb(output, tripFirst);
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run(x => x.FightsOn(Burrow));
        c.Print();

        Assert.Equal(AutopilotMode.Running, c.Logic.Mode);
        Assert.True(c.FightsOn(Burrow), "she fights on the Burrow");
        Assert.Equal([Outskirts, Burrow], c.Grounds); // the run after the move-on is on the next rung's ground, not on the ground the run left
        Assert.Equal((1, 1), (c.Asked, c.Logic.MoveOns)); // one move-on, one restart
        Assert.Empty(c.Stopped);
        Assert.Null(c.Logic.MoveOnRefusal);
        Assert.True(JourneyGrounds.At(StationWhere.Den, c.World.Trip.Vendor.State.Self.Position, GroundCatalog.Get(Burrow)), "she stands on the Burrow");
        Assert.Equal(tripFirst, c.Trips > 0);

        // the run opened the gate by the standing spells, and said so; the hand-over carries that read; the dial reads the named rung's gate with it
        Assert.Contains(c.Lines, l => l.Contains("ladder: rung: asked by the cycle's check: level=10 read_at=10", StringComparison.Ordinal) && l.Contains("run_ground=holtburg-outskirts", StringComparison.Ordinal) && l.EndsWith("-> rung=burrow ground=none reason=gate-open:burrow", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:burrow: ", StringComparison.Ordinal));
        var handOver = c.HandOvers[1]!;
        Assert.Equal(["Focus Self", "Willpower Self"], handOver.GateStands!.Select(s => s.Line));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: move-on restart: hand-over taken from the run on holtburg-outskirts", StringComparison.Ordinal) && l.Contains("gate=2 line(s) of the named rung's gate stand by another's spell (Focus Self, Willpower Self)", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: move-on restart: the named rung's gate is read with the ended run's hand-over", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.Contains("ladder: rung: asked by move-on: level=10 read_at=10", StringComparison.Ordinal) && l.EndsWith("named=burrow -> rung=burrow ground=tusker-burrow reason=stop-named:burrow", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("autopilot start: ", StringComparison.Ordinal) && l.Contains(" on tusker-burrow ", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dial_that_cannot_read_what_the_run_read_refuses_the_move_on_at_once_by_name_and_restarts_nothing(bool tripFirst)
    {
        // The live dial, before the hand-over carried the gate's read: on the book alone the named rung's gate is shut, so its choice is the ground the run just left.
        var c = new Climb(output, tripFirst);
        c.Logic.HandOverForTest = h => h with { GateStands = null };
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run();
        c.Print();

        Assert.Equal(AutopilotMode.Done, c.Logic.Mode);
        Assert.Equal([Outskirts], c.Grounds); // no second run was built
        Assert.Equal((1, 0), (c.Asked, c.Logic.MoveOns));
        Assert.Equal([AutopilotHostLogic.MoveOnLoopStop], c.Stopped);
        Assert.StartsWith("failed:move-on-loop: the run on holtburg-outskirts stopped with rung-moved:burrow to leave that ground, and the dial's own read keeps her on holtburg-outskirts", c.Logic.MoveOnRefusal, StringComparison.Ordinal);
        Assert.Contains(c.Lines, l => l.Contains("ladder: rung: asked by move-on: level=10 read_at=9", StringComparison.Ordinal) && l.EndsWith("named=burrow -> rung=outskirts ground=holtburg-outskirts reason=gate-held:burrow", StringComparison.Ordinal)); // the live line of 05:40:44.551
        Assert.Single(c.Lines, l => l.StartsWith("autopilot host: stop=failed:move-on-loop: ", StringComparison.Ordinal));
        Assert.Equal(1, c.Logic.UnattendedEnd!.ExitCode); // an unattended run ends the game by name, exit code 1
    }

    [Theory]
    [InlineData(false, 1)] // the restarted run stops again in its first frame: no cycle, no second
    [InlineData(true, 1)] // the restarted run makes its trip first (a cycle, seconds), then the same move-on on the same ground: still no progress
    public void A_host_that_restarts_on_the_same_ground_is_stopped_by_name_at_the_second_move_on_and_never_loops(bool tripFirst, int restarts)
    {
        var c = new Climb(output, tripFirst, keepsTheGround: true);
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run();
        c.Print();

        Assert.Equal(AutopilotMode.Done, c.Logic.Mode);
        Assert.Equal(restarts, c.Logic.MoveOns); // live: 33 in 15 s, until the client was killed
        Assert.Equal(Enumerable.Repeat(Outskirts, restarts + 1), c.Grounds);
        Assert.Equal(restarts, c.Asked); // the hook is not asked for the restart that is refused
        Assert.Equal([AutopilotHostLogic.MoveOnLoopStop], c.Stopped);
        Assert.StartsWith("failed:move-on-loop: the run that move-on restart 1 started on holtburg-outskirts stopped with rung-moved:burrow again (the same move-on on the same ground as the run before it", c.Logic.MoveOnRefusal, StringComparison.Ordinal);
        Assert.Single(c.Lines, l => l.StartsWith("autopilot host: stop=failed:move-on-loop: ", StringComparison.Ordinal));
        Assert.Equal(1, c.Logic.UnattendedEnd!.ExitCode);
        Assert.Equal(tripFirst, c.Trips > 0);
    }

    [Fact]
    public void A_stop_asked_of_the_restarted_run_ends_it_by_the_request_and_no_further_restart_is_made()
    {
        var c = new Climb(output, tripFirst: true, keepsTheGround: true);
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run(x => x.Logic.MoveOns == 1 && x.Logic.Scenario is { State: AutopilotState.Trip, CurrentRun: not null }); // the restarted run is in its trip, as live from 05:40:44 to 05:47:04
        Assert.Equal(1, c.Logic.MoveOns);
        c.Logic.Stop(AutopilotHostLogic.StopFileReason);
        c.Run();
        c.Print();

        Assert.Equal(AutopilotMode.Done, c.Logic.Mode);
        Assert.Equal(1, c.Logic.MoveOns);
        Assert.Equal(1, c.Asked);
        Assert.Equal(["request:stop-file"], c.Stopped);
        Assert.Equal(0, c.Logic.UnattendedEnd!.ExitCode); // the stop file's end of an unattended run
        Assert.DoesNotContain(c.Lines, l => l.Contains(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal));
    }

    [Fact]
    public void The_move_ons_ground_uses_a_hand_over_only_of_this_character_this_stop_and_this_moment()
    {
        var c = new Climb(output, tripFirst: false);
        var view = c.World.Trip.View;
        var guid = c.World.Trip.Vendor.State.Self.Guid;
        GateStanding[] stands = [new("Focus Self", 2, "stands by another's spell 2066"), new("Willpower Self", 2, "stands by another's spell 2090")];
        var handOver = new AutopilotHandOver(guid, Outskirts, "rung-moved:burrow", c.World.Now, GateStands: stands);

        Assert.Equal(Burrow, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _, handOver: handOver)?.Key);
        Assert.Equal(Outskirts, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _)?.Key); // the book alone: the gate holds her
        Assert.Equal(Outskirts, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _, handOver: handOver with { CharacterGuid = guid + 1 })?.Key);
        Assert.Equal(Outskirts, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _, handOver: handOver with { Stop = "rung-moved:lodge" })?.Key);
        Assert.Equal(Outskirts, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _, handOver: handOver with { EndedAt = c.World.Now - AutopilotHostLogic.MoveOnReadSeconds - 1 })?.Key);
        Assert.Equal(Outskirts, AutopilotHostLogic.MoveOnGround(view, "rung-moved:burrow", out _, handOver: handOver with { GateStands = [stands[0]] })?.Key); // one line still not met: shut
        Assert.Null(handOver.StandsAt("Focus Self", 3)); // a line that stood at level II says nothing of level III
    }

    /// <summary>
    /// M6 14 gate lapse: the climber at level 10 on the outskirts, the Burrow's gate open but for Bludgeon Bane, which reads down (it ran out); <paramref name="onBar"/>: Bludgeon Bane IV (the level the rig's reach gives) is in her book and
    /// on her spell bar. The scripted pass recasts it (the line reads up after it), and counts.
    /// </summary>
    private (Climb Climb, Func<int> Passes) BaneDown(bool onBar)
    {
        var bane = 0;
        var c = new Climb(output, tripFirst: false, active: name => name == "Bludgeon Bane" ? bane : 8);
        var spell = BuffProfiles.Climber.LineNamed("Bludgeon Bane")!.Spells[3];
        var book = c.World.Trip.Vendor.State.Self.Spellbook;
        book.Learn(spell.Id);
        var table = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(c.World.Trip.Context().Spells)
        {
            [spell.Id] = WorldActionRig.Spell(spell.Id, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = spell.Name, School = DatReaderWriter.Enums.MagicSchool.ItemEnchantment, Power = spell.Power, BaseMana = 10 },
        };
        c.World.Spells = table; // the retail table holds it; the rig's holds the bolts and the fight's lines
        if (onBar)
        {
            Assert.True(book.AddFavorite(spell.Id, 0, 4)); // tab V, the item enchantments
        }

        var passes = 0;
        var prior = c.World.Factory.Script;
        c.World.Factory.Script = spec => spec.Kind == StationKinds.Buff
            ? new ScriptedRun(spec.Name, _ =>
            {
                passes++;
                bane = onBar ? 8 : bane; // the pass recast it (from her bar; on none it is not hers to cast)
                return ScenarioStatus.Succeeded;
            }, () => "buffs=24/24")
            : prior?.Invoke(spec);
        return (c, () => passes);
    }

    [Fact]
    public void M6_14_gate_lapse_at_the_cycles_check_a_gate_line_she_can_recast_herself_is_recast_first_then_the_gate_is_read_open()
    {
        var (c, passes) = BaneDown(onBar: true);
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run(x => x.FightsOn(Burrow));
        c.Print();

        Assert.True(c.FightsOn(Burrow), "the recast opened the Burrow's gate: she moved on to it");
        Assert.True(passes() >= 1);
        Assert.Single(c.Lines, l => l.Contains("the gate recast's pass ended", StringComparison.Ordinal)); // one try a cycle
        var before = Assert.Single(c.Lines, l => l.StartsWith("ladder: gate recast: check before (cycle 1): level 10: Bludgeon Bane up (level 0 of 3, the burrow gate): ", StringComparison.Ordinal));
        Assert.Contains(": in her book and on her spell bar, her mana ", before, StringComparison.Ordinal);
        Assert.Contains("act: a pass where she stands (the rebuff margin, the field's lines) recasts it, then the cycle's check reads the gates again", before, StringComparison.Ordinal);
        Assert.Contains(c.Lines, l => l.Contains("-> known-state (cycle 1): the gate recast's pass ended: the cycle's check reads the gates again (check after", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: gate recast: check after (cycle 1): Bludgeon Bane up again after the pass", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:burrow: ", StringComparison.Ordinal));
        Assert.Equal([Outskirts, Burrow], c.Grounds);
    }

    [Fact]
    public void M6_14_gate_lapse_a_gate_line_on_none_of_her_spell_bars_is_not_hers_to_recast_and_the_gate_holds_by_name()
    {
        var (c, passes) = BaneDown(onBar: false);
        c.Logic.Toggle(c.World.Trip.View, startedBy: StartOption);
        c.Run(x => x.FightsOn(Outskirts));
        c.Print();

        Assert.True(c.FightsOn(Outskirts), "the gate holds her on the outskirts");
        Assert.True(passes() >= 1); // the run's own first pass, not a gate recast
        Assert.DoesNotContain(c.Lines, l => l.Contains("the gate recast's pass ended", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: gate recast: check before (cycle 1): level 10: not recast here: Bludgeon Bane up (level 0 of 3, the burrow gate): ", StringComparison.Ordinal)
            && l.EndsWith("is on none of her spell bars (the autopilot casts only from the spell bar): the gate is read as it stands", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: gate burrow: level 10: ", StringComparison.Ordinal) && l.Contains("Bludgeon Bane up: NOT MET (level 0 of 3)", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.StartsWith("ladder: gate recast: check after", StringComparison.Ordinal));
        Assert.Equal([Outskirts], c.Grounds);
    }
}
