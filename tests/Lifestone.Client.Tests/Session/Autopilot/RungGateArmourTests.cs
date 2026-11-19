using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 (live 2026-11-17, cache/m6/14/live/ladder52): at level 79, launched with <c>--autopilot-hold-grounds=tusker-holding,tusker-armory</c>, the buff bot's round fell short at 04:55:00 (11 of 28 lines: no Armor Self or
/// Bludgeon Protection VII), her own pass put Armor Self I up (her book holds no higher), and at 04:55:43 the way out read every gate from the Holding's to the Burrow's shut ("read_at=9": the gate walk's held level) and she hunted
/// the outskirts for 93 minutes. The gates still hold her (D135: no gate is overridden); proved here: the way out names that the gates read shut only for want of armour lines she can buy or have the bot cast, and which of them
/// a town trip's scrolls answer (<see cref="RungOwner.ArmourWant"/>), once per set of lines in the process (<see cref="GateArmourTrips"/>); a gate shut on anything else names nothing; a rung far below her level is never
/// chosen without a loud line (report-only); and the grounds held at launch are read by every check, so a fresh start with the bot's lines standing picks the Quarters, not the Holding.
/// </summary>
public sealed class RungGateArmourTests
{
    private const string Holding = "tusker-holding";
    private const string Armory = "tusker-armory";
    private const string Quarters = "tusker-quarters";
    private const uint OutskirtsCell = 0xA9B30006; // where the way out of 04:55:43 read her
    private const uint TownCell = 0xA9B40019; // the Holtburg lifestone, a fresh launch's cell

    private static Ladder L => LadderLoader.WarMage;

    private static readonly string[] ArmourOwn = ["Impenetrability", "Bludgeon Bane"];

    private static readonly string[] BotArmour = ["Armor Self", "Bludgeon Protection Self"];

