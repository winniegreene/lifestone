using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 useful cantrips (the owner, 2026-11-16: keep only the useful cantrips, basic attributes or skills she has trained, specialised or plans to train): <see cref="CantripJudge"/> reads what each great cantrip raises from ACE's spell table
/// by spell id (<see cref="AceCantripStatMods"/>), her skills live, and her plan; <see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/> is the one predicate every great-piece
/// decision uses. The spell ids are ACE's own (the extract's rows).
/// </summary>
public sealed class UsefulCantripTests
{
    // ACE's spell ids (src/Lifestone.Data/Magic/Data/ace-world-cantrip-stat-mods.json)
    private const uint MajorFocus = 2574, LegendaryCoordination = 6103, MajorWar = 2534, MajorArcane = 2502, MajorManaConversion = 2525, MajorItem = 2516, MajorPiercingWard = 2613, MajorImpregnability = 2514,
        MajorHeavy = 2531, MajorLeadership = 2519, MajorFealty = 2511, MajorSprint = 2529, MajorAlchemy = 2501, MajorArmor = 2571, MajorManaGain = 2624, MinorFocus = 2581, Mystery = 999_999;

    /// <summary>
    /// Qwinnie's skills as her character was last saved (read-only SELECT of 2026-11-16, <c>ace_shard.biota_properties_skill</c> for the live character): specialised War Magic (34) and Life Magic (33); trained Arcane Lore (14), Magic
    /// Defense (15), Mana Conversion (16), Jump (22), Run (24), Creature (31) and Item Enchantment (32), Loyalty (36) and Salvaging (40); every other skill untrained (Melee Defense 6, Missile Defense 7, Leadership 35, Alchemy 38, Heavy
    /// Weapons 44).
    /// </summary>
    internal static readonly IReadOnlyDictionary<uint, uint> QwinnieClasses = new Dictionary<uint, uint>
    {
        [14] = 2, [15] = 2, [16] = 2, [22] = 2, [24] = 2, [31] = 2, [32] = 2, [33] = 3, [34] = 3, [36] = 2, [40] = 2,
    };

    /// <summary>A judge over <see cref="QwinnieClasses"/> and the mage's plan.</summary>
    internal static CantripJudge Qwinnie { get; } = new(id => QwinnieClasses.TryGetValue(id, out var c) ? c : 1u);

    private static readonly Dictionary<uint, SpellInfo> Spells = new[]
    {
        (MajorFocus, "Major Focus"), (LegendaryCoordination, "Legendary Coordination"), (MajorWar, "Major War Magic Aptitude"), (MajorArcane, "Major Arcane Prowess"), (MajorManaConversion, "Major Mana Conversion Prowess"),
        (MajorItem, "Major Item Enchantment Aptitude"), (MajorPiercingWard, "Major Piercing Ward"), (MajorImpregnability, "Major Impregnability"), (MajorHeavy, "Major Heavy Weapon Aptitude"), (MajorLeadership, "Major Leadership"),
        (MajorFealty, "Major Fealty"), (MajorSprint, "Major Sprint"), (MajorAlchemy, "Major Alchemical Prowess"), (MajorArmor, "Major Armor"), (MajorManaGain, "Major Mana Gain"), (MinorFocus, "Minor Focus"), (Mystery, "Major Mystery"),
    }.ToDictionary(p => p.Item1, p => WorldActionRig.Spell(p.Item1, SpellType.Enchantment) with { Name = p.Item2 });

    private static string? Name(uint id) => Spells.TryGetValue(id, out var s) ? s.Name : null;

    private static CantripVerdict Judge(CantripJudge judge, uint id) => judge.Judge(id, Name(id)!);

    [Fact]
    public void The_extract_reads_what_each_cantrip_raises_by_spell_id_and_every_great_row_names_its_target()
    {
        Assert.Equal(310, AceCantripStatMods.ById.Count); // the SELECT's 310 rows
        Assert.Equal(new CantripStatMod(2574, "Major Focus", 0x9001, 5), AceCantripStatMods.Of(MajorFocus)); // attribute 5, Focus
        Assert.Equal((0x9010u, 34u), (AceCantripStatMods.Of(MajorWar)!.Value.Type, AceCantripStatMods.Of(MajorWar)!.Value.Key)); // skill 34, War Magic
        Assert.Null(AceCantripStatMods.Of(Mystery));

        // a judge that holds every skill trained reads no great row of the extract as unread: every one names one attribute, one skill or a property
        var knowsAll = new CantripJudge(_ => 2u);
        var unread = AceCantripStatMods.ById.Values.Where(r => KeepAsideRule.IsGreat(r.Name)).Select(r => knowsAll.Judge(r.Id, r.Name)).Where(v => v.Use == CantripUse.Unread).Select(v => v.Why).ToList();
        Assert.Empty(unread);
    }

