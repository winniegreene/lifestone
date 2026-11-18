using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 2 d: the return of an attuned piece (<see cref="GearReturn"/>) in the climber's setting: Winnie (Strength 10) with the Pathwarden robe on. The table is the real <c>quests/pathwarden.json</c> <c>returns</c> block, whose rows are the pinned world SQL
/// v0.9.296's Give emotes of Pathwarden Thorolf 33596 (weenie_properties_emote 53553-53558), read offline from the zip and cited in the file; a piece marked "illustrative" is not world data.
/// </summary>
public sealed class GearReturnTests
{
    private const uint Me = 0x50000001;
    private const uint Hands = 0x8000, Head = 0x4000, Feet = 0x10000, RobeCover = 0x17F00, HauberkCover = 0x1C00;
    private const uint HandWear = 0x20, HeadWear = 0x1, FootWear = 0x100;

    private static readonly GearReturnTable Thorolf = GearReturnTable.All.Single(t => t.Quest == "pathwarden");

    private sealed class Climber : IGearSkills
    {
        public int Level => 5;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => skill == MageTemplate.WarMagic ? 60 : null;

        public int? Base(uint skill) => skill == MageTemplate.WarMagic ? 50 : null;

        public int? Attribute(uint attribute) => attribute == 1 ? 10 : null;

        public int? VitalMax(uint vital) => null;
    }

    private static GearContext Context() => GearContext.For(new Climber(), 0x10, null, [], _ => null) with { Notes = new GearScoreNotes() };

    private static Appraisal Appraised(uint guid, int armourLevel, bool attuned, int burden = 100, params (uint Id, int Value)[] more) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable,
            true,
            new Dictionary<uint, int>([.. new Dictionary<uint, int> { [5] = burden, [19] = 0, [28] = armourLevel, [114] = attuned ? 1 : 0 }, .. more.Select(m => KeyValuePair.Create(m.Id, m.Value))]).ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [],
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

    /// <summary>A pack piece: its weenie class, name, wear locations and coverage (the world's: 40439 robe 0x7F01/0x17F00, 33605 sollerets 0x100/0x10000, 33606 gauntlets 0x20/0x8000; the armour levels are illustrative).</summary>
    private static GearReturnHeld Pack(uint guid, uint wcid, string name, uint locations, uint coverage, int armourLevel, bool attuned = true, bool appraised = true) =>
        new(new GearCandidate(guid, name, 0x2, locations, coverage, 100, appraised ? Appraised(guid, armourLevel, attuned) : null), wcid);

    private static WornPiece Worn(uint guid, string name, uint location, uint coverage, int armourLevel, bool attuned = false) =>
        new(guid, name, 0x2, location, coverage, Appraised(guid, armourLevel, attuned));

    /// <summary>A worn piece with more of its appraisal's numbers: an ItemDifficulty (109) above the climber's Arcane Lore (unread here, so 0) leaves its spells off for her; a level wield requirement (158 type 7, 160) above her 5 is one she cannot wield.</summary>
    private static WornPiece WornWith(uint guid, string name, uint location, uint coverage, int armourLevel, params (uint Id, int Value)[] more) =>
        new(guid, name, 0x2, location, coverage, Appraised(guid, armourLevel, attuned: false, 100, more));

    private static readonly (uint Id, int Value) Difficulty = (AppraisalIds.ArcaneLore, 100);

    private static WornPiece WornRobe(int armourLevel = 50) => Worn(0x90000010, "Pathwarden Robe", 0x7F01, RobeCover, armourLevel, attuned: true);

    private static GearReturnHeld Sollerets(bool attuned = true) => Pack(0x90000011, 33605, "Pathwarden Sollerets", FootWear, Feet, 100, attuned);

    private static GearReturnHeld Gauntlets() => Pack(0x90000012, 33606, "Pathwarden Gauntlets", HandWear, Hands, 100);

