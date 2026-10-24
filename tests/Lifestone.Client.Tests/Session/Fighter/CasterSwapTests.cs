using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighters' caster swap (M4 P5a C1b, <see cref="CasterSwap"/> around <see cref="RebuffDriver"/>'s pass) on <see cref="C1bSwapRig"/>: the order of the server's asks for the Brawler (the Mace) and the Archer (the Longbow
/// and its arrows), the refusal that ends <c>swap:wield-caster</c> with the weapon back and peace, the mage that never swaps, and the half-done states the swap repairs.
/// </summary>
public sealed class CasterSwapTests
{
    private const string Heart = "The Healer's Heart";
    private const int Peace = 1;

    [Fact]
    public void A_brawlers_buff_pass_puts_the_Mace_aside_wields_the_caster_casts_and_ends_with_the_Mace_in_hand_in_peace()
    {
        var t = new C1bSwapRig(FighterTemplate.Melee);
        Assert.True(t.RunOnePass());

        Assert.Null(t.Driver.LastFailure);
        Assert.Equal(["unwield:" + C6Names.BrawlerWeapon, "wield:" + Heart, "mode:8"], t.Ops.Take(3));
        Assert.Equal(["mode:1", "unwield:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.Ops.TakeLast(3));
        Assert.DoesNotContain(t.Ops, o => o.StartsWith("wield-refused", StringComparison.Ordinal));
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(Peace, t.CombatMode);
        Assert.NotEmpty(t.Caster.Landed);
        Assert.True(t.Driver.LastResult!.Cast > 0);
        Assert.True(t.Swap!.Swapped);
        Assert.Matches(@" swapped=yes swap_s=\d+\.\d$", t.Driver.LastResult.Fields);
        Assert.Equal(t.Swap.Fields, t.Driver.LastResult.SwapFields);
        Assert.Equal(SwapStatus.Done, t.Swap.Status);
    }

    [Fact]
    public void An_archers_pass_takes_the_arrows_then_the_Longbow_off_and_puts_the_Longbow_then_the_arrows_back()
    {
        var t = new C1bSwapRig(FighterTemplate.Archer);
        Assert.True(t.RunOnePass());

        Assert.Null(t.Driver.LastFailure);
        Assert.Equal(["unwield:Greater Frost Arrow", "unwield:" + C6Names.ArcherBow, "wield:" + Heart, "mode:8"], t.Ops.Take(4));
        Assert.Equal(["mode:1", "unwield:" + Heart, "wield:" + C6Names.ArcherBow, "wield:Greater Frost Arrow"], t.Ops.TakeLast(4));
        Assert.DoesNotContain(t.Ops, o => o.StartsWith("wield-refused", StringComparison.Ordinal));
        Assert.Equal((uint)EquipMask.MissileWeapon, t.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal((uint)EquipMask.MissileAmmo, t.LocationOf(C1bSwapRig.AmmoGuid));
        Assert.Equal(0u, t.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(Peace, t.CombatMode);
        Assert.NotEmpty(t.Caster.Landed);
    }

    [Fact]
    public void A_refused_caster_ends_the_pass_swap_wield_caster_with_the_Mace_back_in_hand_and_peace_and_no_cast()
    {
        var t = new C1bSwapRig(FighterTemplate.Melee, refuseCaster: true);
        Assert.True(t.RunOnePass());

        Assert.Equal("swap:wield-caster", t.Driver.LastFailure);
        Assert.Equal(["unwield:" + C6Names.BrawlerWeapon, "wield-refused:" + Heart, "wield:" + C6Names.BrawlerWeapon], t.Ops);
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(Peace, t.CombatMode);
        Assert.Empty(t.Caster.Answered);
        Assert.Equal(RebuffDriverState.BackingOff, t.Driver.State);
        Assert.Equal(SwapStatus.Failed, t.Swap!.Status);
        Assert.Equal("swap:wield-caster", t.Swap.Failure);
        Assert.Contains("swap_why=swap:wield-caster", t.Swap.Fields, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_caster_with_the_Longbow_wielded_puts_the_arrows_and_the_Longbow_back_in_order()
    {
        var t = new C1bSwapRig(FighterTemplate.Archer, refuseCaster: true);
        Assert.True(t.RunOnePass());

        Assert.Equal("swap:wield-caster", t.Driver.LastFailure);
        Assert.Equal(["unwield:Greater Frost Arrow", "unwield:" + C6Names.ArcherBow, "wield-refused:" + Heart, "wield:" + C6Names.ArcherBow, "wield:Greater Frost Arrow"], t.Ops);
        Assert.Equal((uint)EquipMask.MissileWeapon, t.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal((uint)EquipMask.MissileAmmo, t.LocationOf(C1bSwapRig.AmmoGuid));
    }

    [Fact]
    public void The_half_by_hand_is_idempotent_a_second_In_moves_nothing_and_an_Out_with_nothing_recorded_wields_the_kits_weapon()
    {
        var t = new C1bSwapRig(FighterTemplate.Melee);
        Assert.Equal(SwapStatus.Done, t.RunSwap(SwapDirection.In));
        Assert.True(t.Swap!.Swapped);
        Assert.True(t.Swap.CasterWielded);
        Assert.Equal((int)CombatMode.Magic, t.CombatMode);
        Assert.Equal(["unwield-weapon", "wield-caster", "magic-mode"], t.Swap.Steps);

        // Already armed: nothing is moved, nothing is recorded, the verdict says so.
        Assert.Equal(SwapStatus.Done, t.RunSwap(SwapDirection.In));
        Assert.False(t.Swap.Swapped);
        Assert.Empty(t.Swap.Steps);
        Assert.StartsWith("swapped=no swap_s=", t.Swap.Fields, StringComparison.Ordinal);

        // The Out half has no record of the Mace now, so it wields the kit's own weapon from the pack.
        Assert.Equal(SwapStatus.Done, t.RunSwap(SwapDirection.Out));
        Assert.Equal((uint)EquipMask.MeleeWeapon, t.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(Peace, t.CombatMode);
    }

    [Fact]
    public void The_mage_wields_its_wand_already_and_its_pass_sends_no_wield_and_no_unwield()
    {
        Assert.Null(CasterSwap.ForCharacter(MageTemplate.CharacterName, null!, null!, null!));
        Assert.Null(CasterSwap.ForCharacter(FighterTemplate.HunterName, null!, null!, null!));
        Assert.Null(CasterSwap.ForCharacter(null, null!, null!, null!));
        Assert.Null(CasterSwap.For(FighterTemplate.Hunter, null!, null!, null!));

        var t = new RebuffRig();
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && t.Driver.State != RebuffDriverState.Running, 2000));
        Assert.Null(t.Driver.Swap);
        Assert.Empty(t.R.H.Net.Of(FakeSendKind.GetAndWieldItem));
        Assert.Empty(t.R.H.Net.Of(FakeSendKind.PutItemInContainer));
        Assert.Null(t.Driver.LastResult!.SwapFields);
        Assert.DoesNotContain("swapped=", t.Driver.LastResult.Fields, StringComparison.Ordinal);
    }

    [Fact]
    public void A_swap_exists_for_the_Brawler_and_the_Archer_only()
    {
        var rig = new WorldActionRig();
        foreach (var template in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            Assert.NotNull(CasterSwap.ForCharacter(template.Name, rig.Api, rig.H.State, rig.H.Log));
        }

        Assert.Throws<ArgumentException>(() => new CasterSwap(rig.Api, rig.H.State, FighterKit.Hunter, rig.H.Log));
    }

    [Fact]
    public void A_caster_that_is_not_held_fails_the_swap_by_name_and_sends_nothing()
    {
        var rig = new WorldActionRig();
        var swap = new CasterSwap(rig.Api, rig.H.State, FighterKit.Melee, rig.H.Log);
        Assert.True(swap.Begin(SwapDirection.In, rig.H.R.Clock.Now));
        for (var i = 0; i < 40 && swap.Status == SwapStatus.Running; i++)
        {
            rig.Tick(0.25);
            swap.Step(rig.H.R.Clock.Now);
        }

        Assert.Equal(SwapStatus.Failed, swap.Status);
        Assert.Equal("swap:caster-missing", swap.Failure);
        Assert.Empty(swap.Steps);
    }
}
