using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 casterfair (the owner's ruling, 2026-11-17: the hermetic link is a spell she casts already). Live, ladder61 13:17:14 UTC: "gear: caster pick: keep 'Piercing Sceptre' 0x800003C0 (2.77) ... field: 'Orb' 0.78, 'Fire Baton' 0.72 ...;
/// auras Hermetic Link VI (x1.6), Defender VI (+0.15), Spirit Drinker VI (+0.06)". ACE folds the wielder's auras and the item's own enchantments into the wielded caster's appraisal (AppraiseInfo.cs:415-453 @95ef426d), so the score
/// counted her auras twice for the sceptre she wields and once for every caster in her pack. What these prove: the wielded caster's appraisal is unfolded to its own numbers and scores as it would bare from her pack; her auras at her
/// book's levels apply to every caster alike; a caster's own aura or item cantrip counts for it alone; and the Atlan wands against the sceptre on her fire, frost and lightning lines.
/// <para>The numbers are the shard's and the world's, read-only on 2026-11-17: the wielded Piercing Sceptre 0x800003C0 (weenie 29264): WeaponDefense 1.10, ManaConversionMod 0.07, ElementalDamageMod 1.03 on pierce (DamageType 2),
/// spells Focus Self V 1425, Aura of Hermetic Link Self V 1479, Aura of Defender Self V 1604 and Minor Hermetic Link 3199, wield base War Magic 290, ItemDifficulty 234; its live appraisal of 2026-11-16 21:04 showed "Bonus to Melee
/// Defense: +25.0%", "Bonus to Mana Conversion: +12%", "Piercing ... +9.0%" and "Enchantments: Minor Hermetic Link". Weenie 46122 Atlan Wand: WeaponDefense 1.08 alone. Weenie 46128 Minor Smoldering Atlan Wand: WeaponDefense 1.08,
/// ManaConversionMod 0.02, ElementalDamageMod 1.12 on fire (DamageType 16), wield base War Magic 290, ItemDifficulty 100, item mana 500, spells Mana Conversion Mastery Self VI 658, Fire Protection Self VI 1094, Willpower Self VI 1450,
/// Aura of Defender Self VI 1605, Minor War Magic Aptitude 2569 and Aura of Spirit Drinker Self VI 3258. Her numbers from the same log: War Magic 378, Mana Conversion 284, the targets' Magic Defense 63; the bolts at level VI (the dat:
/// Flame Bolt VI power 250, base mana 30; the frost and lightning lines' level VI alike). Her base War Magic is taken at the 290 her wielded sceptre asks (she wields it).</para>
/// </summary>
public sealed class CasterFairTests(ITestOutputHelper output)
{
    private const uint Sceptre = 0x800003C0, Unstoned = 0x8000A001, Smoldering = 0x8000A002, Fire = 0x10, Cold = 0x8, Electric = 0x40;

    private sealed class Qwinnie : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => skill is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3 : skill == MageTemplate.MeleeDefense ? 1 : 2;

        public int? Current(uint skill) => skill switch
        {
            MageTemplate.WarMagic => 378,
            MageTemplate.ManaConversion => 284,
            MageTemplate.ArcaneLore => 273,
            MageTemplate.LifeMagic => 290,
            MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment => 280,
            _ => null,
        };

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? 290 : null;

        public int? VitalMax(uint vital) => vital == 5 ? 400 : null;
    }

    private static Appraisal Appraised(uint guid, Dictionary<uint, int> ints, Dictionary<uint, double> floats, uint[] spells, ushort resistHighlight = 0) => new(
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
            resistHighlight == 0 ? null : new AppraisalBitfield(resistHighlight, resistHighlight),
            null),
        1,
        0);

    private static Dictionary<uint, int> SceptreInts() => new() { [5] = 50, [45] = 2, [106] = 226, [107] = 1_047, [108] = 1_634, [109] = 234, [158] = 2, [159] = 34, [160] = 290 };

    /// <summary>The sceptre from her pack: its own numbers (the shard's row).</summary>
    private static Appraisal SceptreBare() => Appraised(Sceptre, SceptreInts(), new() { [29] = 1.10, [144] = 0.07, [152] = 1.03 }, [1425, 1479, 1604, 3199]);

    /// <summary>The sceptre wielded: ACE's appraisal with her Defender VI, Hermetic Link VI and Spirit Drinker VI and its own Minor Hermetic Link folded in (the live +25%, +12%, +9%).</summary>
    private static Appraisal SceptreWielded() => Appraised(
        Sceptre,
        SceptreInts(),
        new() { [29] = 1.10 + 0.15, [144] = 0.07 * 1.6 * 1.1, [152] = 1.03 + 0.06 },
        [1425, 1479, 1604, 3199, 3199 | IdentifyResponseMsg.ItemEnchantmentBit],
        resistHighlight: 0x1000 | 0x2000);

    private static Appraisal UnstonedWand() => Appraised(Unstoned, new() { [5] = 150 }, new() { [29] = 1.08 }, []);

    private static Appraisal SmolderingWand() => Appraised(
        Smoldering,
        new() { [5] = 150, [45] = 16, [106] = 275, [107] = 500, [108] = 500, [109] = 100, [158] = 2, [159] = 34, [160] = 290 },
        new() { [29] = 1.08, [144] = 0.02, [152] = 1.12, [5] = -0.025 },
        [658, 1094, 1450, 1605, 2569, 3258]);

    /// <summary>Her auras at her book's levels (Hermetic Link VI, Defender VI, Spirit Drinker VI) and, worn, those same auras on her, with the sceptre wielded.</summary>
    private static CasterTerms Terms(uint element) =>
        new(LadderLoader.WarMage.CasterScore, new CasterBolt(element switch { Fire => "Flame Bolt VI", Cold => "Frost Bolt VI", _ => "Lightning Bolt VI" }, 30, 250), new CasterAuras(6, 6, 6))
        {
            Fold = new CasterFold(Sceptre, 0.15, 1.6, 0.06),
        };

    private static GearContext Context(uint element)
    {
        var caster = Terms(element);
        var wear = new WearTerms(LadderLoader.WarMage.WearScore, caster, 63, null, ArmourLines.None);
        return new SelfEquipJudge([], element, id => CantripRows.Of(id)?.Name, null, null, caster).WithWear(wear).Context(new Qwinnie(), []);
    }

    private GearScoreResult Score(uint guid, string name, Appraisal a, uint element)
    {
        var score = GearScore.Caster(guid, name, a, Context(element));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{element:X2} {GearScore.CasterNumbers(score)}: {string.Join(", ", score.Parts)}"));
        return score;
    }

    [Fact]
    public void The_wielded_sceptre_scores_as_it_would_bare_from_her_pack()
    {
        foreach (var element in new[] { Fire, Cold, Electric, 0u })
        {
            var wielded = Score(Sceptre, "Piercing Sceptre", SceptreWielded(), element);
            var bare = GearScore.Caster(Sceptre, "Piercing Sceptre", SceptreBare(), Context(element) with { Caster = Terms(element) with { Fold = CasterFold.None } });
            Assert.Equal(bare.CasterScore, wielded.CasterScore, 6);
            Assert.Equal(1.10, CasterIntrinsic.Of(Sceptre, SceptreWielded(), Terms(element).Fold).Defence, 9);
            Assert.Equal(0.07, CasterIntrinsic.Of(Sceptre, SceptreWielded(), Terms(element).Fold).ManaConversion, 9);
            Assert.Equal(1.03, CasterIntrinsic.Of(Sceptre, SceptreWielded(), Terms(element).Fold).Elemental!.Value, 9);
        }

        // her auras apply once: Hermetic Link VI (x1.6, over the sceptre's own V) and its own Minor Hermetic Link (x1.1, an enchantment on it), Defender VI (+0.15, over its own V)
        var none = Score(Sceptre, "Piercing Sceptre", SceptreWielded(), 0);
        Assert.Equal(0.07 * 1.6 * 1.1 * 100, none.ManaConversionPercent, 6);
        Assert.Equal((1.10 - 1 + 0.15) * 100, none.MeleeDefencePercent, 6);
    }

    [Fact]
    public void Her_auras_apply_to_every_caster_alike_and_a_casters_own_aura_or_cantrip_to_it_alone()
    {
        // a bare caster in her pack with the sceptre's own numbers and no spells: her book's auras alone
        var plain = Appraised(0x8000A003, SceptreInts(), new() { [29] = 1.10, [144] = 0.07, [152] = 1.03 }, []);
        var plainScore = Score(0x8000A003, "Plain Sceptre", plain, Cold);
        Assert.Equal(0.07 * 1.6 * 100, plainScore.ManaConversionPercent, 6);
        Assert.Equal(25, plainScore.MeleeDefencePercent, 6);

        // an appraisal of the wielded sceptre taken before the wield (no fold marked) is read as its own numbers, not unfolded
        Assert.Equal(1.10, CasterIntrinsic.Of(Sceptre, SceptreBare(), Terms(Cold).Fold).Defence, 9);

        // the sceptre's own Minor Hermetic Link (in its spell book) multiplies on top for it alone; its own Aura of Hermetic Link V and Defender V are under her VI: nothing more
        var sceptre = Score(Sceptre, "Piercing Sceptre", SceptreBare(), Cold);
        Assert.Equal(0.07 * 1.6 * 1.1 * 100, sceptre.ManaConversionPercent, 6);
        Assert.Equal(25, sceptre.MeleeDefencePercent, 6);

        // with no aura in her book, the caster's own Aura of Hermetic Link V counts for it (x1.5), the plain one gets none
        var noBook = Context(Cold) with { Caster = Terms(Cold) with { Auras = CasterAuras.None, Fold = CasterFold.None } };
        Assert.Equal(0.07 * 1.5 * 1.1 * 100, GearScore.Caster(Sceptre, "Piercing Sceptre", SceptreBare(), noBook).ManaConversionPercent, 6);
        Assert.Equal(0.07 * 100, GearScore.Caster(0x8000A003, "Plain Sceptre", plain, noBook).ManaConversionPercent, 6);
    }

    [Fact]
    public void The_Atlan_wands_against_the_sceptre_on_her_fire_frost_and_lightning_lines()
    {
        var results = new Dictionary<uint, (double Sceptre, double Unstoned, double Smoldering)>();
        foreach (var element in new[] { Fire, Cold, Electric })
        {
            var s = Score(Sceptre, "Piercing Sceptre", SceptreWielded(), element).CasterScore;
            var u = Score(Unstoned, "Atlan Wand", UnstonedWand(), element).CasterScore;
            var m = Score(Smoldering, "Minor Smoldering Atlan Wand", SmolderingWand(), element).CasterScore;
            results[element] = (s, u, m);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"element 0x{element:X2}: sceptre {s:0.###}, Atlan Wand {u:0.###}, Minor Smoldering Atlan Wand {m:0.###}"));
        }

        var margin = LadderLoader.WarMage.CasterScore.SwapMargin;

        // fire: the Smoldering wand's own x1.12 on fire plus Spirit Drinker VI is +18% of her bolts' damage, half the hour's weight: it wins by far
        Assert.True(results[Fire].Smoldering > results[Fire].Sceptre + margin);
        Assert.True(results[Fire].Smoldering > results[Fire].Unstoned + margin);

        // the bare Atlan Wand (WeaponDefense 1.08 alone) never beats the sceptre
        Assert.All(results.Values, r => Assert.True(r.Unstoned < r.Sceptre));

        // frost and lightning: no caster is on the line's element; the sceptre's Mana Conversion (0.07 x 1.6 x 1.1) against the wand's (0.02 x 1.6) and its Minor War Magic Aptitude
        foreach (var element in new[] { Cold, Electric })
        {
            var (s, _, m) = results[element];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{element:X2}: the sceptre {(s > m + margin ? "wins" : m > s + margin ? "loses to the wand" : "stays (within the margin)")}"));
            Assert.True(s > m + margin); // off fire the wand's +18% counts nothing, and its Minor War Magic Aptitude lands nothing at War Magic 378 against Magic Defense 63 (0.9998 of her bolts land already)
        }
    }
}