    [Fact]
    public void The_quest_files_returns_table_is_Thorolfs_six_Give_emotes_from_the_pinned_world_sql_and_the_trinket_has_none()
    {
        Assert.Equal(33596u, Thorolf.GiverWcid);
        Assert.Equal("Pathwarden Thorolf", Thorolf.GiverName);
        Assert.Equal(6, Thorolf.Rows.Count);

        // weenie_properties_emote 53553-53558 (object 33596, category 6) and the Give action of each (weenie_Class_Id, stack_Size 1)
        (uint Item, uint Reward, uint Emote)[] expected = [(33607, 33620, 53553), (33606, 33621, 53554), (33605, 33621, 53555), (40439, 6876, 53556), (33601, 6876, 53557), (33597, 6876, 53558)];
        foreach (var (item, reward, emote) in expected)
        {
            var row = Thorolf.RowFor(item);
            Assert.NotNull(row);
            Assert.Equal(reward, row.RewardWcid);
            Assert.Equal(emote, row.Emote);
            Assert.Contains("weenie_properties_emote " + emote, row.Source, StringComparison.Ordinal);
        }

        Assert.Null(Thorolf.RowFor(41513)); // the Pathwarden Trinket: no Give row in the SELECT, kept
        Assert.Equal(["Pathwarden Helm", "Pathwarden Gauntlets", "Pathwarden Sollerets", "Pathwarden Robe", "Pathwarden Plate Leggings", "Pathwarden Plate Hauberk"], Thorolf.Rows.Select(r => r.ItemName));
        Assert.Equal(0xA9B40022u, Thorolf.Place!.Cell); // the quest's go step: he stands in Holtburg
    }

    [Fact]
    public void The_quest_catalog_still_loads_the_file_with_the_returns_block_and_the_pieces_carry_their_sql_names()
    {
        var quest = QuestCatalog.Get("pathwarden");
        Assert.Equal(7, quest.Steps.Count);
        Assert.Equal("Pathwarden Robe", quest.Steps.Single(s => s.Item?.Wcid == 40439).Item!.Name); // m6-pathwarden-item-names: PropertyString 1
        Assert.Equal("Pathwarden Sollerets", quest.Steps.Single(s => s.Item?.Wcid == 33605).Item!.Name);

        // a field the loader does not know is still refused (the returns block is let through by name, nothing else is)
        var json = QuestCatalog.ResourceText("pathwarden.json")!.Replace("\"timer_seconds\": null,", "\"timer_seconds\": null, \"surprise\": 1,", StringComparison.Ordinal);
        Assert.Throws<QuestFileException>(() => QuestCatalog.Parse("pathwarden.json", json));
    }

