using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 2 a: the per-slot score (<see cref="GearScore"/>) in the climber's setting: Winnie (Strength 10, capacity 1,500: the evening climb of 2026-11-01, cache/m6/0/climb, the station's burden line) on the outskirts' fire rung, the
/// Training Wand in the hand, the Pathwarden robe on. Every world number is the pinned world SQL v0.9.296's (the zip <c>WorldSqlDump.ZipParts</c> names), read offline, cited by its row id; a piece marked "illustrative" is not world data.
/// </summary>
public sealed class GearScoreTests
{
    private const uint Fire = 0x10, Acid = 0x20, Bludgeon = 0x4;
    private const uint MajorWar = 4001, MajorFocus = 4004, Useless = 4009;
    private const uint Me = 0x50000001;

    private static readonly Dictionary<uint, string> Names = new() { [MajorWar] = "Major War Magic Aptitude", [MajorFocus] = "Major Focus", [Useless] = "Minor Focus" };

    private static readonly string[] Useful = ["Major War Magic Aptitude", "Major Focus"];

    /// <summary>The climber's numbers: base War Magic <paramref name="warBase"/>, Strength <paramref name="strength"/> (null: unread), maximum mana <paramref name="maxMana"/>.</summary>
    private sealed class Climber(int level = 5, int warBase = 50, int? strength = 10, int? maxMana = 100) : IGearSkills
    {
        public int Level => level;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill switch { MageTemplate.WarMagic => warBase + 10, MageTemplate.ArcaneLore => 50, _ => null };

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? warBase : null;

        public int? Attribute(uint attribute) => attribute switch { 1 => strength, 5 => 60, _ => null };

        public int? VitalMax(uint vital) => vital == 5 ? maxMana : null;
    }

    private static GearContext Ground(IGearSkills? skills = null, uint element = Fire, IReadOnlyList<uint>? damageTypes = null, GearScoreNotes? notes = null) =>
        GearContext.For(skills ?? new Climber(), element, damageTypes, Useful, id => Names.GetValueOrDefault(id)) with { Notes = notes ?? new GearScoreNotes() };

    private static Appraisal Appraised(uint guid, Dictionary<uint, int> ints, Dictionary<uint, double>? floats = null, AppraisalArmorProfile? armor = null, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook | (armor is null ? IdentifyResponseFlags.None : IdentifyResponseFlags.ArmorProfile),
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            (floats ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            armor,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    // ------------------------------------------------------------------ the world's pieces (pinned world SQL v0.9.296)

    /// <summary>The Training Wand 12748: weenie_properties_int 340587 (ItemType 32768), 340588 (burden 50), 340592 (Value 25); weenie_properties_float 184521 (Float 29 = 1); no DamageType, no ManaConversionMod row.</summary>
    private static GearCandidate TrainingWand(uint guid = 0x80000005) =>
        new(guid, "Training Wand", 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, new() { [1] = 32768, [5] = 50, [19] = 25 }, new() { [29] = 1 }));

    /// <summary>The plain Wand 2472 (Archmage Cindrue's, create list row 21071): int 340498 (ItemType 32768), 340500 (burden 50), 340504 (Value 200); float 184504 (Float 29 = 1), 184505 (ManaConversionMod 144 = 0).</summary>
    private static GearCandidate PlainWand(uint guid = 0x90000001) =>
        new(guid, "Wand", 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, new() { [1] = 32768, [5] = 50, [19] = 200 }, new() { [29] = 1, [SelfEquipJudge.ManaConversionModFloat] = 0 }));

    /// <summary>
    /// The Aluvian Pathwarden Robe 40439: int 365360 (ClothingPriority 98048 = 0x17F00), 365361 (burden 200), 365363 (ValidLocations 32513 = 0x7F01), 365367 (ArmorLevel 50), 365368 (Bonded 1), 365371/365372 (item mana 1000/1000),
    /// 365374 (Attuned 1); float 197577 (ManaRate -0.033), 197579-197585 (ArmorModVs slash 0.8, pierce 0.8, bludgeon 1, cold 0.2, fire 0.2, acid 0.1, electric 0.2; no nether row: ACE sends 1, ArmorProfile.cs:37-38 @95ef426d);
    /// spell book 29225-29229 (1328, 1350, 1422, 1446, 1482 Impenetrability II).
    /// </summary>
    private static Appraisal PathwardenRobeAppraisal(uint guid, int mana = 1000) => Appraised(
        guid,
        new() { [5] = 200, [19] = 0, [28] = 50, [33] = 1, [107] = mana, [108] = 1000, [114] = 1 },
        new() { [GearScore.ManaRateFloat] = -0.033 },
        new AppraisalArmorProfile(0.8f, 0.8f, 1f, 0.2f, 0.2f, 0.1f, 1f, 0.2f),
        1328,
        1350,
        1422,
        1446,
        1482);

