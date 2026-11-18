using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.State;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 16, slice E5e4: the one read of "are this worn piece's spells on her" (<see cref="GearLight"/>) and the re-wield's check before (<see cref="GearLight.Relight"/>). What these prove: an activation limit on a
/// skill is read where an appraisal carries it (PropertyInt 176), so a piece ACE would refuse to light is never taken off (the live state of 2026-11-18, ladder67); a spell surpassed by her own stronger one is in her
/// registry on its own layer and reads lit (ACE's layering); a piece whose every unlit spell would only go under a stronger one of hers is left on; a piece put on before her lore rose, with a spell that would show,
/// is the one case that comes off and goes on; and a piece she could not wield back is never taken off.
/// </summary>
/// <remarks>
/// The spell rows: the ids, names and schools are the world's; Armor Self V and VI carry the retail SpellTable's category 115 and powers 200 and 250 (<c>GearLightDatTests</c> prints them); the other categories and
/// powers are stand-ins that keep each family apart, which the same dat tests prove of the real table (no cantrip shares a category with a buff of its stat).
/// </remarks>
public sealed class GearLightTests
{
    private const uint Her = 0x50000001, Bot = 0x50000777;
    private const uint ArmorSelfV = 1311, ArmorSelfVI = 1312, MinorFocus = 2581, FocusSelfVI = 1426, ImpenetrabilityV = 1485, MajorManaConversion = 2525;
    private const uint ArmourCategory = 115, FocusCategory = 102, MinorFocusCategory = 201, ManaConversionCantrip = 202, ImpenCategory = 301;
    private const uint ArmourType = 0x2;
    private const uint Chest = 0x200, ChestCoverage = 0x400;

    private static readonly Dictionary<uint, SpellInfo> Table = new()
    {
        [ArmorSelfV] = Row(ArmorSelfV, "Armor Self V", MagicSchool.LifeMagic, ArmourCategory, 200),
        [ArmorSelfVI] = Row(ArmorSelfVI, "Armor Self VI", MagicSchool.LifeMagic, ArmourCategory, 250),
        [FocusSelfVI] = Row(FocusSelfVI, "Focus Self VI", MagicSchool.CreatureEnchantment, FocusCategory, 300),
        [MinorFocus] = Row(MinorFocus, "Minor Focus", MagicSchool.CreatureEnchantment, MinorFocusCategory, 1),
        [MajorManaConversion] = Row(MajorManaConversion, "Major Mana Conversion Prowess", MagicSchool.CreatureEnchantment, ManaConversionCantrip, 2),
        [ImpenetrabilityV] = Row(ImpenetrabilityV, "Impenetrability V", MagicSchool.ItemEnchantment, ImpenCategory, 250),
    };

    private static SpellInfo Row(uint id, string name, MagicSchool school, uint category, uint power) =>
        WorldActionRig.Spell(id, SpellType.Enchantment) with { Name = name, School = school, Category = category, Power = power };

    private static SpellInfo? Spell(uint id) => Table.TryGetValue(id, out var row) ? row : null;

    /// <summary>Her numbers as the ladder67 log read them at 2026-11-18 00:00:37 (Arcane Lore 275: the re-wield's own check-before lines); the two defences are the test's.</summary>
    private sealed class Skills(int lore = 275, int missile = 120, int melee = 150, int level = 82) : IGearSkills
    {
        public int Level => level;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill switch { MageTemplate.ArcaneLore => lore, 7 => missile, MageTemplate.MeleeDefense => melee, _ => 300 };

        public int? Base(uint skill) => Current(skill);
    }

    /// <summary>An enchantment of her registry as ACE writes it: the spell, its layer, its category and power, who cast it.</summary>
    private static EnchantmentDesc On(uint spell, ushort layer, uint caster) =>
        new((ushort)spell, layer, (ushort)Table[spell].Category, 0, Table[spell].Power, 0, caster == Her ? 3600 : -1, caster, 0f, 0f, 0.0, EnchantmentRules.Additive, 0, 1f, 0, EnchantmentBucket.Additive);

    private static PlayerQualities Registry(params EnchantmentDesc[] on) => new() { IsKnown = true, Enchantments = [.. on] };

