using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-caster</c>; the owner, 2026-11-13: when she gets a caster like the Fire Baton she saved, she equips it and buffs it with item magic). What these prove: the caster pick scores a caster in percent of her hour by the ladder's
/// weights (the damage on the plan's element, the bolts a mana bar its Mana Conversion bonus gives by the mana model, its defences), so today one of her two Wands beats the bare Training Wand she wields; only a wield requirement keeps
/// a caster out (the Fire Baton's base War Magic 290), and the pick takes the baton on the day her base reaches it; the auras she keeps count on the caster they add to; the three caster auras are in the catalog with the world's
/// scrolls, sellers and prices, planned after the banes at the buff bar and only while they add to a caster she holds; a swap owes the pass only the auras that are down.
/// <para>Qwinnie's casters are the read-only shard's rows of 2026-11-13 (biota_properties_int and _float of her five casters): the Training Wand 12748 (0x800001AA, WeaponDefense 1), the Wands 2472 0x80002263 (Value 5,658,
/// ManaConversionMod 0.06, WeaponDefense 1.06, ItemDifficulty 240, spells 1480, 1605, 2555) and 0x80006576 (3,230, 0.05, 1.10), the Staff 2547 0x800020C5 (876, 0.05, 1.07) and the Fire Baton 31823 0x8000191F (8,390,
/// DamageType 16, ElementalDamageMod 1.01, 0.05, 1.07, ItemDifficulty 236, WieldRequirements 2 on skill 34 at 290, spells 1480, 1604, 2600). Her skills: base War Magic 259 (the live log of 2026-11-13 11:54 UTC), Mana Conversion
/// 212 standing (the mana model's level 40 build, cache/m6/mana/model.md), Arcane Lore 75 (Focus 202 / 3 plus 8 ranks). The bolt is the model's Abode bolt, Flame Bolt V: base mana 25, power 200 (CasterAurasDatTests pins it).</para>
/// </summary>
public sealed class CasterPickTests(ITestOutputHelper output)
{
    private const uint Fire = 0x10;
    private const uint TrainingGuid = 0x800001AA, WandA = 0x80002263, WandB = 0x80006576, StaffGuid = 0x800020C5, BatonGuid = 0x8000191F;

    private static Ladder L => LadderLoader.WarMage;

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    /// <summary>Qwinnie's numbers: base War Magic <paramref name="warBase"/> (the bot's War Magic Mastery on top for the current), Mana Conversion <paramref name="manaConversion"/>, Arcane Lore 75.</summary>
    private sealed class Qwinnie(int warBase = 259, int manaConversion = 212) : IGearSkills
    {
        public int Level => 47;

        public int AdvancementClass(uint skill) => skill == MageTemplate.WarMagic ? 3 : 2;

        public int? Current(uint skill) => skill switch { MageTemplate.WarMagic => warBase + 40, MageTemplate.ManaConversion => manaConversion, MageTemplate.ArcaneLore => 75, _ => null };

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? warBase : null;
    }

