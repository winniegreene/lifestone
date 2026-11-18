using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 sprint (the owner's decision of 2026-11-18: the vitae burnt on a known-good ground first, the corpse after): after a death the vitae is burnt on the rung below and the corpse fetched after (<see cref="VitaeBurn"/>). Proved: ACE's rot time
/// (<c>Corpse.cs</c> 110, max(3600, level x 300) s) and the margin the burn keeps before it; the burn's holds, its end (the vitae gone, or the rot near), its release and the corpse taken only by a run on its ground; a second death keeps
/// the first holds; and the rung's owner hunts the rung below a held rung at every check, a way out and the host's restart too, and takes her rung again once the hold is released.
/// </summary>
public sealed class VitaeBurnTests(ITestOutputHelper output)
{
    private static Ladder L => LadderLoader.WarMage;

    private static RungAsk Ask(int level, string? run, bool leaving)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 340, 1, 3u, 0, 10u, 0, 0);
        return new RungAsk(level, null, q, static (_, _) => true, new RungBuffRead(static _ => 8, static _ => 4)) { RunGround = run, Leaving = leaving };
    }

    private static VitaeOwed Owed(string rung, string ground, int level, double diedAt = 100, uint cell = 0x01D90123) => new(new DeathTally(), cell, ground, rung, level, diedAt);

    [Theory]
    [InlineData(1, 3600)]
    [InlineData(12, 3600)]
    [InlineData(13, 3900)]
    [InlineData(40, 12000)]
    [InlineData(70, 21000)]
    public void The_rot_time_is_ACEs_at_the_level_of_the_death(int level, double seconds)
    {
        Assert.Equal(seconds, DeathScenario.PlayerCorpseRotSeconds(level)); // Corpse.cs 110: Math.Max(3600, level * 300)
        Assert.Equal(100 + seconds - VitaeBurn.RotMarginSeconds, VitaeBurn.FetchBy(100, level));
    }

    [Fact]
    public void The_burn_holds_its_rung_and_ground_ends_on_the_vitae_or_the_rot_and_gives_the_corpse_only_to_its_grounds_run()
    {
        var burn = new VitaeBurn();
        var owed = Owed("abode", "tusker-abode", 40);
        burn.Defer(owed);
        Assert.True(burn.Burning);
        Assert.NotNull(burn.HeldWhy("abode"));
        Assert.NotNull(burn.HeldWhy("tusker-abode"));
        Assert.Null(burn.HeldWhy("cave"));

        Assert.Null(burn.DueWhy(0.95f, 100 + 60)); // the vitae on her, the rot far off
        Assert.Equal("the vitae is gone", burn.DueWhy(1f, 100 + 60));
        var rot = burn.DueWhy(0.95f, owed.FetchBy);
        Assert.NotNull(rot);
        Assert.Contains("level 40", rot, StringComparison.Ordinal);
        Assert.Equal(100 + 12000, owed.RotAt);

        Assert.Null(burn.TakeFor(_ => true)); // not before the burn is over
        burn.Release();
        Assert.False(burn.Burning);
        Assert.Null(burn.HeldWhy("abode"));
        Assert.Null(burn.TakeFor(_ => false)); // a run on another ground leaves it owed
        Assert.NotNull(burn.Owed);
        Assert.Same(owed.Tally, burn.TakeFor(cell => cell == 0x01D90123));
        Assert.Null(burn.Owed);
    }

    [Fact]
    public void A_death_during_the_burn_keeps_the_first_holds_so_she_goes_lower_still()
    {
        var burn = new VitaeBurn();
        burn.Defer(Owed("abode", "tusker-abode", 30));
        burn.Defer(Owed("cave", "tusker-cave", 30));
        Assert.NotNull(burn.HeldWhy("abode"));
        Assert.NotNull(burn.HeldWhy("cave"));
        Assert.Equal("cave", burn.Owed!.RungKey);

        var choice = RungOwner.Decide(L, Ask(30, "tusker-cave", leaving: false) with { VitaeHeld = burn.HeldWhy });
        output.WriteLine(choice.Why);
        Assert.Equal(("lodge", "tusker-lodge", "vitae-held:cave"), (choice.Decision.Rung?.Key, choice.Decision.Rung?.GroundKey, choice.Reason));
    }

    [Fact]
    public void A_held_rung_sends_her_to_the_rung_below_at_every_check_and_the_release_takes_her_rung_again()
    {
        var burn = new VitaeBurn();
        burn.Defer(Owed("abode", "tusker-abode", 30));

        // the cycle's check on the Abode after the death: the rung below, by name
        var check = RungOwner.Decide(L, Ask(30, "tusker-abode", leaving: false) with { VitaeHeld = burn.HeldWhy });
        output.WriteLine(check.Why);
        Assert.Equal((LadderStepKind.Moved, "rung-moved:cave", "tusker-cave", "vitae-held:abode"), (check.Decision.Kind, check.Decision.Stop, check.Decision.Rung?.GroundKey, check.Reason));
        Assert.Contains("until the vitae is gone", check.Why, StringComparison.Ordinal);

        // the host's restart on the named rung, and the run's own checks and way out there: the Cave stays hers
        var host = RungOwner.Decide(L, Ask(30, null, leaving: false) with { Buffs = null, NamedRung = "cave", VitaeHeld = burn.HeldWhy });
        Assert.Equal("tusker-cave", host.Ground?.Key);
        var onCave = RungOwner.Decide(L, Ask(30, "tusker-cave", leaving: false) with { VitaeHeld = burn.HeldWhy });
        Assert.Equal((LadderStepKind.Hunt, "tusker-cave"), (onCave.Decision.Kind, onCave.Ground?.Key));
        var wayOut = RungOwner.Decide(L, Ask(30, "tusker-cave", leaving: true) with { VitaeHeld = burn.HeldWhy });
        Assert.Equal((LadderStepKind.Hunt, "tusker-cave"), (wayOut.Decision.Kind, wayOut.Ground?.Key));

        // released: the cycle's check on the Cave takes the Abode again
        burn.Release();
        var back = RungOwner.Decide(L, Ask(30, "tusker-cave", leaving: false) with { VitaeHeld = burn.HeldWhy });
        Assert.Equal((LadderStepKind.Moved, "rung-moved:abode", "tusker-abode"), (back.Decision.Kind, back.Decision.Stop, back.Decision.Rung?.GroundKey));
    }

    [Fact]
    public void A_death_on_the_holding_rungs_alternate_holds_the_rung_and_the_ground_so_she_hunts_the_Quarters()
    {
        var holds = new ApproachHolds();
        holds.NoteAbandoned("tusker-holding", "held for the session at launch"); // the sprint's launch holds
        holds.Vitae.Defer(Owed("holding", "tusker-temple-fort-tethana", 66));
        var choice = RungOwner.Decide(L, Ask(66, "tusker-temple-fort-tethana", leaving: false) with { AheadHeld = holds.IsHeld, VitaeHeld = holds.Vitae.HeldWhy });
        output.WriteLine(choice.Why);
        Assert.Equal(("quarters", "tusker-quarters", "vitae-held:holding"), (choice.Decision.Rung?.Key, choice.Decision.Rung?.GroundKey, choice.Reason));
    }
}