    /// <summary>A worn chest piece with mana, an ItemDifficulty and its spells.</summary>
    private static WornPiece Piece(uint guid, string name, int difficulty, Dictionary<uint, int>? more, params uint[] spells)
    {
        var ints = new Dictionary<uint, int> { [5] = 400, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = difficulty };
        foreach (var (k, v) in more ?? [])
        {
            ints[k] = v;
        }

        return new WornPiece(guid, name, ArmourType, Chest, ChestCoverage, SelfEquipTests.Appraised(guid, ints, null, spells));
    }

    [Fact]
    public void An_activation_limit_on_a_skill_is_read_where_an_appraisal_carries_it()
    {
        // ACE writes the limit's skill into PropertyInt 176 for an appraisal (AppraiseInfo.cs:410-411 @95ef426d); PropertyDataId 37, the server's own, is no assessment property and never arrives
        var asAppraised = SelfEquipTests.Appraised(1, new() { [AppraisalIds.ArcaneLore] = 115, [SelfEquipJudge.ItemSkillLevelLimitInt] = 144, [SelfEquipJudge.AppraisalItemSkillInt] = 7 });

        Assert.Equal(
            "activation: Missile Defense 120 < 144 (its spells would stay off; retail: \"Your Missile Defense is too low to use that item's magic.\")",
            SelfEquipJudge.ActivationUnmet(asAppraised, new Skills(missile: 120)));
        Assert.Null(SelfEquipJudge.ActivationUnmet(asAppraised, new Skills(missile: 144)));

        // what the judge read before: the level limit alone, its skill looked for where no appraisal puts it, so the limit was met whatever her skill
        var before = SelfEquipTests.Appraised(1, new() { [AppraisalIds.ArcaneLore] = 115, [SelfEquipJudge.ItemSkillLevelLimitInt] = 144 });
        Assert.Null(SelfEquipJudge.ActivationUnmet(before, new Skills(missile: 0)));
    }

    [Fact]
    public void A_spell_surpassed_by_her_own_stronger_one_is_in_her_registry_on_its_own_layer_and_reads_lit()
    {
        // ACE writes a surpassed spell to the next layer with the piece as its caster (EnchantmentManager.cs:157-175 @95ef426d): her Armor Self VI on layer 1, the breastplate's Armor Self V on layer 2
        const uint Breastplate = 0x80001001;
        var piece = Piece(Breastplate, "Breastplate", 200, null, ArmorSelfV);
        var registry = Registry(On(ArmorSelfVI, 1, Her), On(ArmorSelfV, 2, Breastplate));

        Assert.True(WearScore.LitOn(registry, Breastplate));
        var light = GearLight.Read(registry, Breastplate, piece.Appraisal!, Spell);
        Assert.True(light.Lit && light.Done);
        Assert.Equal([new SpellLight(ArmorSelfV, "Armor Self V", SpellLightKind.LitSurpassed, "Armor Self VI")], light.Spells);

        var verdict = GearLight.Relight(registry, piece, new Skills(), Spell);
        Assert.Equal(RelightKind.Lit, verdict.Kind);
        Assert.Equal("its spells are on her, surpassed by her own Armor Self VI over its Armor Self V", verdict.Why);
    }

    [Fact]
    public void A_piece_whose_every_unlit_spell_would_only_go_under_a_stronger_one_of_hers_is_left_on()
    {
        // not lit (no entry cast by it), her lore meets its difficulty, and its one spell on her is Armor Self V under her own Armor Self VI: an off-and-on would show nothing
        const uint Breastplate = 0x80001002;
        var piece = Piece(Breastplate, "Breastplate", 200, null, ArmorSelfV, ImpenetrabilityV);
        var registry = Registry(On(ArmorSelfVI, 1, Her), On(FocusSelfVI, 1, Bot));

        var verdict = GearLight.Relight(registry, piece, new Skills(), Spell);

        Assert.Equal(RelightKind.Surpassed, verdict.Kind);
        Assert.Equal("Armor Self VI over its Armor Self V", verdict.Why);
        Assert.True(verdict.Light!.Done); // the check after counts the same state as done: nothing is left to light
        Assert.Equal(SpellLightKind.OnThePiece, verdict.Light.Spells[1].Kind); // Impenetrability lands on the piece (Creature_Magic.cs:128): her registry never shows it
    }

