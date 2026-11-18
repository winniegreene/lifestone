using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-armour</c>; the owner, 2026-11-13: she evaluates the armour she finds as possible upgrades for what she wears, especially with regard to minor and major cantrips). What these prove: the wear pick scores a piece as the set
/// she would wear with it on against the set she wears, in percent of her hour by the ladder's <c>wear_score</c>; a Major War Magic Aptitude beats her bare piece; a ring fills an empty finger; a heavier piece is refused where her kept
/// load would pass the jump's line; a piece whose spells ACE would not activate gets no credit for them (kept for the review instead); her live worn set is the baseline, every piece holding against its own twin.
/// <para>Her worn set is the read-only shard's rows of 2026-11-13 (biota_properties_int 1, 4, 5, 9, 10, 28, 107, 108, 109 and float 15 and the spell book of the twelve pieces wielded by Qwinnie): Shirt 130, Wide Breeches 2604, Metal Cap 46
/// (armour level 203), Long Leather Gauntlets 25646 (180, Impenetrability III, Frost Bane II), Studded Leather Pauldrons 89, Bracers 38, Leggings 84 (90 each), Breastplate 42 (179, Impenetrability II, Bludgeon Bane II), Boots 116 (165),
/// Chainmail Girth 415 (168, bludgeon 0.8, five spells), the Pathwarden Trinket 41513 (Augmented Understanding II) and the Staff 2547; no ring, necklace or bracelet. Her numbers: War Magic 297 under the bot's +40 (the json's Abode
/// source), Mana Conversion 213 and maximum mana 332 (the mana model's level 40 build, cache/m6/mana/model.md), Arcane Lore 75 (Focus 202 / 3 plus ranks). The bolt is Flame Bolt V, base mana 25, power 200 (CasterAurasDatTests pins
/// it); the Abode's Redeemers hold Magic Defense 171 and hit bludgeon only (tusker-abode.json), the rung's hitter and gate are the ladder's.</para>
/// </summary>
public sealed class WearPickTests(ITestOutputHelper output)
{
    private const uint Bludgeon = 0x4, Fire = 0x10;
    private const uint Gauntlets = 0x8000064D, Leggings = 0x8000098C;
    private const uint MajorWarMagicAptitude = 2534, MinorFocus = 2581;

    private sealed class Qwinnie : IGearSkills
    {
        public int Level => 49;

        public int AdvancementClass(uint skill) => skill is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3 : skill == MageTemplate.MeleeDefense ? 1 : 2;

        public int? Current(uint skill) => skill switch
        {
            MageTemplate.WarMagic => 297,
            MageTemplate.LifeMagic => 290,
            MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment => 280,
            MageTemplate.ManaConversion => 213,
            MageTemplate.ArcaneLore => 75,
            _ => null,
        };

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? 257 : null;

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

    /// <summary>A piece's appraisal: armour level, burden, item mana and its maximum, ItemDifficulty, its bludgeon protection, its spells.</summary>
    private static Appraisal Of(uint guid, int armour, int burden, double bludgeon, int? mana = null, int? difficulty = null, params uint[] spells)
    {
        var ints = new Dictionary<uint, int> { [28] = armour, [5] = burden };
        if (mana is { } m)
        {
            (ints[107], ints[108]) = (m, m);
        }

        if (difficulty is { } d)
        {
            ints[109] = d;
        }

        return Appraised(guid, ints, new() { [15] = bludgeon }, spells);
    }

    private static WornPiece Worn(uint guid, string name, uint type, uint location, uint coverage, Appraisal a) => new(guid, name, type, location, coverage, a);