    /// <summary>Her qualities at 79: Item Enchantment trained, War Magic specialised high.</summary>
    private static PlayerQualities Her()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 340, 1, 3u, 0, 10u, 0, 0);
        return q;
    }

    /// <summary>Her book as the gate lines of 04:55:43 read it: her own banes and Impenetrability to VI, Armor Self I only, Bludgeon Protection under IV; every other gate line is the bot's, not in her book.</summary>
    private static bool Book(string line, int level) => line switch
    {
        "Impenetrability" or "Bludgeon Bane" => level <= 6,
        "Armor Self" => level <= 1,
        "Bludgeon Protection Self" => level <= 3,
        _ => false,
    };

    /// <summary>What stood by the bot's spell at 04:55:43: its creature lines at VII, never its armour lines.</summary>
    private static string? BotStands(string line, int level) => !BotArmour.Contains(line) && !ArmourOwn.Contains(line) && level <= 7 ? "stands by another's spell at level VII" : null;

    /// <summary>The live read after her own pass: Armor Self I, Bludgeon Protection V, her armour lines VI, the bot's creature lines VII.</summary>
    private static RungBuffRead Live { get; } = new(
        static line => line switch { "Armor Self" => 1, "Bludgeon Protection Self" => 5, "Impenetrability" or "Bludgeon Bane" => 6, _ => 7 },
        static line => line switch { "Armor Self" => 1, "Bludgeon Protection Self" => 3, _ => 6 });

    /// <summary>The holds of a launch with <c>--autopilot-hold-grounds=tusker-holding,tusker-armory</c>, parsed as the run parsed them.</summary>
    private static ApproachHolds LaunchHolds()
    {
        var o = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-hold-grounds=tusker-holding,tusker-armory"]));
        Assert.Empty(o.Errors);
        return o.ApproachHolds;
    }

    /// <summary>The way out's approach reads: the held grounds read as crossings given up; no other ground of the walk records an approach.</summary>
    private static ApproachVerdict? Judge(string ground) =>
        ground is Holding or Armory ? new ApproachVerdict(true, ground, ground + "'s approach", null, []) { Abandoned = "held for the session by --autopilot-hold-grounds (named at launch)" } : null;

    private static RungAsk WayOut() =>
        new RungAsk(79, OutskirtsCell, Her(), Book, Live) { StandsByAnother = BotStands, RunGround = Holding, Leaving = true, Approach = Judge, AheadHeld = LaunchHolds().IsHeld };

    // ------------------------------------------------------------------------------------------------ the way out of 04:55:43

    [Fact]
    public void The_way_out_of_04_55_43_still_holds_her_by_the_gates_and_says_so_loudly()
    {
        var ask = WayOut();
        var choice = RungOwner.Decide(L, ask);

        // D135 stands: every gate read shut, the walk holds her at 9
        Assert.Equal(("outskirts", LadderStepKind.Moved, 9, "gate-held:holding"), (choice.Decision.Rung?.Key, choice.Decision.Kind, choice.HeldLevel, choice.Reason));
        Assert.Equal(["holding", "quarters", "abode", "cave", "lodge", "burrow"], choice.Gates.Select(g => g.Gate.Rung));
        Assert.EndsWith("level=79 read_at=9 cell=0xA9B30006 run_ground=tusker-holding leaving=yes buffs=live named=none -> rung=outskirts ground=none reason=gate-held:holding", choice.Line("the way out", ask), StringComparison.Ordinal);

        // and the outskirts are far below 79: a warning, by name (report-only)
        Assert.StartsWith("ladder: rung: far below her level: rung outskirts (Holtburg's outskirts and the Redoubt, levels 5-9) at level 79: its band's top 9 is under half her level; chosen by reason=gate-held:holding", choice.FarBelow, StringComparison.Ordinal);
        var logged = new List<(LogSeverity, string)>();
        RungOwner.WarnLoud(choice, new Sink(logged), "autopilot");
        // M6 redoubt: and at 79 the Redoubt's portal (levels 1 to 20) does not admit her, so the rung's outdoor ground stands in its place, by name: the second loud line
        Assert.Equal(2, logged.Count);
        Assert.Equal((LogSeverity.Warning, choice.FarBelow), logged[0]);
        Assert.Equal(("holtburg-outskirts", "gate:entry:holtburg-redoubt:79>20"), (choice.Alternate, choice.EntryRefused));
        Assert.StartsWith("ladder: rung: entry refused: gate:entry:holtburg-redoubt:79>20: the rung outskirts's own ground is entered by a portal that does not admit her, so she hunts its outdoor ground holtburg-outskirts in its place (reason=gate-held:holding", logged[1].Item2, StringComparison.Ordinal);
    }

    [Fact]
    public void The_way_out_of_04_55_43_names_the_armour_lines_the_gates_want_and_which_a_town_trip_buys()
    {
        var want = RungOwner.ArmourWantOf(L, RungOwner.Decide(L, WayOut()));

        Assert.NotNull(want);
        Assert.Equal(["Armor Self", "Bludgeon Protection Self"], want!.Lines);
        Assert.Equal(["Armor Self", "Bludgeon Protection Self"], want.Unlearned); // a scrivener sells both; her book holds neither at the gates' levels
        Assert.StartsWith("the gates read shut (holding, quarters, abode, cave, lodge, burrow) only for want of armour lines she can buy or have the bot cast: ", want.Why, StringComparison.Ordinal);
        Assert.Contains("Armor Self: a scrivener sells it, the buff bot casts it", want.Why, StringComparison.Ordinal);
        Assert.Contains("Bludgeon Protection Self: a scrivener sells it, the buff bot casts it", want.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gate_shut_by_an_armour_line_she_holds_but_has_not_up_wants_no_trip()
    {
        // her book holds every armour line at VI; Armor Self is down (I up): the gates want it up, which no scroll answers (her pass or the bot's round does)
        var ask = new RungAsk(79, null, Her(), static (line, level) => ArmourOwn.Contains(line) || BotArmour.Contains(line) ? level <= 6 : false, Live) { StandsByAnother = BotStands, RunGround = Holding, Leaving = true };
        var want = RungOwner.ArmourWantOf(L, RungOwner.Decide(L, ask));

        Assert.NotNull(want);
        Assert.Contains("Armor Self", want!.Lines);
        Assert.Empty(want.Unlearned);
    }

    [Fact]
    public void A_gate_shut_by_anything_but_an_armour_line_names_no_armour_want()
    {
        // the bot's creature lines are not standing and not in her book: Focus Self and the rest shut the gates too
        var ask = new RungAsk(79, null, Her(), Book, Live) { RunGround = Holding, Leaving = true };
        var choice = RungOwner.Decide(L, ask);

        Assert.StartsWith("gate-held:", choice.Reason, StringComparison.Ordinal);
        Assert.Null(RungOwner.ArmourWantOf(L, choice));

        // and an open way out wants nothing
        var open = RungOwner.Decide(L, new RungAsk(64, null, Her(), static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = Quarters, Leaving = true });
        Assert.Null(RungOwner.ArmourWantOf(L, open));
    }

    [Fact]
    public void A_town_trip_for_a_set_of_armour_lines_is_taken_once_in_the_process()
    {
        var trips = new GateArmourTrips();
        Assert.False(trips.Tried(["Armor Self", "Bludgeon Protection Self"]));
        Assert.True(trips.TryNote(["Bludgeon Protection Self", "Armor Self"]));
        Assert.False(trips.TryNote(["Armor Self", "Bludgeon Protection Self"])); // the same set, in any order
        Assert.True(trips.Tried(["Bludgeon Protection Self", "Armor Self"]));
        Assert.True(trips.TryNote(["Armor Self"])); // another set is its own trip
        Assert.NotSame(new AutopilotOptions().GateArmourTrips, new AutopilotOptions().GateArmourTrips);
        var o = new AutopilotOptions();
        Assert.Same(o.GateArmourTrips, (o with { Minutes = 5 }).GateArmourTrips); // the runs' copies share the process's one
    }

    // ------------------------------------------------------------------------------------------------ the grounds held at launch

    [Fact]
    public void A_fresh_start_with_the_bots_lines_standing_reads_the_launch_holds_and_picks_the_Quarters_not_the_Holding()
    {
        // ladder47, ladder51: the bot's lines stood, the gates read open, the start picked the Holding (held at launch) and only the way out moved her on
        var stands = new GateStartRead(static (line, level) => !ArmourOwn.Contains(line) && level <= 7 ? "stands by the buff bot's spell (level VII)" : null, static line => ArmourOwn.Contains(line) ? "on the worn pieces" : null);
        RungAsk Start(ApproachHolds? holds) => new(79, TownCell, Her(), Book, null) { StartRead = stands, AheadHeld = holds is null ? null : holds.IsHeld };

        var unheld = RungOwner.Decide(L, Start(null));
        Assert.Equal(("holding", "start-pick:holding", 79), (unheld.Decision.Rung?.Key, unheld.Reason, unheld.HeldLevel)); // nothing held: the Holding, as before

        var held = RungOwner.Decide(L, Start(LaunchHolds()));
        Assert.Equal(("quarters", Quarters, "approach-session:holding"), (held.Decision.Rung?.Key, held.Ground?.Key, held.Reason));
        Assert.Contains($"{Armory} held by its approach this session", held.Why, StringComparison.Ordinal); // the Armory, held at launch too, is named and passed over
        Assert.Null(held.FarBelow);
    }

    [Fact]
    public void The_grounds_held_at_launch_are_held_for_every_check_and_no_way_out_releases_them()
    {
        var holds = LaunchHolds();
        Assert.True(holds.IsHeld(Holding));
        Assert.True(holds.IsHeld(Armory));
        Assert.False(holds.IsHeld(Quarters));
        Assert.False(holds.IsHeld("holding")); // a rung's key is held by a way out's approach hold, not by the launch
        Assert.False(holds.Release(Holding));
        Assert.True(holds.IsHeld(Holding));
    }

    [Fact]
    public void A_fresh_start_with_no_line_standing_is_held_by_the_gates_as_before_and_says_so_loudly()
    {
        // ladder50, ladder52 03:49:36: no line stands, every "up" is unread: the gates hold her to the outskirts (D135), with the far-below warning
        var ask = new RungAsk(79, TownCell, Her(), Book, null) { AheadHeld = LaunchHolds().IsHeld };
        var choice = RungOwner.Decide(L, ask);

        Assert.Equal(("outskirts", 9, "gate-held:holding"), (choice.Decision.Rung?.Key, choice.HeldLevel, choice.Reason));
        Assert.NotNull(choice.FarBelow);
    }

    [Theory]
    [InlineData(18, false)] // the outskirts' top 9 is half of 18
    [InlineData(19, true)]
    [InlineData(79, true)]
    public void Far_below_is_a_band_top_under_half_her_level(int level, bool far)
    {
        Assert.Equal(far, RungOwner.FarBelow(L.Rungs.Single(r => r.Key == "outskirts"), level));
    }

    [Fact]
    public void Her_own_bands_rung_carries_no_far_below_line()
    {
        Assert.Null(RungOwner.Decide(L, new RungAsk(64, null, Her(), static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4))).FarBelow);
        Assert.Null(RungOwner.Decide(L, new RungAsk(10, null, Her(), static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4))).FarBelow);
    }

    private sealed class Sink(List<(LogSeverity, string)> lines) : ILogSink
    {
        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => lines.Add((severity, message));
    }
}