    [Fact]
    public void A_piece_put_on_before_her_lore_rose_whose_cantrip_would_show_is_taken_off_and_on_and_then_reads_lit()
    {
        // the case the re-wield was written for: worn while her lore was under its difficulty, so ACE cast nothing; her lore meets it now. Its Armor Self V would go under her own VI; its Minor Focus is a category
        // of its own that nothing of hers stands in (her Focus Self VI is another category), so it would show
        const uint Bracers = 0x80001003;
        var piece = Piece(Bracers, "Bracers", 270, null, ArmorSelfV, MinorFocus);
        var registry = Registry(On(ArmorSelfVI, 1, Her), On(FocusSelfVI, 1, Her));

        Assert.Equal(RelightKind.CannotLight, GearLight.Relight(registry, piece, new Skills(lore: 232), Spell).Kind); // before her lore rose: ACE would refuse again
        var verdict = GearLight.Relight(registry, piece, new Skills(lore: 275), Spell);
        Assert.Equal(RelightKind.Relight, verdict.Kind);
        Assert.Equal("ACE would activate it now and Minor Focus would show on her (Armor Self VI over its Armor Self V)", verdict.Why);
        Assert.False(verdict.Light!.Done);

        // after the off-and-on ACE has cast both: the cantrip on top of its category, the armour under hers
        var after = Registry(On(ArmorSelfVI, 1, Her), On(FocusSelfVI, 1, Her), On(ArmorSelfV, 2, Bracers), On(MinorFocus, 1, Bracers));
        var lit = GearLight.Read(after, Bracers, piece.Appraisal!, Spell);
        Assert.True(lit.Lit && lit.Done);
        Assert.Equal([SpellLightKind.LitSurpassed, SpellLightKind.Lit], lit.Spells.Select(s => s.Kind));
        Assert.Equal(RelightKind.Lit, GearLight.Relight(after, piece, new Skills(lore: 275), Spell).Kind);
    }