    [Fact]
    public void A_basic_attribute_cantrip_is_useful_whatever_she_holds()
    {
        Assert.Equal(new CantripVerdict(MajorFocus, "Major Focus", CantripUse.Attribute, "Major Focus raises Focus (a basic attribute)"), Judge(Qwinnie, MajorFocus));
        Assert.Equal(CantripUse.Attribute, Judge(Qwinnie, LegendaryCoordination).Use); // Coordination: not on the old 11 families, a basic attribute all the same
        Assert.Equal(CantripUse.Attribute, Judge(CantripJudge.Stateless, MajorFocus).Use); // no skills needed for an attribute
        Assert.Equal("useful: Major Focus raises Focus (a basic attribute)", Judge(Qwinnie, MajorFocus).ToString());
    }

    [Fact]
    public void A_skill_she_holds_specialised_or_trained_is_useful_from_her_live_skills()
    {
        Assert.Equal((CantripUse.Specialised, "Major War Magic Aptitude raises War Magic (specialised)"), (Judge(Qwinnie, MajorWar).Use, Judge(Qwinnie, MajorWar).Why));
        Assert.Equal((CantripUse.Trained, "Major Arcane Prowess raises Arcane Lore (trained)"), (Judge(Qwinnie, MajorArcane).Use, Judge(Qwinnie, MajorArcane).Why));
        Assert.Equal(CantripUse.Trained, Judge(Qwinnie, MajorSprint).Use); // Run, trained at creation
        Assert.Equal(CantripUse.Trained, Judge(Qwinnie, MajorFealty).Use); // Loyalty, trained at creation: the owner's rule keeps what she holds trained
    }

    [Fact]
    public void A_skill_her_plan_trains_is_useful_before_she_holds_it_and_Alchemy_on_a_need_is_not_planned()
    {
        var fresh = new CantripJudge(_ => 1u); // a new climber: nothing trained yet
        var manaConversion = Judge(fresh, MajorManaConversion);

        Assert.Equal(CantripUse.Planned, manaConversion.Use);
        Assert.Equal("Major Mana Conversion Prowess raises Mana Conversion (not trained yet: the mage's skill plan (War Magic, the credit order, the experience's skills) trains it)", manaConversion.Why);
        Assert.Equal(CantripUse.Planned, Judge(fresh, MajorItem).Use);
        Assert.Equal([16u, 31u, 32u, 33u, 34u], CantripPlan.Mage.Skills.Order()); // Mana Conversion, Creature, Item, Life, War
        Assert.Equal(CantripUse.NotUseful, Judge(Qwinnie, MajorAlchemy).Use); // Alchemy: trained on a need only (D136), not held
        Assert.Equal(CantripUse.NotUseful, Judge(new CantripJudge(_ => 1u, CantripPlan.None), MajorManaConversion).Use); // no plan: only what she holds counts
    }

    [Fact]
    public void A_ward_an_item_property_or_a_skill_she_neither_holds_nor_plans_is_not_useful()
    {
        Assert.Equal("not useful: Major Piercing Ward raises a float property (PropertyFloat 65: a ward's resistance, a regeneration rate or an item's modifier), not an attribute or a trained or planned skill", Judge(Qwinnie, MajorPiercingWard).ToString());
        Assert.Equal("Major Impregnability raises Missile Defense (untrained), not an attribute or a trained or planned skill", Judge(Qwinnie, MajorImpregnability).Why);
        Assert.Equal(CantripUse.NotUseful, Judge(Qwinnie, MajorHeavy).Use);
        Assert.Equal(CantripUse.NotUseful, Judge(Qwinnie, MajorLeadership).Use);
        Assert.Equal(CantripUse.NotUseful, Judge(Qwinnie, MajorArmor).Use); // body armour
        Assert.Equal(CantripUse.NotUseful, Judge(Qwinnie, MajorManaGain).Use); // a regeneration rate, no attribute or skill
        Assert.False(Judge(Qwinnie, MajorPiercingWard).Keeps);

        var trainedMissile = new CantripJudge(id => id == 7 ? 2u : 1u);
        Assert.Equal(CantripUse.Trained, Judge(trainedMissile, MajorImpregnability).Use); // the same cantrip, once she holds Missile Defense trained
    }

