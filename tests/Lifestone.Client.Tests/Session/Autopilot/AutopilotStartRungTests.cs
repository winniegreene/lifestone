using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14, the start's rung (live, 2026-11-12 UTC, three relaunches; level 29 then, 23 since M6 14 D135 put the Cave at 21-24): the climber at level 23 belongs on the Cave, and every fresh process built its first run on Holtburg's outskirts, because a start read no buff
/// and so every gate shut; the run sold the Cave's stock, bought the outskirts', and moved on to the Cave at its buff stage, 337 s in. Proved over the scripted ACE: with the buff bot's spells standing in her
/// enchantments at the start, the start reads them from the state and the first run is built on the Cave, which she travels to; with none standing, with the lines under the trip rule's seven minutes, with the stop at
/// the bot off or with no options given, the start picks what it picked before; a gate's armour lines are never counted met by a start (they are named, and the run's way out reads them); and a start's pick that
/// the run's way out reads shut ends in one move-on to the lower rung, where she hunts, never in a loop.
/// </summary>
public sealed class AutopilotStartRungTests(ITestOutputHelper output)
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Burrow = "tusker-retreat"; // M6 retreat (the owner, 2026-11-19): the burrow rung's ground is the Tusker Retreat (the Burrow its alternate)
    private const string Cave = "tusker-cave";

    private static AutopilotOptions LevelUp => new() { Job = AutopilotJob.LevelUp, LadderQuests = false };

    /// <summary>The bot's spell on her as ACE's 0x02C2 carries it (the Other spell of the line at level VII), with <paramref name="seconds"/> left.</summary>
    private static void Enchant(ApWorld w, BuffBotLine line, double seconds)
    {
        var category = (ushort)(1000 + BuffBotWire.Tusker.Lines.ToList().IndexOf(line));
        w.Trip.Vendor.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)line.SpellId, 1, category, 0, 300, 0.0, seconds, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
    }

    /// <summary>
    /// The climber in Holtburg after a login, as the build that leans on the bot leaves her: the gates' skill trained, the armour lines of the gates in her book and no scroll of a line the bot casts; the bot's
    /// spells on her with <paramref name="seconds"/> left each (<paramref name="secondsOf"/> for one line's own; a line with 0 is not on her).
    /// </summary>
    private ApWorld Climber(int level = 23, double seconds = 3000, Func<BuffBotLine, double?>? secondsOf = null, bool armourLearned = true)
    {
        var w = new ApWorld(Outskirts, output, atHome: true);
        w.Level(level);
        var self = w.Trip.Vendor.State.Self;
        self.Qualities.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        self.Spellbook.IsKnown = true;
        if (armourLearned)
        {
            foreach (var line in LadderLoader.WarMage.Gates.SelectMany(g => g.Lines).Where(l => !BuffBotWire.Tusker.Lines.Any(b => b.Line == l.Line)))
            {
                self.Spellbook.Learn(RungGateRule.LineOf(BuffProfiles.Climber, line.Line).Spells[line.Level - 1].Id);
            }
        }

        foreach (var line in BuffBotWire.Tusker.Lines)
        {
            if ((secondsOf?.Invoke(line) ?? seconds) is > 0 and var left)
            {
                Enchant(w, line, left);
            }
        }

        w.Trip.Vendor.R.Tick();
        return w;
    }

    private string? Start(ApWorld w, AutopilotOptions? options) => AutopilotHostLogic.StartGround(w.Trip.View, out _, w.Trip.Context().Log, options)?.Key;

    private void Print(ApWorld w) => output.WriteLine(string.Join(Environment.NewLine, w.Lines.Where(l => l.StartsWith("ladder", StringComparison.Ordinal))));

    [Fact]
    public void At_level_23_with_the_bots_lines_standing_the_start_reads_them_from_the_state_and_picks_the_Cave()
    {
        var w = Climber();

        Assert.Equal(Cave, Start(w, LevelUp));
        Print(w);

        // one line names every gate line and what it stands by; the armour lines are named as left to the way out
        var read = Assert.Single(w.Lines, l => l.StartsWith("ladder: start: the gates are read with what stands on her now", StringComparison.Ordinal));
        Assert.Contains("Focus Self: spell 2066, 3000 s left", read, StringComparison.Ordinal);
        Assert.Contains("Armor Self: spell 2052, 3000 s left", read, StringComparison.Ordinal);
        Assert.Contains("not standing: none", read, StringComparison.Ordinal);
        Assert.Contains("left to the way out's check: Impenetrability, Bludgeon Bane (" + AutopilotHostLogic.ArmourLineUnread + ")", read, StringComparison.Ordinal);

        // the Cave's gate, condition by condition: a line the bot casts is met by its spell, in a learned line's place and as up; no armour line is counted up
        var gate = Assert.Single(w.Lines, l => l.StartsWith("ladder: gate cave: level 23: as the start reads it: ", StringComparison.Ordinal));
        Assert.Contains("Armor Self V learned: met (stands by the buff bot's spell 2052 (level VII), 3000 s left)", gate, StringComparison.Ordinal);
        Assert.Contains("Armor Self up: met (stands by the buff bot's spell 2052 (level VII), 3000 s left)", gate, StringComparison.Ordinal);
        Assert.Contains("Impenetrability III learned: met (in the book)", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Impenetrability up", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("Bludgeon Bane up", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT MET", gate, StringComparison.Ordinal);
        Assert.EndsWith("left to the way out's check: Impenetrability, Bludgeon Bane", gate, StringComparison.Ordinal);

        // the choice says it is the start's pick, and one line says what checks it
        Assert.Single(w.Lines, l => l.StartsWith("ladder: rung: asked by start: level=23 read_at=23", StringComparison.Ordinal) && l.Contains(" buffs=standing-now ", StringComparison.Ordinal) && l.EndsWith("-> rung=cave ground=tusker-cave reason=start-pick:cave", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("ladder: start: rung cave (tusker-cave) is the start's pick by what stands on her now, not yet the whole gate: Impenetrability, Bludgeon Bane of its gate cannot be read before a run", StringComparison.Ordinal) && l.Contains("check after: the run's way out reads the whole gate live before she leaves home", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_line_standing_the_start_picks_what_it_picked_before_and_says_why()
    {
        var w = Climber(seconds: 0); // the bot's spells lapsed, or she died

        Assert.Equal(Outskirts, Start(w, LevelUp));
        Assert.Equal(Outskirts, Start(w, null)); // no options: the read of before
        Print(w);

        Assert.Single(w.Lines, l => l.StartsWith("ladder: start: no line of the gates stands on her by the buff bot's spell with at least 30 s left (Focus Self (spell 2066: not on her)", StringComparison.Ordinal) && l.EndsWith("the gates are asked on the book alone, buffs unknown, as before; the run starts lower and moves on after its own stop at the bot", StringComparison.Ordinal));
        var asked = w.Lines.Where(l => l.StartsWith("ladder: rung: asked by start: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, asked.Count);
        Assert.Equal(asked[0], asked[1]); // the same choice line with and without the read
        Assert.Contains(" buffs=unknown ", asked[0], StringComparison.Ordinal);
        Assert.EndsWith("-> rung=outskirts ground=holtburg-outskirts reason=gate-held:cave", asked[0], StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.Contains("is the start's pick", StringComparison.Ordinal));
    }

    [Fact]
    public void The_read_is_made_only_with_the_level_up_jobs_options_and_the_stop_at_the_bot_on()
    {
        var w = Climber();

        Assert.Equal(Outskirts, Start(w, null));
        Assert.Equal(Outskirts, Start(w, LevelUp with { BuffBot = false })); // a run that makes no stop at the bot counts no spell of the bot's in a learned line's place either
        Assert.Equal(Outskirts, Start(w, new AutopilotOptions())); // the den's job
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("ladder: start: ", StringComparison.Ordinal));
        Assert.All(w.Lines.Where(l => l.StartsWith("ladder: rung: asked by start: ", StringComparison.Ordinal)), l => Assert.Contains(" buffs=unknown ", l, StringComparison.Ordinal));

        // a level whose band has no gate asks none: nothing is read and nothing is said (M6 14, D135: the outskirts to 9)
        var low = Climber(level: 9);
        Assert.Equal("holtburg-redoubt", Start(low, LevelUp)); // M6 redoubt: the rung outskirts' own ground while its portal admits her (to 20); the level 23 climber above is held on the rung's outdoor ground
        Assert.DoesNotContain(low.Lines, l => l.StartsWith("ladder: start: ", StringComparison.Ordinal));
        Assert.Single(low.Lines, l => l.StartsWith("ladder: rung: asked by start: level=9 read_at=9", StringComparison.Ordinal) && l.EndsWith(" buffs=unknown named=none -> rung=outskirts ground=holtburg-redoubt reason=band", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(29, Outskirts)] // about to lapse: not read standing
    [InlineData(31, Cave)]
    [InlineData(300, Cave)] // under the trip rule's line (BuffBotRule.TripUnderSeconds, 420): a relaunch at the bot's trip home; the run's stop at the bot renews the lines before its way out reads the gate
    public void Lines_with_too_few_seconds_left_are_not_read_standing(double seconds, string expected)
    {
        Assert.Equal(30, AutopilotHostLogic.StartStandsSeconds);
        Assert.True(AutopilotHostLogic.StartStandsSeconds < BuffBotRule.TripUnderSeconds);
        var w = Climber(seconds: seconds);

        Assert.Equal(expected, Start(w, LevelUp));
        Print(w);
        if (expected == Outskirts)
        {
            Assert.Single(w.Lines, l => l.StartsWith("ladder: start: no line of the gates stands on her by the buff bot's spell with at least 30 s left (Focus Self (spell 2066: 29 s left, under 30)", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void One_line_of_a_gate_under_the_seconds_holds_that_gate_shut_by_name_and_the_chain_reads_the_gates_below()
    {
        // Strength Self is a line of the Cave's and the Lodge's gates and none of the Burrow's: with it at five minutes those two read shut and the Burrow's, whose every line stands, is the start's pick,
        // as a run's own read chains (RungGateRule.Walk)
        var w = Climber(secondsOf: l => l.Line == "Strength Self" ? 20 : null);

        Assert.Equal(Burrow, Start(w, LevelUp));
        Print(w);

        Assert.Single(w.Lines, l => l.StartsWith("ladder: start: the gates are read with what stands on her now", StringComparison.Ordinal) && l.Contains("not standing: Strength Self (spell 2086: 20 s left, under 30)", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("ladder: gate cave: level 23: as the start reads it: ", StringComparison.Ordinal) && l.Contains("Strength Self up: NOT MET (" + RungGateRule.UnreadAtStart + ")", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("ladder: rung: asked by start: level=23 read_at=14", StringComparison.Ordinal) && l.EndsWith("-> rung=burrow ground=tusker-retreat reason=gate-held:cave", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("ladder: start: rung burrow (tusker-retreat) is the start's pick", StringComparison.Ordinal));
    }

    [Fact]
    public void An_armour_line_that_is_not_in_her_book_holds_the_gate_shut_whatever_stands()
    {
        // What is left to the way out is the armour lines' "up" alone: their scrolls are read from the book, by the start as by every read
        var w = Climber(armourLearned: false);

        Assert.Equal(Outskirts, Start(w, LevelUp));
        Print(w);

        Assert.Single(w.Lines, l => l.StartsWith("ladder: gate cave: level 23: as the start reads it: ", StringComparison.Ordinal) && l.Contains("Impenetrability III learned: NOT MET (not in the book)", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("is the start's pick", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gate_is_never_opened_by_what_a_start_left_unread_alone()
    {
        var cave = LadderLoader.WarMage.GateOf("cave")!;
        var q = new PlayerQualities();
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        Func<string, int, bool> wholeBook = static (_, _) => true;
        Func<string, string?> armour = static line => line is "Impenetrability" or "Bludgeon Bane" ? "on the worn pieces" : null;

        // nothing stands: every line "up" is NOT MET, the armour lines too (M6 7 AV: never on the book alone)
        var nothing = new GateStartRead(static (_, _) => null, armour);
        Assert.Empty(RungGateRule.LeftToTheWayOut(cave, nothing));
        var shut = RungGateRule.AtStart(cave, q, wholeBook, nothing).Where(c => c.Name.EndsWith(" up", StringComparison.Ordinal)).ToList();
        Assert.Equal(cave.Lines.Select(l => l.Line + " up"), shut.Select(c => c.Name));
        Assert.All(shut, c => { Assert.False(c.Met); Assert.Equal(RungGateRule.UnreadAtStart, c.Read); });

        // every line the state shows stands: those are met by what stands; the armour lines are no condition of the read, by name
        var stands = new GateStartRead((line, level) => armour(line) is null && level <= 7 ? "stands by spell 1" : null, armour);
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], RungGateRule.LeftToTheWayOut(cave, stands));
        var open = RungGateRule.AtStart(cave, q, static (line, _) => line is "Impenetrability" or "Bludgeon Bane", stands);
        Assert.All(open, c => Assert.True(c.Met, c.Name));
        Assert.Equal(cave.Lines.Count - 2, open.Count(c => c.Name.EndsWith(" up", StringComparison.Ordinal)));
        Assert.Contains(open, c => c is { Name: "Armor Self V learned", Met: true, Read: "stands by spell 1" });
        Assert.Contains(open, c => c is { Name: "Armor Self up", Met: true, Read: "stands by spell 1" });

        // the owner: a start's read is used only where no run read the buffs and no stop named the rung, and the pick is named
        RungChoice Ask(RungBuffRead? buffs = null, string? named = null) => RungOwner.Decide(LadderLoader.WarMage, new RungAsk(23, null, q, wholeBook, buffs, named) { StartRead = stands });
        var pick = Ask();
        Assert.Equal((Cave, "start-pick:cave", 23), (pick.Ground?.Key, pick.Reason, pick.HeldLevel));
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], pick.WayOutReads);
        var live = Ask(new RungBuffRead(static _ => 8, static _ => 4));
        Assert.Equal((Cave, "gate-open:cave"), (live.Ground?.Key, live.Reason)); // a run's live read is the whole gate: nothing is left out
        Assert.Empty(live.WayOutReads);
        Assert.Equal("gate-held:cave", Ask(new RungBuffRead(static name => name == "Impenetrability" ? 0 : 8, static _ => 4)).Reason); // and a live read that finds an armour line down shuts it, whatever the start read
        Assert.Empty(Ask(named: "cave").WayOutReads);
        Assert.Equal("stop-named:cave", Ask(named: "cave").Reason);
    }

    [Fact]
    public void A_fresh_start_with_the_bots_lines_standing_builds_its_first_run_on_the_Cave_and_she_travels_there()
    {
        var c = new AutopilotMoveOnLoopTests.Climb(output, tripFirst: false, level: 23);
        foreach (var line in GroundCatalog.Get(Cave).Supplies.Stock)
        {
            c.World.Trip.Own(line.Name, line.Wcid, VendorRig.MiscType, 1, (ushort)line.Keep); // the Cave's stock in the pack, as the run before the relaunch left it: no supply trip is due
        }

        c.Press();
        c.Run(x => x.FightsOn(Cave));
        c.Print();

        Assert.Equal(AutopilotMode.Running, c.Logic.Mode);
        Assert.True(c.FightsOn(Cave), "she fights on the Cave");
        Assert.Equal([Cave], c.Grounds); // the first run, and the only one: no run on the outskirts, no sale of the Cave's stock, no move-on
        Assert.Equal((0, 0), (c.Asked, c.Logic.MoveOns));
        Assert.Empty(c.Stopped);
        Assert.Null(c.HandOvers[0]); // a cold start: its audit reads everything
        Assert.True(JourneyGrounds.At(StationWhere.Den, c.World.Trip.Vendor.State.Self.Position, GroundCatalog.Get(Cave)), "she stands on the Cave");
        Assert.Single(c.Lines, l => l.StartsWith("ladder: rung: asked by start: level=23 read_at=23", StringComparison.Ordinal) && l.EndsWith("-> rung=cave ground=tusker-cave reason=start-pick:cave", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("autopilot start: ", StringComparison.Ordinal) && l.Contains(" on tusker-cave ", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains("ladder: way out:", StringComparison.Ordinal)); // the way out read the whole gate open: she left
        Assert.DoesNotContain(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_starts_pick_that_the_runs_way_out_reads_shut_ends_in_one_move_on_to_the_lower_rung_and_never_loops()
    {
        // The start read the bot's lines standing and picked the Cave; the run then reads an armour line down (her own pass could not put Impenetrability up): its way out holds her at home, the run stops by the
        // lower rung's name, the dial restarts once there, and that run hunts on: its own check reads the gates shut as the way out did.
        var c = new AutopilotMoveOnLoopTests.Climb(output, tripFirst: false, level: 23, active: static name => name == "Impenetrability" ? 0 : 8);
        c.Press();
        c.Run(x => x.FightsOn(Outskirts));
        c.Print();

        Assert.Equal(AutopilotMode.Running, c.Logic.Mode);
        Assert.True(c.FightsOn(Outskirts), "she fights on the outskirts");
        Assert.Equal([Cave, Outskirts], c.Grounds);
        Assert.Equal((1, 1), (c.Asked, c.Logic.MoveOns)); // one move-on, one restart
        Assert.Empty(c.Stopped);
        Assert.Null(c.Logic.MoveOnRefusal);
        Assert.Single(c.Lines, l => l.StartsWith("ladder: start: rung cave (tusker-cave) is the start's pick", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: gate cave: level 23: ", StringComparison.Ordinal) && l.Contains("Impenetrability up: NOT MET (level 0 of 3)", StringComparison.Ordinal)); // the run's live read of the line the start could not read
        Assert.Single(c.Lines, l => l.StartsWith("ladder: way out: level 23", StringComparison.Ordinal) && l.Contains("she does not leave for tusker-cave: gate-held:cave", StringComparison.Ordinal));
        // M6 14 gate armour: the gate is shut on an armour line her book holds (Impenetrability III) but that is not up: named, and no town trip (no scroll answers it)
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: way out: level 23: gate armour: the gates read shut (cave", StringComparison.Ordinal) && l.Contains("only for want of armour lines she can buy or have the bot cast: Impenetrability: a scrivener sells it", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.StartsWith("ladder: way out: level 23: gate armour: her book holds each line the gates ask", StringComparison.Ordinal) && l.EndsWith("no trip", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains("gate armour: act:", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:", StringComparison.Ordinal));
        Assert.Single(c.Lines, l => l.StartsWith("autopilot stop=rung-moved:outskirts: ", StringComparison.Ordinal));
        Assert.Contains(c.Lines, l => l.Contains("ladder: rung: asked by move-on: level=23", StringComparison.Ordinal) && l.EndsWith("named=outskirts -> rung=outskirts ground=holtburg-outskirts reason=stop-named:outskirts", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Lines, l => l.Contains(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal));

        // and it stays so: she goes on hunting there, with no second move-on
        c.Run(frames: 400);
        Assert.Equal((1, 1), (c.Asked, c.Logic.MoveOns));
        Assert.Equal([Cave, Outskirts], c.Grounds);
        Assert.Empty(c.Stopped);
    }
}
