using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 (D135, the owner, 2026-11-13: the optimal climb's bands for a fresh climber, the island from level 10, reversing the hold until 16 of 2026-11-06): the rung the ladder gives a fresh climber at the study's band edges
/// (10, 15, 25, 42, 59, 60), with the buff bot's lines up and with them down, and the move-on ahead of the band (<see cref="RungOwner.Ahead"/>). Proved: with the bot's lines up she is on the Burrow at 10, the Lodge at 15, the
/// Abode at 25, the Quarters at 42 and 59 and the Holding at 60 (M6 holding at 60, the owner's decision of 2026-11-20: from the level its entry portal admits; 65 by D135 before); with them down and nothing of her own up she is held back through every gate
/// to the outskirts, by name; her own lines at what she casts without the bot open the gates to the Quarters but never the Holding's, which asks the bot's level VII. The move-on: the rung after the band's is taken before its band
/// only when its portals admit her, its gate reads open and she is at least the plan's climber of its first level; one rung, never two; a run on it stays while that holds and leaves it when its gate shuts at the way out; the
/// fight's read, the host's named rung and a corpse run follow it; and with no plan read, or a shut gate, or a portal that refuses, the band decides.
/// </summary>
public sealed class LadderBandsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static Ladder L => LadderLoader.WarMage;

    private static readonly string[] Own = ["Impenetrability", "Bludgeon Bane"]; // the armour lines a player cannot cast on another (D127): hers

    private static bool Bot(string line) => BuffBotWire.LineNamed(line) is not null;

    /// <summary>A climber with Item Enchantment trained and War Magic specialised at <paramref name="war"/> base (Focus and Self at the creation's 100: (100 + 100) / 4 = 50, InitLevel 10, the rest ranks).</summary>
    private static PlayerQualities Climber(uint war = 60)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)(war - 60), 1, 3u, 0, 10u, 0, 0);
        return q;
    }

    /// <summary>A fresh climber's book: her own two armour lines at III; every other line of the gates stands by the bot's spell or not at all.</summary>
    private static bool FreshBook(string line, int level) => Own.Contains(line) && level <= 3;

    private static string? BotStands(string line, int level) => Bot(line) && level <= 7 ? "stands by the buff bot's spell (level VII)" : null;

    /// <summary>The bot's lines up (level VII) and her own armour lines up at III.</summary>
    private static RungBuffRead BotUp { get; } = new(line => Bot(line) ? 7 : Own.Contains(line) ? 3 : 0, _ => 4);

    /// <summary>The bot's lines lapsed and nothing of her own cast but the armour lines.</summary>
    private static RungBuffRead BotDown { get; } = new(line => Own.Contains(line) ? 3 : 0, _ => 4);

    private static RungAsk Ask(int level, RungBuffRead buffs, uint war = 60, string? run = null, bool leaving = false, Func<int, uint?>? plan = null, bool bot = true) =>
        new(level, null, Climber(war), bot ? FreshBook : static (_, l) => l <= 6, buffs) { StandsByAnother = bot ? BotStands : null, RunGround = run, Leaving = leaving, PlanWar = plan };

    [Theory]
    [InlineData(10, "burrow")] // by the Refuge's walk: the Town Network admits her from 10
    [InlineData(15, "lodge")] // the Facility Hub's tusker portals from 15
    [InlineData(25, "abode")] // on Flame Bolt V under the bot's lines
    [InlineData(42, "quarters")]
    [InlineData(59, "quarters")] // the Holding's entry admits her from 60
    [InlineData(60, "holding")] // its gate asks the bot's level VII armour lines: they stand
    public void With_the_bots_lines_up_a_fresh_climber_is_on_the_studys_rung(int level, string rung)
    {
        var choice = RungOwner.Decide(L, Ask(level, BotUp, plan: static l => 5000u)); // a plan she is never ahead of: the band decides

        Assert.Equal(rung, choice.Decision.Rung?.Key);
        Assert.Equal(LadderStepKind.Hunt, choice.Decision.Kind);
        Assert.Equal(level, choice.HeldLevel);
        Assert.Equal(L.Rungs.Single(r => r.Key == rung).GroundKey, choice.Ground?.Key);
        Assert.DoesNotContain(choice.Gates, g => !g.Open && !g.Ahead);
    }

    [Theory]
    [InlineData(10, "burrow")]
    [InlineData(15, "lodge")]
    [InlineData(25, "abode")]
    [InlineData(42, "quarters")]
    [InlineData(59, "quarters")]
    [InlineData(60, "holding")]
    public void With_the_bots_lines_down_and_nothing_of_her_own_up_she_is_held_back_through_every_gate(int level, string band)
    {
        var choice = RungOwner.Decide(L, Ask(level, BotDown));

        Assert.Equal("outskirts", choice.Decision.Rung?.Key);
        Assert.Equal(9, choice.HeldLevel);
        Assert.Equal("gate-held:" + band, choice.Reason); // the first gate that shut is her band's
        Assert.Contains(choice.Gates, g => g.Gate.Rung == band && g.Conditions.Any(c => c is { Name: "Armor Self up", Met: false }));
    }

    [Theory]
    [InlineData(10, 3, "outskirts", 9, "gate-held:burrow")] // her own Armor Self III: the Burrow asks IV (the hold of 2026-11-06, without the bot)
    [InlineData(42, 6, "quarters", 42, "band")] // her own VI meets the Abode's gate on the Quarters
    [InlineData(60, 6, "quarters", 59, "gate-held:holding")] // the Holding rests on the bot: its VII holds her in the Quarters
    public void Without_the_bot_her_own_lines_at_what_she_casts_open_the_gates_but_never_the_Holdings(int level, int own, string rung, int held, string reason)
    {
        var buffs = new RungBuffRead(_ => own, _ => own);
        var choice = RungOwner.Decide(L, Ask(level, buffs, bot: false) with { Knows = (_, l) => l <= own });

        Assert.Equal((rung, held), (choice.Decision.Rung?.Key, choice.HeldLevel));
        Assert.Equal(reason, choice.Reason.StartsWith("gate-open:", StringComparison.Ordinal) ? "band" : choice.Reason);
    }

    // ------------------------------------------------------------------------------------------------ the move-on ahead of the band

    private static Func<int, uint?> Plan(uint at) => _ => at;

    [Fact]
    public void A_climber_ahead_of_the_plan_moves_on_before_the_band_when_the_portal_admits_her_and_the_gate_reads_open()
    {
        var ahead = RungOwner.Ahead(L, 24, Climber(war: 200), Plan(200), out var why);
        Assert.Equal("abode", ahead?.Key);
        Assert.Contains("route by-facility-hub and entry 22654 admit her", why, StringComparison.Ordinal);

        var choice = RungOwner.Decide(L, Ask(24, BotUp, war: 200, run: "tusker-cave", plan: Plan(200)));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:abode", "ahead:abode", 24), (choice.Decision.Kind, choice.Decision.Stop, choice.Reason, choice.HeldLevel));
        Assert.Equal(AutopilotScenario.AheadReason, choice.Decision.Reason);
        Assert.True(choice.Opened); // a run's first check that reads this is a move-on through a gate read open, never a mismatch
        Assert.Contains(choice.Gates, g => g is { Ahead: true, Open: true } && g.Gate.Rung == "abode");

        // the fight's read takes it before the Cave's move-on level, in a lull, as a band's move-on
        var cave = L.Rungs.Single(r => r.Key == "cave");
        Assert.Equal("rung-moved:abode", RungOwner.MoveOnDue(L, cave, choice, "tusker-cave", null)?.Stop);
        Assert.Null(RungOwner.MoveOnDue(L, cave, RungOwner.Decide(L, Ask(24, BotUp, war: 199, run: "tusker-cave", plan: Plan(200))), "tusker-cave", null));
    }

    [Theory]
    [InlineData(24, 199u, "strength: her base War Magic 199 is under the plan's 200 at abode's first level 25")] // the plan's own climber: the band moves her
    [InlineData(29, 400u, "portal: no entry portal and route of tusker-quarters admit level 29")] // the hub's Quarters portal is MinLevel 30
    [InlineData(59, 400u, "portal: no entry portal and route of tusker-holding admit level 59")] // the Holding's entry is MinLevel 60
    [InlineData(9, 400u, "portal: no entry portal and route of tusker-retreat admit level 9")] // the Town Network's island portals are MinLevel 10 (M6 retreat: the burrow rung's ground is the Tusker Retreat)
    [InlineData(70, 400u, "band: holding is the last rung")]
    [InlineData(3, 400u, "band: academy-and-chest is a quest rung")]
    public void The_move_on_ahead_names_the_rule_that_stops_it(int level, uint war, string why)
    {
        Assert.Null(RungOwner.Ahead(L, level, Climber(war), Plan(200), out var read));
        Assert.Equal(why, read);
        if (L.RungFor(level).Kind == LadderRungKind.Ground)
        {
            Assert.Equal(L.RungFor(level).Key, RungOwner.Decide(L, Ask(level, BotUp, war, plan: Plan(200))).Decision.Rung?.Key); // the band decides
        }
    }

    [Fact]
    public void With_no_plan_read_no_rung_is_taken_ahead_and_the_portal_admitting_her_is_not_enough()
    {
        // M6 holding at 60 (2026-11-20): the Holding's band starts at its portal's 60, so the case is the Quarters' (its portal admits 30, its band starts at 42)
        Assert.Null(RungOwner.Ahead(L, 40, Climber(400), null, out var why));
        Assert.StartsWith("plan: ", why, StringComparison.Ordinal);
        Assert.Equal("abode", RungOwner.Decide(L, Ask(40, BotUp, war: 400)).Decision.Rung?.Key); // the Quarters' portal admits 40, its gate is open: the band still holds her

        Assert.Equal("quarters", RungOwner.Decide(L, Ask(40, BotUp, war: 400, plan: Plan(290))).Decision.Rung?.Key); // a climber over the plan of level 42 takes it at 40
        Assert.Equal("ahead:quarters", RungOwner.Decide(L, Ask(40, BotUp, war: 400, plan: Plan(290))).Reason);
    }

    [Fact]
    public void A_rung_held_by_its_approach_this_session_is_not_taken_ahead_of_its_band_and_its_own_band_still_takes_it_to_the_way_out()
    {
        // Live 2026-11-14 (the move-on loop): the caster spend raised her base War Magic to the plan's 290 at 63, the cycle's check took the Holding ahead of its band (then from 65), the way out held it by its approach
        // (approach-costly), and the host's restart took it ahead again. Once held, no check takes it ahead; her band's rung stays hers. The Holding's band now starts at its portal's 60 (M6 holding at 60), so the rule is shown on
        // the Quarters taken ahead from the Abode's band.
        var holds = new ApproachHolds();
        Assert.Equal("quarters", RungOwner.Decide(L, Ask(40, BotUp, war: 400, plan: Plan(290)) with { AheadHeld = holds.IsHeld }).Decision.Rung?.Key); // nothing held yet: ahead
        Assert.True(holds.Note("quarters", "approach-costly:quarters"));
        Assert.False(holds.Note("quarters", "approach-costly:quarters")); // noted once
        var held = RungOwner.Decide(L, Ask(40, BotUp, war: 400, plan: Plan(290)) with { AheadHeld = holds.IsHeld });
        Assert.Equal("abode", held.Decision.Rung?.Key);
        Assert.NotEqual("ahead:quarters", held.Reason);

        Assert.Equal("holding", ApproachHolds.HeldRungOf("approach-costly:holding"));
        Assert.Equal("holding", ApproachHolds.HeldRungOf("approach-abandoned:holding"));
        Assert.Equal("holding", ApproachHolds.HeldRungOf("approach-held:holding"));
        Assert.Null(ApproachHolds.HeldRungOf("gate-held:holding"));
    }

    [Fact]
    public void One_rung_ahead_never_two_and_the_band_stays_the_latest_she_moves()
    {
        // at 14 the Lodge (by the Refuge, its entry MinLevel 5) and the Cave (entry 12) both admit her: only the next rung is taken
        Assert.Equal("lodge", RungOwner.Decide(L, Ask(14, BotUp, war: 400, plan: Plan(100))).Decision.Rung?.Key);
        Assert.Equal("lodge", RungOwner.Ahead(L, 14, Climber(400), Plan(100), out _)?.Key);

        // the band moves her whatever her strength: at 25 the Abode, a climber far under the plan included
        var band = RungOwner.Decide(L, Ask(25, BotUp, war: 60, plan: Plan(5000)));
        Assert.Equal(("abode", "gate-open:abode"), (band.Decision.Rung?.Key, band.Reason));
    }

    [Fact]
    public void A_shut_gate_ahead_holds_her_on_her_band_and_is_no_hold()
    {
        // at 59, her own VI up and the bot's lines lapsed: the Quarters' gate is open, the Holding's (VII) shut and its portal (MinLevel 60) refuses her: she stays on the Quarters, never under it
        var buffs = new RungBuffRead(_ => 6, _ => 6);
        var choice = RungOwner.Decide(L, Ask(59, buffs, war: 400, run: "tusker-quarters", plan: Plan(200), bot: false));

        Assert.Equal((LadderStepKind.Hunt, "quarters", 59), (choice.Decision.Kind, choice.Decision.Rung?.Key, choice.HeldLevel));
        Assert.Null(RungOwner.Ahead(L, 59, Climber(400), Plan(200), out var why));
        Assert.StartsWith("portal: ", why, StringComparison.Ordinal);
        Assert.False(choice.Reason.StartsWith("gate-held:", StringComparison.Ordinal), choice.Reason);
    }

    [Fact]
    public void A_run_on_the_rung_ahead_stays_and_its_way_out_reads_the_gate_again()
    {
        // the cycle's check on the Quarters at 40, ahead of its band (M6 holding at 60: the Holding is never ahead of its band now): her own gate was read when she left for it
        var on = RungOwner.Decide(L, Ask(40, BotUp, war: 400, run: "tusker-quarters", plan: Plan(290)));
        Assert.Equal((LadderStepKind.Hunt, "quarters", "ahead:quarters"), (on.Decision.Kind, on.Decision.Rung?.Key, on.Reason));

        // the way out with the bot's lines up: she goes
        Assert.Equal(LadderStepKind.Hunt, RungOwner.Decide(L, Ask(40, BotUp, war: 400, run: "tusker-quarters", leaving: true, plan: Plan(290))).Decision.Kind);

        // the way out with them lapsed to her own IV: the Quarters' gate (V) is shut, so the run ahead is left by name
        var lapsed = RungOwner.Decide(L, Ask(40, new RungBuffRead(_ => 4, _ => 4), war: 400, run: "tusker-quarters", leaving: true, plan: Plan(290), bot: false));
        Assert.Equal(LadderStepKind.Moved, lapsed.Decision.Kind);
        Assert.NotEqual("quarters", lapsed.Decision.Rung?.Key);
    }

    [Fact]
    public void The_hosts_restart_takes_the_named_rung_ahead_and_a_corpse_on_it_is_reachable()
    {
        // the dial after rung-moved:abode at 24: no run, the hand-over's standing lines, the plan read
        var host = new RungAsk(24, null, Climber(200), FreshBook, null, "abode") { StandsByAnother = BotStands, PlanWar = Plan(200) };
        var named = RungOwner.Decide(L, host);
        Assert.Equal(("tusker-abode", "stop-named:abode"), (named.Ground?.Key, named.Reason));
        Assert.NotEqual("tusker-abode", RungOwner.Decide(L, host with { PlanWar = null }).Ground?.Key); // no plan read: the named rung above her band is ignored, and a host's unknown buffs open no gate

        // a corpse on the Abode at 24: its gate read open again, she may walk back to it
        var corpse = RungOwner.CorpseRun(L, Ask(24, BotUp, war: 200, plan: Plan(200)), "tusker-abode");
        Assert.Equal((true, "abode"), (corpse.Open, corpse.Rung));
        Assert.False(RungOwner.CorpseRun(L, Ask(24, BotUp, war: 199, plan: Plan(200)), "tusker-abode").Open); // the plan's climber: not hers before 25
    }

    [Fact]
    public void With_the_rung_holds_report_only_her_level_alone_decides_and_the_gates_are_still_read()
    {
        // M6 holding at 60 (the owner, 2026-11-20: the only thing that should stop her is her level): the bot's lines down shut every gate, yet at 60 she is on the Holding and at 59 on the Quarters; each gate is still read
        var at60 = RungOwner.Decide(L, Ask(60, BotDown) with { HoldsReportOnly = true });
        Assert.Equal(("holding", 60), (at60.Decision.Rung?.Key, at60.HeldLevel));
        Assert.Contains(at60.Gates, g => g.Gate.Rung == "holding" && !g.Open);
        Assert.Equal("quarters", RungOwner.Decide(L, Ask(59, BotDown) with { HoldsReportOnly = true }).Decision.Rung?.Key);
        Assert.Equal("outskirts", RungOwner.Decide(L, Ask(60, BotDown)).Decision.Rung?.Key); // the default holds, as before
    }

    [Fact]
    public void The_rung_holds_switch_reads_hold_and_report_and_refuses_anything_else()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));
        Assert.False(Parse("--autopilot-job=level-up").RungHoldsReportOnly);
        Assert.True(Parse("--autopilot-job=level-up", "--autopilot-rung-holds=report").RungHoldsReportOnly);
        Assert.False(Parse("--autopilot-job=level-up", "--autopilot-rung-holds=hold").RungHoldsReportOnly);
        Assert.Contains(Parse("--autopilot-job=level-up", "--autopilot-rung-holds=maybe").Errors, e => e.StartsWith("--autopilot-rung-holds: ", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void The_runs_plan_read_is_the_projection_the_spell_levels_are_computed_on()
    {
        var table = XpTableData.Load(fixture.Dats);
        var plan = SpellPlan.PlanWarOf(table)!;

        Assert.Null(SpellPlan.PlanWarOf(null));
        foreach (var level in new[] { 10, 15, 21, 25, 42, 65 })
        {
            Assert.Equal(SpellPlan.ProjectedWarBase(table, level), plan(level));
        }

        Assert.Equal(185u, plan(25)); // the Abode's first level: 185 plus about 60 from the bot is over Flame Bolt V's 243
        Assert.Null(plan(L.TopLevel + 1));
    }
}
