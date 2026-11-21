using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 armory (the owner, 2026-11-14: try the Tusker Armory as the climber's next ground): the Armory is the holding rung's first alternate, and the ladder takes it from 60 (65 before the owner's decision of 2026-11-20) while the Holding is held by its approach. Proved: at the way
/// out a held Holding (approach-held, approach-costly or approach-abandoned) is replaced by its first alternate whose own approach is recorded, judged open on her live state, and whose portal and route admit her, on the Holding's band
/// and gate, the alternate's ground; an alternate with no approach recorded (the Temple, the den, the Quarters) is never taken so; with the Armory held too she hunts the Quarters, as before; a rung taken ahead of its band and held
/// takes no alternate; a check that is not a way out keeps her on the Armory while the session holds the Holding (no move to it and back, the loop the session's holds end), and so does the host's restart; a way out that judges the
/// Holding open again releases its hold; and the trial switch takes <c>tusker-armory</c> from its portal's 50 on the Holding's gate.
/// </summary>
public sealed class ArmoryLadderTests(ITestOutputHelper output)
{
    private const string Armory = "tusker-armory";

    private const string Holding = "tusker-holding";

    private static Ladder L => LadderLoader.WarMage;

    /// <summary>An open crossing (survivable, paid) of <paramref name="ground"/>.</summary>
    private static ApproachVerdict Open(string ground) => new(true, ground, ground + "'s approach is survivable", null, []);

    /// <summary>A crossing given up this run (approach-abandoned), the measured rule's hold.</summary>
    private static ApproachVerdict Abandoned(string ground) => Open(ground) with { Abandoned = "she will not reach the portal above the reserve" };

    /// <summary>A crossing she cannot survive (approach-held).</summary>
    private static ApproachVerdict Unsurvivable(string ground) => new(false, ground, ground + "'s approach is not survivable at her state", null, []);

    /// <summary>
    /// A climber whose gates read open (every line in the book, up at VIII on the run's read): the gate is the band's, not the question here. War Magic specialised at base 400 (Focus and Self 100, as <c>LadderBandsTests</c>' climber):
    /// over the plan's 290 of level 65, so a move-on ahead of the band is hers when a plan is read.
    /// </summary>
    private static RungAsk Ask(int level, string? run, bool leaving)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 340, 1, 3u, 0, 10u, 0, 0);
        return new RungAsk(level, null, q, static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = run, Leaving = leaving };
    }

    private static Func<string, ApproachVerdict?> Judge(ApproachVerdict? holding, ApproachVerdict? armory) => key => key == Holding ? holding : key == Armory ? armory : null;

    [Fact]
    public void The_Armory_is_the_holding_rungs_first_alternate_and_the_one_a_held_crossing_can_send_her_to()
    {
        var holding = L.Rungs.Single(r => r.Key == "holding");
        Assert.Equal((Armory, (ushort?)0x5948, false), (holding.Alternates[0].GroundKey, holding.Alternates[0].Landblock, holding.Alternates[0].Pending));
        Assert.Equal([Armory, "tusker-temple-fort-tethana", "black-spawn-den-aluvian", "tusker-quarters"], holding.Alternates.Select(a => a.GroundKey));
        Assert.Null(RungOwner.AlternateRefusal(holding.Alternates[0], 65));
        Assert.All(holding.Alternates.Skip(1), a => Assert.StartsWith("no-approach: ", RungOwner.AlternateRefusal(a, 65), StringComparison.Ordinal)); // unknown never sends her
        Assert.StartsWith("portal: ", RungOwner.AlternateRefusal(holding.Alternates[0], 49), StringComparison.Ordinal); // the entry 22657's MinLevel 50
        Assert.Null(RungOwner.AlternateRefusal(holding.Alternates[0], 50));
        Assert.Contains("RungOwner.TakeAlternate", holding.Sources["alternates"], StringComparison.Ordinal);
        Assert.Same(L.GateOf("holding"), RungOwner.KillTaskGate(L, Armory)); // the Holding's gate guards it (the bot's VII lines)
        Assert.Equal(RungOwner.RunGuard(L, Holding), RungOwner.RunGuard(L, Armory));
    }

    [Fact]
    public void A_held_Holding_at_65_hunts_the_Armory_at_the_way_out_on_the_Holdings_band_and_gate()
    {
        // the run was built on the Holding (the host has no approach to read): the way out holds it and moves her to its alternate
        var moved = RungOwner.Decide(L, Ask(65, Holding, leaving: true) with { Approach = Judge(Abandoned(Holding), Open(Armory)) });
        output.WriteLine(moved.Why);
        Assert.Equal(("approach-abandoned:holding", Armory), (moved.Reason, moved.Alternate));
        Assert.Equal((LadderStepKind.Moved, "rung-moved:holding", "holding", Armory, (ushort?)0x5948), (moved.Decision.Kind, moved.Decision.Stop, moved.Decision.Rung!.Key, moved.Decision.Rung.GroundKey, moved.Decision.Rung.Landblock));
        Assert.Equal(AutopilotScenario.AlternateReason, moved.Decision.Reason);
        Assert.Equal((60, 126, 35), (moved.Decision.Rung.Band.From, moved.Decision.Rung.Band.To, moved.Decision.Rung.TargetMargin)); // the rung's band (from 60: M6 holding at 60), bolt plan and margin
        Assert.Contains(moved.Gates, g => g.Gate.Rung == "holding" && g.Open);
        Assert.Contains("the rung holding is held and she hunts its alternate tusker-armory in its place", moved.Why, StringComparison.Ordinal);
        Assert.Equal([Armory], moved.ApproachOpened);
        Assert.Empty(moved.HeldAlternates);
        Assert.Equal("holding", ApproachHolds.HeldRungOf(moved.Reason)); // the session holds the Holding

        // the next run, on the Armory: its way out reads the Holding again, held, and stays
        var stays = RungOwner.Decide(L, Ask(65, Armory, leaving: true) with { Approach = Judge(Unsurvivable(Holding), Open(Armory)) });
        Assert.Equal((LadderStepKind.Hunt, Armory, "approach-held:holding"), (stays.Decision.Kind, stays.Ground?.Key, stays.Reason));
    }

    [Fact]
    public void With_the_Armory_held_or_unjudged_she_hunts_the_Quarters_and_a_rung_taken_ahead_takes_no_alternate()
    {
        var both = RungOwner.Decide(L, Ask(65, Holding, leaving: true) with { Approach = Judge(Abandoned(Holding), Unsurvivable(Armory)) });
        Assert.Equal(("approach-abandoned:holding", "quarters", (string?)null), (both.Reason, both.Decision.Rung?.Key, both.Alternate));
        Assert.Equal([(Armory, "approach-held")], both.HeldAlternates);
        Assert.Contains("tusker-armory approach: tusker-armory's approach is not survivable", both.Why, StringComparison.Ordinal);

        var unjudged = RungOwner.Decide(L, Ask(65, Holding, leaving: true) with { Approach = Judge(Abandoned(Holding), null) });
        Assert.Equal(("quarters", (string?)null), (unjudged.Decision.Rung?.Key, unjudged.Alternate)); // no verdict: unknown never sends her
        Assert.Contains("tusker-armory not judged", unjudged.Why, StringComparison.Ordinal);

        // the Holding's own crossing open: the Holding, as before (its alternates are not read)
        var open = RungOwner.Decide(L, Ask(65, Holding, leaving: true) with { Approach = Judge(Open(Holding), Open(Armory)) });
        Assert.Equal((LadderStepKind.Hunt, Holding, (string?)null), (open.Decision.Kind, open.Ground?.Key, open.Alternate));

        // M6 holding at 60 (the owner's decision, 2026-11-20): the Holding's band starts at the level its entry portal admits, so no level takes it ahead of its band (D135's case, at 63 under a band from 65, is gone):
        // at 59 the portal refuses her, she hunts the Quarters, and its alternate is never read
        var below = Ask(59, "tusker-quarters", leaving: true) with { PlanWar = static _ => 290u, Approach = Judge(Abandoned(Holding), Open(Armory)) };
        Assert.Null(RungOwner.Ahead(L, 59, below.Qualities, below.PlanWar, out var refused));
        Assert.StartsWith("portal: ", refused, StringComparison.Ordinal);
        var under = RungOwner.Decide(L, below);
        Assert.Equal(("quarters", (string?)null), (under.Decision.Rung?.Key, under.Alternate));
    }

    [Fact]
    public void A_check_that_is_not_a_way_out_keeps_her_on_the_Armory_while_the_session_holds_the_Holding()
    {
        var holds = new ApproachHolds();
        // before any hold: the cycle's check on the Armory names the Holding (the band's own ground): the move a held rung made, to and fro, before M6 14 armory
        var before = RungOwner.Decide(L, Ask(65, Armory, leaving: false) with { AheadHeld = holds.IsHeld });
        Assert.Equal((LadderStepKind.Moved, "rung-moved:holding"), (before.Decision.Kind, before.Decision.Stop));

        Assert.True(holds.Note("holding", "approach-abandoned:holding"));
        var check = RungOwner.Decide(L, Ask(65, Armory, leaving: false) with { AheadHeld = holds.IsHeld });
        output.WriteLine(check.Why);
        Assert.Equal((LadderStepKind.Hunt, Armory, "approach-session:holding", Armory), (check.Decision.Kind, check.Ground?.Key, check.Reason, check.Alternate));
        Assert.Null(check.Decision.Stop);

        // the host's restart after the way out's rung-moved:holding (no run, no buffs read, the named rung): the Armory's ground is built
        var host = RungOwner.Decide(L, Ask(65, null, leaving: false) with { Buffs = null, NamedRung = "holding", AheadHeld = holds.IsHeld });
        Assert.Equal((Armory, "approach-session:holding"), (host.Ground?.Key, host.Reason));

        // the Armory held too this session: the Quarters, and no move from it while both stay held
        Assert.True(holds.Note(Armory, "approach-held:" + Armory));
        var quarters = RungOwner.Decide(L, Ask(65, "tusker-quarters", leaving: false) with { AheadHeld = holds.IsHeld });
        Assert.Equal((LadderStepKind.Hunt, "tusker-quarters", "approach-session:holding"), (quarters.Decision.Kind, quarters.Ground?.Key, quarters.Reason));
        Assert.Contains("tusker-armory held by its approach this session", quarters.Why, StringComparison.Ordinal);

        // a way out that judges them open again releases the holds: the checks take the Holding again
        var reopened = RungOwner.Decide(L, Ask(65, "tusker-quarters", leaving: true) with { AheadHeld = holds.IsHeld, Approach = Judge(Open(Holding), Open(Armory)) });
        Assert.Equal((LadderStepKind.Moved, "holding", Holding), (reopened.Decision.Kind, reopened.Decision.Rung?.Key, reopened.Decision.Rung?.GroundKey));
        Assert.Equal(["holding"], reopened.ApproachOpened);
        Assert.All(reopened.ApproachOpened, k => Assert.True(holds.Release(k)));
        Assert.False(holds.IsHeld("holding"));
        Assert.True(holds.IsHeld(Armory)); // judged only when the Holding is held again
    }

    [Fact]
    public void The_trial_switch_takes_the_Armory_from_its_portals_50_on_the_Holdings_gate()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));
        var on = Parse("--autopilot-job=level-up", "--autopilot-trial-ground=tusker-armory", "--autopilot-trial-minutes=30");
        Assert.Empty(on.Errors);
        Assert.Equal((Armory, 30.0), (on.Trial!.Ground, on.Trial.Minutes));

        Assert.StartsWith("portal: the entry portal 22657 of tusker-armory admits levels 50 and up; level 49 is not admitted", RungOwner.TrialRefusal(L, Armory, 49, out _), StringComparison.Ordinal);
        Assert.Null(RungOwner.TrialRefusal(L, Armory, 55, out var rung)); // on the Quarters' band (42-59), the Holding the rung after it
        Assert.Equal("holding", rung!.Key);
        Assert.Same(L.GateOf("holding"), RungOwner.TrialGate(L, rung));

        var trial = RungOwner.Decide(L, Ask(55, Armory, leaving: true) with { Trial = new LadderTrial(Armory), Approach = Judge(null, Open(Armory)) });
        Assert.True(trial.Trial!.Steers);
        Assert.Equal(("trial:" + Armory, Armory, (ushort?)0x5948), (trial.Reason, trial.Ground?.Key, trial.Decision.Rung?.Landblock));
        var held = RungOwner.Decide(L, Ask(55, Armory, leaving: true) with { Trial = new LadderTrial(Armory), Approach = Judge(null, Unsurvivable(Armory)) });
        Assert.False(held.Trial!.Steers);
        Assert.StartsWith("approach: tusker-armory's approach is not survivable", held.Trial.Refused, StringComparison.Ordinal);
    }
}