    [Fact]
    public void A_file_without_the_block_has_no_table_and_a_malformed_block_fails_by_name()
    {
        Assert.Null(GearReturnTable.Parse("{ \"key\": \"x\" }"));
        var broken = Assert.Throws<FormatException>(() => GearReturnTable.Parse("{ \"key\": \"x\", \"returns\": { \"giver\": { \"wcid\": 1, \"name\": \"N\" }, \"rows\": [ { \"item\": { \"wcid\": 2, \"name\": \"I\" } } ] } }"));
        Assert.Contains("$.returns.rows[0].reward", broken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outgrown_piece_is_returned_when_a_worn_piece_in_its_slot_scores_higher()
    {
        var worn = new[] { WornRobe(), Worn(0x90000020, "Better Gauntlets (illustrative)", 0x20, Hands, 300) };
        var plan = GearReturn.Plan(Thorolf, [Gauntlets()], worn, Context());

        var item = Assert.Single(plan.Returns);
        Assert.Equal(33606u, item.Wcid);
        Assert.Equal(33621u, item.Row.RewardWcid); // a bag of Steel (emote 53554)
        Assert.StartsWith("outgrown: the worn 'Better Gauntlets (illustrative)'", item.Why, StringComparison.Ordinal);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void A_piece_the_coverage_rule_keeps_off_is_returned_the_sollerets_under_the_Pathwarden_robe()
    {
        var plan = GearReturn.Plan(Thorolf, [Sollerets()], [WornRobe(0)], Context()); // the robe's 0x17F00 holds the feet; the sollerets score above a robe with no armour, so it is blocked, not outgrown

        var item = Assert.Single(plan.Returns);
        Assert.Equal(33605u, item.Wcid);
        Assert.StartsWith("blocked: the worn 'Pathwarden Robe'", item.Why, StringComparison.Ordinal);
        Assert.Contains("You must remove your Pathwarden Robe to wield Pathwarden Sollerets", item.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_with_a_free_slot_is_kept_for_the_equip_pass_not_returned()
    {
        var plan = GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe()], Context()); // the hands are free: 0x17F00 holds no 0x8000

        Assert.Empty(plan.Returns);
        var kept = Assert.Single(plan.Kept);
        Assert.Equal("return: kept Pathwarden Gauntlets (it has a free slot: the equip pass wears it, or says why not)", kept.ToString());
    }

    [Fact]
    public void An_attuned_piece_with_no_row_is_kept_and_named_the_trinket()
    {
        // illustrative: the real trinket 41513 is not attuned in the SQL; an attuned one would still have no Give row
        var trinket = new GearReturnHeld(new GearCandidate(0x90000030, "Pathwarden Trinket", 0x8, 0x4000000, 0, 60, Appraised(0x90000030, 0, attuned: true, burden: 60)), 41513);
        var plan = GearReturn.Plan(Thorolf, [trinket], [WornRobe()], Context());

        Assert.Empty(plan.Returns);
        Assert.Equal("return: kept Pathwarden Trinket (no Give row of Pathwarden Thorolf takes it)", Assert.Single(plan.Kept).ToString());
    }

    [Fact]
    public void The_robe_is_kept_while_it_is_the_only_chest_cover_and_returned_once_another_piece_covers_the_chest()
    {
        var robe = Pack(0x90000040, 40439, "Pathwarden Robe", 0x7F01, RobeCover, 50);

        var bare = GearReturn.Plan(Thorolf, [robe], [], Context()); // nothing worn covers the chest
        Assert.Empty(bare.Returns);
        Assert.Equal("return: kept Pathwarden Robe (it covers the chest and nothing worn does: it is the only chest cover)", Assert.Single(bare.Kept).ToString());

        var better = Worn(0x90000041, "Better Robe (illustrative)", 0x7F01, RobeCover, 400);
        var covered = GearReturn.Plan(Thorolf, [robe], [better], Context());
        var item = Assert.Single(covered.Returns);
        Assert.Equal(6876u, item.Row.RewardWcid); // the Sturdy Iron Key (emote 53556)
        Assert.StartsWith("outgrown", item.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_that_is_not_attuned_a_caster_and_an_unappraised_piece_are_handled_apart()
    {
        // not attuned: an ordinary piece is the sale's and the keep-aside's, the plan says nothing
        var plain = GearReturn.Plan(Thorolf, [Sollerets(attuned: false)], [WornRobe(0)], Context());
        Assert.Empty(plain.Returns);
        Assert.Empty(plain.Kept);

        // a caster is never given back: the floor keeps two
        var wand = new GearReturnHeld(new GearCandidate(0x90000050, "Wand", 0x8000, SelfEquipJudge.Held, 0, 50, Appraised(0x90000050, 0, attuned: true)), 2472);
        var casters = GearReturn.Plan(Thorolf, [wand], [], Context());
        Assert.Empty(casters.Returns);
        Assert.Empty(casters.Kept);

        // a piece with a row that was never appraised: whether it is attuned is unread, so it is kept, named
        var unread = GearReturn.Plan(Thorolf, [Pack(0x90000051, 33605, "Pathwarden Sollerets", FootWear, Feet, 100, appraised: false)], [WornRobe(0)], Context());
        Assert.Empty(unread.Returns);
        Assert.Equal("return: kept Pathwarden Sollerets (not appraised: whether it is attuned is unread)", Assert.Single(unread.Kept).ToString());
    }

    [Fact]
    public void An_npc_out_of_reach_keeps_every_piece_and_names_him()
    {
        var plan = GearReturn.Plan(Thorolf, [Sollerets()], [WornRobe(0)], Context());
        Assert.Single(plan.Returns);

        var kept = plan.Unreachable("Pathwarden Thorolf", "he stands in landblock 0xA9B4, not where the trip is");

        Assert.Empty(kept.Returns);
        Assert.Equal("return: kept Pathwarden Sollerets (Pathwarden Thorolf is out of reach: he stands in landblock 0xA9B4, not where the trip is)", Assert.Single(kept.Kept).ToString());
    }

    [Fact]
    public void The_check_after_a_Give_wants_the_piece_gone_and_the_reward_arrived()
    {
        var item = Assert.Single(GearReturn.Plan(Thorolf, [Sollerets()], [WornRobe(0)], Context()).Returns);

        Assert.Null(GearReturn.Failure(item, pieceStillHeld: false, rewardBefore: 0, rewardAfter: 1));
        Assert.Null(GearReturn.Failure(item, pieceStillHeld: false, rewardBefore: 2, rewardAfter: 3)); // a bag joins the stack held
        Assert.Equal("the Give was not taken: the piece is still in the pack", GearReturn.Failure(item, pieceStillHeld: true, rewardBefore: 0, rewardAfter: 0));
        Assert.Equal("the piece left the pack but no bag of Steel (Salvage) (33621) arrived (held 0 -> 0)", GearReturn.Failure(item, pieceStillHeld: false, rewardBefore: 0, rewardAfter: 0));
    }

    [Fact]
    public void The_pieces_of_a_whole_chest_each_get_their_own_verdict()
    {
        var helm = Pack(0x90000060, 33607, "Pathwarden Helm", HeadWear, Head, 100);
        var leggings = Pack(0x90000061, 33601, "Pathwarden Plate Leggings", 0x400, 0x300, 100); // illustrative coverage: ACE's CoverageMask OuterwearUpperLegs 0x100 and OuterwearLowerLegs 0x200
        var hauberk = Pack(0x90000062, 33597, "Pathwarden Plate Hauberk", 0x200, HauberkCover, 100);
        var plan = GearReturn.Plan(Thorolf, [helm, leggings, hauberk, Sollerets(), Gauntlets()], [WornRobe(0)], Context());

        // the robe 0x17F00 holds every outerwear part (0x100-0x2000), the head 0x4000 and the feet 0x10000, so it blocks the helm, the hauberk, the leggings and the sollerets; only the hands (0x8000) are free
        Assert.Equal(["Pathwarden Helm", "Pathwarden Plate Hauberk", "Pathwarden Plate Leggings", "Pathwarden Sollerets"], plan.Returns.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Pathwarden Gauntlets"], plan.Kept.Select(k => k.Name));
        Assert.All(plan.Returns, r => Assert.StartsWith("blocked", r.Why, StringComparison.Ordinal));
    }

    [Fact]
    public void M6_16_E5e6_a_piece_a_worn_piece_beats_only_with_its_magic_off_is_kept_and_named_never_outgrown_or_blocked()
    {
        // the worn gauntlets ask Arcane Lore 100 of a climber with none read: their spells are off for her, and since E5e5 they score their armour (300 a part against the held piece's 100) where they scored 0
        var unlit = WornWith(0x90000070, "Unlit Gauntlets (illustrative)", 0x20, Hands, 300, Difficulty);
        var score = GearScore.Of(unlit, Context());
        Assert.NotNull(score.SpellsOff);
        Assert.Null(score.Unmet);
        Assert.True(score.Total > GearScore.Of(Gauntlets().Piece, Context()).Total);

        var plan = GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe(), unlit], Context());

        Assert.Empty(plan.Returns); // no Give: not outgrown, and not "blocked" by the piece that covers its hands either
        var kept = Assert.Single(plan.Kept);
        Assert.Equal("gear: return: 'Pathwarden Gauntlets' kept: the worn 'Unlit Gauntlets (illustrative)' beats it only on armour, its magic is off for her (guard: gear-return-unlit)", kept.Guard);
        Assert.StartsWith("return: kept Pathwarden Gauntlets (the worn 'Unlit Gauntlets (illustrative)' 0x90000070 scores ", kept.ToString(), StringComparison.Ordinal);
        Assert.Contains("with its spells off for her (activation: Arcane Lore 0 < 100", kept.Reason, StringComparison.Ordinal);
        Assert.EndsWith("it is not given back to Pathwarden Thorolf for bag of Steel (Salvage) (33621))", kept.ToString(), StringComparison.Ordinal);
        Assert.All(GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe()], Context()).Kept, k => Assert.Null(k.Guard)); // no other kept piece carries the guard's line
    }

    [Fact]
    public void M6_16_E5e6_a_worn_piece_whose_magic_works_for_her_marks_it_outgrown_as_before()
    {
        var unlit = WornWith(0x90000070, "Unlit Gauntlets (illustrative)", 0x20, Hands, 300, Difficulty);

        // the same piece lit on her (her registry holds a spell it cast): its spells are on whatever the requirement reads now, so the score names no spells off and the piece is outgrown
        var lit = GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe(), unlit], Context() with { LitBy = g => g == 0x90000070 });
        var item = Assert.Single(lit.Returns);
        Assert.StartsWith("outgrown: the worn 'Unlit Gauntlets (illustrative)' 0x90000070 scores ", item.Why, StringComparison.Ordinal);
        Assert.Empty(lit.Kept);

        // two worn pieces score above it, one with its magic off (the higher) and one with none to be off: it is outgrown by the one that works, as it was before E5e5 scored the other
        var boots = Pack(0x90000071, 33605, "Pathwarden Sollerets", FootWear, Feet | Hands, 100); // illustrative coverage: one piece over the feet and the hands, to meet two worn pieces
        var plain = Worn(0x90000072, "Better Boots (illustrative)", 0x100, Feet, 250);
        Assert.True(GearScore.Of(unlit, Context()).Total > GearScore.Of(plain, Context()).Total && GearScore.Of(plain, Context()).Total > GearScore.Of(boots.Piece, Context()).Total);
        var both = GearReturn.Plan(Thorolf, [boots], [unlit, plain], Context());
        var given = Assert.Single(both.Returns);
        Assert.StartsWith("outgrown: the worn 'Better Boots (illustrative)' 0x90000072 scores ", given.Why, StringComparison.Ordinal);
        Assert.Empty(both.Kept);
    }

