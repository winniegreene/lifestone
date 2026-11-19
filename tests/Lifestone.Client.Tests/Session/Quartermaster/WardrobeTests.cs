using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slices E5e1 and E5e2: the wear service's one judge (<see cref="Wardrobe.JudgeOn(HuntingGround, IReadOnlyDictionary{uint, SpellInfo}, GearScoreNotes?)"/>) and the gear keeps' one cast
/// chance (<see cref="Wardrobe.KeepCastChance"/>). What these prove. E5e1: on the level up job the town trip and the field keep a caster by one cast chance, the ladder's <c>plan_cast_chance</c>, where before the trip read 0.9
/// and kept a caster the field called sell loot. E5e2, one difference a test: each state in which two sites judged one piece differently, with the answer each gave before (rebuilt here from what that site passed) and the one
/// answer every site gives now: every site asks the one method (the trip, the worn restorer, the fight's own judge and the audit <c>JudgeOn</c>; the keeps and the rearm their named rules beside it).
/// </summary>
public sealed class WardrobeTests
{
    private const uint MajorFocus = 4004;
    private const uint FlameBoltI = 27, FrostBoltII = 70;
    private const uint Fire = 0x10, Cold = 0x8;
    private const uint Ring = 0x00040000 | 0x00080000;
    private const uint JewelleryType = 0x8;

    private sealed class Skills : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => 300;