    [Fact]
    public void A_target_that_cannot_be_read_is_a_loud_named_TODO_and_the_piece_is_kept()
    {
        var mystery = Judge(Qwinnie, Mystery); // a great name with no row in the extract
        var undescribed = Judge(CantripJudge.Stateless, MajorHeavy); // a skill outside the plan before her skills are described
        var noStat = new CantripJudge(_ => 2u, statOf: id => new CantripStatMod(id, "Major Focus", 0x9000, 0)).Judge(MajorFocus, "Major Focus"); // a row that names no stat

        Assert.Equal(CantripUse.Unread, mystery.Use);
        Assert.Contains("has no row in the spell table's extract (ace-world-cantrip-stat-mods.json)", mystery.Why, StringComparison.Ordinal);
        Assert.StartsWith("UNREAD, kept until it is read (TODO(m6-cantrip-target-unread)): ", mystery.ToString(), StringComparison.Ordinal);
        Assert.Equal(CantripUse.Unread, undescribed.Use);
        Assert.Equal(CantripUse.Planned, Judge(CantripJudge.Stateless, MajorWar).Use); // a planned skill needs no description
        Assert.Equal(CantripUse.Unread, noStat.Use);
        Assert.True(mystery.Keeps && undescribed.Keeps && noStat.Keeps);
        Assert.True(CantripVerdictLog.HasUnread([mystery]));
    }

    [Fact]
    public void The_great_tiers_alone_are_judged_and_the_keep_aside_rule_keeps_only_the_useful_ones_of_any_family()
    {
        var rule = new KeepAsideRule(KeepAsideRule.ClimberDefault);
        var appraisal = KeepAsideRuleTests.Appraised(7, null, MinorFocus, MajorImpregnability, LegendaryCoordination, MajorPiercingWard, MajorArcane);

        var verdicts = Qwinnie.GreatOf(appraisal, Name);

        Assert.Equal([MajorImpregnability, LegendaryCoordination, MajorPiercingWard, MajorArcane], verdicts.Select(v => v.SpellId)); // Minor Focus is no great tier
        Assert.Equal(["Legendary Coordination", "Major Arcane Prowess"], rule.GreatCantrips(KeepAsideRule.WearableTypes, appraisal, Name, Qwinnie)); // Impregnability is on the old 11 families, but not useful to her
        Assert.Equal("Legendary Coordination, Major Arcane Prowess", rule.Match(0x8, appraisal, Name, Qwinnie));
        Assert.Null(rule.Match(0x8, KeepAsideRuleTests.Appraised(8, null, MajorImpregnability, MajorPiercingWard), Name, Qwinnie));
    }

    [Fact]
    public void At_the_corpse_a_useful_cantrip_outranks_sell_loot_and_one_she_has_no_use_for_is_plain_loot()
    {
        var rules = new MageLootRules(new MageLootThresholds { LoadLinePercent = 100, KeepAside = new KeepAsideRule(KeepAsideRule.ClimberDefault) }, Spells);
        MageLootItem Piece(uint guid, string name, uint spell) => rules.ItemFor(LootRulesTests.Item(0x8, 900, 15, guid, name), KeepAsideRuleTests.Appraised(guid, null, spell), held: false, cantrips: Qwinnie);

        var necklace = Piece(1, "Necklace", MajorImpregnability);
        var ring = Piece(2, "Ring", LegendaryCoordination);
        var anklet = Piece(3, "Anklet", MajorSprint);
        var bracelet = Piece(4, "Heavy Bracelet", MajorLeadership);

        Assert.Equal((false, (string?)null), (necklace.GreatCantrip, necklace.KeepAside));
        Assert.Equal((true, "Legendary Coordination"), (ring.GreatCantrip, ring.KeepAside));
        Assert.Equal((true, "Major Sprint"), (anklet.GreatCantrip, anklet.KeepAside));
        Assert.Equal((false, (string?)null), (bracelet.GreatCantrip, bracelet.KeepAside));
    }

    private const uint Necklace = 0x8C0D0001, Shoes = 0x8C0D0002, Kote = 0x8C0D0003, Doublet = 0x8C0D0004, Girth = 0x8C0D0005, Bracelet = 0x8C0D0006;

