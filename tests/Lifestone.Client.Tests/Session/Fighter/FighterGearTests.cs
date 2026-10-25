using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighters' magic gear as pinned data (M4 P5a C6, D40): every slot covered once, nothing in two slots, every difficulty and requirement inside what a level-55 fighter has, the burden and the main pack inside their limits, and the picks themselves pinned
/// so a change of the list is a deliberate one. The world database and the dats are checked by <c>FighterGearDatTests</c> and the Loopback-tier <c>FighterGearWorldDataLoopbackTests</c>.
/// </summary>
public sealed class FighterGearTests
{
    private static readonly FighterGearSet[] Sets = [FighterGearSet.Brawler, FighterGearSet.Archer];

    private static readonly Regex Harmful = new(@"Bane|Other|Feeble|Feeblemind|Ineptitude|Defenselessness|Fester|Curse|Corrupt|Vulnerab|Weakness|Clumsi|Slowness|Lethargy|Brittle|Futility|Bafflement|Drain|Harm|Lure|Hydra|Glare|Decay|Consumption|Durance|Infected|Caress", RegexOptions.CultureInvariant);

    [Fact]
    public void The_picks_are_pinned_by_weenie_class_in_kit_order()
    {
        Assert.Equal([45456u, 30528, 30518, 30367, 27912, 27128, 2032, 30531, 28396, 30364, 30363, 30366, 30356], FighterGearSet.Brawler.Pieces.Select(p => p.Wcid));
        Assert.Equal([30303u, 30525, 30369, 30523, 30527, 30530, 30517, 26501, 30357, 7300, 30361, 30353, 29093], FighterGearSet.Archer.Pieces.Select(p => p.Wcid));
        Assert.Empty(FighterGearSet.Brawler.Pieces.Select(p => p.Wcid).Intersect(FighterGearSet.Archer.Pieces.Select(p => p.Wcid)));
        Assert.Same(FighterGearSet.Brawler, FighterTemplate.Melee.Kit.Gear);
        Assert.Same(FighterGearSet.Archer, FighterTemplate.Archer.Kit.Gear);
        Assert.Null(FighterTemplate.Hunter.Kit.Gear); // the Hunter keeps its plain kit
    }

    [Fact]
    public void Each_set_covers_the_nine_armour_slots_once_and_has_a_weapon_a_necklace_two_rings_and_two_bracelets()
    {
        foreach (var set in Sets)
        {
            var armour = set.Pieces.Where(p => p.Kind == GearPieceKind.Armour).ToArray();
            uint used = 0;
            foreach (var piece in armour)
            {
                Assert.Equal(0u, used & piece.Locations); // no two pieces in one slot
                Assert.Equal(0u, piece.Locations & ~0x7FFFu); // body armour and clothing slots only
                used |= piece.Locations;
            }

            Assert.Equal(FighterGearSet.ArmourSlots, used & FighterGearSet.ArmourSlots); // head, hands, feet, chest, abdomen, upper and lower arms, upper and lower legs
            Assert.Equal(GearPieceKind.Weapon, set.Pieces[0].Kind);
            Assert.Single(set.Pieces, p => p.Kind == GearPieceKind.Weapon);
            Assert.Single(set.Pieces, p => p.Kind == GearPieceKind.Necklace && p.Locations == 0x8000);
            var rings = set.Pieces.Where(p => p.Kind == GearPieceKind.Ring).ToArray();
            var bracelets = set.Pieces.Where(p => p.Kind == GearPieceKind.Bracelet).ToArray();
            Assert.Equal(2, rings.Length);
            Assert.Equal(2, bracelets.Length);
            Assert.All(rings, r => Assert.Equal(0xC0000u, r.Locations)); // either finger
            Assert.All(bracelets, b => Assert.Equal(0x30000u, b.Locations)); // either wrist
            Assert.NotEqual(rings[0].Wcid, rings[1].Wcid);
            Assert.NotEqual(bracelets[0].Wcid, bracelets[1].Wcid);
            Assert.Equal(set.Pieces.Count, set.Pieces.Select(p => p.Wcid).Distinct().Count());
            Assert.True(armour.Select(p => p.Name).Distinct().Count() == armour.Length, "the pieces are mismatched: no two of one name");
        }
    }

    [Fact]
    public void Every_difficulty_and_requirement_is_inside_what_a_level_55_fighter_has_and_every_piece_has_helpful_spells_and_lasting_mana()
    {
        foreach (var set in Sets)
        {
            Assert.True(set.MaxItemDifficulty <= FighterGearSet.ArcaneLoreTarget, $"{set.Key}: {set.MaxItemDifficulty}");
            foreach (var piece in set.Pieces)
            {
                Assert.True(piece.ItemDifficulty <= FighterGearSet.ArcaneLoreTarget, piece.Name);
                Assert.All(piece.Requirements, r =>
                {
                    Assert.Equal(7, r.Requirement); // Level only: no skill or attribute requirement moves with the creation attributes
                    Assert.True(r.Difficulty <= FighterGearSet.Level, piece.Name);
                });
                Assert.NotEmpty(piece.Spells);
                Assert.DoesNotContain(piece.Spells, s => Harmful.IsMatch(s.Name));
                Assert.True(piece.ItemMana > 1 && piece.ManaRate < 0 && piece.ManaHours >= 4, $"{piece.Name}: {piece.ManaHours:0.#} h"); // ACE activates a piece's spells only with mana left (Player_Inventory.cs:314)
                Assert.Equal(piece.Spells.Count, piece.Spells.Select(s => s.Id).Distinct().Count());
            }
        }

        Assert.Equal((100, 110), (FighterGearSet.Brawler.MaxItemDifficulty, FighterGearSet.Archer.MaxItemDifficulty));
    }

