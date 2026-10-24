using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>Group 2 of the hunt rows over the scripted rig (M4 4.1 fix2a): the weapon swap and its edge cases (M4 3.1 fix2). The rows that start from a swap the world left half done are in <c>HuntScenarioTests.cs</c> (they need no creature).</summary>
public sealed partial class HuntScenarioTests
{
    [Fact]
    public void A_missile_hunt_swaps_the_weapon_in_the_captures_order_shoots_and_restores_the_mace()
    {
        using var rig = Belt12(Hunt(HuntWeapon.Missile));

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.True(scenario.Returned);
        Assert.True(scenario.Restored);
        var lines = rig.Log.Lines.ToList();
        int At(string text) => lines.FindIndex(l => l.Contains(text, StringComparison.Ordinal));
        var order = new[] { At("the Mace to the pack"), At("wield the Shortbow"), At("wield the Arrows"), At("mode: Missile"), At("MissileAttack 1"), lines.FindIndex(l => l.Contains("to the pack", StringComparison.Ordinal) && l.Contains("the Arrows", StringComparison.Ordinal)), At("the Shortbow to the pack"), At("wield the Mace again") };
        Assert.All(order, i => Assert.True(i >= 0, string.Join(" | ", order)));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace is wielded again");
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack");
        Assert.True(rig.Held(FighterKit.ArrowWcid, out var arrows) && !arrows, "the Arrows are in the pack");
        Assert.Equal((int)CombatMode.NonCombat, rig.Session.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void Two_Arrow_stacks_are_merged_into_the_largest_which_is_wielded_by_its_guid_and_put_back_in_the_pack()
    {
        // Live round 3: the fighter-build top-up had added a second Arrow stack, and the wield of the first one found was not seen in the read model. Two stacks of the class are held (the scripted copy has the kit stack's size).
        using var rig = Belt12(Hunt(HuntWeapon.Missile));
        var second = rig.Fighter.AddCopy(FighterKit.ArrowWcid);
        Assert.Equal(2, rig.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.ArrowWcid));

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.True(rig.Logged("merge the Arrows"), "the stacks were merged");
        var after = rig.Guids(FighterKit.ArrowWcid);
        Assert.Single(after);
        Assert.True(rig.Logged($"wield the Arrows 0x{after[0]:X8}"), "the wield names the stack by its guid");
        Assert.False(rig.Equipped(after[0]), "the stack is back in the pack");
        Assert.True(scenario.Restored);
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace is wielded again");
        Assert.NotEqual(0u, second);
    }

    [Fact]
    public void A_second_Shortbow_in_the_pack_does_not_stall_the_swap_and_the_restore_ends_with_the_Mace_and_no_launcher_3_1_fix2()
    {
        // The live round 4 defect: the loot adds a Shortbow, the pack copy is listed before the equipped one, and the lookup that returned the first of the class read "not wielded" for ever.
        using var rig = Belt12(Hunt(HuntWeapon.Missile));
        var second = rig.Fighter.AddCopy(FighterKit.ShortbowWcid);

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        var mace = rig.Guids(FighterKit.MaceWcid).Single();
        var first = rig.Guids(FighterKit.ShortbowWcid).Single(g => g != second);
        Assert.False(rig.Logged("still waiting"), "the swap's wait ended");
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace is wielded again");
        Assert.False(rig.Equipped(first) || rig.Equipped(second), "no Shortbow is wielded");
        Assert.Equal(2, rig.Guids(FighterKit.ShortbowWcid).Count);
        Assert.DoesNotContain(rig.Guids(FighterKit.ArrowWcid), rig.Equipped);
        // The Shortbow the swap wielded went back by its own guid, and before the Mace; the other copy was never touched.
        var wielded = rig.Sends.Single(x => x.Type == GameActionTypes.GetAndWieldItem && x.Second == (uint)EquipMask.MissileWeapon).First;
        Assert.Contains(wielded, new[] { first, second });
        var other = wielded == first ? second : first;
        Assert.DoesNotContain(rig.Sends, x => x.First == other);
        var bowBack = rig.LastSend(GameActionTypes.PutItemInContainer, wielded);
        var maceBack = rig.LastSend(GameActionTypes.GetAndWieldItem, mace);
        Assert.True(bowBack >= 0 && maceBack > bowBack, $"the Shortbow to the pack ({bowBack}) before the Mace is wielded ({maceBack})");
        Assert.True(rig.Logged($"wield the Shortbow 0x{wielded:X8}: confirmed") && rig.Logged($"the Shortbow to the pack 0x{wielded:X8}: confirmed"), "each step is logged by guid");
    }

    [Fact]
    public void A_looted_second_Mace_in_the_pack_is_left_there_the_wielded_one_is_swapped_and_restored_3_1_fix2()
    {
        using var rig = Belt12(Hunt(HuntWeapon.Missile));
        var looted = rig.Fighter.AddCopy(FighterKit.MaceWcid); // in the pack, listed after the wielded Mace

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        var mace = rig.Guids(FighterKit.MaceWcid).Single(g => g != looted);
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace), "the Mace the hunt put aside is the one wielded again");
        Assert.False(rig.Equipped(looted), "the looted Mace stays in the pack");
        Assert.DoesNotContain(rig.Sends, x => x.First == looted);
        Assert.Equal(2, rig.Guids(FighterKit.MaceWcid).Count);
        Assert.True(rig.Logged($"the Mace to the pack (ACE refuses the bow while it is wielded) 0x{mace:X8}"), "the step names the wielded Mace");
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack");
    }

    [Fact]
    public void A_missile_hunt_that_starts_from_a_half_done_swap_goes_on_from_the_wielded_Shortbow_and_restores_the_Mace_3_1_fix2()
    {
        using var rig = Belt12(Hunt(HuntWeapon.Missile));
        var mace = rig.Fighter.PutAway(FighterKit.MaceWcid); // the state a half-done swap leaves, before the login describes it
        var bow = rig.Fighter.Hold(FighterKit.ShortbowWcid, EquipMask.MissileWeapon);

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.True(scenario.Tally.Shots >= 1, scenario.VerdictFields());
        Assert.False(rig.Logged("the Mace to the pack (ACE"), "the Mace was already in the pack: the swap does not move it again");
        Assert.False(rig.Logged("wield the Shortbow"), "the Shortbow was already wielded: it is not wielded again");
        Assert.True(scenario.Restored);
        Assert.True(rig.Equipped(mace) && !rig.Equipped(bow));
    }

    // ---------------------------------------------------------------------------------------------- M4 P4 arch: the Archer's Longbow and Greater Frost Arrows

    private static Func<RecordingLogSink, HuntScenario> ArcherMissileHunt() =>
        log => new HuntScenario(new HuntOptions { Weapon = HuntWeapon.Missile, Kills = 1, Seconds = 300, OpenCorpse = true, Loot = LootOptions.Default, Recovery = RecoveryOptions.Default, Sell = false, BuyKits = 0, AppraiseLoot = true }, log, _ => 40f, endSession: false, template: FighterTemplate.Archer);

    [Fact]
    public void The_archers_missile_hunt_wields_the_kits_Longbow_and_Greater_Frost_Arrows_never_the_old_Shortbow_and_Arrows_held_beside_them_P4_arch()
    {
        using var rig = HuntScriptRig.Create(ArcherMissileHunt(), FighterTemplate.Archer.Name);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        // Both are held: the archer built before keeps the Shortbow and 200 Arrows in its pack (listed after the kit's own).
        var oldBow = rig.Fighter.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ShortbowWcid)!.Item);
        var oldArrows = rig.Fighter.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ArrowWcid)!.Item);

        var status = rig.Run(600);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.True(rig.Logged("wield the " + C6Names.ArcherBow), "the Longbow was wielded");
        Assert.True(rig.Logged($"wield the Arrows 0x{rig.Guids(FighterKit.GreaterFrostArrowWcid)[0]:X8}"), "the Greater Frost Arrows were wielded, by their guid");
        Assert.False(rig.Logged("wield the Shortbow"), "the old Shortbow was never wielded");
        Assert.False(rig.Equipped(oldBow) || rig.Equipped(oldArrows), "the old items stay in the pack");
        Assert.DoesNotContain(rig.Guids(FighterKit.Archer.LauncherWcid), rig.Equipped);
        Assert.DoesNotContain(rig.Guids(FighterKit.GreaterFrostArrowWcid), rig.Equipped);
        Assert.True(rig.Scenario.Restored);
    }

    [Fact]
    public void An_old_Shortbow_and_Arrows_left_wielded_are_put_away_before_the_archer_wields_its_own_P4_arch()
    {
        using var rig = HuntScriptRig.Create(ArcherMissileHunt(), FighterTemplate.Archer.Name);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12);
        rig.Fighter.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ShortbowWcid)!.Item);
        rig.Fighter.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ArrowWcid)!.Item);
        var oldBow = rig.Fighter.Hold(FighterKit.ShortbowWcid, EquipMask.MissileWeapon);
        var oldArrows = rig.Fighter.Hold(FighterKit.ArrowWcid, EquipMask.MissileAmmo);

        var status = rig.Run(600);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.True(rig.Logged($"the other launcher (weenie 307) 0x{oldBow:X8} to the pack"), "the old launcher went to the pack by its guid");
        Assert.True(rig.Logged($"the other ammunition (weenie 300) 0x{oldArrows:X8} to the pack"), "the old ammunition went to the pack by its guid");
        Assert.True(rig.Logged("wield the " + C6Names.ArcherBow), "then the kit's own was wielded");
        Assert.False(rig.Equipped(oldBow) || rig.Equipped(oldArrows));
    }
}
