using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Tests.Fighter;

/// <summary>The fighter's kit as data: wcids, stacks, slots, burden and the capacity margin (no dats needed).</summary>
public sealed class FighterKitTests
{
    [Fact]
    public void The_kit_holds_the_wcids_and_stacks_of_the_research()
    {
        var expected = new (uint Wcid, int Stack)[] { (331, 1), (45, 1), (47, 1), (81, 1), (56, 1), (115, 1), (307, 1), (300, 200), (628, 2), (377, 5) };
        Assert.Equal(expected, FighterKit.Hunter.Items.Select(i => (i.Wcid, i.Item.Stack)).ToArray());
        Assert.Equal(expected.Length, FighterKit.Hunter.Items.Select(i => i.Wcid).Distinct().Count());
        Assert.Equal(new uint[] { 45, 47, 81, 56, 115 }, FighterKit.Hunter.Items.Where(i => i.Role == FighterRole.Armour).Select(i => i.Wcid).ToArray());
        Assert.All(FighterKit.Hunter.Items.Where(i => i.Item.MaxStack is not null), i => Assert.True(i.Item.Stack <= i.Item.MaxStack));
    }

    [Fact]
    public void The_healing_kit_and_the_potion_are_misc_items_0x80_not_zero()
    {
        // The lead's read-only SELECT (M4 3.1 s0, closes TODO(m4-kit-itemtype)): Handy Healing Kit 628 (Healer 28) and Potion of Healing 377 (Food 18) are ItemType 0x80, Misc.
        var kit = FighterKit.Hunter.Items.Single(i => i.Role == FighterRole.HealingKit).Item;
        var potion = FighterKit.Hunter.Items.Single(i => i.Role == FighterRole.Potion).Item;

        Assert.Equal((628u, FighterKit.WeenieTypeHealer, 0x80u), (kit.Wcid, kit.WeenieType, kit.ItemType));
        Assert.Equal((377u, FighterKit.WeenieTypeFood, 0x80u), (potion.Wcid, potion.WeenieType, potion.ItemType));
        Assert.Equal(0x80u, FighterKit.ItemTypeMisc);
        Assert.DoesNotContain(FighterKit.Hunter.Items, i => i.Item.ItemType == 0);
    }

    [Fact]
    public void The_weapons_name_their_skills_and_the_mace_is_wielded_while_the_bow_stays_in_the_pack()
    {
        var mace = FighterKit.Hunter.ItemOf(331)!;
        var bow = FighterKit.Hunter.ItemOf(307)!;
        Assert.Equal(44u, mace.SkillId);
        Assert.Equal(47u, bow.SkillId);
        Assert.Equal(FighterTemplateSkills.Mace, mace.SkillId);
        Assert.Equal(FighterTemplateSkills.Bow, bow.SkillId);
        Assert.True(mace.Wield);
        Assert.False(bow.Wield);
        Assert.Equal(EquipMask.MeleeWeapon, mace.Item.ValidLocations);
        Assert.Equal(EquipMask.MissileWeapon, bow.Item.ValidLocations);
        Assert.Equal(EquipMask.MissileAmmo, FighterKit.Hunter.ItemOf(300)!.Item.ValidLocations);
        Assert.Equal(FighterRole.Ammo, FighterKit.Hunter.ItemOf(300)!.Role);
    }

    [Fact]
    public void The_worn_and_wielded_slots_do_not_collide()
    {
        EquipMask seen = EquipMask.None;
        foreach (var item in FighterKit.Hunter.Worn)
        {
            var mask = item.Item.ValidLocations;
            Assert.NotEqual(EquipMask.None, mask);
            Assert.Equal(EquipMask.None, seen & mask);
            seen |= mask;
        }

        Assert.Equal(6, FighterKit.Hunter.Worn.Count());
        Assert.Equal(EquipMask.None, seen & (EquipMask.MissileWeapon | EquipMask.MissileAmmo));
    }

    [Fact]
    public void The_burden_is_4860_the_sum_of_the_lines_and_fits_the_capacity_with_a_wide_margin()
    {
        Assert.Equal(4_860, FighterKit.Burden);
        Assert.Equal(FighterKit.Burden, FighterKit.Hunter.TotalBurden);
        Assert.Equal(675 + 450 + 1_000 + 2_560 + 100 + 75, FighterKit.Hunter.TotalBurden);

        // Strength at creation (the floor) and at level 40 (the template's plan).
        var atCreation = FighterKit.Capacity(FighterKit.CapacityStrength);
        var atLevel40 = FighterKit.Capacity((int)FighterTemplate.Hunter.AttributeAtTarget(FighterTemplate.Strength));
        Assert.Equal(15_000, atCreation);
        Assert.Equal(29_100, atLevel40);
        Assert.True(FighterKit.Hunter.TotalBurden * 3 < atCreation, "the kit is under a third of the capacity at creation");
        Assert.True(atLevel40 - FighterKit.Hunter.TotalBurden > 20_000);
        Assert.Equal(FighterKit.Capacity(194), atLevel40);
    }

    [Fact]
    public void The_values_are_the_research_ones()
    {
        Assert.Equal([260, 1100, 150, 2400, 1100, 1100, 200, 1, 10, 170], FighterKit.Hunter.Items.Select(i => i.Value).ToArray());
    }
}

/// <summary>The template's weapon skill ids, for the kit test.</summary>
internal static class FighterTemplateSkills
{
    public const uint Mace = FighterTemplate.MaceSkill;
    public const uint Bow = FighterTemplate.BowSkill;
}