    /// <summary>The live set (see the class remarks): the shard's guids, locations, ClothingPriority and properties.</summary>
    private static List<WornPiece> LiveSet() =>
    [
        Worn(0x8000019A, "Shirt", 0x4, 30, 0x68, Of(0x8000019A, 0, 75, 1.0)),
        Worn(0x8000019B, "Wide Breeches", 0x4, 68, 0x13, Of(0x8000019B, 0, 90, 1.0)),
        Worn(0x80000383, "Pathwarden Trinket", 0x8, 0x4000000, 0, Of(0x80000383, 0, 60, 1.0, 5943, 15, 5154)),
        Worn(0x8000035C, "Metal Cap", 0x2, 1, 0x4000, Of(0x8000035C, 203, 120, 1.0)),
        Worn(Gauntlets, "Long Leather Gauntlets", 0x2, 32, 0x8000, Of(Gauntlets, 180, 270, 1.0, 257, 37, 1483, 1524)),
        Worn(0x80000B8A, "Studded Leather Pauldrons", 0x2, 2048, 0x1000, Of(0x80000B8A, 90, 350, 1.0)),
        Worn(0x80000B8B, "Studded Leather Bracers", 0x2, 4096, 0x2000, Of(0x80000B8B, 90, 330, 1.0)),
        Worn(Leggings, "Studded Leather Leggings", 0x2, 24576, 0x300, Of(Leggings, 90, 900, 1.0)),
        Worn(0x80000D8A, "Studded Leather Breastplate", 0x2, 512, 0x400, Of(0x80000D8A, 179, 651, 1.0, 249, 47, 1482, 1512)),
        Worn(0x80000DAC, "Studded Leather Boots", 0x2, 384, 0x10000, Of(0x80000DAC, 165, 683, 1.0, 140, 14, 1482, 1524, 1558)),
        Worn(0x800015CA, "Chainmail Girth", 0x2, 1024, 0x800, Of(0x800015CA, 168, 463, 0.8, 521, 59, 1482, 1493, 1523, 1537, 1569)),
        Worn(0x800019C5, "Staff", 0x8000, SelfEquipJudge.Held, 0, Appraised(0x800019C5, new() { [5] = 50 }, new() { [29] = 1.07, [144] = 0.05 })),
    ];

    private static Ladder L => LadderLoader.WarMage;

    private static WearTerms Abode(bool jumps = false, int? kept = null) =>
        new(L.WearScore, new CasterTerms(L.CasterScore, new CasterBolt("Flame Bolt V", 25, 200), CasterAuras.None), 171, L.RungFor(49).Health!.Hitter, ArmourLines.Of(L.GateOf("abode"))) { Capacity = 17_250, KeptBurden = kept, Jumps = jumps };

    private static SelfEquipJudge Judge(WearTerms terms) => new SelfEquipJudge([], Fire, id => CantripRows.Of(id)?.Name, [Bludgeon]).WithWear(terms);

    private WearPick Pick(GearCandidate candidate, WearTerms terms, List<WornPiece>? worn = null)
    {
        var judge = Judge(terms);
        var pick = WearScore.Judge(candidate, worn ?? LiveSet(), judge.Context(new Qwinnie()), terms);
        output.WriteLine($"{pick.Kind} '{candidate.Name}': {pick.Why}");
        return pick;
    }

    private static GearCandidate GauntletsWith(uint guid, int? difficulty, int? mana, params uint[] spells) =>
        new(guid, "Long Leather Gauntlets", 0x2, 0x20, 0x8000, 270, Of(guid, 180, 270, 1.0, mana, difficulty, spells));