    [Fact]
    public void The_kits_burden_fits_the_plans_strength_and_the_main_pack_keeps_its_headroom_with_the_components_in_the_side_packs()
    {
        foreach (var template in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            // M4 P5a merge (C5 + C6): the Archer is created with Strength 10 (capacity 1,500) and the kit is granted after the raises, so the capacity is the plan's Strength, not the creation value.
            var capacity = FighterKit.Capacity((int)template.AttributeAtTarget(FighterTemplate.Strength));
            var kit = template.Kit;
            Assert.True(kit.Gear!.Pieces.Where(p => p.Kind == GearPieceKind.Armour).Sum(p => p.Burden) <= 6_500);
            Assert.True(kit.TotalBurden < capacity, $"{template.Key}: {kit.TotalBurden} of {capacity}");
            Assert.True(kit.MainPackSlots + FighterKit.MainPackHeadroom <= GearKit.PackCapacity, $"{template.Key}: {kit.MainPackSlots}");
            Assert.Equal(FighterKit.SidePacksFor(kit.ComponentSlots), kit.Items.Single(i => i.Role == FighterRole.SidePack).Item.Stack);
            Assert.Equal(FighterKit.SidePackWcid, kit.Items.Single(i => i.Role == FighterRole.SidePack).Wcid);
            Assert.Equal(5, kit.Worn.Count(i => i.Role == FighterRole.Jewelry));
        }

        // The slots per fighter: the main pack, then the side packs (printed by the components' dat test with the real component lines). The gear's mana charge takes one main-pack slot (closes TODO(m4-gear-mana)).
        Assert.Equal((5, 7), (FighterKit.Melee.MainPackSlots, FighterKit.Archer.MainPackSlots));
    }

    [Fact]
    public void The_kit_lines_follow_the_gear_the_weapon_is_wielded_the_bow_stays_in_the_pack_and_the_swap_finds_both_by_role()
    {
        var melee = FighterKit.Melee;
        Assert.Equal(FighterGearSet.Brawler.Weapon.Wcid, melee.WeaponWcid);
        Assert.Equal(FighterRole.Weapon, melee.ItemOf(melee.WeaponWcid)!.Role);
        Assert.True(melee.ItemOf(melee.WeaponWcid)!.Wield);
        Assert.Equal(FighterKit.MaceSkillId, melee.ItemOf(melee.WeaponWcid)!.SkillId); // D23: a Heavy Weapons melee weapon, the melee combat mode
        Assert.Equal(EquipMask.MeleeWeapon, melee.ItemOf(melee.WeaponWcid)!.Item.ValidLocations);
        Assert.Null(melee.ItemOf(FighterKit.MaceWcid));
        var archer = FighterKit.Archer;
        Assert.Equal(FighterGearSet.Archer.Weapon.Wcid, archer.LauncherWcid);
        Assert.False(archer.ItemOf(archer.LauncherWcid)!.Wield);
        Assert.Equal(FighterKit.BowSkillId, archer.ItemOf(archer.LauncherWcid)!.SkillId);
        Assert.Equal(EquipMask.MissileWeapon, archer.ItemOf(archer.LauncherWcid)!.Item.ValidLocations);
        Assert.Equal(FighterKit.GreaterFrostArrowWcid, archer.AmmoWcid);
        Assert.Contains(archer.LauncherWcid, FighterItems.LauncherWcids);
        Assert.All(FighterGearSet.AllWcids, w => Assert.True(SellRules.IsKept(w), "the sale rule never sells the gear: " + w));
    }

    [Fact]
    public void The_refusal_names_every_piece_above_the_characters_arcane_lore_and_none_at_150()
    {
        Assert.Empty(FighterGearCheck.Refused(FighterGearSet.Brawler, 150));
        Assert.Empty(FighterGearCheck.Refused(FighterGearSet.Archer, 150));
        Assert.Equal([28396u], FighterGearCheck.Refused(FighterGearSet.Brawler, 99).Select(p => p.Wcid));
        Assert.Equal([7300u], FighterGearCheck.Refused(FighterGearSet.Archer, 109).Select(p => p.Wcid));
        Assert.Equal(2, FighterGearCheck.Refused(FighterGearSet.Archer, null).Count); // untrained: the pieces with a difficulty above 0
    }
}