    private static GearCandidate PathwardenRobe(uint guid = 0x90000010) => new(guid, "Pathwarden Robe", 0x4, 0x7F01, 0x17F00, 200, PathwardenRobeAppraisal(guid));

    /// <summary>The Faran Robe with Hood 5851 (Ecutha's, 68 pyreals): int 39067 (ClothingPriority 98048), 39068 (burden 200), 39070 (ValidLocations 32513), 39072 (Value 50), 39074 (ArmorLevel 0); the same protections (14945-14951).</summary>
    private static WornPiece FaranRobeWorn(uint guid = 0x80000001) => new(guid, "Faran Robe with Hood", 0x4, 0x7F01, 0x17F00, Appraised(guid, new() { [5] = 200, [19] = 50, [28] = 0 }, null, new AppraisalArmorProfile(0.8f, 0.8f, 1f, 0.2f, 0.2f, 0.1f, 1f, 0.2f)));

    /// <summary>The Pathwarden Sollerets 33605: int 353113 (ClothingPriority 65536 = Feet), 353114 (burden 540), 353115 (ValidLocations 256), 353118 (ArmorLevel 120); float 190172-190179 (1.3, 1, 1, 0.4, 0.4, 0.6, 0.4, nether 1); spells 1398, 1482.</summary>
    private static GearCandidate PathwardenSollerets(uint guid = 0x90000011) => new(guid, "Pathwarden Sollerets", 0x2, 0x100, 0x10000, 540, Appraised(
        guid, new() { [5] = 540, [28] = 120, [33] = 1, [107] = 1000, [108] = 1000, [114] = 1 }, new() { [GearScore.ManaRateFloat] = -0.033 }, Plate, 1398, 1482));

    /// <summary>The Pathwarden Helm 33607: int 353143 (ClothingPriority 16384 = Head), 353144 (burden 600), 353145 (ValidLocations 1), 353148 (ArmorLevel 120); float 190192-190199 (as the sollerets'); spells 1422, 1446, 1482.</summary>
    private static GearCandidate PathwardenHelm(uint guid = 0x90000012) => new(guid, "Pathwarden Helm", 0x2, 0x1, 0x4000, 600, Appraised(
        guid, new() { [5] = 600, [28] = 120, [33] = 1, [107] = 1000, [108] = 1000, [114] = 1 }, new() { [GearScore.ManaRateFloat] = -0.033 }, Plate, 1422, 1446, 1482));

    /// <summary>The Pathwarden Gauntlets 33606: int 353128 (ClothingPriority 32768 = Hands), 353129 (burden 900), 353130 (ValidLocations 32), 353133 (ArmorLevel 120); float 190182-190189; spells 1374, 1482.</summary>
    private static GearCandidate PathwardenGauntlets(uint guid = 0x90000013) => new(guid, "Pathwarden Gauntlets", 0x2, 0x20, 0x8000, 900, Appraised(
        guid, new() { [5] = 900, [28] = 120, [33] = 1, [107] = 1000, [108] = 1000, [114] = 1 }, new() { [GearScore.ManaRateFloat] = -0.033 }, Plate, 1374, 1482));

    /// <summary>The Pathwarden Plate Hauberk 33597: int 352993 (ClothingPriority 15360 = 0x3C00), 352994 (burden 2500), 352995 (ValidLocations 7680), 352998 (ArmorLevel 120); float 190092-190099; spells 1328, 1482.</summary>
    private static GearCandidate PathwardenHauberk(uint guid = 0x90000014) => new(guid, "Pathwarden Plate Hauberk", 0x2, 0x1E00, 0x3C00, 2500, Appraised(
        guid, new() { [5] = 2500, [28] = 120, [33] = 1, [107] = 1000, [108] = 1000, [114] = 1 }, new() { [GearScore.ManaRateFloat] = -0.033 }, Plate, 1328, 1482));