    /// <summary>
    /// The five pieces of ladder67 (2026-11-18 00:00:37 to 00:00:54 and 00:59:49 to 01:00:06): each was taken off and put on, and ACE answered each wield "Your Missile Defense is too low to use that item's magic."
    /// (the breastplate, the bracers, the tassets) or "Your Melee Defense ..." (the greaves, the girth). Known from the log: the names and guids, the skill ACE named, her Arcane Lore 275, that each holds mana. Assumed,
    /// for no examine line of them is in the log: the ItemDifficulty (met, as the old rule read it), the limit's level (above her skill, as ACE answered), her two defences, and one cantrip a piece.
    /// </summary>
    [Theory]
    [InlineData(0x8000DEEE, "Tenassa Breastplate", 7, "Missile Defense")]
    [InlineData(0x800087D8, "Diforsa Bracers", 7, "Missile Defense")]
    [InlineData(0x80005AF7, "Studded Leather Greaves", 6, "Melee Defense")]
    [InlineData(0x80005360, "Studded Leather Tassets", 7, "Missile Defense")]
    [InlineData(0x800039C9, "Studded Leather Girth", 6, "Melee Defense")]
    public void The_five_pieces_of_the_live_log_are_left_on_because_ACE_would_refuse_their_magic(uint guid, string name, int limitSkill, string skillName)
    {
        var registry = Registry(On(ArmorSelfVI, 1, Her), On(FocusSelfVI, 1, Her));
        var her = new Skills(lore: 275, missile: 120, melee: 150);
        var limit = new Dictionary<uint, int> { [SelfEquipJudge.ItemSkillLevelLimitInt] = 220, [SelfEquipJudge.AppraisalItemSkillInt] = limitSkill };

        // before: the limit's skill was not read (no appraisal carries PropertyDataId 37), the piece read as one her lore now lights, and it came off
        var asReadBefore = Piece(guid, name, 240, new() { [SelfEquipJudge.ItemSkillLevelLimitInt] = 220 }, MinorFocus);
        Assert.Equal(RelightKind.Relight, GearLight.Relight(registry, asReadBefore, her, Spell).Kind);

        // now: ACE's own answer is read before anything is sent, and the piece stays on
        var verdict = GearLight.Relight(registry, Piece(guid, name, 240, limit, MinorFocus), her, Spell);
        Assert.Equal(RelightKind.CannotLight, verdict.Kind);
        Assert.StartsWith($"activation: {skillName} {(limitSkill == 7 ? 120 : 150)} < 220 ", verdict.Why, StringComparison.Ordinal);
        Assert.Contains($"Your {skillName} is too low to use that item's magic.", verdict.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_she_could_not_wield_back_is_never_taken_off()
    {
        const uint Coat = 0x80001004;
        var piece = new WornPiece(Coat, "Coat", ArmourType, Chest, ChestCoverage, WithLevelRequirement(Coat, 100));

        var verdict = GearLight.Relight(Registry(), piece, new Skills(level: 82), Spell);

        Assert.Equal(RelightKind.CannotWieldBack, verdict.Kind);
        Assert.StartsWith("wield: level 82 < 100", verdict.Why, StringComparison.Ordinal);
    }

    /// <summary>A coat her lore lights, with a wield requirement of a level (WieldRequirements type 7, PropertyInt 158 to 160).</summary>
    private static Appraisal WithLevelRequirement(uint guid, int level) =>
        SelfEquipTests.Appraised(guid, new() { [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = 200, [158] = 7, [159] = 1, [160] = level }, null, MinorFocus);

    [Fact]
    public void What_the_re_wield_does_not_read_is_not_asked_and_nothing_is_called_done_on_a_guess()
    {
        var registry = Registry(On(ArmorSelfVI, 1, Her));
        var her = new Skills();

        var caster = new WornPiece(0x80001005, "Sceptre", SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, SelfEquipTests.Appraised(0x80001005, new() { [AppraisalIds.ArcaneLore] = 100 }, null, MinorFocus));
        Assert.Equal(RelightKind.NotAsked, GearLight.Relight(registry, caster, her, Spell).Kind);

        var noDifficulty = new WornPiece(0x80001006, "Cap", ArmourType, 0x1, 0x4000, SelfEquipTests.Appraised(0x80001006, new() { [AppraisalIds.ManaCurrent] = 100 }, null, MinorFocus));
        Assert.Equal(RelightKind.NotAsked, GearLight.Relight(registry, noDifficulty, her, Spell).Kind);

        // a helm whose spells all land on itself (the Leather Helm of the log, 01:02:13: Piercing Bane V, Impenetrability V, Lightning Bane V): her registry cannot show it lit, so nothing is decided on it
        var onItself = Piece(0x80001007, "Leather Helm", 115, null, ImpenetrabilityV);
        var verdict = GearLight.Relight(registry, onItself, her, Spell);
        Assert.Equal(RelightKind.NotAsked, verdict.Kind);
        Assert.False(verdict.Light!.Done);

        // a spell the SpellTable does not hold is not read and counts for nothing
        var unknown = Piece(0x80001008, "Girth", 115, null, 9999);
        Assert.Equal(SpellLightKind.Unread, GearLight.Read(registry, 0x80001008, unknown.Appraisal!, Spell).Spells[0].Kind);
        Assert.Equal(RelightKind.NotAsked, GearLight.Relight(registry, unknown, her, Spell).Kind);
    }

    [Fact]
    public void A_spell_of_equal_power_from_another_caster_does_not_surpass_and_a_stronger_one_from_the_piece_itself_does_not_count()
    {
        // ACE calls a spell surpassed only by a standing one of more power (AddEnchantmentResult.cs:119-123 @95ef426d); an equal one from another caster shares the top, so the piece's would show
        const uint Bracelet = 0x80001009;
        var piece = Piece(Bracelet, "Bracelet", 200, null, ArmorSelfV);

        Assert.Equal(RelightKind.Relight, GearLight.Relight(Registry(On(ArmorSelfV, 1, Bot)), piece, new Skills(), Spell).Kind);
        Assert.Equal(RelightKind.Surpassed, GearLight.Relight(Registry(On(ArmorSelfVI, 1, Bot)), piece, new Skills(), Spell).Kind);
    }
}
