using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// M5 7 D142: the item window draws a line an enchantment raised in the raised colour and one it lowered in the lowered colour (<c>ItemExamineUI</c>'s mod_high_font 1 and mod_low_font 2, 11.6096 0x004ABB70; the masks read as
/// <c>AppraisalProfile::InqIntEnchantmentMod</c> 0x005B3CC0 and <c>InqFloatEnchantmentMod</c> 0x005B3D20 read them), and shows the armour's protections (<c>Appraisal_ShowArmorMods</c> 0x004B2600) and the defence bonuses
/// (<c>Appraisal_ShowDefenseModData</c> 0x004B2490). The answers are scripted 00C9s as the bundled ACE writes them (its masks: <c>ArmorMaskHelper</c>, <c>WeaponMaskHelper</c>, <c>ResistMaskHelper</c>).
/// </summary>
public sealed class GameExaminePanelEnchantedTests
{
    private const uint Item = 0x80000B01;

    private static Appraisal Answered(AceAppraisal info)
    {
        var r = new WorldActionRig();
        r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, Item, info));
        Assert.True(r.H.State.Appraisals.TryGet(Item, out var appraisal));
        return appraisal;
    }

    private static int ColorOf(ExamineItemPage page, string line)
    {
        var index = page.Lines.ToList().IndexOf(line);
        Assert.True(index >= 0, $"no line \"{line}\" in [{string.Join(" | ", page.Lines)}]");
        return page.ColorOf(index);
    }

    // The wand: a caster's mana conversion bonus (PropertyFloat 144) under the resist mask's 0x1000 (ACE ResistMask.ManaConversionMod), its melee defence bonus (29) under the weapon mask's 0x2.
    private static AceAppraisal Wand((ushort Highlight, ushort Color)? resist, (ushort Highlight, ushort Color)? weapon = null) => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x8000, [AppraisalIds.Value] = 900, [AppraisalIds.DamageType] = 0x10 },
        Floats = new Dictionary<uint, double> { [GameExaminePanel.ManaConversionModId] = 0.0575, [GameExaminePanel.ElementalDamageModId] = 1.03, [29] = 1.15 },
        ResistBits = resist,
        WeaponBits = weapon,
    };

    // The robe: an armour profile (ACE ArmorProfile's order: slash, pierce, bludgeon, cold, fire, acid, nether, lightning) and an armour level, under the armour mask.
    private static AceAppraisal Robe((ushort Highlight, ushort Color)? armor) => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x4, [AppraisalIds.Value] = 300, [AppraisalIds.ArmorLevel] = 120 },
        Armor = [1.4f, 0.6f, 1.0f, 0.3f, 1.7f, 2.5f, 1.0f, 0f],
        ArmorBits = armor,
    };

    [Fact]
    public void A_mana_conversion_bonus_hermetic_link_raised_is_drawn_raised_lowered_lowered_and_plain_without_the_bit()
    {
        const string Line = "Bonus to Mana Conversion: +6%.";
        Assert.Equal(ExamineColor.Raised, ColorOf(GameExaminePanel.ItemPage(Answered(Wand((0x1000, 0x1000))), new RecordingLogSink()), Line));
        Assert.Equal(ExamineColor.Lowered, ColorOf(GameExaminePanel.ItemPage(Answered(Wand((0x1000, 0))), new RecordingLogSink()), Line));
        Assert.Equal(ExamineColor.Normal, ColorOf(GameExaminePanel.ItemPage(Answered(Wand(null)), new RecordingLogSink()), Line));
        Assert.Equal(ExamineColor.Normal, ColorOf(GameExaminePanel.ItemPage(Answered(Wand((0x2000, 0x2000))), new RecordingLogSink()), Line)); // another bit: not this line's

        // The elemental bonus (0x2000) colours its three lines alike (Appraisal_ShowCasterData).
        var page = GameExaminePanel.ItemPage(Answered(Wand((0x2000, 0x2000))), new RecordingLogSink());
        Assert.Equal(ExamineColor.Raised, ColorOf(page, "Damage bonus for Fire spells:"));
        Assert.Equal(ExamineColor.Raised, ColorOf(page, " vs. Monsters: +3.0%."));
        Assert.Equal(ExamineColor.Raised, ColorOf(page, " vs. Players: +1.5%."));
        Assert.Equal(ExamineColor.Normal, ColorOf(page, "Value: 900"));
    }

    [Fact]
    public void The_melee_defence_bonus_shows_as_retail_and_takes_the_weapon_masks_colour_missile_and_magic_never_do()
    {
        var info = Wand(null, (0x2, 0x2)) with { Floats = new Dictionary<uint, double> { [29] = 1.15, [149] = 1.05, [150] = 0.98 } };
        var page = GameExaminePanel.ItemPage(Answered(info), new RecordingLogSink());
        Assert.Equal(ExamineColor.Raised, ColorOf(page, "Bonus to Melee Defense: +15.0%."));
        Assert.Equal(ExamineColor.Normal, ColorOf(page, "Bonus to Missile Defense: +5.0%."));
        Assert.Equal(ExamineColor.Normal, ColorOf(page, "Bonus to Magic Defense: -2.0%."));
        Assert.Equal(ExamineColor.Lowered, ColorOf(GameExaminePanel.ItemPage(Answered(info with { WeaponBits = (0x2, 0) }), new RecordingLogSink()), "Bonus to Melee Defense: +15.0%."));

        // A bonus of exactly 1 has no line.
        var none = Wand(null) with { Floats = new Dictionary<uint, double> { [29] = 1.0 } };
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(none), new RecordingLogSink()), l => l.StartsWith("Bonus to Melee Defense", StringComparison.Ordinal));
    }

    [Fact]
    public void Impenetrability_raises_the_armour_level_line_a_bane_its_protection_and_a_lure_cut_lowers_it()
    {
        // 0x1 the level, 0x2 slashing (a bane raised), 0x10 cold (lowered).
        var page = GameExaminePanel.ItemPage(Answered(Robe((0x1 | 0x2 | 0x10, 0x1 | 0x2))), new RecordingLogSink());
        var level = page.Lines.ToList().IndexOf("Armor Level: 120");
        Assert.True(level > 0);
        Assert.Equal(string.Empty, page.Lines[level - 1]); // retail's "\nArmor Level: "
        Assert.Equal(ExamineColor.Raised, page.ColorOf(level));
        Assert.Equal(
            ["Slashing: Above Average  (168)", "Piercing: Below Average  (72)", "Bludgeoning: Average  (120)", "Fire: Excellent  (204)", "Cold: Poor  (36)", "Acid: Unparalleled  (240)", "Electric: No  (0)", "Nether: Average  (120)"],
            page.Lines.Skip(level + 1).Take(8));
        Assert.Equal(ExamineColor.Raised, ColorOf(page, "Slashing: Above Average  (168)"));
        Assert.Equal(ExamineColor.Lowered, ColorOf(page, "Cold: Poor  (36)"));
        Assert.Equal(ExamineColor.Normal, ColorOf(page, "Piercing: Below Average  (72)"));

        Assert.All(GameExaminePanel.ItemPage(Answered(Robe(null)), new RecordingLogSink()).Colors, c => Assert.Equal(ExamineColor.Normal, c));
        Assert.Equal(ExamineColor.Lowered, ColorOf(GameExaminePanel.ItemPage(Answered(Robe((0x1, 0))), new RecordingLogSink()), "Armor Level: 120"));
    }

    [Fact]
    public void No_armour_profile_or_no_armour_level_shows_no_armour_lines_as_retail()
    {
        var noProfile = Robe(null) with { Armor = null };
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(noProfile), new RecordingLogSink()), l => l.StartsWith("Armor Level", StringComparison.Ordinal));
        var noLevel = Robe(null) with { Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x4, [AppraisalIds.ArmorLevel] = 0 } };
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(noLevel), new RecordingLogSink()), l => l.StartsWith("Armor Level", StringComparison.Ordinal) || l.StartsWith("Slashing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.5f, "Your armor will rend and slash you if hit.  (-50)")]
    [InlineData(0.0001f, "Slashing: No  (0)")]
    [InlineData(0.4f, "Slashing: Below Average  (40)")] // the float 0.4 is above the double 0.4 retail compares with
    [InlineData(0.39f, "Slashing: Poor  (39)")]
    [InlineData(1.19f, "Slashing: Average  (119)")]
    [InlineData(1.2f, "Slashing: Above Average  (120)")]
    [InlineData(3.0f, "Slashing: Unparalleled  (200)")] // counted as 2
    public void The_protection_words_follow_retails_thresholds(float modifier, string expected) => Assert.Equal(expected, GameExaminePanel.ProtectionLine("Slashing", 100, modifier));

    [Fact]
    public void The_weapon_damage_takes_the_damage_bit_else_the_variance_bit_and_the_speed_its_own()
    {
        var mace = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x1 },
            Weapon = new AceWeaponProfile(4, 40, 44, 12, 0.25, 1.1, 0, 0, 1, 0),
        };
        var plain = GameExaminePanel.ItemPage(Answered(mace), new RecordingLogSink());
        Assert.Equal(ExamineColor.Normal, ColorOf(plain, "Damage: 9 - 12, Bludgeoning"));
        Assert.Equal(ExamineColor.Normal, ColorOf(plain, "Speed: Average (40)"));

        var blooded = GameExaminePanel.ItemPage(Answered(mace with { WeaponBits = (0x8 | 0x4, 0x8) }), new RecordingLogSink());
        Assert.Equal(ExamineColor.Raised, ColorOf(blooded, "Damage: 9 - 12, Bludgeoning"));
        Assert.Equal(ExamineColor.Lowered, ColorOf(blooded, "Speed: Average (40)")); // a slower weapon (ACE's colour bit is clear for a time enchantment above 0)

        Assert.Equal(ExamineColor.Lowered, ColorOf(GameExaminePanel.ItemPage(Answered(mace with { WeaponBits = (0x10, 0) }), new RecordingLogSink()), "Damage: 9 - 12, Bludgeoning")); // the variance alone
    }

    [Theory]
    [InlineData(0, 0, 0x1u, ExamineColor.Normal)]
    [InlineData(0x2, 0x2, 0x1u, ExamineColor.Normal)] // another value's bit
    [InlineData(0x1, 0x1, 0x1u, ExamineColor.Raised)]
    [InlineData(0x1, 0, 0x1u, ExamineColor.Lowered)]
    [InlineData(0x0, 0x1, 0x1u, ExamineColor.Normal)] // a colour bit with no highlight: not changed
    public void The_mask_reads_as_the_creature_windows_highlight_then_colour(int highlight, int color, uint bit, int expected) =>
        Assert.Equal(expected, GameExaminePanel.EnchantmentColor((ushort)highlight, (ushort)color, bit));
}

