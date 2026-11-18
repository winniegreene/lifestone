using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 capacity (the owner's ruling, 2026-11-17: in her major strength shoes she would have a lot more capacity). Live, ladder58 12:23:21 UTC: Qwinnie at level 80, Arcane Lore 273 under the buff bot's lines, her pack at
/// 21,674 of 21,750, looted 'Shoes' 0x80000B09 (armour level 234, burden 90, ItemDifficulty 251, Impenetrability V and Major Strength among their spells) while she wore 'Shoes' 0x8000C7B0 (armour level 242, burden 90, ItemDifficulty 275,
/// Strength Self V and Impenetrability V: dark at 273). What these prove: a Strength cantrip counts as capacity (150 a point, ACE <c>GetEncumbranceCapacity</c>, Player_Inventory.cs:46-51 @95ef426d) on her live load, so the Major Strength
/// shoes replace the dark pair; two Major Strength pieces count once (ACE's top layer); and the same find is no gain when her pack is far below its load line, while a caster's Major Strength wins the caster pick at the line.
/// </summary>
public sealed class CapacityGearTests(ITestOutputHelper output)
{
    private const uint Fire = 0x10, Feet = 0x10000, FootWear = 0x100;
    private const uint NewShoes = 0x80000B09, OldShoes = 0x8000C7B0, Gloves = 0x80005B3F;
    private const uint MajorStrength = 2576, StrengthSelfV = 1331, ImpenetrabilityV = 1485;
    private const int Capacity = 21_750, AtTheLine = 21_674;

    /// <summary>Her numbers at 12:23 (Arcane Lore 273 with the bot's lines on her; the magic skills of the wear pick's tests).</summary>
    private sealed class Qwinnie(int lore = 273) : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => skill is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3 : skill == MageTemplate.MeleeDefense ? 1 : 2;

        public int? Current(uint skill) => skill switch
        {
            MageTemplate.WarMagic => 297,
            MageTemplate.LifeMagic => 290,
            MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment => 280,
            MageTemplate.ManaConversion => 213,
            MageTemplate.ArcaneLore => lore,
            _ => null,
        };

        public int? Base(uint skill) => null;

        public int? VitalMax(uint vital) => vital == 5 ? 332 : vital == 1 ? 156 : null;
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

    /// <summary>A pair of shoes: armour level, burden 90, item mana 1,000, ItemDifficulty, the bludgeon protection 1.</summary>
    private static Appraisal ShoesOf(uint guid, int armour, int difficulty, params uint[] spells) =>
        Appraised(guid, new() { [28] = armour, [5] = 90, [107] = 1_000, [108] = 1_000, [109] = difficulty }, new() { [15] = 1.0 }, spells);

    private static GearCandidate Candidate(uint guid, Appraisal a) => new(guid, "Shoes", 0x2, FootWear, Feet, 90, a);

    private static WornPiece Wearing(uint guid, Appraisal a) => new(guid, "Shoes", 0x2, FootWear, Feet, a);

    private static Ladder L => LadderLoader.WarMage;

    private static WearTerms Terms(int carried) =>
        new(L.WearScore, new CasterTerms(L.CasterScore, new CasterBolt("Flame Bolt VI", 30, 250), CasterAuras.None), 171, null, ArmourLines.None) { Capacity = Capacity, Carried = carried };

    private static SelfEquipJudge Judge(WearTerms terms) => new SelfEquipJudge([], Fire, id => CantripRows.Of(id)?.Name, [0x4]).WithWear(terms);

    private WearPick Pick(GearCandidate candidate, IReadOnlyList<WornPiece> worn, WearTerms terms, int lore = 273)
    {
        var pick = WearScore.Judge(candidate, worn, Judge(terms).Context(new Qwinnie(lore)), terms);
        output.WriteLine($"{pick.Kind} '{candidate.Name}' 0x{candidate.Guid:X8}: {pick.Why}");
        return pick;
    }

    /// <summary>The capacity term's own value at <paramref name="carried"/>: 0.01 a percent of the capacity (the wear score's burden weight), all of it when her load is within the gain of the line.</summary>
    private static double Worth(int points, int carried)
    {
        var gain = points * 150.0;
        var reach = Capacity - gain;
        var fill = carried >= reach ? 1.0 : carried / reach;
        return L.WearScore.Burden * (gain / Capacity * 100.0) * fill;
    }

    [Fact]
    public void Major_Strength_shoes_replace_the_dark_pair_at_the_load_line_for_2250_capacity()
    {
        var worn = new List<WornPiece> { Wearing(OldShoes, ShoesOf(OldShoes, 242, 275, StrengthSelfV, ImpenetrabilityV)) };
        var find = Candidate(NewShoes, ShoesOf(NewShoes, 234, 251, ImpenetrabilityV, MajorStrength));
        var pick = Pick(find, worn, Terms(AtTheLine));

        Assert.Equal(SelfEquipKind.Replace, pick.Kind);
        Assert.Equal([OldShoes], pick.Displaced.Select(d => d.Guid));
        Assert.Equal(Worth(15, AtTheLine), pick.Part("capacity"), 6);
        Assert.Contains("+15 Strength = +2250 capacity (150 a point, Player_Inventory.cs:46-51)", pick.Why, StringComparison.Ordinal);
        Assert.Contains("cantrips first", pick.Why, StringComparison.Ordinal);

        // the worn pair is dark at 273 (ItemDifficulty 275): its Strength Self V counts nothing, the find's Major Strength is on (251 met)
        Assert.NotNull(WearScore.SpellsOff(worn[0].Appraisal!, new Qwinnie()));
        Assert.Null(WearScore.SpellsOff(find.Appraisal!, new Qwinnie()));
    }

    [Fact]
    public void Two_Major_Strength_pieces_count_once()
    {
        var gloves = new WornPiece(Gloves, "Gloves", 0x2, 0x20, 0x8000, Appraised(Gloves, new() { [28] = 200, [5] = 50, [107] = 600, [108] = 600, [109] = 200 }, new() { [15] = 1.0 }, MajorStrength));
        var worn = new List<WornPiece> { Wearing(OldShoes, ShoesOf(OldShoes, 234, 251, ImpenetrabilityV)), gloves };
        var find = Candidate(NewShoes, ShoesOf(NewShoes, 234, 251, ImpenetrabilityV, MajorStrength));
        var pick = Pick(find, worn, Terms(AtTheLine));
        Assert.Equal(0, pick.Part("capacity"));
        Assert.NotEqual(SelfEquipKind.Replace, pick.Kind);

        // a wielded sceptre's Major Strength is the same top layer: the shoes add nothing over it either
        var sceptre = new WornPiece(0x800003C0, "Piercing Sceptre", 0x8000, SelfEquipJudge.Held, 0, Appraised(0x800003C0, new() { [5] = 50, [107] = 1_600, [108] = 1_600 }, new() { [144] = 0.05 }, MajorStrength));
        var withSceptre = new List<WornPiece> { worn[0], sceptre };
        Assert.Equal(0, Pick(find, withSceptre, Terms(AtTheLine)).Part("capacity"));

        // and the caster pick: a sceptre's Major Strength counts nothing over a worn Major Strength
        var judge = Judge(Terms(AtTheLine));
        var context = judge.Context(new Qwinnie(), worn);
        Assert.Equal(15, context.WornStrength);
        Assert.Equal(0, GearScore.Caster(sceptre.Guid, sceptre.Name, sceptre.Appraisal!, context).CapacityPercent);
        Assert.Equal(Worth(15, AtTheLine), GearScore.Caster(sceptre.Guid, sceptre.Name, sceptre.Appraisal!, judge.Context(new Qwinnie(), [worn[0]])).CapacityPercent, 6);
    }

    [Fact]
    public void A_capacity_gain_changes_the_gear_decision_at_the_load_line()
    {
        // the same armour as the worn pair, plus Major Strength: at the load line it is worn; with an empty pack the room is not filled and it is not
        var worn = new List<WornPiece> { Wearing(OldShoes, ShoesOf(OldShoes, 234, 251, ImpenetrabilityV)) };
        var find = Candidate(NewShoes, ShoesOf(NewShoes, 234, 251, ImpenetrabilityV, MajorStrength));
        var full = Pick(find, worn, Terms(AtTheLine));
        var empty = Pick(find, worn, Terms(0));
        Assert.Equal(SelfEquipKind.Replace, full.Kind);
        Assert.True(full.Part("capacity") > 0);
        Assert.Equal(0, empty.Part("capacity"));
        Assert.Equal(SelfEquipKind.None, empty.Kind);

        // the caster pick: a sceptre with Major Strength and otherwise the held caster's numbers beats it by more than the swap margin at the line, and does not with an empty pack
        var held = new WornPiece(0x800003C0, "Piercing Sceptre", 0x8000, SelfEquipJudge.Held, 0, Appraised(0x800003C0, new() { [5] = 50 }, new() { [144] = 0.05 }));
        var strong = Appraised(0x80006056, new() { [5] = 50, [107] = 1_600, [108] = 1_600 }, new() { [144] = 0.05 }, MajorStrength);
        var worn2 = new List<WornPiece> { worn[0], held };
        var atLine = Judge(Terms(AtTheLine)).Context(new Qwinnie(), worn2);
        var bare = Judge(Terms(0)).Context(new Qwinnie(), worn2);
        var margin = L.CasterScore.SwapMargin;
        var heldAt = GearScore.Caster(held.Guid, held.Name, held.Appraisal!, atLine);
        var strongAt = GearScore.Caster(0x80006056, "Piercing Sceptre", strong, atLine);
        output.WriteLine(GearScore.CasterNumbers(strongAt) + " against " + GearScore.CasterNumbers(heldAt));
        Assert.Equal(Worth(15, AtTheLine), strongAt.CasterScore - heldAt.CasterScore, 6);
        Assert.True(Worth(15, AtTheLine) > margin);
        Assert.True(GearScore.CasterBeats(strongAt, heldAt, margin));
        Assert.False(GearScore.CasterBeats(GearScore.Caster(0x80006056, "Piercing Sceptre", strong, bare), GearScore.Caster(held.Guid, held.Name, held.Appraisal!, bare), margin));
    }

    [Fact]
    public void The_capacity_term_is_150_a_Strength_point_valued_by_her_load()
    {
        var load = new CapacityLoad(Capacity, AtTheLine, 100, L.WearScore.Burden);
        var part = GearScore.CapacityPart(15, load);
        output.WriteLine(part.ToString());
        Assert.Equal(L.WearScore.Burden * (2_250.0 / Capacity * 100.0), part.Value, 9);
        Assert.Equal(0, GearScore.CapacityPart(15, null).Value);
        Assert.Contains("her load is unread", GearScore.CapacityPart(15, null).Note, StringComparison.Ordinal);

        // half way to the line less the gain: half of it
        var half = GearScore.CapacityPart(15, load with { Carried = (Capacity - 2_250) / 2 });
        Assert.Equal(part.Value / 2, half.Value, 6);
        Assert.Equal(15, GearScore.StrengthCantrip(ShoesOf(NewShoes, 234, 251, ImpenetrabilityV, MajorStrength)));
    }

    /// <summary>
    /// The price of the two Arcane Lore points (273 to 275) on the retail XpTable: her trained Arcane Lore at rank 131 (live ladder58 09:18:39 UTC: "Arcane Lore ranks 131 -&gt; 163 for 126,550,212", which pins what she has spent on it).
    /// </summary>
    public sealed class Retail(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
    {
        [RequiresDatsFact]
        public void Her_two_Arcane_Lore_points_cost_the_trained_lists_ranks_131_to_133()
        {
            var list = XpTableData.Load(fixture.Dats).TrainedSkillXpList;
            var spent = (long)list[163] - 126_550_212;
            Assert.InRange(spent, (long)list[131], (long)list[132] - 1); // the live line's price ended rank 163 from inside rank 131
            var two = (long)list[133] - spent;
            output.WriteLine($"Arcane Lore ranks 131 -> 133 (273 -> 275): {two:N0} experience (rank 132 {(long)list[132] - spent:N0}, rank 133 {(long)list[133] - (long)list[132]:N0}); spent on it {spent:N0}");
            Assert.True(two > 0);
            Assert.True(two < 2_455_655, "her 2,455,655 unassigned at level 80 (06:45 UTC) cover the two points");
        }
    }
}
