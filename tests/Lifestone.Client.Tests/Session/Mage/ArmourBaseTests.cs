using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 (the owner's go-ahead of 2026-11-19: the armour score counted her Impenetrability twice). What these prove: a piece's own armour level and protection are read out of an appraisal taken with enchantments on it
/// (<see cref="ArmourBase"/>: ACE sends both with the enchantments in, AppraiseInfo.cs:407-408 and ArmorProfile.cs:40-59 @95ef426d, and lists the enchantments beside them), so the same piece scores the same whether it was
/// appraised bare in the pack or worn under her spells, by every reader: the wear pick's damage factor, the gear score, the armour report the fight reads.
/// <para>The live case is run 6 of 2026-11-19 (cache/m6/sprint/run6/one-window.log): the Pathwarden Robe (armour level 50 in ace_world, its own Impenetrability II 1482) read 100 at 21:43 UTC and 150 at 23:40:07 after her
/// Impenetrability IV 1484 (cast 22:09:59) and Bludgeon Bane IV 1514 (22:10:04); the blacksmith's leather read its shop numbers (90, 130, 140), never having been cast on. The wear pick then put the robe back on over the set
/// a quarter of a second after each swap. The set's pieces are the Pathwarden quest file's (<see cref="LeatherSet"/>).</para>
/// </summary>
public sealed class ArmourBaseTests(ITestOutputHelper output)
{
    private const uint Bludgeon = 0x4, Fire = 0x10;
    private const uint Robe = 0x80009ED3, Gauntlets = 0x80009ED5;
    private const uint ImpenII = 1482, ImpenIV = 1484, BrittlemailIII = 1489, MinorImpen = 2604, BludgeonBaneIV = 1514, BludgeonLureII = 1506, FlameBaneIII = 1549;
    private const uint E = IdentifyResponseMsg.ItemEnchantmentBit;

    private sealed class Climber : IGearSkills
    {
        public int Level => 18;

        public int AdvancementClass(uint skill) => 2;

        public int? Current(uint skill) => skill == MageTemplate.ArcaneLore ? 112 : 228;

        public int? Base(uint skill) => null;
    }

    /// <summary>An armour appraisal: its armour level, its bludgeon protection in a profile (the other protections 1), its spell book (an enchantment on the piece carries <see cref="E"/>) and, optionally, the armour highlight.</summary>
    private static Appraisal Appraised(uint guid, int armour, float bludgeon, uint[] book, ushort highlight = 0, float fire = 1f, int burden = 200) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook | IdentifyResponseFlags.ArmorProfile,
            true,
            new Dictionary<uint, int> { [28] = armour, [5] = burden, [107] = 1000, [108] = 1000 }.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. book],
            new AppraisalArmorProfile(1f, 1f, bludgeon, 1f, fire, 1f, 1f, 1f),
            null,
            null,
            null,
            highlight == 0 ? null : new AppraisalBitfield(highlight, highlight),
            null,
            null,
            null),
        1,
        0);

    /// <summary>The robe as the pack shows it: its own numbers, nothing on it.</summary>
    private static Appraisal RobeBare() => Appraised(Robe, 50, 1f, [ImpenII]);

    /// <summary>The robe as run 6 read it at 21:43 UTC: worn, its own Impenetrability II lit on it.</summary>
    private static Appraisal RobeLit() => Appraised(Robe, 100, 1f, [ImpenII, ImpenII | E], highlight: 0x1);

    /// <summary>The robe as run 6 read it at 23:40:07 UTC: her Impenetrability IV and Bludgeon Bane IV over its own spell.</summary>
    private static Appraisal RobeBuffed() => Appraised(Robe, 150, 1.75f, [ImpenII, ImpenII | E, ImpenIV | E, BludgeonBaneIV | E], highlight: 0x1 | 0x8);

    private static WornPiece WornRobe(Appraisal a) => new(Robe, "Pathwarden Robe", 0x4, 0x7F01, 0x17F00, a);

    private static WornPiece WornGauntlets() => new(Gauntlets, "Pathwarden Gauntlets", 0x2, 0x20, 0x8000, Appraised(Gauntlets, 120, 1f, [], burden: 900));

    /// <summary>The blacksmith's set as she holds it on the swap: every piece at its shop numbers, nothing cast on any.</summary>
    private static List<WornPiece> Leather() =>
        [.. LeatherSet.Default!.Pieces.Select((p, i) => new WornPiece(0x80000300 + (uint)i, p.Name, 0x2, p.Locations, p.Coverage, Appraised(0x80000300 + (uint)i, p.ArmourLevel, 1f, [], burden: p.Burden)))];

    private static GearContext Context() => new SelfEquipJudge([], Fire, _ => null, [Bludgeon]).Context(new Climber());

    [Fact]
    public void A_pieces_own_armour_level_is_the_appraised_one_less_the_top_layer_of_each_family_on_it()
    {
        Assert.Equal(50, ArmourBase.Level(RobeBare(), out var bare));
        Assert.Contains("as appraised", bare, StringComparison.Ordinal);
        Assert.Equal(50, ArmourBase.Level(RobeLit()));

        // her IV over its own II: one family, ACE applies the top layer (100), never the sum (150)
        Assert.Equal(50, ArmourBase.Level(RobeBuffed(), out var buffed));
        Assert.Contains("appraised 150 less 100", buffed, StringComparison.Ordinal);

        // a cantrip and a Brittlemail are families of their own: 50 + 100 + 20 - 75 = 95
        Assert.Equal(50, ArmourBase.Level(Appraised(Robe, 95, 1f, [ImpenIV | E, MinorImpen | E, BrittlemailIII | E])));

        // a spell the piece casts or teaches is not an enchantment on it, and a bane changes no level
        Assert.Equal(90, ArmourBase.Level(Appraised(Robe, 90, 1.75f, [ImpenIV, BludgeonBaneIV | E])));

        // ACE marks the level as enchanted and lists nothing that explains it: taken as appraised, by name
        Assert.Equal(150, ArmourBase.Level(Appraised(Robe, 150, 1f, [], highlight: 0x1), out var unread));
        Assert.Contains("TODO(m6-armour-base-unread)", unread, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pieces_own_protection_is_the_profiles_less_the_banes_and_lures_on_it_by_damage_type()
    {
        Assert.Equal(1.0, GearScore.ModVs(RobeBuffed(), Bludgeon, out _), 3); // 1.75 less Bludgeon Bane IV's 0.75
        Assert.Equal(1.0, GearScore.ModVs(RobeBuffed(), Fire, out _), 3); // no flame bane on it
        Assert.Equal(1.0, GearScore.ModVs(Appraised(Robe, 50, 0.75f, [BludgeonLureII | E]), Bludgeon, out _), 3); // a lure: 0.75 + 0.25
        Assert.Equal(0.2, GearScore.ModVs(Appraised(Robe, 50, 1f, [FlameBaneIII | E], fire: 0.7f), Fire, out _), 3); // the robe's fire 0.2 under Flame Bane III's 0.5
        Assert.Equal(0.0, ArmourBase.Mod(RobeBare(), 15));
    }

    [Fact]
    public void The_same_piece_scores_the_same_bare_in_the_pack_and_worn_under_her_spells()
    {
        var lines = new ArmourLines(4, 4, 3, 3);
        var context = Context();
        var bare = WearScore.DamageFactor([WornRobe(RobeBare()), WornGauntlets()], context, lines);
        Assert.Equal(bare, WearScore.DamageFactor([WornRobe(RobeLit()), WornGauntlets()], context, lines), 9);
        Assert.Equal(bare, WearScore.DamageFactor([WornRobe(RobeBuffed()), WornGauntlets()], context, lines), 9);

        // the gear score of the piece alone
        Assert.Equal(GearScore.Armour(Robe, "Pathwarden Robe", RobeBare(), 0x17F00, 200, context).Total, GearScore.Armour(Robe, "Pathwarden Robe", RobeBuffed(), 0x17F00, 200, context).Total, 6);

        // the armour report the fight's line reads: the piece's own level, protection and spell, whatever was on it at the appraisal
        var report = ArmorReport.Read([WornRobe(RobeBuffed())], Bludgeon, out var unread);
        Assert.Equal(0, unread);
        Assert.Equal(new ArmorPiece("Pathwarden Robe", 0x17F00, 50, 1.0, 50), report.Single() with { BludgeonMod = Math.Round(report.Single().BludgeonMod, 3) });
        Assert.Equal(ArmorReport.Read([WornRobe(RobeBare())], Bludgeon, out _).Single(), report.Single() with { BludgeonMod = Math.Round(report.Single().BludgeonMod, 3) });
    }

    [Theory]
    [InlineData(4, 4, 3, 3)] // the Lodge gate's floor, the lines run 6's wear pick read
    [InlineData(4, 4, 4, 4)] // what she casts at level 18 (Impenetrability IV and Bludgeon Bane IV, run 6 22:09:59 UTC)
    public void Under_the_same_lines_the_leather_set_lets_less_through_than_the_robe_however_the_robe_was_appraised(int armor, int protection, int impen, int bane)
    {
        var lines = new ArmourLines(armor, protection, impen, bane);
        var context = Context();
        var robe = WearScore.DamageFactor([WornRobe(RobeBuffed()), WornGauntlets()], context, lines);
        var leather = WearScore.DamageFactor(Leather(), context, lines);
        output.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"lines {lines}: robe and gauntlets {robe:0.0000}, leather set {leather:0.0000}: the leather lets {100 * (1 - (leather / robe)):0.#}% less through"));
        Assert.True(leather < robe, $"leather {leather} against robe {robe}");

        // before the fix the buffed robe read 150 + 75 on every part and won: the appraised numbers scored as its own
        var doubled = WearScore.DamageFactor([WornRobe(Appraised(Robe, 150, 1.75f, [ImpenII])), WornGauntlets()], context, lines);
        Assert.True(doubled < leather, "the double count is what put the robe ahead");
    }

    [Fact]
    public void The_live_case_the_robe_is_not_put_back_on_over_the_leather_set_she_has_just_put_on()
    {
        // run 6, 23:40:07 UTC, the equip pass after the swap: the seven bought pieces and the Pathwarden Gauntlets worn, the robe (appraised under her spells) in the pack; the Lodge gate's lines, the Lodge rung's hitter
        // (Male Tusker, hit 36), capacity 9,900, as the log's wear pick line reads them. It answered "replace with 'Pathwarden Robe' ... gain 0.646% of the hour = ... armour 0.32 ... burden 0.32".
        var ladder = Lifestone.Client.Session.Grounds.LadderLoader.WarMage;
        var lines = ArmourLines.Of(ladder.GateOf("lodge"));
        Assert.Equal(new ArmourLines(4, 4, 3, 3), lines);
        var hitter = ladder.RungFor(18).Health!.Hitter;
        Assert.Equal(36, hitter!.Hit);
        var terms = new WearTerms(ladder.WearScore, new CasterTerms(ladder.CasterScore, new CasterBolt("Flame Bolt III", 15, 100), CasterAuras.None), 63, hitter, lines) { Capacity = 9_900 };
        var judge = new SelfEquipJudge([], Fire, _ => null, [Bludgeon]).WithWear(terms);
        List<WornPiece> worn = [.. Leather().Where(p => p.Coverage != 0x8000), WornGauntlets()];

        var pick = WearScore.Judge(new GearCandidate(Robe, "Pathwarden Robe", 0x4, 0x7F01, 0x17F00, 200, RobeBuffed()), worn, judge.Context(new Climber()), terms);
        output.WriteLine($"{pick.Kind}: {pick.Why}");
        Assert.NotEqual(SelfEquipKind.Replace, pick.Kind);
        Assert.Equal(0.32, pick.Parts.Single(p => p.Name == "burden").Value, 2); // the robe is 3,198 lighter than the seven pieces (3,398 against 200), as the log's line: the weight is counted, and the armour outweighs it

        // the appraised numbers read as the robe's own (what the pick did): the robe wins
        var before = WearScore.Judge(new GearCandidate(Robe, "Pathwarden Robe", 0x4, 0x7F01, 0x17F00, 200, Appraised(Robe, 150, 1.75f, [ImpenII])), worn, judge.Context(new Climber()), terms);
        output.WriteLine($"read as its own: {before.Kind}: {before.Why}");
        Assert.Equal(SelfEquipKind.Replace, before.Kind);
    }
}

/// <summary>The stacking families of <see cref="ArmourSpellRows"/> against the retail dats' spell table, whenever the dats are present.</summary>
public sealed class ArmourBaseDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Every_rows_family_is_the_dats_spell_category()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(192, ArmourSpellRows.ById.Count);
        foreach (var row in ArmourSpellRows.ById.Values)
        {
            Assert.True(spells.TryGetValue(row.Id, out var info), $"spell {row.Id} is not in the dats");
            Assert.Equal((row.Id, info!.Category), (row.Id, row.Category));
        }
    }
}