    [Fact]
    public void M6_16_E5e6_a_worn_piece_she_cannot_wield_scores_0_and_outgrows_nothing_as_before_E5e5()
    {
        // a level 100 wield requirement on a level 5 climber: the score is 0 with the requirement named and no spells-off note, so the guard does not read it and the plan goes on as it did
        var unwieldable = WornWith(0x90000080, "Unwieldable Gauntlets (illustrative)", 0x20, Hands, 300, (AppraisalIds.WieldType, 7), (AppraisalIds.WieldDifficulty, 100), Difficulty);
        var score = GearScore.Of(unwieldable, Context());
        Assert.Equal(0, score.Total);
        Assert.NotNull(score.Unmet);
        Assert.Null(score.SpellsOff);

        var plan = GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe(), unwieldable], Context());

        // not outgrown; the piece covers its hands, so it is "blocked", the plan's own later rule, as before E5e5 and before this guard
        var item = Assert.Single(plan.Returns);
        Assert.StartsWith("blocked: the worn 'Unwieldable Gauntlets (illustrative)'", item.Why, StringComparison.Ordinal);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void M6_16_E5e6_the_guard_reads_only_a_worn_piece_that_scores_above_it_a_weaker_unlit_one_blocks_as_before()
    {
        // the worn piece's spells are off and its armour (50) is under the held piece's (100): nothing beats the held piece, so the guard has nothing to say and the coverage rule decides, as before E5e5 (when the worn piece scored 0)
        var weak = WornWith(0x90000090, "Weak Unlit Gauntlets (illustrative)", 0x20, Hands, 50, Difficulty);
        var plan = GearReturn.Plan(Thorolf, [Gauntlets()], [WornRobe(), weak], Context());

        var item = Assert.Single(plan.Returns);
        Assert.StartsWith("blocked: the worn 'Weak Unlit Gauntlets (illustrative)'", item.Why, StringComparison.Ordinal);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void M6_16_E5e6_a_piece_the_guard_kept_is_withheld_from_every_later_plan_of_the_visit()
    {
        var plan = GearReturn.Plan(Thorolf, [Gauntlets(), Sollerets()], [WornRobe(0), Worn(0x90000020, "Better Gauntlets (illustrative)", 0x20, Hands, 300)], Context());
        Assert.Equal(["Pathwarden Gauntlets", "Pathwarden Sollerets"], plan.Returns.Select(r => r.Name));

        Assert.Same(plan, plan.Withheld(new HashSet<uint>()));
        Assert.Same(plan, plan.Withheld(new HashSet<uint> { 0x90000099 })); // a piece the plan does not give

        var withheld = plan.Withheld(new HashSet<uint> { Gauntlets().Piece.Guid });
        Assert.Equal(["Pathwarden Sollerets"], withheld.Returns.Select(r => r.Name));
        Assert.Equal("return: kept Pathwarden Gauntlets (the guard gear-return-unlit kept it earlier this visit: it is not given back in the same visit)", Assert.Single(withheld.Kept).ToString());
    }
}
