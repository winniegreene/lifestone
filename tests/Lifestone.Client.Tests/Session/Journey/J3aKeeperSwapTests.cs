using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The vitals keeper's casts inside a caster swap (M4 P5b lane J3a, closes <c>TODO(m4-keeper-swap)</c>) over the scripted rig <see cref="J3aSwapKeeperRig"/>: a fighter whose caster is in the pack casts Heal Self with one swap in before the casts and one swap out
/// after them (never one per cast), reports no <c>cast=needs-swap</c>, runs no swap when nothing is cast, and a refused caster blocks the keeper loudly with the weapon back in hand.
/// </summary>
public sealed class J3aKeeperSwapTests
{
    private const uint HealSelfIII = 1158;

    private static readonly uint[] HealSelfToIII = [6, 1157, HealSelfIII];

    private static C2Character Fighter(uint health = 100) => new() { HealingClass = 1, Caster = false, CasterInPack = true, Potions = 5, Spells = HealSelfToIII, Health = health };

    [Fact]
    public void A_fighter_with_the_caster_in_the_pack_swaps_in_once_casts_Heal_Self_and_swaps_out_once()
    {
        var rig = new J3aSwapKeeperRig(Fighter(), FighterTemplate.Melee);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        var wandIn = rig.Timeline.IndexOf("wield:" + GearKit.Wand.Name);
        var firstCast = rig.Timeline.FindIndex(t => t.StartsWith("cast:", StringComparison.Ordinal));
        var lastCast = rig.Timeline.FindLastIndex(t => t.StartsWith("cast:", StringComparison.Ordinal));
        var weaponBack = rig.Timeline.FindLastIndex(t => t.StartsWith("wield:", StringComparison.Ordinal));
        Assert.True(wandIn >= 0 && firstCast > wandIn, "the caster is wielded before the first cast: " + string.Join(" ", rig.Timeline));
        Assert.True(weaponBack > lastCast, "the weapon is wielded again after the last cast: " + string.Join(" ", rig.Timeline));
        Assert.Equal(2, rig.Timeline.Count(t => t.StartsWith("wield:", StringComparison.Ordinal))); // the caster in, the weapon back: once each, however many casts
        Assert.True(rig.Keeper.HealSelfCasts >= 1);
        Assert.Equal(1, rig.Keeper.Swaps);
        Assert.Null(rig.Keeper.CastOff);
        Assert.DoesNotContain("cast=needs-swap", rig.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" swaps=1", rig.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(SwapStatus.Done, rig.Swap.Status);
        Assert.NotEqual(0u, rig.LocationOf(J3aSwapKeeperRig.WeaponGuid)); // the Mace is in hand again
        Assert.Equal(0u, rig.LocationOf(C2VitalsRig.WandGuid)); // the caster is in the pack again
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_pass_that_casts_nothing_runs_no_swap()
    {
        var rig = new J3aSwapKeeperRig(Fighter(100) with { Spells = [] }, FighterTemplate.Melee);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(0, rig.Keeper.Swaps);
        Assert.DoesNotContain(rig.Timeline, t => t.StartsWith("wield", StringComparison.Ordinal) || t.StartsWith("unwield", StringComparison.Ordinal));
        Assert.True(rig.Keeper.Potions >= 1); // the potion heals as before
    }

    [Fact]
    public void A_refused_caster_blocks_the_keeper_with_the_swap_failure_and_the_weapon_is_back_in_hand()
    {
        var rig = new J3aSwapKeeperRig(Fighter(), FighterTemplate.Melee, refuseCaster: true);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Blocked, rig.RunToEnd());

        Assert.StartsWith("swap:", rig.Keeper.Why, StringComparison.Ordinal);
        Assert.Equal(rig.Keeper.Why, rig.Keeper.SwapFailure);
        Assert.DoesNotContain(rig.Timeline, t => t.StartsWith("cast:", StringComparison.Ordinal));
        Assert.NotEqual(0u, rig.LocationOf(J3aSwapKeeperRig.WeaponGuid)); // CasterSwap restored the weapon after the refused wield
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Blocked why=swap:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_keeper_without_a_swap_still_reports_needs_swap_and_casts_nothing()
    {
        var rig = new C2VitalsRig(Fighter());

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(VitalsKeeper.NeedsSwap, rig.Keeper.CastOff);
        Assert.Equal(0, rig.Keeper.Swaps);
    }
}