    private static Appraisal Appraised(uint guid, Dictionary<uint, int> ints, Dictionary<uint, double> floats, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            floats.ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    private static GearCandidate Caster(uint guid, string name, Dictionary<uint, int> ints, Dictionary<uint, double> floats, params uint[] spells) =>
        new(guid, name, 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(guid, new Dictionary<uint, int>(ints) { [1] = 32768, [5] = 50 }, floats, spells));

    private static GearCandidate TrainingWand() => Caster(TrainingGuid, "Training Wand", new() { [19] = 25 }, new() { [29] = 1 });

    private static GearCandidate WandOfA() => Caster(WandA, "Wand", new() { [19] = 5658, [105] = 6, [106] = 232, [107] = 2139, [108] = 2139, [109] = 240 }, new() { [5] = -0.05, [29] = 1.06, [144] = 0.06 }, 1480, 1605, 2555);

    private static GearCandidate WandOfB() => Caster(WandB, "Wand", new() { [19] = 3230, [105] = 7 }, new() { [29] = 1.1, [144] = 0.05 });

    private static GearCandidate Staff() => Caster(StaffGuid, "Staff", new() { [19] = 876, [105] = 5 }, new() { [29] = 1.07, [144] = 0.05 });

    private static GearCandidate FireBaton() => Caster(
        BatonGuid,
        "Fire Baton",
        new() { [19] = 8390, [45] = 16, [105] = 7, [106] = 229, [107] = 2500, [108] = 2500, [109] = 236, [158] = 2, [159] = 34, [160] = 290 },
        new() { [5] = -0.05, [29] = 1.07, [144] = 0.05, [152] = 1.01 },
        1480,
        1604,
        2600);

    private static WornPiece Worn(GearCandidate c) => new(c.Guid, c.Name, c.ItemType, SelfEquipJudge.Held, 0, c.Appraisal);

    private static CasterTerms Terms(CasterAuras? auras = null) => new(L.CasterScore, new CasterBolt("Flame Bolt V", 25, 200), auras ?? CasterAuras.None);

    private static SelfEquipJudge Judge(CasterAuras? auras = null) => new([], Fire, _ => null, null, null, Terms(auras));

    // ------------------------------------------------------------------------------------------------ the weights

    [Fact]
    public void The_weights_are_the_ladders_data_with_their_reasons()
    {
        Assert.Equal(new LadderCasterScore(0.5, 0.1, 0.02, 0.02, 0.1), L.CasterScore);
        Assert.Equal(LadderCasterScore.Default, L.CasterScore); // a judge given no terms reads the same weights
        var why = L.Sources["caster_score"];
        foreach (var part in new[] { "damage 0.5:", "mana 0.1:", "melee_defence 0.02:", "magic_defence 0.02:", "swap_margin 0.1:" })
        {
            Assert.Contains(part, why, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------------------------------------ the pick

    [Fact]
    public void Today_a_Wand_beats_the_Training_Wand_on_its_Mana_Conversion_and_defence()
    {
        var judge = Judge();
        var context = judge.Context(new Qwinnie());
        var scores = new[] { TrainingWand(), WandOfA(), WandOfB(), Staff(), FireBaton() }.Select(c => GearScore.Of(c, context)).ToList();
        foreach (var s in scores)
        {
            output.WriteLine(s.ToString());
        }

        // The bare Training Wand gives nothing; each Wand's Mana Conversion bonus is 13 to 16% more bolts a mana bar by the model, at 0.1 a percent.
        Assert.Equal(0, scores[0].CasterScore, 6);
        Assert.InRange(scores[1].CastsPercent, 14, 18);
        Assert.InRange(scores[2].CastsPercent, 11, 15);
        Assert.True(scores[1].CastsPercent > scores[2].CastsPercent);
        Assert.Equal((6.0, 10.0), (Math.Round(scores[1].MeleeDefencePercent, 6), Math.Round(scores[2].MeleeDefencePercent, 6)));
        Assert.InRange(scores[1].CasterScore, 1.4, 2.0);

        // The Wand of ItemDifficulty 240 is in the field: her Arcane Lore 75 leaves only its spells off (ACE wields it, TryActivateSpells refuses its spells).
        Assert.NotNull(scores[1].SpellsOff);
        Assert.StartsWith("activation: Arcane Lore 75 < 240", scores[1].SpellsOff, StringComparison.Ordinal);
        Assert.Null(scores[1].Unmet);
        Assert.Equal(scores[1].CasterScore, scores[1].Total);

        // The Fire Baton is short of its wield requirement: 0, and out of the pick.
        Assert.Equal(0, scores[4].Total);
        Assert.StartsWith("wield: skill 34 base 259 < 290", scores[4].Unmet, StringComparison.Ordinal);

        var choice = judge.BestCaster([WandOfA(), WandOfB(), Staff(), FireBaton()], [Worn(TrainingWand())], new Qwinnie(), out var why);
        output.WriteLine(why);
        output.WriteLine(choice?.ToString());
        Assert.NotNull(choice);
        Assert.Equal(SelfEquipKind.Swap, choice.Kind);
        Assert.Contains(choice.Guid, new[] { WandA, WandB });
        Assert.Equal(TrainingGuid, choice.Replaces);
        Assert.Equal(WandA, choice.Guid); // the 0.06 Wand: 15.8% more bolts a bar against the other's 13.3%, and 6% melee defence against 10% at a fiftieth
        Assert.Contains("the best of 3 usable caster(s) held", why, StringComparison.Ordinal);
        Assert.Contains("field: 'Wand' 0x80002263 1.71, 'Wand' 0x80006576 1.53, 'Staff' 0x800020C5 1.47", why, StringComparison.Ordinal);
        Assert.Contains("1 short of a wield requirement ('Fire Baton' 0x8000191F wield: skill 34 base 259 < 290", why, StringComparison.Ordinal);
        Assert.Contains("over 'Training Wand' 0x800001AA (0)", why, StringComparison.Ordinal);

        // The swap's line names both casters' numbers and that the spells of the new one stay off.
        Assert.Contains("'Wand' 0x80002263 score", choice.Why, StringComparison.Ordinal);
        Assert.Contains("against 'Training Wand' 0x800001AA score 0", choice.Why, StringComparison.Ordinal);
        Assert.Contains("its spells stay off (activation: Arcane Lore 75 < 240", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Fire_Baton_is_refused_under_base_War_Magic_290_and_picked_at_290()
    {
        var judge = Judge();
        var bag = new[] { TrainingWand(), WandOfB(), Staff(), FireBaton() };

        // Base 289: the baton is out; the Wand she wields from today stays.
        Assert.Null(judge.BestCaster(bag, [Worn(WandOfA())], new Qwinnie(warBase: 289), out var under));
        output.WriteLine(under);
        Assert.Contains("keep 'Wand' 0x80002263", under, StringComparison.Ordinal);
        Assert.Contains("'Fire Baton' 0x8000191F wield: skill 34 base 289 < 290", under, StringComparison.Ordinal);
        Assert.Equal(SelfEquipKind.Review, judge.Judge(FireBaton(), [Worn(WandOfA())], new Qwinnie(warBase: 289)).Kind); // better but short: the review pack keeps it

        // Base 290: the first level-up spend that raises her base to 290 makes it the pick: +1% fire damage at 0.5 and its own Mana Conversion bonus beat the Wand's larger bonus.
        var at = judge.BestCaster(bag, [Worn(WandOfA())], new Qwinnie(warBase: 290), out var why);
        output.WriteLine(why);
        output.WriteLine(at?.ToString());
        Assert.NotNull(at);
        Assert.Equal((SelfEquipKind.Swap, BatonGuid, WandA), (at.Kind, at.Guid, at.Replaces));
        Assert.Contains("wield: skill 34 base 290 >= 290", at.Why, StringComparison.Ordinal);
        Assert.Contains("its spells stay off (activation: Arcane Lore 75 < 236", at.Why, StringComparison.Ordinal);

        // With her own Spirit Drinker IV kept up the baton's lead grows by 4% of fire damage (2 points of the hour); on the Wand, with no element, the aura adds nothing.
        var context = Judge(new CasterAuras(0, 0, 4)).Context(new Qwinnie(warBase: 290));
        var baton = GearScore.Of(FireBaton(), context);
        var wand = GearScore.Of(WandOfA(), context);
        Assert.Equal((5.0, 0.0), (Math.Round(baton.WarPercent, 6), wand.WarPercent));
        var plain = GearScore.Of(FireBaton(), Judge().Context(new Qwinnie(warBase: 290)));
        Assert.Equal(2.0, baton.CasterScore - plain.CasterScore, 6);
    }

    [Fact]
    public void Hermetic_Link_multiplies_the_casters_own_bonus_and_Defender_adds_to_every_caster()
    {
        var context = Judge(new CasterAuras(5, 5, 0)).Context(new Qwinnie());
        var wand = GearScore.Of(WandOfA(), context);
        var training = GearScore.Of(TrainingWand(), context);
        output.WriteLine(wand.ToString());

        Assert.Equal(9.0, wand.ManaConversionPercent, 6); // 0.06 x 1.5 (Aura of Hermetic Link Self V, stat_Mod_Val 1.5)
        Assert.Equal(0.0, training.ManaConversionPercent); // ACE: a caster with no base mod gains nothing from the aura
        Assert.Equal(19.0, wand.MeleeDefencePercent, 6); // 1.06 + 0.13 (Aura of Defender Self V)
        Assert.Equal(13.0, training.MeleeDefencePercent, 6);
        Assert.True(wand.CastsPercent > GearScore.Of(WandOfA(), Judge().Context(new Qwinnie())).CastsPercent);
        Assert.Contains("its own 0.06 x Hermetic Link 1.5", wand.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_caster_better_by_less_than_the_swap_margin_stays_in_the_pack()
    {
        // Two Wands with the same Mana Conversion bonus, one with a little more melee defence: 1% at a fiftieth is 0.02 of the hour, under the margin 0.1.
        var held = Caster(0x90000001, "Wand", new(), new() { [29] = 1.05, [144] = 0.05 });
        var other = Caster(0x90000002, "Wand", new(), new() { [29] = 1.06, [144] = 0.05 });
        Assert.Null(Judge().BestCaster([other], [Worn(held)], new Qwinnie(), out var why));
        Assert.Contains("beats it by the score's margin 0.1", why, StringComparison.Ordinal);

        // With no caster in the hands the best that scores anything is put on (no margin); one that scores nothing is the rearm's, as before (HeldCaster.Rearm).
        var wear = Judge().BestCaster([TrainingWand(), WandOfB()], [], new Qwinnie(), out _);
        Assert.Equal((SelfEquipKind.Wear, WandB), (wear!.Kind, wear.Guid));
        Assert.Null(Judge().BestCaster([TrainingWand()], [], new Qwinnie(), out var rearm));
        Assert.StartsWith("gear: caster pick: keep no caster", rearm, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_bolt_the_mana_term_stands_in_by_the_skills_percent_and_says_so_once()
    {
        var notes = new GearScoreNotes();
        var context = new SelfEquipJudge([], Fire, _ => null, null, notes, new CasterTerms(L.CasterScore, null, CasterAuras.None)).Context(new Qwinnie());
        var wand = GearScore.Of(WandOfA(), context);
        Assert.Equal(6.0, wand.CastsPercent, 6);
        Assert.Contains("TODO(m6-caster-bolt-unread)", wand.ToString(), StringComparison.Ordinal);
        _ = GearScore.Of(WandOfB(), context);
        Assert.Equal(["m6-caster-bolt-unread"], notes.Noted);
    }

    // ------------------------------------------------------------------------------------------------ the catalog and the learn plan

    /// <summary>The world's rows (the read-only world copy, 2026-11-13): per aura line the spells I to VI, the scrolls I to VI and the create-list rows of 20215, 30009, 20216, 30010, 20213, 30029, 20214, 20211, 30027, 20212, 30028.</summary>
    private static readonly (string Line, uint[] Spells, uint[] Scrolls, int[] Rows, uint[] Powers)[] World =
    [
        ("Spirit Drinker", [3253, 3254, 3255, 3256, 3257, 3258], [28002, 28003, 28004, 28005, 28006, 28007], [77994, 79015, 78057, 79074, 77902, 80492, 77950, 77819, 80409, 77859, 80449], [1, 50, 100, 150, 200, 250]),
        ("Hermetic Link", [1475, 1476, 1477, 1478, 1479, 1480], [1896, 2887, 2888, 2889, 2891, 2892], [77985, 79006, 78048, 79065, 77893, 80483, 77941, 77810, 80400, 77850, 80440], [25, 50, 100, 150, 200, 250]),
        ("Defender", [1599, 1601, 1602, 1603, 1604, 1605], [1880, 2807, 2808, 2809, 2810, 2811], [77975, 78996, 78038, 79055, 77883, 80473, 77931, 77800, 80390, 77840, 80430], [1, 50, 100, 150, 200, 250]),
    ];

    [Fact]
    public void The_catalog_holds_the_three_caster_auras_I_to_VI_with_the_worlds_scrolls_sellers_and_prices()
    {
        Assert.Equal(CasterAuras.LineNames, C.Lines.Where(l => l.Kind == ScrivenerLineKind.CasterAura).Select(l => l.Name));
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        foreach (var (name, ids, scrolls, rows, powers) in World)
        {
            var line = C.Line(name)!;
            Assert.Equal((ScrivenerLineKind.CasterAura, DatReaderWriter.Enums.MagicSchool.ItemEnchantment, (string?)null), (line.Kind, line.School, line.Element));
            var spells = C.LineSpells(name);
            Assert.Equal(ids, spells.Select(s => s.SpellId));
            Assert.Equal(powers, spells.Select(s => s.Power));
            Assert.Equal(ArenaProfile.LineNamed(name)!.Spells.Take(6).Select(s => (s.Id, s.Name, s.Power)), spells.Select(s => (s.SpellId, s.Name, s.Power))); // the buff profile's own lines
            Assert.Equal(scrolls, spells.Select(s => s.ScrollWcid));
            Assert.Equal([1, 5, 20, 100, 200, 1000], spells.Select(s => s.ScrollValue));
            Assert.Equal([new ScrivenerSale(20215, rows[0]), new ScrivenerSale(30009, rows[1])], spells[0].Sellers);
            Assert.Equal([new ScrivenerSale(20216, rows[2]), new ScrivenerSale(30010, rows[3])], spells[1].Sellers);
            Assert.Equal([new ScrivenerSale(20213, rows[4]), new ScrivenerSale(30029, rows[5])], spells[2].Sellers);
            Assert.Equal([new ScrivenerSale(20214, rows[6])], spells[3].Sellers); // Holtburg's Scrivener of Item Magic
            Assert.Equal([new ScrivenerSale(20211, rows[7]), new ScrivenerSale(30027, rows[8])], spells[4].Sellers);
            Assert.Equal([new ScrivenerSale(20212, rows[9]), new ScrivenerSale(30028, rows[10])], spells[5].Sellers);

            // Prices: ACE's GetSellCost, ceil(50 x Value - 0.1): 50, 250, 1,000 and 5,000 at Holtburg on foot, V 10,000 at the Master Scriveners, VI 50,000 at the Grand Masters (Stonehold among them).
            Assert.Equal([50, 250, 1000, 5000], spells.Take(4).Select(s => outskirts.PriceOf(s.ScrollWcid)));
            Assert.Equal((10_000, 50_000), (C.ScrivenerOf(20211)!.PriceOf(spells[4].ScrollValue), C.ScrivenerOf(20212)!.PriceOf(spells[5].ScrollValue)));
            Assert.Equal(["Dryreach", "Plateau Village", "Qalaba'r", "Westwatch"], C.TownsSelling(spells[4]));
            Assert.Contains("Stonehold", C.TownsSelling(spells[5]));
        }

        // What each level adds (the world's spell rows, BuffStatMods): Hermetic Link x1.1 to x1.6, Defender +0.03 to +0.15, Spirit Drinker +0.01 a level.
        Assert.Equal([1.1, 1.2, 1.3, 1.4, 1.5, 1.6], Enumerable.Range(1, 6).Select(l => Math.Round(new CasterAuras(l, 0, 0).ManaConversionFactor, 6)));
        Assert.Equal([0.03, 0.05, 0.075, 0.1, 0.13, 0.15], Enumerable.Range(1, 6).Select(l => Math.Round(new CasterAuras(0, l, 0).DefenceAdd, 6)));
        Assert.Equal([0.01, 0.02, 0.03, 0.04, 0.05, 0.06], Enumerable.Range(1, 6).Select(l => Math.Round(new CasterAuras(0, 0, l).ElementalAdd, 6)));
    }

    /// <summary>BotBuildTests' Winnie on the Abode: Focus and Self 100, Item Enchantment trained with 120 ranks: 230 with the bot's lines up, so level V at the buff bar (216) and not VI (266).</summary>
    private static PlayerQualities Climber(int level = 47)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 0, 1, 1u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 120, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static IReadOnlyList<LearnNeed> Needs(CasterGain? casters, Spellbook? book = null)
    {
        var ground = GroundCatalog.Get("tusker-abode");
        var q = Climber();
        return ScrivenerLearnPlan.Needs(ground, ScrivenerLearnPlan.RungOf(L, ground.Key, q.Level)!, L.PlanCastChance, q, book ?? new Spellbook { IsKnown = true }, C, L, BuffBotWire.Tusker, null, casters);
    }

    [Fact]
    public void The_caster_auras_come_after_the_banes_at_the_buff_bar_and_only_while_they_add_to_a_caster_she_holds()
    {
        var rung = ScrivenerLearnPlan.RungOf(L, "tusker-abode", 47)!;
        var order = ScrivenerLearnPlan.LinesFor(rung, C).Select(l => l.Name).ToList();
        Assert.Equal(order.IndexOf("Acid Bane") + 1, order.IndexOf("Spirit Drinker")); // after every line that keeps her alive
        Assert.Equal(["Spirit Drinker", "Hermetic Link", "Defender"], order.Skip(order.IndexOf("Spirit Drinker")).Take(3));
        Assert.True(order.IndexOf("Defender") < order.IndexOf("Fire Vulnerability Other")); // before the debuffs
        Assert.True(ScrivenerLearnPlan.IsBuffLine(ScrivenerLineKind.CasterAura));

        // Today: the Wand 0.06 she holds: Hermetic Link and Defender are due; Holtburg's IV first, since V (her level at the buff bar) is the Master Scriveners'; Spirit Drinker adds to no caster she holds.
        var today = Needs(new CasterGain(true, 0.06, "Wand", null, 4));
        foreach (var need in today.Where(n => n.Line.Kind == ScrivenerLineKind.CasterAura))
        {
            output.WriteLine($"{need.Line.Name}: {need.Kind} {need.Spell?.Name}: {need.Why}");
        }

        var spirit = Assert.Single(today, n => n.Line.Name == "Spirit Drinker");
        Assert.Equal(LearnNeedKind.NoGain, spirit.Kind);
        Assert.Contains("no caster she holds and can wield is of the plan's element", spirit.Why, StringComparison.Ordinal);
        foreach (var name in new[] { "Hermetic Link", "Defender" })
        {
            var need = Assert.Single(today, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.Due, 4), (need.Kind, need.Target));
            Assert.Equal($"Aura of {name} Self IV", need.Spell!.Name);
            Assert.Contains("ItemEnchantment skill 230", need.Why, StringComparison.Ordinal);
            Assert.Contains("plan chance 0.75: level V at the chance", need.Why, StringComparison.Ordinal);
            Assert.Contains("the highest level Holtburg sells is bought first", need.Why, StringComparison.Ordinal);
            Assert.Contains("it adds to her caster: ", need.Why, StringComparison.Ordinal);
        }

        // With IV in the book, V is the Town Network's (the Master Scriveners of Item Magic).
        var book = new Spellbook { IsKnown = true };
        book.Learn(1478);
        book.Learn(1603);
        var then = Needs(new CasterGain(true, 0.06, "Wand", null, 4), book);
        Assert.All(new[] { "Hermetic Link", "Defender" }, name => Assert.Equal((LearnNeedKind.OffHome, 5), (then.Single(n => n.Line.Name == name).Kind, then.Single(n => n.Line.Name == name).Target)));

        // At base War Magic 290 the Fire Baton is a caster of the plan's element she can wield: Spirit Drinker is due too.
        var baton = Needs(new CasterGain(true, 0.06, "Wand", "Fire Baton", 5));
        Assert.Equal((LearnNeedKind.Due, 4), (baton.Single(n => n.Line.Name == "Spirit Drinker").Kind, baton.Single(n => n.Line.Name == "Spirit Drinker").Target));
        Assert.EndsWith("it adds to her caster: 'Fire Baton' is of the plan's element", baton.Single(n => n.Line.Name == "Spirit Drinker").Why, StringComparison.Ordinal);

        // Only the Training Wand: Hermetic Link multiplies nothing; Defender still adds. Casters not read: none of the three is bought on an assumption.
        var bare = Needs(new CasterGain(true, 0, null, null, 1));
        Assert.Equal(LearnNeedKind.NoGain, bare.Single(n => n.Line.Name == "Hermetic Link").Kind);
        Assert.Equal(LearnNeedKind.Due, bare.Single(n => n.Line.Name == "Defender").Kind);
        Assert.All(Needs(null).Where(n => n.Line.Kind == ScrivenerLineKind.CasterAura), n => Assert.Equal(LearnNeedKind.NoGain, n.Kind));
    }

    // ------------------------------------------------------------------------------------------------ the swap's check

    private static EnchantmentDesc Aura(ushort spell, double duration) => new(spell, 1, 0, 0, 0, 0, duration, 0x50000001, 0f, 0f, 0.0, 0, 0, 1f, 0, EnchantmentBucket.Multiplicative);

    [Fact]
    public void A_swap_owes_the_pass_the_auras_that_are_down_and_carries_the_ones_up()
    {
        var state = new GameState();
        state.Self.Spellbook.IsKnown = true;
        foreach (var id in new uint[] { 1478, 1603, 3256 })
        {
            state.Self.Spellbook.Learn(id); // Hermetic Link IV, Defender IV, Spirit Drinker IV
        }

        // Hermetic Link is up with 1,500 s left (carried: it is hers, not the old caster's); Defender with 100 s (under the 300 s margin); Spirit Drinker down.
        state.Self.Qualities.Enchantments = [Aura(1478, 1500), Aura(1603, 100)];
        var lines = CasterAuraCheck.AfterSwap(state, 0, 300);
        output.WriteLine(CasterAuraCheck.Describe(lines));
        Assert.Equal(
            [("Spirit Drinker", 4, CasterAuraVerdict.Owed), ("Hermetic Link", 4, CasterAuraVerdict.Carried), ("Defender", 4, CasterAuraVerdict.Owed)],
            lines.Select(l => (l.Line, l.Level, l.Verdict)));
        Assert.Contains("2 owed: the pass before the next fight casts them on her", CasterAuraCheck.Describe(lines), StringComparison.Ordinal);

        // The owed lines ask a re-buff pass before the fight (not a first pass), and nothing more once a pass has stood on them.
        var why = LearnedPassRule.Why(70, 70, "'Fire Baton' 0x8000191F wielded at the visit: Spirit Drinker, Defender owed");
        Assert.StartsWith(LearnedPassRule.CasterSwapPrefix + " since the last pass: 'Fire Baton'", why, StringComparison.Ordinal);
        Assert.False(LearnedPassRule.IsStartPass(why!));
        Assert.Null(LearnedPassRule.Why(70, 70, null));
        Assert.True(LearnedPassRule.IsStartPass(LearnedPassRule.Why(null, 70, "x")!));

        // Every aura up with time to spare: nothing to cast, and the line says why.
        state.Self.Qualities.Enchantments = [Aura(1478, 1500), Aura(1603, 1500), Aura(3256, 1500)];
        var carried = CasterAuraCheck.AfterSwap(state, 0, 300);
        Assert.All(carried, l => Assert.Equal(CasterAuraVerdict.Carried, l.Verdict));
        Assert.Contains("nothing to cast", CasterAuraCheck.Describe(carried), StringComparison.Ordinal);
    }
}