    private static readonly AppraisalArmorProfile Plate = new(1.3f, 1f, 1f, 0.4f, 0.4f, 0.6f, 1f, 0.4f);

    private static WornPiece Worn(GearCandidate c) => new(c.Guid, c.Name, c.ItemType, c.ValidLocations, c.Coverage, c.Appraisal);

    /// <summary>Illustrative (not world data): a caster of <paramref name="element"/> with the bonus, the mana conversion and the spells given.</summary>
    private static GearCandidate Caster(uint guid, string name, uint element, double bonus, double mc = 0, Dictionary<uint, int>? ints = null, double? rate = null, params uint[] spells)
    {
        var i = new Dictionary<uint, int>(ints ?? []) { [SelfEquipJudge.DamageTypeInt] = (int)element };
        var f = new Dictionary<uint, double> { [AppraisalIds.ElementalDamageMod] = bonus, [SelfEquipJudge.ManaConversionModFloat] = mc };
        if (rate is { } r)
        {
            f[GearScore.ManaRateFloat] = r;
        }

        return new GearCandidate(guid, name, 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, i, f, null, spells));
    }

    private static SelfEquipJudge Judge(IReadOnlyList<uint>? damageTypes = null, GearScoreNotes? notes = null, uint element = Fire) => new(Useful, element, id => Names.GetValueOrDefault(id), damageTypes, notes);

    // ------------------------------------------------------------------ casters

    [Fact]
    public void The_Training_Wand_and_the_plain_Wand_score_the_same_so_the_held_one_stays()
    {
        var ground = Ground();
        var training = GearScore.Of(TrainingWand(), ground);
        var wand = GearScore.Of(PlainWand(), ground);

        Assert.Equal(GearSlot.Caster, training.Slot);
        Assert.Equal(0, training.Total);
        Assert.Equal(0, wand.Total);
        Assert.False(GearScore.CasterBeats(wand, training)); // neither has an element (no Int 45 row) nor a mana conversion above 0: the 175 pyreals buy no damage
        Assert.False(GearScore.CasterBeats(training, wand));
        Assert.StartsWith("gear score: caster 'Training Wand' 0x80000005 0 = intrinsic 0 (its appraisal is its own numbers), war 0 (no element (Int 45 absent): ACE adds no bonus and no aura (WorldObject_Weapon.cs:384)), mana conversion 0", training.ToString(), StringComparison.Ordinal);

        var choice = Judge().Judge(PlainWand(), [Worn(TrainingWand())], new Climber());
        Assert.Equal(SelfEquipKind.None, choice.Kind);
        Assert.StartsWith("not better: 'Wand' 0x90000001 score 0 (war +0%", choice.Why, StringComparison.Ordinal);
        Assert.Contains("on the plan's element 0x10", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wand_on_the_rungs_element_beats_a_higher_bonus_wand_of_another_element()
    {
        var ground = Ground();
        var fire = GearScore.Of(Caster(0x90000002, "Fire Wand", Fire, 1.02), ground);
        var acid = GearScore.Of(Caster(0x90000003, "Acid Wand", Acid, 1.05), ground);

        Assert.Equal(2, fire.WarPercent, 6);
        Assert.Equal(1, fire.Total, 6); // M6 14 (m6-caster): the score, 0.5 of the hour a percent of damage (the ladder's caster_score)
        Assert.Equal(0, acid.Total); // ACE applies the bonus only on the spell's element (WorldObject_Weapon.cs:382-400)
        Assert.Contains("its element 0x20 is not the rung's 0x10: its x1.05 counts 0", acid.ToString(), StringComparison.Ordinal);
        Assert.True(GearScore.CasterBeats(fire, acid));
        Assert.False(GearScore.CasterBeats(acid, fire));

        var held = Worn(Caster(0x90000003, "Acid Wand", Acid, 1.05));
        Assert.Equal(SelfEquipKind.Swap, Judge().Judge(Caster(0x90000002, "Fire Wand", Fire, 1.02), [held], new Climber()).Kind);
    }

    [Fact]
    public void The_rungs_element_comes_first_then_the_plans_family_then_the_named_TODO()
    {
        Assert.Equal(Acid, SelfEquipJudge.ElementOf("acid", "Flame Bolt", out var rung));
        Assert.Equal("the rung's element acid 0x20", rung);
        Assert.Equal(Fire, SelfEquipJudge.ElementOf(null, "Flame Bolt", out var plan));
        Assert.Equal("the spell plan's family Flame Bolt 0x10", plan);
        Assert.Equal(0x8u, SelfEquipJudge.ElementOf("none: the quest's own fights", "Frost Bolt", out var quest));
        Assert.Contains("(the rung's 'none: the quest's own fights' is no element)", quest, StringComparison.Ordinal);
        Assert.Equal(0u, SelfEquipJudge.ElementOf(null, null, out var none));
        Assert.StartsWith("TODO(m5-self-equip-element)", none, StringComparison.Ordinal);

        var acidRung = Judge(element: SelfEquipJudge.ElementOf("acid", "Flame Bolt", out _));
        Assert.True(acidRung.Beats(Caster(2, "Acid Wand", Acid, 1.05).Appraisal, Caster(3, "Fire Wand", Fire, 1.02).Appraisal));
    }

    [Fact]
    public void An_unwieldable_caster_scores_0_and_names_the_base_War_Magic_it_lacks()
    {
        var sceptre = Caster(0x90000004, "Fire Sceptre", Fire, 1.06, 0, new() { [158] = 2, [159] = 34, [160] = 290 }); // tier 5: RawSkill War Magic 290 (the ladder note section 11.1)

        var score = GearScore.Of(sceptre, Ground());

        Assert.Equal(0, score.Total);
        Assert.Equal(6, score.WarPercent, 6); // the comparison's key is kept: the judge says "better but" and reviews it
        Assert.StartsWith("wield: skill 34 base 50 < 290", score.Unmet, StringComparison.Ordinal);
        Assert.Contains("; 0: wield: skill 34 base 50 < 290", score.ToString(), StringComparison.Ordinal);
        Assert.Null(Judge().BestCaster([sceptre], [Worn(TrainingWand())], new Climber(), out var why));
        Assert.Contains("1 short of a wield requirement", why, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_mana_conversion_counts_whatever_the_casters_element()
    {
        // M6 14 (m6-caster; the owner, 2026-11-13): ACE multiplies her Mana Conversion by any wielded caster's bonus (GetWeaponManaConversionModifier reads no element, WorldObject_Weapon.cs:235-263), so the rehearsal of
        // 2026-10-29 14:07:39's Electric Sceptre with 0.05 is a real gain over a bare Training Wand; only its damage bonus counts 0 on Flame Bolt.
        var ground = Ground();
        var held = GearScore.Of(TrainingWand(), ground);
        var electric = GearScore.Of(Caster(3, "Electric Sceptre", 0x40, 1.05, 0.05), ground);

        Assert.True(GearScore.CasterBeats(GearScore.Of(Caster(2, "Fire Orb", Fire, 1.0, 0.05), ground), held));
        Assert.True(GearScore.CasterBeats(electric, held));
        Assert.Equal(0, electric.WarPercent);
        Assert.Equal(5, electric.ManaConversionPercent, 6);
    }

    [Fact]
    public void A_casters_cantrips_count_only_while_it_has_mana_and_its_hours_are_shown()
    {
        var ground = Ground();
        var charged = GearScore.Of(Caster(2, "Fire Baton", Fire, 1.0, 0, new() { [107] = 360, [108] = 500 }, -0.05, MajorWar, Useless), ground); // illustrative: 360 mana at 0.05 a second
        var empty = GearScore.Of(Caster(3, "Fire Baton", Fire, 1.0, 0, new() { [107] = 0, [108] = 500 }, -0.05, MajorWar), ground);
        var held = GearScore.Of(TrainingWand(), ground);

        Assert.Equal(1, charged.Cantrips);
        Assert.Equal(2.0, charged.ManaHours!.Value, 6);
        Assert.True(GearScore.CasterBeats(charged, held));
        Assert.Equal(0, empty.Cantrips);
        Assert.Contains("cantrips 0 (out of mana: its spells are off)", empty.ToString(), StringComparison.Ordinal);
        Assert.False(GearScore.CasterBeats(empty, held));
        Assert.Null(held.ManaHours);
    }

    // ------------------------------------------------------------------ armour

    [Fact]
    public void The_Pathwarden_robe_scores_its_eight_parts_with_its_own_Impenetrability_II()
    {
        var score = GearScore.Of(PathwardenRobe(), Ground(new Climber(strength: null)));

        Assert.Equal(GearSlot.Armour, score.Slot);
        Assert.Equal(8, score.Part("parts")); // 0x17F00: the six outerwear parts, the head and the feet (no hands)
        Assert.Equal(50, score.Part("impenetrability")); // spell 1482 Impenetrability II, +50 (the buff table's SQL row)
        Assert.Equal(3.3 / 7, score.Part("protection"), 5);
        Assert.Equal(100 * 3.3 / 7 * 8, score.Total, 2); // 377.14
        Assert.Equal(1000 / 0.033 / 3600, score.ManaHours!.Value, 4); // about 8.4 hours worn (the ladder note section 11.1)
        Assert.Contains("burden 0 (capacity unread (Strength unknown): not weighed)", score.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_Pathwarden_robe_replaces_the_Faran_robe_and_the_why_names_both_scores()
    {
        var faran = FaranRobeWorn();
        var choice = Judge().Judge(PathwardenRobe(), [faran, Worn(TrainingWand())], new Climber());

        Assert.Equal(SelfEquipKind.Replace, choice.Kind);
        Assert.Equal(faran.Guid, choice.Replaces);
        Assert.Equal(0x7F01u, choice.Location);
        Assert.StartsWith("gear: replace with 'Pathwarden Robe' 0x90000010: above the worn 'Faran Robe with Hood' 0x80000001 by more than x1.1: gear score: armour 'Pathwarden Robe' 0x90000010 326.86 = parts 8", choice.ToString(), StringComparison.Ordinal);
        Assert.Contains("against gear score: armour 'Faran Robe with Hood' 0x80000001 0 = ", choice.Why, StringComparison.Ordinal);
        Assert.False(choice.IsUpgrade); // the loot's keep-aside of heavy finds stays D149's
    }

    [Fact]
    public void With_the_Pathwarden_robe_on_the_helm_and_the_sollerets_stay_off_its_coverage_holds_the_head_and_the_feet()
    {
        var worn = new List<WornPiece> { Worn(PathwardenRobe()), Worn(TrainingWand()) };

        var helm = Judge().Judge(PathwardenHelm(), worn, new Climber());
        var sollerets = Judge().Judge(PathwardenSollerets(), worn, new Climber());

        Assert.Equal(SelfEquipKind.None, helm.Kind);
        Assert.StartsWith("not above the worn 'Pathwarden Robe' 0x90000010 by x1.1: gear score: armour 'Pathwarden Helm'", helm.Why, StringComparison.Ordinal); // one part against eight
        Assert.Equal(SelfEquipKind.None, sollerets.Kind);
        Assert.Contains("parts 1 (foot)", sollerets.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Armour_for_a_free_coverage_is_worn_the_Pathwarden_gauntlets_on_the_bare_hands()
    {
        var choice = Judge().Judge(PathwardenGauntlets(), [Worn(PathwardenRobe()), Worn(TrainingWand())], new Climber());

        Assert.Equal(SelfEquipKind.Wear, choice.Kind);
        Assert.Equal(0x20u, choice.Location);
        Assert.Contains("armour in a free coverage 0x8000: gear score: armour 'Pathwarden Gauntlets' 0x90000013 49.54 = parts 1 (hand)", choice.Why, StringComparison.Ordinal); // (120 + 50) x 0.7286 less 900 of 1,500 free
    }

    [Fact]
    public void M6_2_f_a_replace_never_leaves_the_chest_bare_the_sollerets_do_not_take_the_Faran_robe_off()
    {
        var worn = new List<WornPiece> { FaranRobeWorn(), Worn(TrainingWand()) };

        var choice = Judge().Judge(PathwardenSollerets(), worn, new Climber(strength: null));

        Assert.Equal(SelfEquipKind.None, choice.Kind); // before 2-R5 this was a Replace: the robe (AL 0, 0x17F00) scores 0 and any one-part piece in its coverage beat it
        Assert.Contains("only chest-covering piece worn", choice.Why, StringComparison.Ordinal);
        Assert.Contains("would leave the chest bare", choice.Why, StringComparison.Ordinal);
        Assert.Equal(SelfEquipKind.Replace, Judge().Judge(PathwardenRobe(), worn, new Climber(strength: null)).Kind); // a replacement that covers the chest still goes on
    }

    [Fact]
    public void A_replacement_needs_more_than_the_margin_and_an_appraised_cantrip_free_worn_piece()
    {
        static GearCandidate Cap(uint guid, int al, params uint[] spells) =>
            new(guid, "Leather Cap", 0x2, 0x1, 0x4000, 0, Appraised(guid, new() { [28] = al }, null, new AppraisalArmorProfile(1, 1, 1, 1, 1, 1, 1, 1), spells)); // illustrative
        var judge = Judge();
        var climber = new Climber(strength: null);

        Assert.Equal(SelfEquipKind.None, judge.Judge(Cap(2, 105), [Worn(Cap(1, 100))], climber).Kind); // x1.05: under the margin
        Assert.Equal(SelfEquipKind.Replace, judge.Judge(Cap(2, 115), [Worn(Cap(1, 100))], climber).Kind);
        Assert.Contains("is not appraised: no replacement on a guess", judge.Judge(Cap(2, 115), [new WornPiece(1, "Leather Cap", 0x2, 0x1, 0x4000, null)], climber).Why, StringComparison.Ordinal);
        Assert.Contains("casts Major Focus: kept", judge.Judge(Cap(2, 300), [Worn(Cap(1, 100, MajorFocus))], climber).Why, StringComparison.Ordinal);
        Assert.Equal(1.10, SelfEquipJudge.ReplaceMargin);
    }

    [Fact]
    public void A_piece_over_the_free_capacity_is_worth_nothing_and_a_heavy_one_pays_its_share()
    {
        var hauberk = GearScore.Of(PathwardenHauberk(), Ground()); // 2,500 over Strength 10's 1,500
        var loaded = GearScore.Of(PathwardenHelm(), Ground() with { CarriedBurden = 1000 }); // 600 over the 500 left
        var strong = Ground(new Climber(strength: 50)); // capacity 7,500

        Assert.Equal(0, hauberk.Total);
        Assert.Contains("2500 over the free capacity 1500 of 1500: worth nothing", hauberk.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, loaded.Total);
        Assert.Equal(170 * (5.1 / 7) * 4 * (1 - (2500.0 / 7500)), GearScore.Of(PathwardenHauberk(), strong).Total, 2);
        Assert.True(GearScore.Of(PathwardenHelm(), strong).Total > GearScore.Of(PathwardenHelm(), Ground()).Total); // the same helm costs less of a larger capacity
    }

    [Fact]
    public void Without_the_grounds_damage_types_the_stub_logs_its_TODO_once_a_run_and_every_line_names_it()
    {
        var lines = new List<string>();
        var notes = new GearScoreNotes(lines.Add);
        var ground = Ground(notes: notes);

        var robe = GearScore.Of(PathwardenRobe(), ground);
        _ = GearScore.Of(PathwardenSollerets(), ground);
        _ = Judge(notes: notes).Judge(PathwardenHelm(), [Worn(PathwardenRobe())], new Climber());

        Assert.Single(lines);
        Assert.StartsWith("gear score: TODO(m6-ground-damage-types):", lines[0], StringComparison.Ordinal);
        Assert.Equal(["m6-ground-damage-types"], notes.Noted);
        Assert.Contains("the mean of its seven protections from the armour profile: TODO(m6-ground-damage-types)", robe.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_grounds_damage_types_the_protection_is_theirs_and_no_TODO_is_noted()
    {
        var notes = new GearScoreNotes();
        var score = GearScore.Of(PathwardenRobe(), Ground(new Climber(strength: null), damageTypes: [Bludgeon], notes: notes)); // the tuskers' bludgeon

        Assert.Equal(1.0, score.Part("protection"), 6);
        Assert.Equal(800, score.Total, 3);
        Assert.Empty(notes.Noted);
        Assert.Contains("the mean vs the ground's 0x4 from the armour profile", score.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Armour_cantrips_are_credited_while_the_piece_has_mana()
    {
        static GearCandidate Coat(int mana) => new(2, "Coat", 0x2, 0x200, 0x400, 0, Appraised( // illustrative: AL 100, protections 1, Major Armor 2571 (+40 body) and Minor Impenetrability 2604 (+20), the SQL rows
            2, new() { [28] = 100, [107] = mana, [108] = 100 }, null, new AppraisalArmorProfile(1, 1, 1, 1, 1, 1, 1, 1), 2571, 2604));
        var ground = Ground(new Climber(strength: null));

        var on = GearScore.Of(Coat(100), ground);
        var off = GearScore.Of(Coat(0), ground);

        Assert.Equal(20, on.Part("impenetrability"));
        Assert.Equal(40 * 9, on.Part("cantrip armour")); // a body-armour spell adds to every part (GetBodyArmorMod)
        Assert.Equal(120 + 360, on.Total, 6);
        Assert.Equal(100, off.Total, 6);
        Assert.Contains("impenetrability 0 (out of mana: its spells are off)", off.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ jewellery, flags, requirements

    [Fact]
    public void Jewellery_scores_its_useful_cantrips_while_it_has_mana()
    {
        static GearCandidate Ring(int mana) => new(3, "Ring", 0x8, 0x000C0000, 0, 10, Appraised(3, new() { [107] = mana, [108] = 300 }, null, null, MajorFocus, Useless)); // illustrative

        var ring = GearScore.Of(Ring(300), Ground());

        Assert.Equal(GearSlot.Jewellery, ring.Slot);
        Assert.Equal(1, ring.Total);
        Assert.Contains("cantrips 1 (Major Focus)", ring.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, GearScore.Of(Ring(0), Ground()).Total);
    }

    [Fact]
    public void The_attuned_and_bonded_flags_are_read_raw_from_Int_114_and_Int_33()
    {
        var robe = GearScore.Of(PathwardenRobe(), Ground());
        var wand = GearScore.Of(TrainingWand(), Ground());

        Assert.True(robe.Attuned);
        Assert.True(robe.Bonded);
        Assert.EndsWith("; attuned; bonded", robe.ToString(), StringComparison.Ordinal);
        Assert.False(wand.Attuned); // the Training Wand is neither (the map's finding 3)
        Assert.False(wand.Bonded);
        Assert.True(GearScore.Attuned(PathwardenRobeAppraisal(1)));
        Assert.False(GearScore.Bonded(null));
    }

    [Theory]
    [InlineData(3, 1, 50, "wield: Strength (current, read raw) 10 < 50 (ACE refuses the wield with 0x00A0")]
    [InlineData(4, 5, 60, null)]
    [InlineData(5, 5, 150, "wield: Maximum Mana (maximum) 100 < 150 (")]
    [InlineData(6, 5, 80, null)]
    [InlineData(3, 2, 10, "a wield requirement on Endurance (current, read raw) (10) the read model does not hold (TODO(m5-self-equip-wield-type))")]
    [InlineData(9, 1, 10, "a wield requirement of type 9 (skill 1, 10) this code does not read (TODO(m5-self-equip-wield-type))")]
    public void Wield_requirements_of_types_3_to_6_read_the_attributes_and_vitals_and_the_rest_stay_loud(int type, int stat, int difficulty, string? unmet)
    {
        var appraisal = Appraised(1, new() { [158] = type, [159] = stat, [160] = difficulty });
        var met = new List<string>();

        var why = SelfEquipJudge.Unmet(appraisal, new Climber(), met);

        if (unmet is null)
        {
            Assert.Null(why);
            Assert.Single(met);
            Assert.Contains(type == 6 ? "Maximum Mana (base, read as the maximum) 100 >= 80" : "Focus (base) 60 >= 60", met[0], StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith(unmet, why, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_unappraised_piece_scores_0_named_and_a_shield_is_no_slot_the_score_reads()
    {
        var unread = GearScore.Of(new GearCandidate(Me, "Robe", 0x4, 0x7F01, 0x17F00, 200, null), Ground());
        var shield = GearScore.Of(new GearCandidate(2, "Covenant Shield", 0x2, 0x00200000, 0, 600, Appraised(2, new() { [28] = 200 })), Ground());

        Assert.Equal(0, unread.Total);
        Assert.Contains("unread 0 (no appraisal of it is held)", unread.ToString(), StringComparison.Ordinal);
        Assert.Equal(GearSlot.Other, shield.Slot);
        Assert.Equal(0, shield.Total);
    }
}
