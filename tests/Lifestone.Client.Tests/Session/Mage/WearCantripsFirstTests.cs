using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 cantrips first (the owner, 2026-11-20: a lit major cantrip beats armour differences, armour barely matters so long as it is not terrible, and the built-in spells that are not cantrips are ignored, for her own spells are cast over
/// them). The live case (run 6, 2026-11-20 17:48:29 UTC, Qwinnie at level 53, Arcane Lore 209 under her buffs): she wore a plain Jerkin (Blade Protection Self II, ItemDifficulty 53) and held a Jerkin with Major Coordination and
/// Piercing Protection Self V (ItemDifficulty 206); the pick refused the swap at -0.132% of the hour, Major Coordination being worth nothing to its terms. Proved: a lit great cantrip of any family wears its piece over armour
/// differences; a great cantrip it would put out keeps the worn piece; no such swap leaves a part of her body unarmoured; an item's own spells count in her armour only when they are cantrips. The spell ids and values are the world
/// database's (a read-only SELECT of <c>spell</c>, 2026-11-20).
/// </summary>
public sealed class WearCantripsFirstTests(ITestOutputHelper output)
{
    private const uint MajorFocus = 2574, MajorQuickness = 2575, MajorEndurance = 2573, MajorSalvaging = 3834, MajorArcane = 2502, EpicCoordination = 3963;
    private const uint MajorCoordination = 2572, PiercingProtectionV = 1137, BladeProtectionII = 1110, ArmorSelfVI = 1312, MajorArmor = 2571, ImpenetrabilityVI = 1486, MajorImpenetrability = 2592;
    private const uint Clothing = 0x4, ArmourType = 0x2, ChestWear = 0x2, UnderwearChest = 0x8, HeadWear = 0x1, Head = 0x4000;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [MajorCoordination] = "Major Coordination",
        [PiercingProtectionV] = "Piercing Protection Self V",
        [BladeProtectionII] = "Blade Protection Self II",
        [ArmorSelfVI] = "Armor Self VI",
        [MajorArmor] = "Major Armor",
        [ImpenetrabilityVI] = "Impenetrability VI",
        [MajorImpenetrability] = "Major Impenetrability",
        [MajorFocus] = "Major Focus",
        [MajorQuickness] = "Major Quickness",
        [MajorEndurance] = "Major Endurance",
        [MajorSalvaging] = "Major Salvaging Aptitude",
        [MajorArcane] = "Major Arcane Prowess",
        [EpicCoordination] = "Epic Coordination",
    };

    /// <summary>Her numbers at level 53: Arcane Lore 209 under her buffs (fight-sim her, 2026-11-20); the magic skills of the wear pick's tests.</summary>
    private sealed class Qwinnie : IGearSkills
    {
        public int Level => 53;

        public int AdvancementClass(uint skill) => skill is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3 : 2;

        public int? Current(uint skill) => skill switch
        {
            MageTemplate.WarMagic => 351,
            MageTemplate.LifeMagic => 335,
            MageTemplate.CreatureEnchantment => 299,
            MageTemplate.ItemEnchantment => 294,
            MageTemplate.ManaConversion => 245,
            MageTemplate.ArcaneLore => 209,
            _ => null,
        };

        public int? Base(uint skill) => null;

        public int? VitalMax(uint vital) => vital == 5 ? 365 : vital == 1 ? 227 : null;
    }

    private static Appraisal Appraised(uint guid, int armour, int difficulty, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            new Dictionary<uint, int> { [28] = armour, [5] = 40, [107] = 1_000, [108] = 1_000, [109] = difficulty }.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            new Dictionary<uint, double> { [13] = 1.0, [14] = 1.0, [15] = 1.0 }.ToImmutableDictionary(),
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

    private static WearTerms Terms()
    {
        var ladder = LadderLoader.WarMage;
        return new WearTerms(ladder.WearScore, new CasterTerms(ladder.CasterScore, new CasterBolt("Flame Bolt VI", 30, 250), CasterAuras.None), 171, ladder.RungFor(53).Health!.Hitter, ArmourLines.None) { Capacity = 17_250 };
    }

    private static GearContext Context() => new SelfEquipJudge([], 0x10, id => Names.GetValueOrDefault(id), [0x4]).WithWear(Terms()).Context(new Qwinnie());

    private WearPick Pick(GearCandidate candidate, IReadOnlyList<WornPiece> worn)
    {
        var pick = WearScore.Judge(candidate, worn, Context(), Terms());
        output.WriteLine($"{pick.Kind} '{candidate.Name}' 0x{candidate.Guid:X8}: {pick.Why}");
        return pick;
    }

    [Fact]
    public void A_Jerkin_with_a_lit_Major_Coordination_replaces_the_plain_one_whatever_the_armour_says()
    {
        var plain = new WornPiece(0x8000A351, "Jerkin", Clothing, ChestWear, UnderwearChest, Appraised(0x8000A351, 0, 53, BladeProtectionII));
        var major = new GearCandidate(0x80001286, "Jerkin", Clothing, ChestWear, UnderwearChest, 40, Appraised(0x80001286, 0, 206, MajorCoordination, PiercingProtectionV));
        var pick = Pick(major, [plain]);
        Assert.True(pick.Kind == SelfEquipKind.Replace, pick.Why);
        Assert.Contains("great cantrips first (", pick.Why, StringComparison.Ordinal);
        Assert.Contains("it lights Major Coordination (the armour differences are secondary)", pick.Why, StringComparison.Ordinal);

        // its Major Coordination dark (ItemDifficulty over her Arcane Lore 209): no great cantrip lights, and the plain one stays
        var dark = new GearCandidate(0x8000E97D, "Flared Shirt", Clothing, ChestWear, UnderwearChest, 75, Appraised(0x8000E97D, 0, 269, MajorCoordination));
        Assert.NotEqual(SelfEquipKind.Replace, Pick(dark, [plain]).Kind);
    }

    [Fact]
    public void A_lit_great_cantrip_worn_is_never_put_out_for_more_armour()
    {
        var lit = new WornPiece(0x80000101, "Cap", ArmourType, HeadWear, Head, Appraised(0x80000101, 100, 100, MajorCoordination));
        var helm = new GearCandidate(0x80000102, "Helm", ArmourType, HeadWear, Head, 400, Appraised(0x80000102, 800, 0));
        var pick = Pick(helm, [lit]);
        Assert.True(pick.Kind == SelfEquipKind.None, pick.Why);
        Assert.Contains("it would put out Major Coordination", pick.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MajorQuickness, MajorFocus, true, "it lights Major Focus")] // Focus first
    [InlineData(MajorFocus, MajorQuickness, false, "it would put out Major Focus")]
    [InlineData(MajorCoordination, MajorEndurance, true, "it lights Major Endurance")] // Endurance before Coordination
    [InlineData(MajorEndurance, MajorCoordination, false, "it would put out Major Endurance")]
    [InlineData(MajorSalvaging, MajorQuickness, true, "it lights Major Quickness")] // the rest come last
    [InlineData(MajorCoordination, EpicCoordination, true, "it lights Epic Coordination over Major Coordination")] // a higher tier of the same family
    public void Great_cantrips_go_by_the_owners_order(uint worn, uint candidate, bool replaces, string why)
    {
        var cap = new WornPiece(0x80000401, "Cap", ArmourType, HeadWear, Head, Appraised(0x80000401, 250, 100, worn));
        var find = new GearCandidate(0x80000402, "Helm", ArmourType, HeadWear, Head, 300, Appraised(0x80000402, 200, 100, candidate));
        var pick = Pick(find, [cap]);
        Assert.True((pick.Kind == SelfEquipKind.Replace) == replaces, pick.Why);
        Assert.Contains(why, pick.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_great_cantrips_of_the_rest_against_one_of_the_list_lose_to_the_list()
    {
        // the order decides before any count: a Major Quickness put out is not bought back by two of the rest
        var cap = new WornPiece(0x80000501, "Cap", ArmourType, HeadWear, Head, Appraised(0x80000501, 250, 100, MajorQuickness));
        var find = new GearCandidate(0x80000502, "Helm", ArmourType, HeadWear, Head, 300, Appraised(0x80000502, 250, 100, MajorSalvaging, MajorArcane));
        var pick = Pick(find, [cap]);
        Assert.True(pick.Kind == SelfEquipKind.None, pick.Why);
        Assert.Contains("it would put out Major Quickness", pick.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_great_cantrip_never_leaves_a_part_of_her_body_unarmoured()
    {
        // a hat with a lit Major Coordination and no armour of its own in place of her only head armour: refused by name
        var cap = new WornPiece(0x80000201, "Cap", ArmourType, HeadWear, Head, Appraised(0x80000201, 260, 0));
        var hat = new GearCandidate(0x80000202, "Hat", Clothing, HeadWear, Head, 20, Appraised(0x80000202, 0, 100, MajorCoordination));
        var pick = Pick(hat, [cap]);
        Assert.True(pick.Kind == SelfEquipKind.None, pick.Why);
        Assert.Contains("leave her body unarmoured", pick.Why, StringComparison.Ordinal);

        // the same cantrip on a helm of lower armour: worn (the armour differences are secondary)
        var helm = new GearCandidate(0x80000203, "Helm", ArmourType, HeadWear, Head, 300, Appraised(0x80000203, 237, 100, MajorCoordination));
        Assert.True(Pick(helm, [cap]).Kind == SelfEquipKind.Replace);
    }

    [Fact]
    public void An_items_own_spells_count_in_her_armour_only_when_they_are_cantrips()
    {
        var context = Context();
        double Factor(params uint[] spells) => WearScore.DamageFactor([new WornPiece(0x80000301, "Breastplate", ArmourType, 0x200, 0x400, Appraised(0x80000301, 200, 0, spells))], context, ArmourLines.None);
        var bare = Factor();
        output.WriteLine($"bare {bare:0.0000}; Armor Self VI {Factor(ArmorSelfVI):0.0000}; Impenetrability VI {Factor(ImpenetrabilityVI):0.0000}; Major Armor {Factor(MajorArmor):0.0000}; Major Impenetrability {Factor(MajorImpenetrability):0.0000}");
        Assert.Equal(bare, Factor(ArmorSelfVI), 9); // her own spells are cast over it
        Assert.Equal(bare, Factor(ImpenetrabilityVI), 9);
        Assert.True(Factor(MajorArmor) < bare); // a cantrip stacks on top of her spells: it counts
        Assert.True(Factor(MajorImpenetrability) < bare);
    }
}