/// <summary>M5 7 D142: the worn items <c>--examine-worn-frames</c> inspects: the wielded caster, the worn robe (a piece named Robe first), and whether a page shows a coloured line.</summary>
public sealed class ExamineWornTargetsTests
{
    private const uint Self = 0x50000001;

    private static WorldObject Worn(uint guid, string name, uint itemType, uint? wielder = Self)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        return new WorldObject(basis with { Weenie = basis.Weenie with { WielderId = wielder } }, isSelf: false, createdAt: 0);
    }

    [Fact]
    public void The_caster_is_the_wielded_caster_and_the_robe_the_worn_piece_named_robe_else_the_first_worn_piece()
    {
        var wand = Worn(0x80000003, "Wand", 0x8000);
        var boots = Worn(0x80000001, "Leather Boots", 0x2);
        var robe = Worn(0x80000002, "Faran Robe with Hood", 0x4);
        var packed = Worn(0x80000000, "Staff", 0x8000, wielder: null); // in the pack: not wielded
        Assert.Same(wand, ExamineWornTargets.Pick([packed, boots, robe, wand], Self, ExamineWornKind.Caster));
        Assert.Same(robe, ExamineWornTargets.Pick([packed, boots, robe, wand], Self, ExamineWornKind.Robe));
        Assert.Same(boots, ExamineWornTargets.Pick([packed, boots, wand], Self, ExamineWornKind.Robe));
        Assert.Null(ExamineWornTargets.Pick([packed, boots], Self, ExamineWornKind.Caster));
        Assert.Null(ExamineWornTargets.Pick([wand, robe], 0, ExamineWornKind.Caster)); // no player yet

        Assert.False(ExamineWornTargets.ShowsEnchantment([ExamineColor.Normal, ExamineColor.Normal]));
        Assert.True(ExamineWornTargets.ShowsEnchantment([ExamineColor.Normal, ExamineColor.Raised]));
        Assert.True(ExamineWornTargets.ShowsEnchantment([ExamineColor.Lowered]));
    }
}