    [Fact]
    public void A_piece_with_Major_War_Magic_Aptitude_beats_her_bare_piece()
    {
        // the same gauntlets as hers (armour level 180, burden 270, her two spells) with Major War Magic Aptitude: the armour and burden terms are 0, the War Magic term is the gain
        var candidate = GauntletsWith(0x80007001, 37, 257, 1483, 1524, MajorWarMagicAptitude);
        var pick = Pick(candidate, Abode());
        Assert.Equal(SelfEquipKind.Replace, pick.Kind);
        Assert.Equal([Gauntlets], pick.Displaced.Select(d => d.Guid));
        Assert.Equal(0, pick.Part("armour"), 6);
        Assert.Equal(0, pick.Part("burden"), 6);
        Assert.True(pick.Part("war magic") > L.WearScore.SwapMargin, pick.Why);
        Assert.Equal(pick.Gain, pick.Own - pick.DisplacedOwn, 6);

        var choice = Judge(Abode()).Judge(candidate, LiveSet(), new Qwinnie());
        Assert.Equal((SelfEquipKind.Replace, Gauntlets), (choice.Kind, choice.Replaces));
        Assert.Contains("Major War Magic Aptitude", choice.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_displaced_piece_the_sale_keeps_still_weighs_and_one_it_sells_does_not()
    {
        // the leggings' swap above (900 worn, 2,000 held): +1,100 when the old pair is sold, +2,000 when the sale keeps the old pair in her pack
        var heavy = new GearCandidate(0x80007004, "Platemail Leggings", 0x2, 0x6000, 0x300, 2000, Of(0x80007004, 400, 2000, 1.0));
        var sold = Pick(heavy, Abode());
        var kept = Pick(heavy, Abode() with { StaysInPack = p => p.Guid == Leggings });
        var other = Pick(heavy, Abode() with { StaysInPack = p => p.Guid != Leggings });
        Assert.Equal(SelfEquipKind.Replace, sold.Kind);
        Assert.Contains("+1100 of 17250", sold.Why, StringComparison.Ordinal);
        Assert.Contains("+2000 of 17250", kept.Why, StringComparison.Ordinal);
        Assert.True(kept.Part("burden") < sold.Part("burden"), kept.Why);
        Assert.Equal(sold.Part("burden"), other.Part("burden"), 9); // a keeper elsewhere changes nothing here
        output.WriteLine($"burden part: sold {sold.Part("burden"):0.####}, kept {kept.Part("burden"):0.####}; gain {sold.Gain:0.###} -> {kept.Gain:0.###}");
    }

    [Fact]
    public void A_ring_fills_an_empty_finger_and_a_plain_ring_does_not()
    {
        var ring = new GearCandidate(0x80007002, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(0x80007002, new() { [5] = 15, [107] = 800, [108] = 800, [109] = 60 }, [], MinorFocus));
        var pick = Pick(ring, Abode());
        Assert.Equal(SelfEquipKind.Wear, pick.Kind);
        Assert.Equal(0x80000u, pick.Location); // the right finger first, as WieldSlotChoice's preference
        Assert.Empty(pick.Displaced);
        Assert.True(pick.Gain > 0 && pick.Part("war magic") > 0 && pick.Part("mana conversion") > 0, pick.Why); // Focus +5 is War Magic +1.25 and Mana Conversion +0.83

        var plain = new GearCandidate(0x80007003, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(0x80007003, new() { [5] = 15 }, []));
        var none = Pick(plain, Abode());
        Assert.Equal(SelfEquipKind.None, none.Kind); // its burden is its only term
        Assert.True(none.Gain < 0, none.Why);
    }

    [Fact]
    public void A_heavier_piece_is_refused_where_her_kept_load_would_pass_the_jumps_line()
    {
        // leggings of armour level 400 and burden 2,000 for her 900: worth wearing for the armour, 1,100 heavier
        var heavy = new GearCandidate(0x80007004, "Platemail Leggings", 0x2, 0x6000, 0x300, 2000, Of(0x80007004, 400, 2000, 1.0));
        var flat = Pick(heavy, Abode());
        Assert.Equal(SelfEquipKind.Replace, flat.Kind);
        Assert.Equal([Leggings], flat.Displaced.Select(d => d.Guid));
        Assert.True(flat.Part("armour") > 0 && flat.Part("burden") < 0, flat.Why);

        var roomy = Pick(heavy, Abode(jumps: true, kept: 12_000));
        Assert.Equal(SelfEquipKind.Replace, roomy.Kind); // 12,000 + 1,100 is under the line of 17,250

        var full = Pick(heavy, Abode(jumps: true, kept: 16_500));
        Assert.Equal(SelfEquipKind.None, full.Kind); // 16,500 + 1,100 passes it
        Assert.Contains("refused by the load", full.Why, StringComparison.Ordinal);
        Assert.Contains("17600", full.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 (live 2026-11-14 17:33, ladder11): after a death her Arcane Lore read 213 without her buffs, and the wear pick judged her worn Major Mana Conversion Prowess bracelet (ItemDifficulty 222) dark, swapped it for an
    /// Armor Self V bracelet with a little more armour, and the sale sold it. ACE checks the lore only at the wield: a worn piece lit on her (an enchantment she wears cast by it, <see cref="GearContext.LitBy"/>) keeps its
    /// spells whatever her lore now, so they count and the swap is not made. Unlit, the old reading stands.
    /// </summary>
    [Fact]
    public void A_worn_piece_lit_on_her_keeps_its_cantrip_whatever_her_lore_reads_now_and_is_not_swapped_for_armour()
    {
        const uint MajorManaConversion = 2525, ArmorSelfV = 1311;
        const uint Lit = 0x8000A4C2, Other = 0x8000AF90;
        var worn = LiveSet();
        worn.Add(Worn(Lit, "Bracelet", 0x8, 0x10000, 0, Appraised(Lit, new() { [5] = 60, [107] = 1901, [108] = 1901, [109] = 222 }, [], ArmorSelfV, MajorManaConversion)));
        worn.Add(Worn(Other, "Bracelet", 0x8, 0x20000, 0, Appraised(Other, new() { [5] = 60, [107] = 1900, [108] = 1900, [109] = 60 }, [], MinorFocus)));
        var armour = new GearCandidate(0x80007010, "Bracelet", 0x8, 0x10000 | 0x20000, 0, 60, Appraised(0x80007010, new() { [5] = 60, [28] = 60, [107] = 1800, [108] = 1800, [109] = 60 }, [], ArmorSelfV));
        var terms = Abode();
        var judge = Judge(terms);

        var dark = WearScore.Judge(armour, worn, judge.Context(new Qwinnie()), terms); // her lore 75 under 222: the Major's spells read off, as live
        output.WriteLine(dark.Why);
        Assert.Contains(Lit, dark.Displaced.Select(d => d.Guid)); // the old reading: the lit bracelet is the side that comes off

        var lit = WearScore.Judge(armour, worn, judge.Context(new Qwinnie()) with { LitBy = guid => guid == Lit }, terms);
        output.WriteLine(lit.Why);
        Assert.DoesNotContain(Lit, lit.Displaced.Select(d => d.Guid)); // its Major Mana Conversion counts: it is never the side that comes off for the armour
    }

    /// <summary>
    /// M6 16 E5e4 (live 2026-11-18, ladder67: ACE answered the wield of five worn pieces "Your Missile Defense is too low to use that item's magic." or "Your Melee Defense ..."): loot armour and jewellery may carry an
    /// activation limit on a skill (LootGenerationFactory_Magic.cs:177-203 @95ef426d), and an appraisal carries its skill as PropertyInt 176 (AppraiseInfo.cs:410-411). The pick read the skill where no appraisal puts it,
    /// so the limit was always met and a ring she cannot light was worn for its cantrip. Read, the limit keeps its spells off and the pick gives them no credit.
    /// </summary>
    [Fact]
    public void A_ring_whose_skill_limit_she_does_not_meet_is_not_worn_for_a_cantrip_ACE_would_not_light()
    {
        Dictionary<uint, int> ints = new() { [5] = 15, [107] = 800, [108] = 800, [109] = 60, [SelfEquipJudge.ItemSkillLevelLimitInt] = 144 };
        var asReadBefore = new GearCandidate(0x80007020, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(0x80007020, ints, [], MinorFocus));
        var before = Pick(asReadBefore, Abode());
        Assert.Equal(SelfEquipKind.Wear, before.Kind); // the limit's level alone, its skill unread: worn, the Minor Focus counted
        Assert.True(before.Part("war magic") > 0, before.Why);

        ints[SelfEquipJudge.AppraisalItemSkillInt] = 7; // Missile Defense, as the appraisal carries it; her Missile Defense is not among the numbers this judge is given, so it is unmet by name
        var ring = new GearCandidate(0x80007021, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(0x80007021, ints, [], MinorFocus));
        var pick = Pick(ring, Abode());
        Assert.NotEqual(SelfEquipKind.Wear, pick.Kind);
        Assert.Equal(0, pick.Part("war magic"), 6);
        Assert.Contains("activation: Missile Defense unread < 144", pick.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 (the owner's ruling, 2026-11-15: the armor self V spell is overridden by the active spells, and cantrips are buffs on top of those spells): a worn bracelet whose only spell is Armor Self V adds nothing while her Armor Self
    /// line is V or higher (one category, ACE's top layer), so a bracelet with Major Life Magic Aptitude replaces it: no swap margin protects a piece worth nothing to her.
    /// </summary>
    [Fact]
    public void A_great_cantrip_bracelet_replaces_one_whose_armor_self_her_line_overrides()
    {
        const uint MajorLifeMagic = 2520, ArmorSelfV = 1311, BludgeonProtectionV = 1022;
        const uint ArmorOnly = 0x8000AF90, FocusMinor = 0x80004E6E;
        var worn = LiveSet();
        worn.Add(Worn(ArmorOnly, "Bracelet", 0x8, 0x10000, 0, Appraised(ArmorOnly, new() { [5] = 60, [107] = 1900, [108] = 1900, [109] = 60 }, [], ArmorSelfV)));
        worn.Add(Worn(FocusMinor, "Bracelet", 0x8, 0x20000, 0, Appraised(FocusMinor, new() { [5] = 60, [107] = 1900, [108] = 1900, [109] = 60 }, [], MinorFocus)));
        var terms = Abode();
        var judge = Judge(terms);
        var context = judge.Context(new Qwinnie());

        // the Armor Self V bracelet's armour is her line's: off or on, the damage factor is the same
        var without = worn.Where(w => w.Guid != ArmorOnly).ToList();
        Assert.Equal(WearScore.DamageFactor(without, context, terms.Lines), WearScore.DamageFactor(worn, context, terms.Lines), 9);

        var major = new GearCandidate(0x80001B1B, "Bracelet", 0x8, 0x10000 | 0x20000, 0, 60, Appraised(0x80001B1B, new() { [5] = 60, [107] = 1900, [108] = 1900, [109] = 60 }, [], BludgeonProtectionV, MajorLifeMagic));
        var pick = WearScore.Judge(major, worn, context, terms);
        output.WriteLine(pick.Why);
        Assert.Equal(SelfEquipKind.Replace, pick.Kind);
        Assert.Equal([ArmorOnly], pick.Displaced.Select(d => d.Guid)); // the side worth nothing comes off, not the Minor Focus
        Assert.True(pick.Part("life magic") > 0, pick.Why);
    }

    /// <summary>
    /// M6 14 (the owner, 2026-11-15: the aim is a Minor or better cantrip for her core attributes and skills in every slot; the armour's own numbers are secondary): gauntlets of armour level 40 with Minor Focus replace her
    /// worn Long Leather Gauntlets of 180 with no cantrip: the cantrips decide first.
    /// </summary>
    [Fact]
    public void Cantrips_first_a_minor_focus_glove_replaces_better_armoured_gloves_with_none()
    {
        var thin = new GearCandidate(0x80007011, "Gloves", 0x2, 32, 0x8000, 150, Of(0x80007011, 40, 150, 1.0, 800, 60, MinorFocus));
        var pick = Pick(thin, Abode());
        Assert.Equal(SelfEquipKind.Replace, pick.Kind);
        Assert.Equal([Gauntlets], pick.Displaced.Select(d => d.Guid));
        Assert.StartsWith("cantrips first: it raises her core cantrips", pick.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_whose_spells_will_not_activate_gets_no_credit_for_them()
    {
        // ItemDifficulty 400 against her Arcane Lore 75: ACE equips it and leaves its spells off (CheckUseRequirements, WorldObject_Use.cs:204-210; TryActivateSpells, Player_Inventory.cs:327-335)
        var lore = Pick(GauntletsWith(0x80007005, 400, 257, 1483, 1524, MajorWarMagicAptitude), Abode());
        Assert.Equal(0, lore.Part("war magic"), 6);
        Assert.Equal(SelfEquipKind.Review, lore.Kind); // worth wearing were its spells on: kept for the next visit, not worn and not sold
        Assert.True(lore.CantripsAsIf > L.WearScore.SwapMargin, lore.Why);

        // out of mana: no spell is activated (Player_Inventory.cs:313-315): no War Magic, and its own Impenetrability III is off too (her pass's line III stands in, so the armour is hers); a stone can refill it, so it is kept for review
        var dry = Pick(GauntletsWith(0x80007006, 37, 0, 1483, 1524, MajorWarMagicAptitude), Abode());
        Assert.Equal(0, dry.Part("war magic"), 6);
        Assert.Equal(0, dry.Part("armour"), 6);
        Assert.Equal(SelfEquipKind.Review, dry.Kind);

        // a lesser cantrip of a family she already wears adds nothing: ACE reads the top layer of a category
        var withMajor = LiveSet();
        withMajor.Add(Worn(0x80007007, "Ring", 0x8, 0x80000, 0, Appraised(0x80007007, new() { [5] = 15, [107] = 800, [108] = 800 }, [], MajorWarMagicAptitude)));
        var minor = Pick(new GearCandidate(0x80007008, "Ring", 0x8, 0x40000 | 0x80000, 0, 15, Appraised(0x80007008, new() { [5] = 15, [107] = 800, [108] = 800 }, [], 2569)), Abode(), withMajor);
        Assert.Equal(0, minor.Part("war magic"), 6);
    }

    [Fact]
    public void Her_live_worn_set_is_the_baseline_each_piece_holding_against_its_twin()
    {
        var set = LiveSet();
        var judge = Judge(Abode());
        var context = judge.Context(new Qwinnie());
        var factor = WearScore.DamageFactor(set, context, Abode().Lines);
        output.WriteLine($"her damage factor on the live set under the Abode gate's lines ({Abode().Lines}): {factor:0.0000}; a Redeemer's {Abode().Hitter!.Hit} lands as {Abode().Hitter!.Hit * factor * ArmorReport.ProtectionAt(5):0.#}");
        Assert.InRange(factor, 0.05, 0.25);
        Assert.True(WearScore.DamageFactor([], context, Abode().Lines) > factor); // bare is worse

        foreach (var piece in set.Where(p => (p.ItemType & SelfEquipJudge.CasterType) == 0))
        {
            var twin = new GearCandidate(0x80008000 + (piece.Guid & 0xFFF), piece.Name, piece.ItemType, piece.Coverage == 0 ? piece.Location : piece.Location, piece.Coverage, piece.Appraisal!.Burden ?? 0, piece.Appraisal);
            var pick = Pick(twin, Abode());
            Assert.Equal(SelfEquipKind.None, pick.Kind); // a twin gains nothing over the piece it would displace
            Assert.Equal(0, pick.Gain, 6);
            output.WriteLine($"  '{piece.Name}' own {pick.DisplacedOwn:0.###}% of the hour");
            if (piece.Appraisal.ArmorLevel is > 0)
            {
                Assert.True(pick.DisplacedOwn > 0, piece.Name);
            }
        }

        // no ring, necklace or bracelet: each of those slots is free
        foreach (var (name, locations) in new[] { ("Ring", 0x40000u | 0x80000u), ("Bracelet", 0x10000u | 0x20000u), ("Necklace", 0x8000u) })
        {
            var piece = new GearCandidate(0x80009000, name, 0x8, locations, 0, 15, Appraised(0x80009000, new() { [5] = 15 }, []));
            Assert.NotNull(SelfEquipJudge.FreeLocation(piece, set, out _));
        }

        Assert.Equal(new LadderWearScore(0.005, 0.002, 0.002, 0.01, 0.43, 0.023, 0.40, 0.018, 0.01, 0.1), L.WearScore);
        Assert.Equal(new ArmourLines(5, 5, 3, 3), Abode().Lines);
    }

    [Fact]
    public void A_piece_the_wear_pick_keeps_takes_the_gear_review_rule_and_is_not_sold()
    {
        var (cls, verdict) = PackRuleBook.Classify(new PackFacts(297, "Ring", 0x8, 5236, WearReview: true));
        Assert.Equal((PackClass.Gear, "gear-review", PackAction.Keep), (cls, verdict.Rule, verdict.Action));
        Assert.Equal("gear-loot", PackRuleBook.Classify(new PackFacts(297, "Ring", 0x8, 5236)).Verdict.Rule);
    }

    [Fact]
    public void The_cantrip_rows_are_the_spell_tables_by_id()
    {
        Assert.Equal(60, CantripRows.BySpell.Count);
        Assert.Equal(("Major War Magic Aptitude", 0x9010u, MageTemplate.WarMagic, 15.0), (CantripRows.Of(2534)!.Value.Name, CantripRows.Of(2534)!.Value.Mod.Type, CantripRows.Of(2534)!.Value.Mod.Key, CantripRows.Of(2534)!.Value.Mod.Value));
        Assert.Equal(("Legendary Willpower", 35.0), (CantripRows.Of(6101)!.Value.Name, CantripRows.Of(6101)!.Value.Mod.Value));
        Assert.Equal(("Minor Mana Gain", 1.15), (CantripRows.Of(2627)!.Value.Name, CantripRows.Of(2627)!.Value.Mod.Value));
        Assert.Null(CantripRows.Of(1483)); // Impenetrability III is an item spell, GearScore.StatOf's
    }
}
