using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e5 (the owner's decision of 2026-11-18): a piece with a useful great cantrip she cannot activate is kept, never sold and never dropped, and it holds no worn slot against a piece whose magic works
/// for her. What these prove. The two questions are two (<see cref="GreatOnBody"/>): "is this a keeper" answers as before for a piece ACE can never light, and "does its great cantrip count for her on her body" answers
/// no. The four cases: (a) such a worn piece is replaced by a piece whose minor cantrip lights, and taken off it is in the sale's keep, out of the make-room rule's sell loot and still a keeper; (b) a piece she can
/// activate, or one lit on her, keeps both guards of E5e2 with their line; (c) a piece short of her Arcane Lore alone is kept worn and wanted as before; (d) nothing puts the replaced piece back for its dead
/// cantrip: the worn restorer, the pick over a piece lying in the review pack, the next visit's pick.
/// </summary>
/// <remarks>
/// The five pieces are the live log's of 2026-11-18 (ladder67) as slice E5e4's tests rebuilt them (<c>GearLightTests</c>): known, the names and guids and the skill ACE named at each wield; assumed, the ItemDifficulty
/// 240 (met by her Arcane Lore 275), the limit's level 220, and her two defences. The Major Focus on them is this slice's, as are the pieces they are judged
/// against. ACE rolls a skill limit on armour and weapons alone (<c>TryMutate_ItemSkillLimit</c>, LootGenerationFactory_Magic.cs:177-203 @95ef426d); the bracelets with one here are stand-ins for the pick's tie-break.
/// </remarks>
public sealed class WearWhatWorksTests
{
    private const uint MajorFocus = 2574, MinorFocus = 2581, MajorEndurance = 2573;
    private const uint ArmourType = 0x2, JewelleryType = 0x8;
    private const uint Chest = 0x200, ChestCoverage = 0x400, Head = 0x1, HeadCoverage = 0x4000, Arms = 0x1000, ArmsCoverage = 0x2000;
    private const uint RightWrist = 0x00010000, LeftWrist = 0x00020000, Bracelet = RightWrist | LeftWrist;
    private const uint MissileDefense = 7, Healing = 21;
    private const string MagePlan = "the mage's skill plan (War Magic, the credit order, the experience's skills)";

    /// <summary>Her numbers as E5e4's tests state them (Arcane Lore 275, Missile Defense 120, Melee Defense 150), with what she holds trained: the defences and Healing are untrained unless a test says otherwise.</summary>
    private sealed class Her(int lore = 275, int missile = 120, int melee = 150, int missileClass = 1, int? unread = null, int heritage = 1) : IGearSkills
    {
        public int Level => 82;

        public int Heritage => heritage;

        public int AdvancementClass(uint skill) => skill switch { MissileDefense => missileClass, MageTemplate.MeleeDefense or Healing or MageTemplate.LifeMagic => 1, _ => 2 };

        public int? Current(uint skill) => skill == unread ? null : skill switch { MageTemplate.ArcaneLore => lore, MissileDefense => missile, MageTemplate.MeleeDefense => melee, _ => 300 };

        public int? Base(uint skill) => Current(skill);
    }

    /// <summary>A piece of the log as E5e4 rebuilt it (mana 600, ItemDifficulty 240, the limit 220 on <paramref name="limitSkill"/>), casting Major Focus.</summary>
    private static Appraisal LogPiece(uint guid, int limitSkill, Dictionary<uint, int>? more = null)
    {
        var ints = new Dictionary<uint, int> { [5] = 400, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = 240 };
        if (limitSkill != 0)
        {
            ints[SelfEquipJudge.ItemSkillLevelLimitInt] = 220;
            ints[SelfEquipJudge.AppraisalItemSkillInt] = limitSkill;
        }

        foreach (var (k, v) in more ?? [])
        {
            ints[k] = v;
        }

        return SelfEquipTests.Appraised(guid, ints, null, MajorFocus);
    }

    [Theory]
    [InlineData(0x8000DEEE, "Tenassa Breastplate", 7, "Missile Defense", 120)]
    [InlineData(0x800087D8, "Diforsa Bracers", 7, "Missile Defense", 120)]
    [InlineData(0x80005AF7, "Studded Leather Greaves", 6, "Melee Defense", 150)]
    [InlineData(0x80005360, "Studded Leather Tassets", 7, "Missile Defense", 120)]
    [InlineData(0x800039C9, "Studded Leather Girth", 6, "Melee Defense", 150)]
    public void A_piece_of_the_live_log_given_a_Major_cantrip_is_a_keeper_whose_cantrip_can_never_light_for_her(uint guid, string name, int limitSkill, string skillName, int hers)
    {
        var piece = LogPiece(guid, limitSkill);
        Assert.StartsWith($"activation: {skillName} {hers} < 220 ", SelfEquipJudge.ActivationUnmet(piece, new Her()), StringComparison.Ordinal); // ACE's own answer at a wield, as E5e4 reads it

        // the second question: a keeper, and no slot held for a cantrip ACE can never light for her
        var onBody = GreatOnBody.Of(keeper: true, lit: false, piece, new Her());
        Assert.True(onBody.Kind == GreatOnBodyKind.Never, name + ": " + onBody.Why);
        Assert.Equal($"its magic asks {skillName} 220 and hers is {hers}: she does not hold {skillName} trained and her plan does not train it ({MagePlan})", onBody.Why);
        Assert.False(onBody.HoldsSlot);

        // lit on her (her registry holds a spell cast by it), it counts whatever ACE would answer now
        Assert.Equal(GreatOnBodyKind.Lit, GreatOnBody.Of(keeper: true, lit: true, piece, new Her()).Kind);

        // read at each ask: the day her skill meets the limit ACE lights it, and it holds its slot again
        var met = GreatOnBody.Of(keeper: true, lit: false, piece, new Her(missile: 220, melee: 220));
        Assert.Equal((GreatOnBodyKind.Lights, true), (met.Kind, met.HoldsSlot));

        // a piece that is no keeper has nothing to count
        Assert.Equal(GreatOnBodyKind.None, GreatOnBody.Of(keeper: false, lit: false, piece, new Her()).Kind);
    }

    [Fact]
    public void Cannot_yet_is_told_from_can_never_by_what_her_build_moves()
    {
        const uint Guid = 0x80001100;

        // her Arcane Lore alone: it rises with her experience (the lore spend's case), so "not yet", and the slot is held as before
        var loreShort = GreatOnBody.Of(true, false, LogPiece(Guid, 0), new Her(lore: 232));
        Assert.Equal((GreatOnBodyKind.NotYet, true), (loreShort.Kind, loreShort.HoldsSlot));
        Assert.StartsWith("not yet: activation: Arcane Lore 232 < 240 ", loreShort.Why, StringComparison.Ordinal);
        Assert.Null(GreatOnBody.NeverActivates(LogPiece(Guid, 0), new Her(lore: 0)));

        // a limit on a skill she holds trained, short of its level: her experience raises it: not yet
        var trained = GreatOnBody.Of(true, false, LogPiece(Guid, 7), new Her(missileClass: 2));
        Assert.Equal(GreatOnBodyKind.NotYet, trained.Kind);
        Assert.StartsWith("not yet: activation: Missile Defense 120 < 220 ", trained.Why, StringComparison.Ordinal);

        // a skill her plan trains, asked trained while she holds it untrained (Life Magic before its credits): not yet
        Assert.Equal(GreatOnBodyKind.NotYet, GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [SelfEquipJudge.UseRequiresSkillInt] = (int)MageTemplate.LifeMagic }), new Her()).Kind);

        // a skill asked trained, or specialised, that she neither holds nor plans: never
        var healing = GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [SelfEquipJudge.UseRequiresSkillInt] = (int)Healing }), new Her());
        Assert.Equal((GreatOnBodyKind.Never, $"its magic asks skill 21 trained: she does not hold skill 21 trained and her plan does not train it ({MagePlan})"), (healing.Kind, healing.Why));
        Assert.Equal(GreatOnBodyKind.Never, GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [SelfEquipJudge.UseRequiresSkillSpecInt] = (int)Healing }), new Her()).Kind);

        // another heritage's piece: never; her own: lights
        var heritage = GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [SelfEquipJudge.HeritageGroupInt] = 3 }), new Her());
        Assert.Equal((GreatOnBodyKind.Never, "its magic is for heritage 3 and hers is 1"), (heritage.Kind, heritage.Why));
        Assert.Equal(GreatOnBodyKind.Lights, GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [SelfEquipJudge.HeritageGroupInt] = 1 }), new Her()).Kind);

        // a limit whose skill this code cannot read is unmet by name and never called "never": nothing is taken off her on a number that was not read
        var unread = GreatOnBody.Of(true, false, LogPiece(Guid, 7), new Her(unread: (int)MissileDefense));
        Assert.Equal(GreatOnBodyKind.NotYet, unread.Kind);
        Assert.Contains("Missile Defense unread < 220", unread.Why, StringComparison.Ordinal);

        // out of mana: the worn items' refill brings it back: not yet
        var dry = GreatOnBody.Of(true, false, LogPiece(Guid, 0, new() { [AppraisalIds.ManaCurrent] = 0 }), new Her());
        Assert.Equal(GreatOnBodyKind.NotYet, dry.Kind);

        // no appraisal held: what keeps its spells off is not read, so the slot is held as before
        Assert.Equal(GreatOnBodyKind.NotYet, GreatOnBody.Of(true, false, null, new Her()).Kind);
    }

    [Fact]
    public void Case_a_a_worn_keeper_ACE_can_never_light_is_replaced_by_a_piece_whose_minor_cantrip_lights_and_taken_off_it_is_still_kept()
    {
        // The state: she wears the Tenassa Breastplate of the log (the limit 220 on Missile Defense, hers 90 and untrained), which casts Major Focus; a leather breastplate of a quarter its armour, casting Minor Focus
        // she can light, lies in her pack.
        var rig = Session();
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());
        var dead = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(7, armour: 400), [MajorFocus]);
        var working = Piece(rig, "Leather Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 100, difficulty: 100), [MinorFocus]);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        Assert.Equal((275, 90, 150), (skills.Current(MageTemplate.ArcaneLore), skills.Current(MissileDefense), skills.Current(MageTemplate.MeleeDefense))); // her lore meets the difficulty 240; her defences are under the limit 220
        Assert.Equal(0u, CantripJudge.SkillClassOf(rig.State.Self.Qualities, MissileDefense)); // not among her skills: she does not hold it trained
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        var worn = SelfEquipJudge.Worn(rig.State);

        // the first question is unchanged: a keeper, whatever ACE would answer a wield
        Assert.True(CantripRoom.IsUsefulGreatPiece(rig.State, dead, spells));
        Assert.True(judge.UsefulGreat!(dead) && !judge.UsefulGreat(working)); // the whole of what the guard read before this slice: the worn piece casts one, the piece in her pack none

        // the second question, which the guard asks now: no
        var onBody = judge.Context(skills).GreatOn(dead, worn.Single(w => w.Guid == dead).Appraisal);
        Assert.Equal(GreatOnBodyKind.Never, onBody.Kind);

        // Before: the guard asked the first question of the worn piece (E5e2: "casts a great cantrip useful to her and this piece casts none: never taken off for it") and the breastplate stayed on with its Major
        // Focus dark. Now: she wears what works.
        var pick = judge.WithWear(terms).Judge(Held(rig, working), worn, skills);
        Assert.True(pick.Kind == SelfEquipKind.Replace, pick.Why);
        Assert.Equal([dead], pick.Displaced);
        Assert.Contains("cantrips first: it raises her core cantrips by ", pick.Why, StringComparison.Ordinal);
        Assert.Contains(
            $"the worn 'Tenassa Breastplate' 0x{dead:X8} casts a great cantrip useful to her that can never light for her (its magic asks Missile Defense 220 and hers is 90: she does not hold Missile Defense trained and her plan does not train it ({MagePlan})): it holds no slot against a piece that works and is judged on what it gives; taken off it is kept (never sold, never dropped)",
            pick.Why,
            StringComparison.Ordinal);

        // The swap as the trip carries it out: the breastplate to the main pack, the leather one on.
        rig.Put(dead);
        rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, working, (int)Chest));
        rig.Tick();
        Assert.Equal((InventoryPlaceKind.MainPack, InventoryPlaceKind.Equipped), (rig.State.Self.Inventory.Find(dead).Kind, rig.State.Self.Inventory.Find(working).Kind));
        var item = rig.State.Objects.TryGet(dead, out var o) ? o : throw new InvalidOperationException("no such piece");

        // still a keeper: the pack sorter's and the layouts' read (the review pack), the villa's store list's
        Assert.True(CantripRoom.IsUsefulGreatPiece(rig.State, dead, spells));

        // never offered to the sale: the rule book keeps it by its appraisal
        var sale = SaleVerdict.Of(dead, item, inSidePack: false, SaleContext.For(rig.State, ground, MageTemplate.ClimberName, spells));
        Assert.Equal((PackAction.Keep, "kept-aside-match"), (sale.Action, sale.Rule));

        // never dropped for room: the make-room rule's sell loot does not hold it, and says why
        var census = new Dictionary<string, int>(StringComparer.Ordinal);
        var book = SaleContext.For(rig.State, ground, MageTemplate.ClimberName, spells);
        var loot = CantripRoom.SellLoot(rig.State, book, census);
        Assert.DoesNotContain(loot, l => l.Guid == dead);
        Assert.Contains("pack rule kept-aside-match (keep)", census.Keys); // M6 16 E5c2: the make-room rule asks the rule book's verdict, and a keep is named by its row

        // and with no keep-aside rule in force the field's own keep holds it (M6 16 E5c2, CantripRoom.FieldKeep): the keeper question, not whether it lights on her body
        var noRule = book with { KeepAside = null };
        Assert.Equal(CantripRoom.KeepGreatCantrip, CantripRoom.FieldKeep(dead, item, noRule));
        Assert.DoesNotContain(CantripRoom.SellLoot(rig.State, noRule), l => l.Guid == dead);
    }

    [Fact]
    public void Case_b_a_worn_great_cantrip_she_can_activate_or_that_is_lit_on_her_keeps_both_guards_of_E5e2_with_their_line()
    {
        const string Line = "casts a great cantrip useful to her and this piece casts none: never taken off for it";
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());

        // she can activate it (no limit on a skill; her lore 275 meets its 240): a plain breastplate of twice the armour never takes it off, in the wear pick and in the armour rule of before the pick
        var rig = Session();
        var usable = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(0, armour: 400), [MajorEndurance]);
        var plain = Piece(rig, "Plain Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 800, difficulty: 0), null);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        Assert.Equal(GreatOnBodyKind.Lights, judge.Context(skills).GreatOn(usable, SelfEquipJudge.Worn(rig.State).Single(w => w.Guid == usable).Appraisal).Kind);
        foreach (var site in new[] { judge.WithWear(terms), judge })
        {
            var choice = site.Judge(Held(rig, plain), SelfEquipJudge.Worn(rig.State), skills);
            Assert.True(choice.Kind == SelfEquipKind.None, choice.Why);
            Assert.Contains($"the worn 'Tenassa Breastplate' 0x{usable:X8} " + Line, choice.Why, StringComparison.Ordinal);
        }

        // ACE would refuse it now, but its spells are on her (lit at an earlier wield): it counts, and the guards hold
        var other = Session();
        var lit = Piece(other, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(7, armour: 400), [MajorEndurance]);
        var plainToo = Piece(other, "Plain Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 800, difficulty: 0), null);
        other.R.Server((p, e) => AceM3Writers.MagicUpdateEnchantment(p, e, new AceEnchantment((ushort)MajorEndurance, 1, (ushort)MajorEndurance, 0, 1, 0, -1, lit, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));
        other.Tick();
        var hers = new QualitiesSkills(other.State.Self.Qualities);
        var session = Wardrobe.For(other.State).JudgeOn(ground, spells);
        Assert.Equal(GreatOnBodyKind.Lit, session.Context(hers).GreatOn(lit, SelfEquipJudge.Worn(other.State).Single(w => w.Guid == lit).Appraisal).Kind);
        foreach (var site in new[] { session.WithWear(terms), session })
        {
            var choice = site.Judge(Held(other, plainToo), SelfEquipJudge.Worn(other.State), hers);
            Assert.True(choice.Kind == SelfEquipKind.None, choice.Why);
            Assert.Contains(Line, choice.Why, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Case_c_a_great_cantrip_short_of_her_Arcane_Lore_alone_is_kept_worn_and_wanted_as_before()
    {
        // The state: she wears a breastplate casting Major Focus whose ItemDifficulty 300 is above her Arcane Lore 275 (no limit on a skill; not lit); a plain one of twice the armour and a leather one casting a
        // Minor Focus she can light lie in her pack, with a bracelet casting Major Focus behind an ItemDifficulty of 290.
        var rig = Session();
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());
        var loreShort = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, Ints(armour: 400, difficulty: 300), [MajorFocus]);
        var plain = Piece(rig, "Plain Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 800, difficulty: 0), null);
        var working = Piece(rig, "Leather Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 100, difficulty: 100), [MinorFocus]);
        var bracelet = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, 0, Ints(armour: 0, difficulty: 290), [MajorFocus]);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells).WithWear(terms);
        var worn = SelfEquipJudge.Worn(rig.State);

        var onBody = judge.Context(skills).GreatOn(loreShort, worn.Single(w => w.Guid == loreShort).Appraisal);
        Assert.Equal((GreatOnBodyKind.NotYet, true), (onBody.Kind, onBody.HoldsSlot));
        Assert.StartsWith("not yet: activation: Arcane Lore 275 < 300 ", onBody.Why, StringComparison.Ordinal);

        // kept worn: neither the plain armour nor the working cantrip takes it off (E5e2's guard, its line unchanged)
        foreach (var candidate in new[] { plain, working })
        {
            var choice = judge.Judge(Held(rig, candidate), worn, skills);
            Assert.True(choice.Kind == SelfEquipKind.None, choice.Why);
            Assert.Contains("casts a great cantrip useful to her and this piece casts none: never taken off for it", choice.Why, StringComparison.Ordinal);
        }

        // wanted: the worn piece is the lore spend's own want (lit by a re-wield once her lore is there), and the bracelet in the pack is kept for the review with the lore it waits on
        Assert.Contains(LoreSpend.WornWants(judge, worn, skills), w => w.Guid == loreShort && w.Difficulty == 300 && w.Worn);
        var review = judge.Judge(Held(rig, bracelet), worn, skills);
        Assert.True(review.Kind == SelfEquipKind.Review && review.LoreNeed == 290, review.Why);
        Assert.Contains(LoreSpend.Wants(judge, [Held(rig, bracelet)], worn, skills), w => w.Guid == bracelet && w.Difficulty == 290);
    }

    [Fact]
    public void Case_d_the_worn_restorer_does_not_put_a_piece_back_on_for_a_cantrip_ACE_can_never_light()
    {
        // The state after the swap: the leather breastplate worn (and remembered: the worn set's memory is the set she wears), the Tenassa Breastplate in her pack; the Diforsa Bracers of the log, casting Major Focus
        // behind the same limit, in her pack with her arms bare.
        var rig = Session();
        var (ground, spells) = (GroundCatalog.Get("tusker-abode"), Spells());
        var working = Piece(rig, "Leather Breastplate", ArmourType, Chest, ChestCoverage, Chest, Ints(armour: 100, difficulty: 100), [MinorFocus]);
        var dead = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, 0, LogInts(7, armour: 400), [MajorFocus]);
        var bracers = Piece(rig, "Diforsa Bracers", ArmourType, Arms, ArmsCoverage, 0, LogInts(7, armour: 300), [MajorFocus]);
        var memory = new WornMemory();
        memory.Snapshot(rig.State);
        Assert.Equal([working], memory.Set.Keys);
        var restorer = new WornRestorer(ground, 82, spells, rig.H.Log, "autopilot", "the start-up audit", memory);

        var view = new J3aView(rig.R);
        var answered = 0;
        for (var i = 0; i < 50 && restorer.Step(view); i++)
        {
            rig.Tick(0.1);
            var wields = rig.H.Net.Of(FakeSendKind.GetAndWieldItem);
            for (; answered < wields.Count; answered++)
            {
                var send = wields[answered];
                rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, send.Guid, (int)send.Amount)); // ACE's side: the wield lands
            }

            rig.Tick(0.1);
        }

        // The breastplate is not put back over the working one (the restore wears into free slots alone). The bracers go on her bare arms for the armour they give, as plain bracers would, and the line says
        // their Major Focus stays dark: nothing is worn for a cantrip ACE can never light.
        Assert.True(restorer.IsDone);
        Assert.Equal([bracers], rig.H.Net.Of(FakeSendKind.GetAndWieldItem).Select(s => s.Guid));
        Assert.Equal((1, InventoryPlaceKind.MainPack, InventoryPlaceKind.Equipped), (restorer.Restored, rig.State.Self.Inventory.Find(dead).Kind, rig.State.Self.Inventory.Find(bracers).Kind));
        Assert.Equal(working, rig.State.Self.Inventory.EquippedAt(Chest));
        Assert.Contains(rig.H.Log.Entries, e => e.Message.StartsWith("gear: restore: wear 'Diforsa Bracers' ", StringComparison.Ordinal) && e.Message.Contains("Major Focus would stay off (activation: Missile Defense 90 < 220 ", StringComparison.Ordinal) && e.Message.Contains("judged on its armour alone", StringComparison.Ordinal));
    }

    [Fact]
    public void Case_d_a_piece_lying_in_the_review_pack_is_not_worn_again_for_its_dead_cantrip_and_stays_kept_there()
    {
        // The state at the next visit: the sorter has moved the keeper to the review pack; the leather breastplate is worn.
        var rig = Session();
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());
        var sack = rig.NextGuid();
        rig.Arrive(sack, "Sack", 166, 0x200, 65);
        rig.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 8_100, sack, VendorRig.Player, 0, 1));
        var working = Piece(rig, "Leather Breastplate", ArmourType, Chest, ChestCoverage, Chest, Ints(armour: 100, difficulty: 100), [MinorFocus]);
        var dead = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, 0, LogInts(7, armour: 400), [MajorFocus]);
        rig.Put(dead, sack);
        rig.Tick();
        Assert.Equal(InventoryPlaceKind.InSidePack, rig.State.Self.Inventory.Find(dead).Kind);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);

        // the gear phase's pick over it: its Major Focus would stay dark, so it gives her armour alone and the Minor Focus she wears is worth more to her: it stays where it is
        var pick = judge.WithWear(terms).Judge(Held(rig, dead), SelfEquipJudge.Worn(rig.State), skills);
        Assert.True(pick.Kind == SelfEquipKind.None, pick.Why);
        Assert.Contains("cantrips first: it would lower her core cantrips by ", pick.Why, StringComparison.Ordinal);

        // the rule of before the pick reads no minor cantrip, so it calls the keeper's armour 400 a replace over the worn 100, as it would plain armour of that level, and says the Major Focus stays dark. No
        // site carries that rule's replace out on a piece already in her packs: the gear phase and the equip pass judge with the pick's terms (above), the worn restorer wears into free slots alone, and the loot
        // step judges a corpse's finds
        var plainRule = judge.Judge(Held(rig, dead), SelfEquipJudge.Worn(rig.State), skills);
        Assert.True(plainRule.Kind == SelfEquipKind.Replace, plainRule.Why);
        Assert.StartsWith("Major Focus would stay off (activation: Missile Defense 90 < 220 ", plainRule.Why, StringComparison.Ordinal);
        Assert.Contains("judged on its armour alone", plainRule.Why, StringComparison.Ordinal);

        // and the sale leaves it in the review pack
        var item = rig.State.Objects.TryGet(dead, out var o) ? o : throw new InvalidOperationException("no such piece");
        var sale = SaleVerdict.Of(dead, item, inSidePack: true, SaleContext.For(rig.State, ground, MageTemplate.ClimberName, spells));
        Assert.Equal((PackAction.Keep, "side-pack-held"), (sale.Action, sale.Rule));
    }

    [Theory]
    [InlineData(100, true)] // the working piece casts a Minor Focus she lights: cantrips first, both ways
    [InlineData(800, false)] // the working piece is plain armour of twice the level: the armour decides, both ways
    public void Case_d_the_next_visits_pick_does_not_put_the_replaced_piece_back(int workingArmour, bool cantrip)
    {
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());

        // visit one: the keeper ACE can never light is worn, the working piece is in her pack: replaced
        var first = Session();
        var dead = Piece(first, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(7, armour: 400), [MajorFocus]);
        var working = Piece(first, "Working Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: workingArmour, difficulty: 100), cantrip ? [MinorFocus] : null);
        var pick = Wardrobe.For(first.State).JudgeOn(ground, spells).WithWear(terms).Judge(Held(first, working), SelfEquipJudge.Worn(first.State), new QualitiesSkills(first.State.Self.Qualities));
        Assert.True(pick.Kind == SelfEquipKind.Replace, pick.Why);
        Assert.Equal([dead], pick.Displaced);

        // visit two, the same two pieces the other way round: the working piece worn, the keeper in her pack: it is judged on what it gives and stays in the pack
        var second = Session();
        _ = Piece(second, "Working Breastplate", ArmourType, Chest, ChestCoverage, Chest, Ints(armour: workingArmour, difficulty: 100), cantrip ? [MinorFocus] : null);
        var kept = Piece(second, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, 0, LogInts(7, armour: 400), [MajorFocus]);
        var back = Wardrobe.For(second.State).JudgeOn(ground, spells).WithWear(terms).Judge(Held(second, kept), SelfEquipJudge.Worn(second.State), new QualitiesSkills(second.State.Self.Qualities));
        if (cantrip)
        {
            Assert.True(back.Kind == SelfEquipKind.None, back.Why);
            Assert.Contains("cantrips first: it would lower her core cantrips by ", back.Why, StringComparison.Ordinal);
        }
        else
        {
            // against plain armour it loses on armour, and the pick keeps it for the review as it keeps any piece whose spells are off and would be worth wearing: the gear-review keep it was in before, never a wear
            Assert.True(back.Kind == SelfEquipKind.Review, back.Why);
            Assert.Contains("its spells would stay off (activation: Missile Defense 90 < 220 ", back.Why, StringComparison.Ordinal);
        }

        Assert.True(CantripRoom.IsUsefulGreatPiece(second.State, kept, spells)); // and it is a keeper all the while
    }

    [Fact]
    public void A_candidate_whose_own_great_cantrip_can_never_light_does_not_take_a_working_great_cantrip_off()
    {
        // The state: she wears a cap casting Major Endurance she can light (useful to her; no term of the pick scores it); a helm of eight times the armour casting Major Focus behind a Missile Defense limit is in
        // her pack. Before: the guard asked the first question of the candidate too (it casts a useful great cantrip, so the guard stood down) and its armour took the working Major Endurance off her head.
        var rig = Session();
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());
        var cap = Piece(rig, "Endurance Cap", ArmourType, Head, HeadCoverage, Head, Ints(armour: 100, difficulty: 100), [MajorEndurance]);
        var helm = Piece(rig, "Tenassa Helm", ArmourType, Head, HeadCoverage, 0, LogInts(7, armour: 800), [MajorFocus]);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        Assert.True(judge.UsefulGreat!(helm) && judge.UsefulGreat(cap)); // the first question says yes of both, which is what the guard read before: it stood down, and the score alone decided
        var scoreAlone = new SelfEquipJudge([], 0, id => spells.TryGetValue(id, out var row) ? row.Name : null, [0x4]).WithWear(terms).Judge(Held(rig, helm), SelfEquipJudge.Worn(rig.State), skills);
        Assert.True(scoreAlone.Kind == SelfEquipKind.Replace, scoreAlone.Why);

        var pick = judge.WithWear(terms).Judge(Held(rig, helm), SelfEquipJudge.Worn(rig.State), skills);

        Assert.True(pick.Kind == SelfEquipKind.None, pick.Why);
        Assert.Contains($"the worn 'Endurance Cap' 0x{cap:X8} casts a great cantrip useful to her and this piece's own great cantrip can never light for her (its magic asks Missile Defense 220 and hers is 90: ", pick.Why, StringComparison.Ordinal);
        Assert.Contains("never taken off for it", pick.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_two_sides_worth_the_same_the_one_whose_great_cantrip_can_never_light_holds_no_side()
    {
        // The state: two bracelets worn, each worth its burden alone to the pick: the right one casts Major Endurance behind a limit she can never meet (a stand-in: ACE rolls none on jewellery), the left one
        // is plain. A bracelet casting a Minor Focus she lights is in her pack. Before: the dead Major Endurance held its side (the first question) and the plain one came off; now neither holds a side, and the
        // lower guid comes off as between any two equal pieces.
        var rig = Session();
        var (ground, spells, terms) = (GroundCatalog.Get("tusker-abode"), Spells(), AbodeTerms());
        var dead = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, RightWrist, LogInts(7, armour: 0), [MajorEndurance]);
        var plain = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, LeftWrist, new() { [5] = 400 }, null);
        var working = Piece(rig, "Bracelet", JewelleryType, Bracelet, 0, 0, Ints(armour: 0, difficulty: 100), [MinorFocus]);
        Assert.True(dead < plain);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        Assert.True(judge.UsefulGreat!(dead));

        var pick = judge.WithWear(terms).Judge(Held(rig, working), SelfEquipJudge.Worn(rig.State), skills);

        Assert.True(pick.Kind == SelfEquipKind.Replace, pick.Why);
        Assert.Equal([dead], pick.Displaced);
        Assert.Contains("that can never light for her", pick.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_armour_rule_of_before_the_pick_judges_such_a_worn_piece_on_the_armour_it_gives_with_its_spells_off()
    {
        // The rule of before the wear pick (the loot step until a trip has published its pick): the state of case (a), with plain breastplates in her pack. Before: the worn piece's Major Focus is on the keep-aside
        // list, and the names kept it ("casts Major Focus: kept") whatever lay in the pack.
        var rig = Session();
        var (ground, spells) = (GroundCatalog.Get("tusker-abode"), Spells());
        var dead = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(7, armour: 400), [MajorFocus]);
        var better = Piece(rig, "Plate Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 800, difficulty: 0), null);
        var weaker = Piece(rig, "Cloth Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 100, difficulty: 0), null);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        var worn = SelfEquipJudge.Worn(rig.State);
        Assert.Equal(["Major Focus"], judge.UsefulCantrips(worn.Single(w => w.Guid == dead).Appraisal));
        var namesAlone = new SelfEquipJudge(KeepAsideRule.SettingFor(ground, MageTemplate.ClimberName)!.SpellNames(), 0, id => spells.TryGetValue(id, out var s) ? s.Name : null);
        Assert.Contains("casts Major Focus: kept", namesAlone.Judge(Held(rig, better), worn, skills).Why, StringComparison.Ordinal);

        // Now: twice the armour replaces it, and the line says why the keeper held no slot
        var replace = judge.Judge(Held(rig, better), worn, skills);
        Assert.True(replace.Kind == SelfEquipKind.Replace, replace.Why);
        Assert.Contains("can never light for her", replace.Why, StringComparison.Ordinal);

        // a quarter of the armour does not: the worn piece is scored on the armour it gives (its level, its spells off), not as nothing
        var stay = judge.Judge(Held(rig, weaker), worn, skills);
        Assert.True(stay.Kind == SelfEquipKind.None, stay.Why);
        Assert.Contains($"not above the worn 'Tenassa Breastplate' 0x{dead:X8} by x1.1", stay.Why, StringComparison.Ordinal);
        Assert.Contains("its spells are off for her", stay.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_worn_piece_whose_spells_cannot_light_is_scored_on_its_armour_and_never_as_nothing()
    {
        // The state of the live defect this closes (TODO(engine-armour-rule-unmet-zero)): the Tenassa Breastplate of the log worn, armour 400, casting a Minor Focus (no keeper) behind a Missile Defense limit of 220
        // (hers 90). Under the rule of before the pick (the loot step's until a trip has published its pick) it scored 0 for its unmet limit, and a breastplate of a quarter its armour replaced it.
        var rig = Session();
        var (ground, spells) = (GroundCatalog.Get("tusker-abode"), Spells());
        var limited = Piece(rig, "Tenassa Breastplate", ArmourType, Chest, ChestCoverage, Chest, LogInts(7, armour: 400), [MinorFocus]);
        var weaker = Piece(rig, "Cloth Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 100, difficulty: 0), null);
        var better = Piece(rig, "Plate Breastplate", ArmourType, Chest, ChestCoverage, 0, Ints(armour: 800, difficulty: 0), null);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        var worn = SelfEquipJudge.Worn(rig.State);

        // the score itself: the armour it gives with its spells off, the requirement named, and no "0:" (that is a wield requirement's)
        var score = GearScore.Of(worn.Single(w => w.Guid == limited), judge.Context(skills));
        Assert.True(score.Total > 0, score.ToString());
        Assert.Null(score.Unmet);
        Assert.StartsWith("activation: Missile Defense 90 < 220 ", score.SpellsOff, StringComparison.Ordinal);
        Assert.Contains("its spells are off for her: activation: Missile Defense 90 < 220 ", score.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("; 0: ", score.ToString(), StringComparison.Ordinal);
        Assert.Equal(GearScore.Of(Held(rig, weaker), judge.Context(skills)).Part("armour") * 4, score.Part("armour"), 6); // four times the armour level on the same parts, and nothing for the Minor Focus

        // armour 100 does not replace it; armour 800 does
        var stay = judge.Judge(Held(rig, weaker), worn, skills);
        Assert.True(stay.Kind == SelfEquipKind.None, stay.Why);
        Assert.Contains($"not above the worn 'Tenassa Breastplate' 0x{limited:X8} by x1.1", stay.Why, StringComparison.Ordinal);
        var replace = judge.Judge(Held(rig, better), worn, skills);
        Assert.True(replace.Kind == SelfEquipKind.Replace, replace.Why);
        Assert.Equal(limited, replace.Replaces);

        // a wield requirement she does not meet still scores 0: she cannot put the piece on (a level of 100 against her 82)
        var unwieldable = Piece(rig, "Plate Hauberk", ArmourType, Chest, ChestCoverage, 0, new(Ints(armour: 800, difficulty: 0)) { [158] = 7, [159] = 1, [160] = 100 }, null);
        var refused = GearScore.Of(Held(rig, unwieldable), judge.Context(skills));
        Assert.Equal(0, refused.Total);
        Assert.StartsWith("wield: level 82 < 100", refused.Unmet, StringComparison.Ordinal);
        Assert.Equal(SelfEquipKind.None, judge.Judge(Held(rig, unwieldable), worn, skills).Kind);

        // lit on her (her registry holds a spell it cast), its spells count as before whatever the limit reads now
        rig.R.Server((p, e) => AceM3Writers.MagicUpdateEnchantment(p, e, new AceEnchantment((ushort)MinorFocus, 1, (ushort)MinorFocus, 0, 1, 0, -1, limited, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 10f, 0)));
        rig.Tick();
        Assert.Null(GearScore.Of(SelfEquipJudge.Worn(rig.State).Single(w => w.Guid == limited), Wardrobe.For(rig.State).JudgeOn(ground, spells).Context(skills)).SpellsOff);
    }

    [Theory]
    [InlineData(MinorFocus)] // a cantrip the keep-aside list does not name: the armour rule's own candidate
    [InlineData(MajorFocus)] // a cantrip the list names: it took the cantrip road before and was kept for the review whatever its armour
    public void A_find_whose_spells_cannot_light_is_a_candidate_on_its_armour_alone_no_better_and_no_worse(uint cantrip)
    {
        // The state: she wears a plain breastplate of armour 400. Three finds behind a Missile Defense limit of 220 (hers 90), each beside a plain piece of its armour: armour 800 over the worn one, armour 100
        // under it, and bracers for her bare arms.
        var rig = Session();
        var (ground, spells) = (GroundCatalog.Get("tusker-abode"), Spells());
        var plainWorn = Piece(rig, "Plain Breastplate", ArmourType, Chest, ChestCoverage, Chest, Ints(armour: 400, difficulty: 0), null);
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var judge = Wardrobe.For(rig.State).JudgeOn(ground, spells);
        var worn = SelfEquipJudge.Worn(rig.State);
        SelfEquipChoice Judged(string name, uint valid, uint coverage, int armour, bool limitedFind) =>
            judge.Judge(Held(rig, Piece(rig, name, ArmourType, valid, coverage, 0, limitedFind ? LogInts(7, armour) : Ints(armour, 0), limitedFind ? [cantrip] : null)), worn, skills);

        // over the worn piece: replaced, as by plain armour of that level. Before: its score was 0 for the unmet limit, so nothing (or, with a named cantrip, the review)
        var (find, plain) = (Judged("Tenassa Breastplate", Chest, ChestCoverage, 800, true), Judged("Plate Breastplate", Chest, ChestCoverage, 800, false));
        Assert.True(find.Kind == SelfEquipKind.Replace && plain.Kind == SelfEquipKind.Replace, find.Why);
        Assert.Equal((plainWorn, plainWorn), (find.Replaces, plain.Replaces));
        Assert.Contains("its spells stay off (activation: Missile Defense 90 < 220 ", find.Why, StringComparison.Ordinal);
        Assert.Contains("its armour alone counts", find.Why, StringComparison.Ordinal);

        // under it: not worn, as plain armour of that level is not; the named cantrip's piece is kept for the review as before
        var (low, plainLow) = (Judged("Tenassa Breastplate", Chest, ChestCoverage, 100, true), Judged("Cloth Breastplate", Chest, ChestCoverage, 100, false));
        Assert.Equal(SelfEquipKind.None, plainLow.Kind);
        Assert.True(low.Kind == (cantrip == MajorFocus ? SelfEquipKind.Review : SelfEquipKind.None), low.Why);

        // a free coverage: worn for its armour, as plain bracers are
        var (bracers, plainBracers) = (Judged("Diforsa Bracers", Arms, ArmsCoverage, 300, true), Judged("Plain Bracers", Arms, ArmsCoverage, 300, false));
        Assert.True(bracers.Kind == SelfEquipKind.Wear && plainBracers.Kind == SelfEquipKind.Wear, bracers.Why);
        if (cantrip == MajorFocus)
        {
            Assert.StartsWith("Major Focus would stay off (activation: Missile Defense 90 < 220 ", bracers.Why, StringComparison.Ordinal);
            Assert.Contains("judged on its armour alone: armour in a free coverage", bracers.Why, StringComparison.Ordinal);
        }
    }

    /// <summary>The wear pick's terms of the Abode rung, as <c>WearPickTests</c> and <c>WardrobeTests</c> state them.</summary>
    private static WearTerms AbodeTerms()
    {
        var ladder = LadderLoader.WarMage;
        return new WearTerms(ladder.WearScore, new CasterTerms(ladder.CasterScore, new CasterBolt("Flame Bolt V", 25, 200), CasterAuras.None), 171, ladder.RungFor(49).Health!.Hitter, ArmourLines.Of(ladder.GateOf("abode"))) { Capacity = 17_250 };
    }

    /// <summary>The cantrips' names: the judges name a spell by the SpellTable's row.</summary>
    private static Dictionary<uint, SpellInfo> Spells() => new()
    {
        [MajorFocus] = WorldActionRig.Spell(MajorFocus, SpellType.Enchantment) with { Name = "Major Focus" },
        [MinorFocus] = WorldActionRig.Spell(MinorFocus, SpellType.Enchantment) with { Name = "Minor Focus" },
        [MajorEndurance] = WorldActionRig.Spell(MajorEndurance, SpellType.Enchantment) with { Name = "Major Endurance" },
    };

    /// <summary>
    /// A session of the climber at level 82: Arcane Lore 275 (Focus 100 / 3 and 242 ranks, trained) as the log read it; War Magic specialised and Mana Conversion trained; Quickness and Coordination 225 each, so her
    /// untrained Missile Defense reads 90 and her Melee Defense 150 (ACE's formulas, a fifth and a third of their sum). The attributes are the test's.
    /// </summary>
    private static VendorRig Session()
    {
        var rig = new VendorRig(1000, new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 82),
            Attributes =
            [
                new AttributeDesc(MageTemplate.Strength, 0, 100, 0), new AttributeDesc(MageTemplate.Endurance, 0, 100, 0), new AttributeDesc(MageTemplate.Quickness, 0, 225, 0),
                new AttributeDesc(MageTemplate.Coordination, 0, 225, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0),
            ],
            Skills =
            [
                new SkillDesc(MageTemplate.WarMagic, 329, 1, 3u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ArcaneLore, 242, 1, 2u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ManaConversion, 253, 1, 2u, 0, 0u, 0, 0),
            ],
        });
        rig.State.Self.Name = MageTemplate.ClimberName;
        return rig;
    }

    /// <summary>A piece's appraisal numbers: its armour level, burden 400, mana 600 of 600 and its ItemDifficulty (0: none).</summary>
    private static Dictionary<uint, int> Ints(int armour, int difficulty)
    {
        var ints = new Dictionary<uint, int> { [5] = 400, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600 };
        if (armour > 0)
        {
            ints[28] = armour;
        }

        if (difficulty > 0)
        {
            ints[AppraisalIds.ArcaneLore] = difficulty;
        }

        return ints;
    }

    /// <summary>A piece of the log as E5e4 rebuilt it: ItemDifficulty 240, the limit 220 on <paramref name="limitSkill"/> (0: no limit), with an armour level.</summary>
    private static Dictionary<uint, int> LogInts(int limitSkill, int armour)
    {
        var ints = Ints(armour, 240);
        if (limitSkill != 0)
        {
            ints[SelfEquipJudge.ItemSkillLevelLimitInt] = 220;
            ints[SelfEquipJudge.AppraisalItemSkillInt] = limitSkill;
        }

        return ints;
    }

    private static uint _event = 11_000;

    /// <summary>A piece she holds, appraised (illustrative, not world data): in the main pack, or worn at <paramref name="wornAt"/>.</summary>
    private static uint Piece(VendorRig rig, string name, uint itemType, uint valid, uint coverage, uint wornAt, Dictionary<uint, int> ints, uint[]? spells)
    {
        var guid = rig.NextGuid();
        rig.Arrive(guid, name, 9_301, itemType, 200, validLocations: valid, priority: coverage == 0 ? null : coverage); // a weenie class no ground's stock, kit or quest names
        rig.Put(guid);
        if (wornAt != 0)
        {
            rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)wornAt));
        }

        rig.Tick();
        rig.R.H.R.Reducer.OnIdentifyResponse(rig.R.H.R.Context, new GameEventHeader(VendorRig.Player, ++_event, 0x00C9), new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | (spells is null ? 0 : IdentifyResponseFlags.SpellBook),
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
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
}