        public int? Base(uint skill) => 280;
    }

    /// <summary>A ground whose profile has no keep-aside block: the keep-aside in force is then the character's default, or none.</summary>
    private static HuntingGround NoBlock() => GroundCatalog.All.First(g => g.Loot?.KeepAside is null);

    private static GearCandidate FocusRing() => new(0x90000010, "Ring", JewelleryType, Ring, 0, 5, SelfEquipTests.Appraised(0x90000010, null, null, MajorFocus));

    /// <summary>A session of <paramref name="character"/> (Focus and Self 100, War Magic 85, Flame Bolt I and Frost Bolt II in the book).</summary>
    /// <param name="frost">False for a book that holds the Flame Bolt alone (M6 16 E5e3: the judge's element is the fight's own choice, and with Frost Bolt II in her book the fight casts frost on the outskirts: a level above her Flame Bolt I).</param>
    private static VendorRig Session(string character, bool frost = true)
    {
        var rig = new VendorRig(1000, Climber(35, frost: frost));
        rig.State.Self.Name = character;
        return rig;
    }

    /// <summary>The two bolts' rows and the cantrip's name: the judges name a spell by the SpellTable's row.</summary>
    private static Dictionary<uint, SpellInfo> SpellsWithFocus()
    {
        var spells = Spells();
        spells[MajorFocus] = WorldActionRig.Spell(MajorFocus, SpellType.Enchantment) with { Name = "Major Focus" };
        return spells;
    }

    [Fact]
    public void E5e2_the_useful_cantrips_are_the_keep_aside_in_force_for_her_at_every_site_where_the_restorer_and_a_plain_fight_read_the_grounds_block_alone()
    {
        // The state: the climber on a ground whose profile has no keep-aside block, a ring casting Major Focus in her pack, a bare finger.
        var ground = NoBlock();
        var spells = SpellsWithFocus();
        var wardrobe = Wardrobe.For(Session(MageTemplate.ClimberName).State);

        // Before: the town trip read the keep-aside in force (her default list) and wore the ring; the worn restorer and a plain fight's own judge read the ground's block alone (none here) and passed it over.
        var groundBlockAlone = new SelfEquipJudge(ground.Loot?.KeepAside?.SpellNames() ?? [], Fire, id => spells.TryGetValue(id, out var s) ? s.Name : null);
        Assert.Equal(SelfEquipKind.None, groundBlockAlone.Judge(FocusRing(), [], new Skills()).Kind);

        // Now: one answer, the trip's.
        var judge = wardrobe.JudgeOn(ground, spells);
        Assert.Equal(SelfEquipKind.Wear, judge.Judge(FocusRing(), [], new Skills()).Kind);
        Assert.Equal(["Major Focus"], judge.UsefulCantrips(FocusRing().Appraisal));

        Assert.Equal(["Major Focus"], wardrobe.KeepJudges(ground, spells)(Fire).UsefulCantrips(FocusRing().Appraisal));
    }

    [Fact]
    public void E5e2_the_keep_aside_default_is_the_sessions_characters_and_a_grounds_own_block_is_everyones()
    {
        var spells = SpellsWithFocus();
        var other = Wardrobe.For(Session("Somebody Else").State);
        var den = GroundCatalog.Get("black-spawn-den-aluvian"); // its profile has the block

        Assert.Equal(SelfEquipKind.None, other.JudgeOn(NoBlock(), spells).Judge(FocusRing(), [], new Skills()).Kind); // no default for another character
        Assert.Equal(SelfEquipKind.Wear, other.JudgeOn(den, spells).Judge(FocusRing(), [], new Skills()).Kind); // the profile's own block wins, whoever she is
    }

    [Fact]
    public void E5e2_a_rearm_with_empty_hands_breaks_a_tie_by_the_useful_cantrips_as_the_trips_pick_does_where_it_read_none()
    {
        // The state: no caster in the hands, two plain wands in the pack that score the same, the second casting Major Focus; the autopilot's job stated its ground (one without a keep-aside block).
        var rig = Session(MageTemplate.ClimberName);
        var spells = SpellsWithFocus();
        var plain = Caster(rig, "Wand", 0, 1.0);
        var focus = Caster(rig, "Focus Wand", 0, 1.0, spells: [MajorFocus]);
        Assert.True(plain < focus);
        var wardrobe = Wardrobe.For(rig.State);

        // Before: the rearm's judge read no useful cantrip (and no job stated a ground to it): the tie falls to the lower guid, the plain wand.
        Assert.Equal(plain, HeldCaster.Rearm(rig.State, new SelfEquipJudge([], 0, static _ => null), out _)!.Guid);
        Assert.Equal(plain, HeldCaster.Rearm(rig.State, wardrobe.RearmJudge(), out _)!.Guid); // no ground stated: no SpellTable at hand, so no cantrip can be named (the judge of no ground)

        // Now, the job's ground stated: the wand with the useful cantrip, as the trip's own rearm picks.
        wardrobe.StateGround(NoBlock(), spells);
        Assert.Equal(focus, HeldCaster.Rearm(rig.State, wardrobe.RearmJudge(), out var why)!.Guid);
        Assert.Contains("Focus Wand", why, StringComparison.Ordinal);
        Assert.Equal(focus, HeldCaster.Rearm(rig.State, wardrobe.JudgeOn(NoBlock(), spells), out _)!.Guid);
    }

    [Fact]
    public void E5e2_a_worn_piece_with_a_great_cantrip_useful_to_her_is_never_replaced_by_a_piece_that_casts_none_whatever_the_lists_names_say()
    {
        // The state: a plain fight (no trip has published its wear pick), she wears a cap casting Major Strength (a basic attribute: useful to her, and on no keep-aside list), a plain cap of half again the armour in her pack.
        const uint MajorStrength = 2576, Head = 0x1, HeadCoverage = 0x4000, ArmourType = 0x2;
        var rig = Session(MageTemplate.ClimberName);
        var spells = SpellsWithFocus();
        spells[MajorStrength] = WorldActionRig.Spell(MajorStrength, SpellType.Enchantment) with { Name = "Major Strength" };
        var ground = NoBlock();
        Piece(rig, "Strength Cap", ArmourType, Head, HeadCoverage, Head, new() { [28] = 100 }, null, [MajorStrength]);
        var plain = Piece(rig, "Plain Cap", ArmourType, Head, HeadCoverage, 0, new() { [28] = 150 });
        var strong = Piece(rig, "Strength Helm", ArmourType, Head, HeadCoverage, 0, new() { [28] = 150 }, null, [MajorStrength]);
        var worn = SelfEquipJudge.Worn(rig.State);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        Assert.Empty(judge.UsefulCantrips(worn.Single(w => w.Name == "Strength Cap").Appraisal)); // Major Strength is on no list: the names do not protect the cap

        // Before: the armour rule asked the list's names alone, and the plain cap's armour took the Major Strength off her head.
        var namesAlone = new SelfEquipJudge(KeepAsideRule.SettingFor(ground, MageTemplate.ClimberName)!.SpellNames(), Fire, id => spells.TryGetValue(id, out var s) ? s.Name : null);
        Assert.Equal(SelfEquipKind.Replace, namesAlone.Judge(Held(rig, plain), worn, new Skills()).Kind);

        // Now: the owner's rule is asked of the piece itself.
        var choice = judge.Judge(Held(rig, plain), worn, new Skills());
        Assert.Equal(SelfEquipKind.None, choice.Kind);
        Assert.Contains("casts a great cantrip useful to her and this piece casts none: never taken off for it", choice.Why, StringComparison.Ordinal);
        Assert.Equal(SelfEquipKind.Replace, judge.Judge(Held(rig, strong), worn, new Skills()).Kind); // a piece that casts one too is judged by its armour, as before
    }

    [Fact]
    public void E5e2_a_rearm_counts_a_casters_bonus_on_her_rungs_element_as_the_trips_rearm_does_where_it_read_no_element()
    {
        // The state E5e1 named: no caster in the hands, a plain wand and a fire sceptre (x1.1) in the pack, level 6 on the outskirts (the fire rung; her book holds the Flame Bolt alone, so the fight casts fire there).
        var rig = Session(MageTemplate.ClimberName, frost: false);
        var spells = Spells();
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var wand = Caster(rig, "Wand", 0, 1.0);
        var sceptre = Caster(rig, "Fire Sceptre", Fire, 1.1);
        Assert.True(wand < sceptre);
        var wardrobe = Wardrobe.For(rig.State);

        // Before: the trip's rearm counted the bonus and wielded the sceptre; the recovery table's judge read no element, the two scored the same and the lower guid, the wand, went into her hands.
        Assert.Equal(sceptre, HeldCaster.Rearm(rig.State, wardrobe.JudgeOn(ground, spells), out _)!.Guid);
        Assert.Equal(wand, HeldCaster.Rearm(rig.State, new SelfEquipJudge([], 0, static _ => null), out _)!.Guid);

        // Now, the job's ground stated: the rearm reads her rung's element and wields the sceptre.
        wardrobe.StateGround(ground, spells);
        var judge = wardrobe.RearmJudge();
        Assert.Equal(Fire, judge.Element);
        Assert.Equal(sceptre, HeldCaster.Rearm(rig.State, judge, out var why)!.Guid);
        Assert.Contains("Fire Sceptre", why, StringComparison.Ordinal);
    }

    [Fact]
    public void E5e2_the_element_is_the_rungs_first_at_every_site_where_the_fights_own_judge_read_the_plans_family_alone()
    {
        // A state no profile of today holds (the rung's element and the plan's family name one element on every ground and level of the ladder): the outskirts at level 6, the fire rung, with a spell plan of Frost Bolt.
        var ground = GroundCatalog.Get("holtburg-outskirts") with { SpellPlan = [new SpellPlanRow(1, 126, "Frost Bolt", 1)] };
        Assert.Equal(Cold, SelfEquipJudge.ElementOf(ground.PlanFor(6)?.Family)); // before E5e2: what the fight's own judge read

        // E5e2's one rule, the rung's first, stands where the fight's own choice is not known: her book holds no bolt the SpellTable given names.
        var wardrobe = Wardrobe.For(Session(MageTemplate.ClimberName).State);
        var read = wardrobe.JudgeOn(ground, new Dictionary<uint, SpellInfo>(), out var blind);
        Assert.Equal(Fire, read.Element);
        Assert.Contains("the rung's element fire 0x10", blind.ElementSource, StringComparison.Ordinal);
        Assert.Contains("the fight's own choice is not known", blind.ElementSource, StringComparison.Ordinal);

        // M6 16 E5e3: where it is known it comes first. With the Flame Bolt alone in her book the fight casts fire whatever the plan names (the planned line is the first line only when her book can cast it).
        Assert.Equal(Fire, Wardrobe.For(Session(MageTemplate.ClimberName, frost: false).State).JudgeOn(ground, Spells()).Element);

        // And with Frost Bolt II beside Flame Bolt I the fight casts frost on the outskirts (a level above, on creatures that resist neither more): the judge reads cold, where the rung's label said fire.
        wardrobe.JudgeOn(ground, Spells(), out var seeing);
        Assert.Equal(Cold, seeing.Element);
        Assert.StartsWith("the fight's own choice on holtburg-outskirts: cold deals ", seeing.ElementSource, StringComparison.Ordinal);
    }

    [Fact]
    public void E5e2_armour_is_scored_against_the_grounds_damage_types_at_every_site_where_the_fights_own_judge_read_the_mean_of_the_seven_protections()
    {
        // The state: a tusker ground (its creatures deal bludgeon alone), a worn cap of armour level 100 with every protection 1.0, a looted cap of the same level that protects x2.0 against bludgeon and x0.5 against the rest.
        const uint Head = 0x1, HeadCoverage = 0x4000, ArmourType = 0x2;
        var ground = GroundCatalog.Get("tusker-abode");
        Assert.Equal([0x4u], SelfEquipJudge.DamageTypesOf(ground));
        var rig = Session(MageTemplate.ClimberName);
        var spells = Spells();
        Piece(rig, "Flat Cap", ArmourType, Head, HeadCoverage, Head, new() { [28] = 100 }, Protections(1.0, 1.0));
        var looted = Piece(rig, "Bludgeon Cap", ArmourType, Head, HeadCoverage, 0, new() { [28] = 100 }, Protections(2.0, 0.5));
        var worn = SelfEquipJudge.Worn(rig.State);

        // Before: the fight's own judge read no damage types: the mean of the seven protections, (2.0 + 6 x 0.5) / 7 = 0.71, under the worn cap's 1.0: left alone. The trip scored it against bludgeon, x2.0, and replaced.
        var meanOfSeven = new SelfEquipJudge([], 0, static _ => null);
        Assert.Equal(SelfEquipKind.None, meanOfSeven.Judge(Held(rig, looted), worn, new Skills()).Kind);

        // Now: one answer, the trip's.
        var choice = Wardrobe.For(rig.State).JudgeOn(ground, spells).Judge(Held(rig, looted), worn, new Skills());
        Assert.True(choice.Kind == SelfEquipKind.Replace, choice.Why);
        Assert.Contains("the mean vs the ground's 0x4", choice.Why, StringComparison.Ordinal);
    }

    /// <summary>The ArmorModVs floats (PropertyFloat 13 to 19, slash to lightning): <paramref name="bludgeon"/> for 15, <paramref name="rest"/> for the six others.</summary>
    private static Dictionary<uint, double> Protections(double bludgeon, double rest) => new() { [13] = rest, [14] = rest, [15] = bludgeon, [16] = rest, [17] = rest, [18] = rest, [19] = rest };

    [Fact]
    public void E5e2_a_caster_is_scored_on_the_ladders_terms_at_every_site_where_the_fights_own_judge_and_the_rearm_read_the_default_terms()
    {
        // The state: her Mana Conversion trained (33), level 6 on the outskirts (the fire rung, Flame Bolt I the bolt she casts), no caster in the hands; in the pack a fire sceptre (x1.015 on her element) and a wand with a
        // Mana Conversion bonus of +10%.
        var rig = new VendorRig(1000, Climber(35, manaConversion: true, frost: false)); // M6 16 E5e3: the Flame Bolt alone in her book, so the fight's line on the outskirts is her rung's
        rig.State.Self.Name = MageTemplate.ClimberName;
        var spells = Spells();
        spells[FlameBoltI] = spells[FlameBoltI] with { BaseMana = 10 }; // the bolt's base mana as the other tests state Flame Bolt I
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var sceptre = Caster(rig, "Fire Sceptre", Fire, 1.015);
        var wand = Piece(rig, "Mana Wand", SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, 0, null, new() { [SelfEquipJudge.ManaConversionModFloat] = 0.10 });
        var wardrobe = Wardrobe.For(rig.State);

        // Before: on the default terms (no bolt) the wand's bonus counted its own percent of the skill, 0.1 x 10 = 1.0, over the sceptre's 0.5 x 1.5 = 0.75: the fight's own judge and the rearm called the wand the better caster.
        var defaultTerms = new SelfEquipJudge([], Fire, static _ => null, SelfEquipJudge.DamageTypesOf(ground));
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        GearScorePart Bolts(SelfEquipJudge judge) => GearScore.Caster(wand, "Mana Wand", Held(rig, wand).Appraisal!, judge.Context(skills)).Parts.First(p => p.Name == "bolts a mana bar");
        Assert.Equal(wand, HeldCaster.Rearm(rig.State, defaultTerms, out _)!.Guid);
        Assert.Equal(10.0, Bolts(defaultTerms).Value, 6);
        Assert.Contains("TODO(m6-caster-bolt-unread)", Bolts(defaultTerms).Note, StringComparison.Ordinal);

        // The trip's judge ran the bonus through the mana model on the bolt she casts (5.26 mana a cast at Mana Conversion 33, 4.95 at 36: 6.38% more bolts a mana bar, 0.1 x 6.38 = 0.64) and wielded the sceptre.
        // Now every site does.
        wardrobe.StateGround(ground, spells);
        var judge = wardrobe.JudgeOn(ground, spells);
        Assert.Equal(LadderLoader.WarMage.CasterScore, judge.Caster.Weights);
        Assert.NotNull(judge.Caster.Bolt);
        var pick = HeldCaster.Rearm(rig.State, judge, out var why);
        Assert.True(pick!.Guid == sceptre, why + " / " + Bolts(judge));
        Assert.Equal(6.38, Bolts(judge).Value, 2);

        Assert.NotNull(wardrobe.RearmJudge().Caster.Bolt);
        Assert.Equal(sceptre, HeldCaster.Rearm(rig.State, wardrobe.RearmJudge(), out _)!.Guid);
    }

    /// <summary>Her numbers with Arcane Lore 75 (unbuffed after a death) and every other skill 300.</summary>
    private sealed class LowLore : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill == MageTemplate.ArcaneLore ? 75 : 300;

        public int? Base(uint skill) => 280;
    }

    [Fact]
    public void E5e2_every_sites_judge_reads_the_pieces_lit_on_her_so_a_worn_bracelet_lit_before_her_lore_fell_is_not_swapped_for_armour_wherever_the_wear_pick_is_asked()
    {
        // The state of the live loss (2026-11-14, WearPickTests): she wears a bracelet casting Major Mana Conversion Prowess, ItemDifficulty 222, lit on her (an enchantment in her registry cast by it); her Arcane Lore
        // reads 75 now (unbuffed). The other wrist holds a Minor Focus bracelet; a looted bracelet casts Armor Self V alone.
        const uint MajorManaConversion = 2525, ArmorSelfV = 1311, MinorFocus = 2581;
        const uint RightWrist = 0x00010000, LeftWrist = 0x00020000, Bracelet = RightWrist | LeftWrist;
        var rig = Session(MageTemplate.ClimberName);
        var spells = Spells();
        var ground = GroundCatalog.Get("tusker-abode");
        var lit = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, RightWrist, new() { [5] = 60, [107] = 1901, [108] = 1901, [109] = 222 }, null, [ArmorSelfV, MajorManaConversion]);
        Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, LeftWrist, new() { [5] = 60, [107] = 1900, [108] = 1900, [109] = 60 }, null, [MinorFocus]);
        var armour = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, 0, new() { [5] = 60, [28] = 60, [107] = 1800, [108] = 1800, [109] = 60 }, null, [ArmorSelfV]);
        rig.R.Server((p, e) => AceM3Writers.MagicUpdateEnchantment(p, e, new AceEnchantment((ushort)MajorManaConversion, 1, (ushort)MajorManaConversion, 0, 1, 0, -1, lit, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));
        rig.Tick();
        Assert.True(WearScore.LitOn(rig.State.Self.Qualities, lit));
        var terms = AbodeTerms();
        var worn = SelfEquipJudge.Worn(rig.State);

        // Before: the town trip alone read what is lit on her. A judge of another site given the wear pick's terms read the bracelet dark (her lore 75 under its 222), worth nothing, and the armour took its wrist.
        var unread = new SelfEquipJudge([], 0, id => CantripRows.Of(id)?.Name, [0x4]).WithWear(terms);
        Assert.Contains(lit, unread.Judge(Held(rig, armour), worn, new LowLore()).Displaced);

        // Now: every site's judge carries the session's reading, and the lit bracelet is never the side that comes off.
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        Assert.NotNull(judge.LitBy);
        Assert.True(judge.LitBy!(lit));
        Assert.DoesNotContain(lit, judge.WithWear(terms).Judge(Held(rig, armour), worn, new LowLore()).Displaced);

        Assert.True(Wardrobe.For(rig.State).KeepJudges(ground, spells)(Fire).LitBy!(lit));
    }

    [Fact]
    public void E5e2_a_caster_whose_wield_did_not_land_is_passed_over_by_every_judge_of_the_session_where_a_rearm_read_another_memory_than_the_trips()
    {
        // The state: the trip's swap to the fire sceptre did not land (caster-wield-failed) and marked it held back; later her hands are empty with the sceptre and a plain wand in the pack.
        var rig = Session(MageTemplate.ClimberName);
        var spells = Spells();
        var ground = GroundCatalog.Get("holtburg-outskirts");
        var wand = Caster(rig, "Wand", 0, 1.0);
        var sceptre = Caster(rig, "Fire Sceptre", Fire, 1.1);
        var wardrobe = Wardrobe.For(rig.State);
        wardrobe.StateGround(ground, spells);
        const string Failed = "caster-wield-failed: the wield of 'Fire Sceptre' did not land";

        // Before: a plain journey's trip marked its own memory, and the rearm's judge read another: it wielded the refused sceptre again.
        var tripsOwn = new CasterHeldBack();
        tripsOwn.Hold(sceptre, Failed);
        var rearmBefore = new SelfEquipJudge([], Fire, static _ => null, SelfEquipJudge.DamageTypesOf(ground), null, CasterTerms.OfLadder(rig.State, spells, Fire, out _)) { HeldBack = new CasterHeldBack() };
        Assert.Equal(sceptre, HeldCaster.Rearm(rig.State, rearmBefore, out _)!.Guid);

        // Now: the trip's judge marks the session's memory, and the rearm, the pick and the spend's read pass the sceptre over, named.
        wardrobe.JudgeOn(ground, spells).HeldBack.Hold(sceptre, Failed);
        Assert.Equal(wand, HeldCaster.Rearm(rig.State, wardrobe.RearmJudge(), out _)!.Guid);
        Assert.Equal(wand, HeldCaster.Rearm(rig.State, wardrobe.JudgeOn(ground, spells), out _)!.Guid);
        Assert.Null(wardrobe.JudgeOn(ground, spells).BestCaster([Held(rig, wand), Held(rig, sceptre)], SelfEquipJudge.Worn(rig.State), new QualitiesSkills(rig.State.Self.Qualities), out var why));
        Assert.Contains("1 held back for the run ('Fire Sceptre'", why, StringComparison.Ordinal);
        Assert.Same(wardrobe.HeldBack, wardrobe.KeepJudges(ground, spells)(Fire).HeldBack);

        // One memory a session: another session's wardrobe holds nothing back.
        Assert.False(Wardrobe.For(Session(MageTemplate.ClimberName).State).HeldBack.IsHeldBack(sceptre, out _));
    }

    /// <summary>The wear pick's terms of the Abode rung, as <c>WearPickTests</c> states them.</summary>
    private static WearTerms AbodeTerms()
    {
        var ladder = LadderLoader.WarMage;
        return new WearTerms(ladder.WearScore, new CasterTerms(ladder.CasterScore, new CasterBolt("Flame Bolt V", 25, 200), CasterAuras.None), 171, ladder.RungFor(49).Health!.Hitter, ArmourLines.Of(ladder.GateOf("abode"))) { Capacity = 17_250 };
    }

    [Fact]
    public void E5e2_the_wear_pick_never_takes_a_worn_piece_with_a_great_cantrip_useful_to_her_off_for_a_piece_that_casts_none()
    {
        // The state: she wears a cap casting Major Endurance (a basic attribute: useful to her, kept aside by the keep-aside rule; the wear pick's terms score her core cantrips and not this one); a plain cap of twice the
        // armour in her pack. With every site reading the keep-aside in force, the restore wears such a piece on any ground, and the next visit's wear pick judges it.
        const uint MajorEndurance = 2573, Head = 0x1, HeadCoverage = 0x4000, ArmourType = 0x2;
        var rig = Session(MageTemplate.ClimberName);
        var spells = Spells();
        spells[MajorEndurance] = WorldActionRig.Spell(MajorEndurance, SpellType.Enchantment) with { Name = "Major Endurance" };
        var ground = GroundCatalog.Get("tusker-abode");
        var cap = Piece(rig, "Endurance Cap", ArmourType, Head, HeadCoverage, Head, new() { [28] = 100, [107] = 1000, [108] = 1000 }, null, [MajorEndurance]);
        var plain = Piece(rig, "Plain Cap", ArmourType, Head, HeadCoverage, 0, new() { [28] = 200 });
        var worn = SelfEquipJudge.Worn(rig.State);
        var terms = AbodeTerms();
        Assert.True(CantripRoom.IsUsefulGreatPiece(rig.State, cap, spells));

        // Before: the cap's cantrip is worth nothing to the pick's terms, the plain cap's armour is a gain, and the pick took the Major Endurance off her head.
        var before = new SelfEquipJudge([], 0, id => spells.TryGetValue(id, out var row) ? row.Name : null, [0x4]).WithWear(terms).Judge(Held(rig, plain), worn, new Skills());
        Assert.True(before.Kind == SelfEquipKind.Replace, before.Why);

        // Now: the owner's rule is asked of the pieces themselves.
        var now = Wardrobe.For(rig.State).JudgeOn(ground, spells).WithWear(terms).Judge(Held(rig, plain), worn, new Skills());
        Assert.True(now.Kind == SelfEquipKind.None, now.Why);
        Assert.Contains("the worn 'Endurance Cap'", now.Why, StringComparison.Ordinal);
        Assert.Contains("casts a great cantrip useful to her and this piece casts none: never taken off for it", now.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_wardrobe_read_is_given_for_the_sites_lines_and_the_judge_carries_the_sessions_readings()
    {
        var rig = Session(MageTemplate.ClimberName, frost: false);
        var ground = GroundCatalog.Get("holtburg-outskirts");

        var trip = Wardrobe.For(rig.State).JudgeOn(ground, Spells(), out var read);

        Assert.Equal(MageTemplate.ClimberName, read.Character);
        Assert.Equal((Fire, Fire), (read.Element, trip.Element)); // the Flame Bolt alone in her book: the fight casts fire on the outskirts
        Assert.StartsWith("the fight's own choice on holtburg-outskirts: fire deals 100% of her damage there", read.ElementSource, StringComparison.Ordinal); // M6 16 E5e3
        Assert.EndsWith(" 0x10", read.ElementSource, StringComparison.Ordinal);
        Assert.Same(read.Caster, trip.Caster);
        Assert.Equal(LadderLoader.WarMage.CasterScore, trip.Caster.Weights); // the ladder's
        Assert.NotNull(trip.LitBy);
        Assert.Same(Wardrobe.For(rig.State).HeldBack, trip.HeldBack);
    }

    [Fact]
    public void The_gear_keeps_judges_carry_the_element_asked_and_the_ladders_caster_terms()
    {
        var rig = Session(MageTemplate.ClimberName);
        var judges = Wardrobe.For(rig.State).KeepJudges(NoBlock(), SpellsWithFocus());

        var cold = judges(Cold);
        var fire = judges(Fire);

        Assert.Equal((Cold, Fire), (cold.Element, fire.Element)); // the keep's own question: the element is asked
        Assert.Equal(LadderLoader.WarMage.CasterScore, cold.Caster.Weights); // the ladder's weights, as the trip's judge
        Assert.Equal(["Major Focus"], cold.UsefulCantrips(FocusRing().Appraisal)); // the keep-aside in force: the climber's default on a ground without a block
        Assert.Same(Wardrobe.For(rig.State).HeldBack, cold.HeldBack);
    }

    [Fact]
    public void Unstated_the_trip_reads_the_dens_chance_and_the_field_its_own_fights_and_a_stated_chance_is_read_by_both()
    {
        var wardrobe = Wardrobe.For(new VendorRig().State);

        Assert.Equal(BuffOptions.DefaultCastChance, wardrobe.KeepCastChance); // the den job states nothing: the trip's 0.9, as before
        Assert.Equal(0.8, wardrobe.KeepCastChanceIn(0.8)); // and the field its own fight's, as before

        wardrobe.StateKeepCastChance(LadderLoader.WarMage.PlanCastChance, "the test's level up job");

        Assert.Equal(LadderLoader.WarMage.PlanCastChance, wardrobe.KeepCastChance);
        Assert.Equal(LadderLoader.WarMage.PlanCastChance, wardrobe.KeepCastChanceIn(BuffOptions.DefaultCastChance));
        Assert.Equal("the test's level up job", wardrobe.KeepCastChanceSource);
        Assert.Throws<ArgumentOutOfRangeException>(() => wardrobe.StateKeepCastChance(0, "none"));
        Assert.Throws<ArgumentOutOfRangeException>(() => wardrobe.StateKeepCastChance(1.5, "none"));
    }

    [Fact]
    public void E5e2_the_jobs_statement_of_the_keeps_cast_chance_is_the_chance_its_fight_is_built_with_the_command_lines_when_given()
    {
        var ladder = LadderLoader.WarMage.PlanCastChance;
        var ladderSpec = AutopilotScenario.FightSpec with { Args = new Dictionary<string, string> { [JourneyStations.CastChanceArgument] = ladder.ToString(System.Globalization.CultureInfo.InvariantCulture) } };

        // What the fight is built with (the den-fight station's rule) against what the job states to the wardrobe, for each job with and without the option.
        foreach (var (job, given) in new[] { ("level-up", (string?)null), ("level-up", "0.8"), ("den", null), ("den", "0.8"), ("level-up", "0.9") })
        {
            string[] line = given is null ? ["--autopilot-job=" + job] : ["--autopilot-job=" + job, "--buff-cast-chance=" + given];
            var arguments = CommandLineArgument.ParseAll(line);
            var options = AutopilotOptions.FromArguments(arguments);
            Assert.Equal(job == "level-up" ? AutopilotJob.LevelUp : AutopilotJob.Den, options.Job);
            var fight = JourneyStations.DenFightBuff(job == "level-up" ? ladderSpec : AutopilotScenario.FightSpec, BuffOptions.FromArguments(arguments), arguments).CastChance;
            var wardrobe = Wardrobe.For(new VendorRig().State);
            if (AutopilotScenario.FightCastChance(options) is var (chance, source))
            {
                wardrobe.StateKeepCastChance(chance, source);
            }

            Assert.Equal(fight, wardrobe.KeepCastChance); // the trip's and the sale's
            Assert.Equal(fight, wardrobe.KeepCastChanceIn(fight)); // the field's
        }

        // Before: the level up job stated the ladder's chance whatever the command line said: with --buff-cast-chance=0.8 the fight cast at 0.8 and the keeps read 0.95.
        Assert.Equal(0.8, AutopilotScenario.FightCastChance(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", "--buff-cast-chance=0.8"])))!.Value.Chance);
        Assert.Equal(ladder, AutopilotScenario.FightCastChance(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up"])))!.Value.Chance);
        Assert.Null(AutopilotScenario.FightCastChance(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=den"])))); // the den job says nothing: 0.9 unstated, as before
    }

    [Fact]
    public void Q10c_the_sites_that_judged_armour_by_the_rule_of_before_the_pick_read_the_trips_terms_and_a_cantrip_piece_that_comes_off_names_its_reason()
    {
        // The state: a plain fight (no trip has published its wear pick) on a ground with the climber's default list (Major Focus is on it). She wears a cap of armour level 100 casting Major Focus; in her pack, a helm of
        // armour level 1000 casting Major Focus too (M6 redoubt: 400 cleared the swap margin against the rung's Mite Scion, hit 7; against the Redoubt's Drudge Prowler, hit 4, it reads 0.082 of the margin's 0.1).
        var rig = Session(MageTemplate.ClimberName);
        var spells = SpellsWithFocus();
        var ground = NoBlock();
        Piece(rig, "Focus Cap", 0x2, 0x1, 0x4000, 0x1, new() { [28] = 100 }, null, [MajorFocus]);
        var helm = Piece(rig, "Focus Helm", 0x2, 0x1, 0x4000, 0, new() { [28] = 1000 }, null, [MajorFocus]);
        var worn = SelfEquipJudge.Worn(rig.State);
        var wardrobe = Wardrobe.For(rig.State);

        // Before: the pick's terms were the trip's alone; the restorer and the corpse plan's verdict read the rule of before them, which never replaces a worn piece that casts a useful cantrip.
        var before = wardrobe.JudgeOn(ground, spells).Judge(Held(rig, helm), worn, new Skills());
        Assert.Equal(SelfEquipKind.Review, before.Kind);

        // Now: the terms of her now, and the verdict names its reason with its numbers.
        var judge = wardrobe.JudgeWithTerms(ground, spells, out _);
        Assert.NotNull(judge.Wear);
        var after = judge.Judge(Held(rig, helm), worn, new Skills());
        Assert.True(after.Kind == SelfEquipKind.Replace, after.Why);
        Assert.Contains("beats what it displaces by more than the swap margin", after.Why, StringComparison.Ordinal);
        Assert.Contains("'Focus Cap' 0x", after.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Q10c_a_free_slot_piece_the_rule_of_before_passed_over_is_worn_by_the_terms_at_the_restore_and_the_corpse_plan()
    {
        // The state: a bare finger and a ring casting Minor Focus in her pack, a plain fight, the climber's default list (it names Major Focus alone).
        const uint MinorFocus = 2581;
        var rig = Session(MageTemplate.ClimberName);
        var spells = SpellsWithFocus();
        spells[MinorFocus] = WorldActionRig.Spell(MinorFocus, SpellType.Enchantment) with { Name = "Minor Focus" };
        var ring = Piece(rig, "Minor Focus Ring", JewelleryType, Ring, 0, 0, new() { [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600 }, null, [MinorFocus]);
        var wardrobe = Wardrobe.For(rig.State);

        var before = wardrobe.JudgeOn(NoBlock(), spells).Judge(Held(rig, ring), [], new Skills());
        var after = wardrobe.JudgeWithTerms(NoBlock(), spells, out _).Judge(Held(rig, ring), [], new Skills());

        Assert.Equal(SelfEquipKind.None, before.Kind);
        Assert.True(after.Kind == SelfEquipKind.Wear, after.Why);
        Assert.Contains("cantrips first", after.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void One_wardrobe_a_session()
    {
        var a = new VendorRig().State;
        var b = new VendorRig().State;

        Assert.Same(Wardrobe.For(a), Wardrobe.For(a));
        Assert.NotSame(Wardrobe.For(a), Wardrobe.For(b));
    }

    /// <summary>Focus and Self 100, War Magic trained with <paramref name="warRanks"/> ranks (current skill (100 + 100) / 4 + ranks), the book holding Flame Bolt I and Frost Bolt II.</summary>
    private static PlayerDescriptionMsg Climber(int warRanks, bool manaConversion = false, bool frost = true) => new()
    {
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 6),
        Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
        Skills = manaConversion
            ? [new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ManaConversion, 0, 1, 2u, 0, 0u, 0, 0)] // trained: (Focus + Self) / 6 = 33
            : [new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 2u, 0, 0u, 0, 0)],
        Spells = frost ? ImmutableDictionary<uint, float>.Empty.Add(FlameBoltI, 2f).Add(FrostBoltII, 2f) : ImmutableDictionary<uint, float>.Empty.Add(FlameBoltI, 2f),
    };

    /// <summary>The two bolts' rows: the powers are the SpellTable's for a level I and a level II war spell (1 and 50).</summary>
    private static Dictionary<uint, SpellInfo> Spells() => new()
    {
        [FlameBoltI] = WorldActionRig.Spell(FlameBoltI, SpellType.Projectile) with { Name = "Flame Bolt I", Power = 1 },
        [FrostBoltII] = WorldActionRig.Spell(FrostBoltII, SpellType.Projectile) with { Name = "Frost Bolt II", Power = 50 },
    };

    private static uint _event = 7000;

    private static uint Caster(VendorRig rig, string name, uint element, double bonus, bool wielded = false, uint[]? spells = null) =>
        Piece(rig, name, SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, wielded ? SelfEquipJudge.Held : 0, element == 0 ? null : new() { [SelfEquipJudge.DamageTypeInt] = (int)element }, new() { [AppraisalIds.ElementalDamageMod] = bonus }, spells);

    /// <summary>A piece she holds, appraised (illustrative, not world data): in the main pack, or worn at <paramref name="wornAt"/>.</summary>
    private static uint Piece(VendorRig rig, string name, uint itemType, uint valid, uint coverage, uint wornAt, Dictionary<uint, int>? ints = null, Dictionary<uint, double>? floats = null, uint[]? spells = null)
    {
        var guid = rig.NextGuid();
        rig.Arrive(guid, name, 2472, itemType, 200, validLocations: valid, priority: coverage == 0 ? null : coverage);
        rig.Put(guid);
        if (wornAt != 0)
        {
            rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)wornAt));
        }

        rig.Tick();
        rig.R.H.R.Reducer.OnIdentifyResponse(rig.R.H.R.Context, new GameEventHeader(VendorRig.Player, ++_event, 0x00C9), new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | (spells is null ? 0 : IdentifyResponseFlags.SpellBook),
            true,
            (ints ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            (floats ?? []).ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells ?? []],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
        return guid;
    }

    private static GearCandidate Held(VendorRig rig, uint guid) => SelfEquipJudge.CandidateOf(rig.State, rig.State.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException("no such piece"));

    [Fact]
    public void On_the_level_up_job_the_trip_and_the_field_keep_a_caster_by_one_cast_chance_where_the_trip_kept_one_the_field_sold()
    {
        // War Magic 85 and Frost Bolt II (power 50) the only frost level she knows: its chance sits between the den's bar and the ladder's.
        var rig = new VendorRig(1000, Climber(35));
        var state = rig.State;
        var spells = Spells();
        var ladder = LadderLoader.WarMage.PlanCastChance;
        Assert.True(GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var war, out var skillWhy), skillWhy);
        var frost = SpellPlan.Chance(war, spells[FrostBoltII].Power);
        Assert.InRange(frost, BuffOptions.DefaultCastChance, ladder - 0.001);
        Assert.True(SpellPlan.Chance(war, spells[FlameBoltI].Power) >= ladder);

        // The plain wand in the hand, the plan's fire caster (the floor's second) and a frost caster for a line the fight put in hand this session.
        var ground = GroundCatalog.Get("holtburg-outskirts");
        Assert.Equal(Fire, WornRestorer.ElementFor(ground, state.Self.Qualities.Level, out var elementWhy));
        Caster(rig, "Wand", 0, 1.0, wielded: true);
        var fire = Caster(rig, "Fire Sceptre", Fire, 1.1);
        var cold = Caster(rig, "Frost Sceptre", Cold, 1.1);
        ElementsUsed.For(state).Note("cold");

        // Before this slice: the trip read 0.9 and kept the frost caster, the level up job's field read the ladder's chance and called it sell loot (the make-room rule may drop sell loot).
        var tripBefore = CantripRoom.GearKeeps(state, ground, spells, BuffOptions.DefaultCastChance);
        var fieldBefore = CantripRoom.GearKeeps(state, ground, spells, ladder);
        Assert.StartsWith("the best caster for the cold line the fight uses", tripBefore.Casters(cold), StringComparison.Ordinal);
        Assert.Null(fieldBefore.Casters(cold));
        Assert.NotNull(fieldBefore.Casters(fire)); // the plan's caster is kept at either chance

        // Now: the level up job states the ladder's chance once, and the trip, the sale's context and the field read that one.
        var wardrobe = Wardrobe.For(state);
        wardrobe.StateKeepCastChance(ladder, "the level up job: the ladder's plan_cast_chance");
        var trip = CantripRoom.GearKeeps(state, ground, spells, wardrobe.KeepCastChance);
        var field = CantripRoom.GearKeeps(state, ground, spells, wardrobe.KeepCastChanceIn(ladder));
        var sale = SaleContext.For(state, ground, MageTemplate.ClimberName, spells);

        Assert.Null(trip.Casters(cold));
        Assert.Null(field.Casters(cold));
        Assert.Null(sale.CasterNeed(cold));
        Assert.NotNull(trip.Casters(fire));
        Assert.NotNull(sale.CasterNeed(fire));
        Assert.Equal(trip.Verdicts.Select(v => (v.Guid, v.KeptFor)), field.Verdicts.Select(v => (v.Guid, v.KeptFor)));
    }

    [Fact]
    public void On_the_den_job_nothing_is_stated_and_the_sales_context_keeps_the_caster_the_trip_kept_before()
    {
        var rig = new VendorRig(1000, Climber(35));
        var state = rig.State;
        var ground = GroundCatalog.Get("holtburg-outskirts");
        Caster(rig, "Wand", 0, 1.0, wielded: true);
        Caster(rig, "Fire Sceptre", Fire, 1.1);
        var cold = Caster(rig, "Frost Sceptre", Cold, 1.1);
        ElementsUsed.For(state).Note("cold");

        var sale = SaleContext.For(state, ground, MageTemplate.ClimberName, Spells());

        Assert.StartsWith("the best caster for the cold line the fight uses", sale.CasterNeed(cold), StringComparison.Ordinal); // 0.9, as before the slice
    }
}