    [Fact]
    public void Pieces_already_in_her_pack_that_the_rule_calls_not_useful_turn_into_sell_loot_and_the_useful_ones_stay_kept()
    {
        var ground = GroundCatalog.Get("tusker-barracks");
        var r = new WorldActionRig();
        var items = new (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name, uint Spell)[]
        {
            (Necklace, 9201, 0x8, 2000, 30, "Necklace", MajorImpregnability), // Missile Defense untrained: sell loot
            (Shoes, 9202, 0x2, 1500, 200, "Shoes", MajorHeavy), // Heavy Weapons: sell loot
            (Kote, 9203, 0x2, 1800, 150, "Kote", MajorArcane), // Arcane Lore trained: kept
            (Doublet, 9204, 0x4, 1200, 100, "Doublet", MajorFealty), // Loyalty trained: kept
            (Girth, 9205, 0x2, 4000, 400, "Studded Leather Girth", MajorFocus), // an attribute: kept
            (Bracelet, 9206, 0x8, 1600, 40, "Heavy Bracelet", MajorLeadership), // Leadership untrained: sell loot
        };
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, i.Value, i.Burden, i.Guid, i.Name, i.Wcid);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg
        {
            Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))],
            Skills = [.. QwinnieClasses.Select(p => new SkillDesc(p.Key, 0, 1, p.Value, 0, 0, 0, 0))],
        });
        var state = r.H.State;
        foreach (var i in items)
        {
            state.Appraisals.Add(KeepAsideRuleTests.Answer(i.Guid, null, i.Spell), 0);
        }

        var lines = new List<string>();
        var useful = items.Where(i => CantripRoom.IsUsefulGreatPiece(state, i.Guid, Spells, log: lines.Add)).Select(i => i.Guid).ToList();
        var loot = CantripRoom.SellLoot(state, CantripRoomTests.Book(state, ground, new KeepAsideRule(KeepAsideRule.ClimberDefault), Spells));

        Assert.Equal([Kote, Doublet, Girth], useful); // the review pack's pieces: the sorter moves the rest back to the main pack, where the sale offers them
        Assert.Equal([Kote, Doublet, Girth], items.Where(i => CantripRoom.IsGreatPiece(state, i.Guid, Spells)).Select(i => i.Guid)); // the old name is the same predicate
        Assert.Equal([(Necklace, "gear-loot"), (Shoes, "gear-loot"), (Bracelet, "gear-loot")], loot.Select(l => (l.Guid, l.Rule))); // judged by value per burden like any other item
        // M6 16 E5c2: the field's great-cantrip keep is the keeper question (IsUsefulGreatPiece), asked with no keep-aside rule in force: never the on-her-body question of the wear pick (GearScore.GreatOn)
        var noRule = CantripRoomTests.Book(state, ground, null, Spells);
        Assert.All(items, i => Assert.Equal(
            CantripRoom.IsUsefulGreatPiece(state, i.Guid, Spells),
            CantripRoom.FieldKeep(i.Guid, state.Objects.TryGet(i.Guid, out var o) ? o : null!, noRule) == CantripRoom.KeepGreatCantrip));
        Assert.Contains("cantrip keep (the packs): 'Necklace' 0x8C0D0001: sell loot, judged by its value per burden: not useful: Major Impregnability raises Missile Defense (untrained), not an attribute or a trained or planned skill (the owner, 2026-11-16: only the useful cantrips)", lines);
        Assert.Contains("cantrip keep (the packs): 'Kote' 0x8C0D0003: a great piece, kept: useful: Major Arcane Prowess raises Arcane Lore (trained) (the owner, 2026-11-16: only the useful cantrips)", lines);
        Assert.Equal(6, lines.Count);

        lines.Clear();
        Assert.False(CantripRoom.IsUsefulGreatPiece(state, Necklace, Spells, log: lines.Add));
        Assert.Empty(lines); // each piece's verdict once
    }

    [Fact]
    public void A_piece_with_a_cantrip_whose_target_cannot_be_read_is_kept_and_its_line_names_the_TODO()
    {
        var r = new WorldActionRig();
        var o = LootRulesTests.Item(0x8, 900, 15, 0x8C0D0010, "Odd Ring", 9210);
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        r.H.Describe(new PlayerDescriptionMsg { Inventory = [new ContentDesc(0x8C0D0010, 0)] });
        r.H.State.Appraisals.Add(KeepAsideRuleTests.Answer(0x8C0D0010, null, Mystery), 0);
        var lines = new List<string>();

        Assert.True(CantripRoom.IsUsefulGreatPiece(r.H.State, 0x8C0D0010, Name, log: lines.Add));
        Assert.Contains(lines, l => l.Contains("UNREAD, kept until it is read (TODO(m6-cantrip-target-unread))", StringComparison.Ordinal));
    }
}
