using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The examine panel (M4 3.2 s4c; <c>gmExaminationUI</c>, floaty 0x100005F7 of the gameplay layout): opened by the item list's right click and by the paperdoll figure's body-part click (the colour of the click map, the worn item that covers
/// the part, the player when none does), each of which sends the Identify with the Ui source and shows the answer from a scripted 00C9 (a kit, a weapon, a creature, a failed roll); the parts the model lacks answer with a named TODO.
/// No creature is spawned (D23): the creature is a scripted answer for an NPC's guid, drawn as text.
/// </summary>
public sealed class GameExaminePanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Kit = 0x80000A02;
    private const uint Mace = 0x80000A01;
    private const uint Cuirass = 0x80000A04;
    private const uint Npc = 0x7A9B4027;
    private const uint InventoryButton = 0x100001B1;

    private static AceAppraisal KitAnswer() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x8000, [AppraisalIds.Value] = 1200, [AppraisalIds.Burden] = 20, [AppraisalIds.HealingBoost] = 10, [AppraisalIds.MaxStructure] = 25, [AppraisalIds.Structure] = 24 },
        Floats = new Dictionary<uint, double> { [100] = 1.25 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Handy Healing Kit", [AppraisalIds.LongDescription] = "A kit of bandages." },
    };

    private static AceAppraisal MaceAnswer() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x1, [AppraisalIds.Value] = 450, [AppraisalIds.Burden] = 90, [AppraisalIds.WieldType] = 2, [AppraisalIds.WieldSkill] = 44, [AppraisalIds.WieldDifficulty] = 100, [AppraisalIds.Workmanship] = 6 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Mace" },
        Weapon = new AceWeaponProfile(4, 40, 44, 12, 0.25, 1.1, 0, 0, 1, 0),
    };

    private static AceAppraisal CreatureAnswer(bool success) => new()
    {
        Success = success,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 8 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Thelnoth Cort the Healer" },
        Creature = success ? new AceCreatureProfile(55, 60) { Attributes = [20, 60, 60, 75, 90, 65, 70, 50, 80, 60] } : null,
    };

    private static Appraisal Answered(AceAppraisal info, uint guid)
    {
        var r = new WorldActionRig();
        r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, info));
        Assert.True(r.H.State.Appraisals.TryGet(guid, out var appraisal));
        return appraisal;
    }

    // ------------------------------------------------------------------ the fields (no shell)

    [Fact]
    public void A_kit_shows_value_burden_the_healing_boost_the_restoration_bonus_the_uses_left_and_the_description()
    {
        var log = new RecordingLogSink();
        var lines = GameExaminePanel.ItemLines(Answered(KitAnswer(), Kit), log);
        Assert.Equal(
            ["Value: 1,200", "Burden: 20", "Bonus to Healing Skill: 10", "Restoration Bonus: 125%", "Number of uses remaining: 24", "A kit of bandages."],
            lines);
    }

    [Fact]
    public void A_potion_shows_the_vital_it_restores()
    {
        var info = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 170, [AppraisalIds.HealingBoost] = 70, [AppraisalIds.BoosterVital] = 2 } };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink());
        Assert.Contains("Restores 70 Health when used.", lines);
        Assert.Contains("Value: 170", lines);
        Assert.Contains("Burden: ???", lines); // the table has no burden: retail's text for an unknown
    }

    [Fact]
    public void A_weapon_shows_the_skill_the_damage_range_with_its_type_the_speed_and_what_it_takes_to_wield()
    {
        var log = new RecordingLogSink();
        var lines = GameExaminePanel.ItemLines(Answered(MaceAnswer(), Mace), log);
        Assert.Equal("Value: 450", lines[0]);
        Assert.Equal("Workmanship: Nearly flawless (6)", lines[2]); // Appraisal_ShowTinkeringInfo's "Workmanship: %s (%d)", right after the burden (M5 7 S4)
        Assert.Contains(lines, l => l.StartsWith("Skill: ", StringComparison.Ordinal));
        Assert.Contains("Damage: 9 - 12, Bludgeoning", lines); // 3.2-R29: 12 less the profile's 0.25 variance share, then the greatest
        Assert.Contains("Speed: Average (40)", lines); // WeaponTimeToString's word, then the time (M5 7 S4)
        Assert.Contains(lines, l => l.StartsWith("Wield requires ", StringComparison.Ordinal) && l.EndsWith(" 100", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-words)", StringComparison.Ordinal)); // closed (M5 7 S4)
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-damage-range)", StringComparison.Ordinal)); // closed
    }

    [Fact]
    public void A_creature_shows_its_level_and_vitals()
    {
        var (level, vitals, note) = GameExaminePanel.CreatureLines(Answered(CreatureAnswer(success: true), Npc), new RecordingLogSink());
        Assert.Equal("8", level);
        Assert.Equal("Health: 55/60 (92 %)", vitals[0]); // Attribute2ndInfoRegion::Update: "%d/%d (%d %%)" for the health region, which shows the percentage (M5 7 D137)
        Assert.Equal("Stamina: 70/80", vitals[1]); // "%d/%d" for stamina and mana
        Assert.Equal("Mana: 50/60", vitals[2]);
        Assert.Equal(string.Empty, note);
    }

    [Fact]
    public void The_set_and_ratings_follow_the_workmanship_with_retails_blank_lines_and_the_details_only_when_given_D137()
    {
        // ItemExamineUI::SetAppraiseInfo 0x004B7ED0: ShowTinkeringInfo, then ShowSet 0x004AEC00, ShowRatings 0x004AF400 (its own blank line), SetAppraiseInfo's blank line, then the weapon's data.
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1, [AppraisalIds.Workmanship] = 6, [265] = 13, [370] = 3, [372] = 2, [379] = 10, [AppraisalIds.WeaponSkill] = 44 },
            Weapon = new AceWeaponProfile(4, 40, 44, 12, 0.25, 1.1, 0, 0, 1, 0),
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink()).ToList();
        var set = lines.IndexOf("Set: Soldier's");
        Assert.True(set > lines.FindIndex(static l => l.StartsWith("Workmanship", StringComparison.Ordinal)));
        Assert.Equal(["Set: Soldier's", "Ratings: Dam 3, Crit 2.", "This item adds 10 Vitality.", string.Empty, string.Empty], lines.Skip(set).Take(5));
        Assert.True(lines.FindIndex(static l => l.StartsWith("Skill:", StringComparison.Ordinal)) > set + 4);

        var detailed = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), setDetails: ["Pieces worn: 2"]).ToList();
        Assert.Equal("Pieces worn: 2", detailed[detailed.IndexOf("Set: Soldier's") + 1]);
    }

    [Fact]
    public void A_failed_creature_roll_shows_the_health_as_a_percentage_and_stamina_and_mana_unknown_as_retail()
    {
        // M5 7 D137: ACE's failed roll sends the creature profile with its health alone (CreatureProfile(creature, false): no ShowAttributes); retail's health region then shows "%d %%", the others "???".
        var log = new RecordingLogSink();
        var failed = new AceAppraisal
        {
            Success = false,
            Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 8 },
            Creature = new AceCreatureProfile(37, 50),
        };
        var (level, vitals, note) = GameExaminePanel.CreatureLines(Answered(failed, Npc), log);
        Assert.Equal("8", level); // the int table is still sent
        Assert.Equal(["Health: 74 %", "Stamina: ???", "Mana: ???"], vitals);
        Assert.Equal(string.Empty, note);
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-failed-roll-text)", StringComparison.Ordinal));

        // No profile at all: every value unknown.
        (_, vitals, _) = GameExaminePanel.CreatureLines(Answered(CreatureAnswer(success: false), Npc), log);
        Assert.Equal(["Health: ???", "Stamina: ???", "Mana: ???"], vitals);
    }

    [Fact]
    public void A_failed_roll_on_a_healer_or_a_lockpick_says_its_uses_are_unknown_as_retail()
    {
        // Appraisal_ShowRemainingUses: with no uses in the answer and a failed roll, an object flagged Healer (0x10000) or Lockpick (0x20000) says "Number of uses remaining:  Unknown" (two spaces).
        var failed = new AceAppraisal { Success = false, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 10 } };
        var log = new RecordingLogSink();
        Assert.Contains("Number of uses remaining:  Unknown", GameExaminePanel.ItemLines(Answered(failed, Kit), log, objectFlags: 0x10000));
        Assert.Contains("Number of uses remaining:  Unknown", GameExaminePanel.ItemLines(Answered(failed, Kit), log, objectFlags: 0x20000));
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(failed, Kit), log, objectFlags: 0), static l => l.StartsWith("Number of uses", StringComparison.Ordinal));
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(failed with { Success = true }, Kit), log, objectFlags: 0x10000), static l => l.StartsWith("Number of uses", StringComparison.Ordinal));
    }

    private static string? Names(uint map, uint value) => (map, value) switch
    {
        (GameExaminePanel.HeritageEnumMap, 1) => "Aluvian",
        (GameExaminePanel.HeritageEnumMap, 3) => "Sho",
        (GameExaminePanel.CreatureTypeEnumMap, 9) => "Phyntos_Wasp",
        _ => null,
    };

    [Theory]
    [InlineData(1, 34, 150, false, "Wield requires War Magic 150")]
    [InlineData(2, 34, 100, false, "Wield requires base War Magic 100")]
    [InlineData(3, 1, 200, false, "Wield requires Strength 200")]
    [InlineData(4, 6, 180, false, "Wield requires base Self 180")]
    [InlineData(5, 2, 120, false, "Wield requires Health 120")]
    [InlineData(6, 1, 90, false, "Wield requires base Maximum Health 90")]
    [InlineData(7, 0, 80, false, "Wield requires level 80")]
    [InlineData(8, 34, 3, false, "Wield requires specialized War Magic")]
    [InlineData(8, 34, 2, false, "Wield requires trained War Magic")]
    [InlineData(8, 34, 3, true, "Wield requires Specialized War Magic")]
    [InlineData(9, 287, 10, false, "Wield requires Standing with the Celestial Hand 10")]
    [InlineData(10, 400, 5, false, "Wield requires unknown quality 5")]
    [InlineData(11, 0, 9, false, "Wield requires Phyntos Wasp type")]
    [InlineData(12, 0, 3, false, "Wield requires Sho race")]
    [InlineData(12, 0, 2, false, "Wield requires Gharu'ndim race")]
    public void Every_wield_requirement_kind_reads_as_retails_line(int type, int skill, int difficulty, bool fourthSlot, string expected) =>
        Assert.Equal(expected, GameExaminePanel.WieldLine(type, skill, difficulty, fourthSlot, new RecordingLogSink(), Names));

    [Fact]
    public void The_four_requirement_slots_the_owner_heritage_and_account_lines_read_in_retails_order_and_an_unknown_first_kind_ends_them()
    {
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int>
            {
                [AppraisalIds.Value] = 1, [26] = 1, [324] = 1,
                [158] = 7, [159] = 0, [160] = 50, [270] = 8, [271] = 34, [272] = 3, [273] = 3, [274] = 5, [275] = 100, [276] = 8, [277] = 33, [278] = 2,
            },
            Bools = new Dictionary<uint, bool> { [85] = true },
            Strings = new Dictionary<uint, string> { [25] = "Ser Thing" },
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), enumString: Names).Where(static l => l.Contains("requires", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            ["Wield requires Ser Thing", "Use requires Throne of Destiny.", "Wield requires Aluvian", "Wield requires level 50", "Wield requires specialized War Magic", "Wield requires Focus 100", "Wield requires Trained Life Magic"],
            lines);

        var log = new RecordingLogSink();
        var unknownFirst = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [158] = 40, [159] = 0, [160] = 1, [270] = 7, [271] = 0, [272] = 50 } };
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(unknownFirst, Kit), log), static l => l.StartsWith("Wield requires", StringComparison.Ordinal));
        Assert.Contains(log.Lines, static l => l.Contains("TODO(m5-examine-requirement-kind)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_activation_heritage_is_named_from_the_dats_or_left_out_with_a_named_todo()
    {
        var info = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.ArcaneLore] = 120, [188] = 3 } };
        Assert.Contains("Activation requires Arcane Lore: 120, Sho", GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), enumString: Names));
        var log = new RecordingLogSink();
        Assert.Contains("Activation requires Arcane Lore: 120", GameExaminePanel.ItemLines(Answered(info, Kit), log));
        Assert.Contains(log.Lines, static l => l.Contains("TODO(m5-examine-heritage-name)", StringComparison.Ordinal));
    }

    [Fact]
    public void What_the_model_lacks_answers_not_in_M4_with_a_named_todo_each()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1, [307] = 5 },
            Strings = new Dictionary<uint, string> { [7] = "for Bob" },
            SpellBook = [0x80000002],
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log);
        Assert.DoesNotContain(lines, static l => l.StartsWith("Inscription", StringComparison.Ordinal)); // built (M5 7 D137): the inscription is its own box, not a line
        Assert.DoesNotContain(lines, static l => l.StartsWith("Ratings", StringComparison.Ordinal)); // built (M5 7 D137, L2): 307 is a creature's rating, which the item window does not read
        Assert.DoesNotContain(lines, l => l.StartsWith("Spells", StringComparison.Ordinal)); // an enchantment on the item is not one of its spells
        Assert.DoesNotContain(lines, l => l.StartsWith("Enchantments: not in", StringComparison.Ordinal)); // built (M5 7 S4): the enchantments are listed
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-ratings)", StringComparison.Ordinal));

        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-enchantments)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ an item's spells and magic (M5 7)

    private static Lifestone.Data.Magic.SpellInfo Spell(uint id, string name, string description = "") => new()
    {
        Id = id, Name = name, Description = description, School = default, IconId = 0, Category = 0, Flags = default, BaseMana = 0, BaseRangeConstant = 0, BaseRangeMod = 0, Power = 0, SpellEconomyMod = 0,
        FormulaVersion = 0, ComponentLoss = 0, MetaSpellType = default, MetaSpellId = 0, EncryptedFormula = [], Formula = [], CasterEffect = default, TargetEffect = default, FizzleEffect = default,
        RecoveryInterval = 0, RecoveryAmount = 0, DisplayOrder = 0, NonComponentTargetType = 0, ManaMod = 0,
    };

    private static Lifestone.Data.Magic.SpellInfo? Names(uint id) => id switch
    {
        3000 => Spell(3000, "Major War Magic Aptitude", "Increases the caster's War Magic skill by 15 points."),
        3001 => Spell(3001, "Minor Mana Conversion Prowess", "Increases the caster's Mana Conversion skill by 5 points."),
        2000 => Spell(2000, "Spirit Thirst", "Increases a magic casting implement's damage modifier by 5%."),
        _ => null,
    };

    [Fact]
    public void An_item_with_two_spells_lists_both_names_under_Spells_in_the_order_sent()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 9 }, SpellBook = [3000, 3001] };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names);
        Assert.Contains("Spells: Major War Magic Aptitude, Minor Mana Conversion Prowess", lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m5-examine-spell-name)", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("not in Lifestone M4", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spell_the_table_lacks_shows_its_number_with_a_named_todo()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal { Success = true, SpellBook = [3000, 4242] };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names);
        Assert.Contains("Spells: Major War Magic Aptitude, Spell 4242", lines);
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-spell-name)", StringComparison.Ordinal));
        Assert.Contains("Spells: Spell 3000, Spell 4242", GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink())); // no table at all
    }

    [Fact]
    public void An_item_enchantment_is_not_listed_among_the_spells()
    {
        var info = new AceAppraisal { Success = true, SpellBook = [3000, 0x80000000u | 3001] };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), Names);
        Assert.Contains("Spells: Major War Magic Aptitude", lines);
    }

    [Fact]
    public void An_item_with_no_spells_shows_no_Spells_line()
    {
        var lines = GameExaminePanel.ItemLines(Answered(KitAnswer(), Kit), new RecordingLogSink(), Names);
        Assert.DoesNotContain(lines, l => l.StartsWith("Spells", StringComparison.Ordinal));
    }

    [Fact]
    public void A_three_percent_fire_caster_shows_retails_three_damage_bonus_lines_and_its_mana_conversion_bonus()
    {
        // Appraisal_ShowCasterData (11.6096 0x004B21B0): the bonus against monsters, and against players at half the bonus (CombatSystem::ElementalModPKModifier 0.5); closes TODO(m5-examine-caster-wording).
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.DamageType] = 0x10 },
            Floats = new Dictionary<uint, double> { [GameExaminePanel.ElementalDamageModId] = 1.03, [GameExaminePanel.ManaConversionModId] = 0.05 },
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names).ToList();
        var first = lines.IndexOf("Bonus to Mana Conversion: +5%.");
        Assert.True(first > 0);
        Assert.Equal(["Damage bonus for Fire spells:", " vs. Monsters: +3.0%.", " vs. Players: +1.5%."], lines.Skip(first + 1).Take(3));
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m5-examine-caster-wording)", StringComparison.Ordinal));

        // Retail needs the element (PropertyInt 45) to name the bonus: without it there is no damage bonus line.
        var noElement = new AceAppraisal { Success = true, Floats = new Dictionary<uint, double> { [GameExaminePanel.ElementalDamageModId] = 1.03 } };
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(noElement, Kit), new RecordingLogSink(), Names), l => l.Contains("Damage bonus", StringComparison.Ordinal));
        Assert.DoesNotContain(GameExaminePanel.ItemLines(Answered(KitAnswer(), Kit), new RecordingLogSink(), Names), l => l.Contains("Damage bonus", StringComparison.Ordinal) || l.Contains("Mana Conversion", StringComparison.Ordinal));
    }

    [Fact]
    public void A_worn_wand_shows_its_spells_by_name_and_its_mana_now_and_at_most_with_each_spells_description_as_retail()
    {
        // The owner's proof (D122, D125): a wand the mage wears, examined, shows its spells and its current and maximum mana. Retail's order and words: Appraisal_ShowShortMagicInfo's "Spells: " line after the item's data,
        // Appraisal_ShowActivationRequirements, then after the sale rule Appraisal_ShowMagicInfo's spellcraft, "Mana: N / M.", the mana cost and "Spell Descriptions:" with a "~ name: description" line each.
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int>
            {
                [AppraisalIds.Value] = 2500, [AppraisalIds.Burden] = 50, [AppraisalIds.Workmanship] = 4, [AppraisalIds.ArcaneLore] = 120, [AppraisalIds.Spellcraft] = 150,
                [AppraisalIds.ManaCurrent] = 1180, [AppraisalIds.ManaMax] = 1500,
            },
            Floats = new Dictionary<uint, double> { [GameExaminePanel.ManaRateId] = -1.0 / 30.0 },
            Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Wand", [AppraisalIds.LongDescription] = "A wand." },
            SpellBook = [3000, 3001],
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names);
        Assert.Equal(
            [
                "Value: 2,500", "Burden: 50", "Workmanship: Exquisitely crafted (4)", "Spells: Major War Magic Aptitude, Minor Mana Conversion Prowess", "Activation requires Arcane Lore: 120", "Spellcraft: 150.", "Mana: 1180 / 1500.",
                "Mana Cost: 1 point per 30 seconds.", "Spell Descriptions:", "~ Major War Magic Aptitude: Increases the caster's War Magic skill by 15 points.",
                "~ Minor Mana Conversion Prowess: Increases the caster's Mana Conversion skill by 5 points.", "A wand.",
            ],
            lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(", StringComparison.Ordinal));
    }

    [Fact]
    public void An_item_mana_cost_says_the_skill_can_reduce_it_and_enchantments_on_the_item_are_listed_under_their_own_heading()
    {
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [GameExaminePanel.ItemManaCostId] = 20, [AppraisalIds.ManaCurrent] = 5, [AppraisalIds.ManaMax] = 10 },
            SpellBook = [3000, 0x80000000u | 2000],
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), Names).ToList();
        var cost = lines.IndexOf("Mana Cost: 20.");
        Assert.Equal("(Can be reduced by the Mana Conversion skill)", lines[cost + 1]);
        var heading = lines.IndexOf("Enchantments:");
        Assert.Equal([string.Empty, "~ Spirit Thirst: Increases a magic casting implement's damage modifier by 5%."], lines.Skip(heading + 1).Take(2));
        Assert.True(lines.IndexOf("Spell Descriptions:") < heading);

        // Only enchantments: no spell of its own, so no spellcraft, mana or descriptions (retail's fAddedSpell), but the enchantments still show.
        var enchanted = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.ManaCurrent] = 5, [AppraisalIds.ManaMax] = 10 }, SpellBook = [0x80000000u | 2000] };
        var only = GameExaminePanel.ItemLines(Answered(enchanted, Kit), new RecordingLogSink(), Names);
        Assert.Contains("Enchantments:", only);
        Assert.DoesNotContain(only, l => l.StartsWith("Mana", StringComparison.Ordinal) || l.StartsWith("Spell", StringComparison.Ordinal) || l.StartsWith("Stored", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_appraisal_with_a_spell_book_says_spells_unknown_twice_as_retail()
    {
        var info = new AceAppraisal { Success = false, Ints = new Dictionary<uint, int> { [AppraisalIds.ArcaneLore] = 50 }, SpellBook = [3000] };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), Names);
        Assert.Equal(2, lines.Count(static l => l == "Spells: unknown.")); // Appraisal_ShowShortMagicInfo and Appraisal_ShowMagicInfo each say it
        Assert.DoesNotContain(lines, l => l.StartsWith("Activation", StringComparison.Ordinal)); // Appraisal_ShowActivationRequirements needs a success
    }

    [Fact]
    public void A_mana_stone_shows_its_stored_mana_efficiency_and_chance_of_destruction()
    {
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.ManaCurrent] = 3000 },
            Floats = new Dictionary<uint, double> { [87] = 0.5, [137] = 0.25 },
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), Names);
        Assert.Equal(["Stored Mana: 3000", "Efficiency: 50%", "Chance of Destruction: 25%"], lines.Skip(2).Take(3));
        Assert.DoesNotContain(lines, l => l.StartsWith("Mana:", StringComparison.Ordinal));
    }

    [Fact]
    public void An_item_only_its_owner_activates_says_so_and_a_heritage_retail_reads_from_the_dats_is_a_named_todo()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [110] = 3, [188] = 2 },
            Bools = new Dictionary<uint, bool> { [94] = true },
            Strings = new Dictionary<uint, string> { [25] = "Bael'Zharon" },
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names);
        Assert.Contains("Activation requires Allegiance Rank: 3, Gharu'ndim", lines);
        Assert.Contains("This item can only be activated by Bael'Zharon.", lines);
        var anyOwner = info with { Strings = new Dictionary<uint, string>(), Ints = new Dictionary<uint, int> { [188] = 1 } };
        var owner = GameExaminePanel.ItemLines(Answered(anyOwner, Kit), log, Names);
        Assert.Contains("This item can only be activated by the original owner.", owner);
        Assert.DoesNotContain(owner, l => l.StartsWith("Activation requires", StringComparison.Ordinal)); // heritage 1 is named from the dats: left out, loudly
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-heritage-name)", StringComparison.Ordinal));
    }

    [Fact]
    public void Tinkering_and_a_salvage_bags_workmanship_show_retails_lines()
    {
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [GameExaminePanel.TinkeredId] = 2, [AppraisalIds.Workmanship] = 22, [GameExaminePanel.SalvagedItemsId] = 4 },
            Strings = new Dictionary<uint, string> { [39] = "Tinker Tom", [40] = "Imbuer Ida" },
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink(), Names);
        Assert.Equal(
            ["This item has been tinkered 2 times.", "Last tinkered by Tinker Tom.", "Imbued by Imbuer Ida.", "Workmanship: Nearly flawless (5.50)", string.Empty, "Salvaged from 4 items."],
            lines.Skip(2).Take(6)); // a salvage bag: the average 22 / 4 = 5.5 to two places, its word that of the average rounded half up (6)
        var once = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [GameExaminePanel.TinkeredId] = 1 } };
        Assert.Contains("This item has been tinkered 1 time.", GameExaminePanel.ItemLines(Answered(once, Kit), new RecordingLogSink(), Names));
    }

    [Theory]
    [InlineData(0, "crafted")]
    [InlineData(1, "Poorly crafted")]
    [InlineData(2, "Well-crafted")]
    [InlineData(3, "Finely crafted")]
    [InlineData(4, "Exquisitely crafted")]
    [InlineData(5, "Magnificent")]
    [InlineData(6, "Nearly flawless")]
    [InlineData(7, "Flawless")]
    [InlineData(8, "Utterly flawless")]
    [InlineData(9, "Incomparable")]
    [InlineData(10, "Priceless")]
    [InlineData(14, "Priceless")]
    public void The_workmanship_words_are_retails_eleven(int level, string word) => Assert.Equal(word, GameExaminePanel.WorkmanshipWord(level));

    [Theory]
    [InlineData(10, "Very Fast")]
    [InlineData(11, "Fast")]
    [InlineData(30, "Fast")]
    [InlineData(31, "Average")]
    [InlineData(49, "Average")]
    [InlineData(50, "Slow")]
    [InlineData(79, "Slow")]
    [InlineData(80, "Very Slow")]
    public void The_speed_words_follow_retails_thresholds(int time, string word) => Assert.Equal(word, GameExaminePanel.SpeedWord(time));

    [Theory]
    [InlineData(1.05, "+5%", "+5.0%")]
    [InlineData(0.9, "-10%", "-10.0%")]
    [InlineData(1.016, "+2%", "+1.6%")] // the whole percent rounds half up (1.6 + 0.5, cut)
    public void The_modifier_texts_are_retails(double modifier, string whole, string tenth)
    {
        Assert.Equal(whole, GameExaminePanel.ModifierText(modifier));
        Assert.Equal(tenth, GameExaminePanel.SmallModifierText(modifier));
    }

    [Fact]
    public void The_requirements_spellcraft_mana_and_arcane_lore_show_when_the_answer_holds_them()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int>
            {
                [AppraisalIds.WieldType] = 7, [AppraisalIds.WieldSkill] = 1, [AppraisalIds.WieldDifficulty] = 150, [AppraisalIds.Spellcraft] = 291, [AppraisalIds.ManaCurrent] = 800, [AppraisalIds.ManaMax] = 1200, // retail reads a requirement only when its three ints are there
                [AppraisalIds.ArcaneLore] = 250, [115] = 100, [176] = 34, [258] = 90, [257] = 6, [260] = 120, [259] = 5,
            },
            SpellBook = [3000],
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log, Names).ToList();
        Assert.Contains("Wield requires level 150", lines);
        Assert.Contains("Activation requires Arcane Lore: 250, War Magic: 100, Self: 90, Maximum Mana: 120", lines); // Appraisal_ShowActivationRequirements' order and words
        Assert.Contains("Spellcraft: 291.", lines);
        Assert.Contains("Mana: 800 / 1200.", lines); // "Mana: %d / %d." (Appraisal_ShowMagicInfo)
        foreach (var closed in new[] { "m5-examine-skill-limit", "m5-examine-activation-wording" })
        {
            Assert.DoesNotContain(log.Lines, l => l.Contains($"TODO({closed})", StringComparison.Ordinal));
        }

        // Retail's order: the spell names, the wield requirement, the activation, then (after the sale rule) the spellcraft and the mana.
        Assert.Contains("Spells: Major War Magic Aptitude", lines);
        Assert.True(lines.IndexOf("Spells: Major War Magic Aptitude") < lines.IndexOf("Wield requires level 150"));
        Assert.True(lines.IndexOf("Wield requires level 150") < lines.FindIndex(static l => l.StartsWith("Activation requires", StringComparison.Ordinal)));
        Assert.True(lines.FindIndex(static l => l.StartsWith("Activation requires", StringComparison.Ordinal)) < lines.IndexOf("Spellcraft: 291."));
        var plain = GameExaminePanel.ItemLines(Answered(KitAnswer(), Kit), new RecordingLogSink(), Names);
        Assert.DoesNotContain(plain, l => l.StartsWith("Activation", StringComparison.Ordinal) || l.StartsWith("Spellcraft", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the figure's click map

    [Fact]
    public void The_nine_hit_test_colours_name_the_nine_body_parts_and_retails_masks()
    {
        Assert.Equal(1u, GamePaperDoll.MaskOfColor(0, 0, 255)); // head
        Assert.Equal(514u, GamePaperDoll.MaskOfColor(0, 255, 0)); // chest
        Assert.Equal(1028u, GamePaperDoll.MaskOfColor(255, 0, 0)); // abdomen
        Assert.Equal(2056u, GamePaperDoll.MaskOfColor(0, 255, 255)); // upper arm
        Assert.Equal(4112u, GamePaperDoll.MaskOfColor(255, 0, 255)); // lower arm
        Assert.Equal(8256u, GamePaperDoll.MaskOfColor(255, 255, 0)); // upper leg
        Assert.Equal(16512u, GamePaperDoll.MaskOfColor(0, 0, 128)); // lower leg
        Assert.Equal(32u, GamePaperDoll.MaskOfColor(0, 128, 0)); // hand
        Assert.Equal(256u, GamePaperDoll.MaskOfColor(128, 0, 0)); // foot
        Assert.Equal(0u, GamePaperDoll.MaskOfColor(12, 34, 56)); // none of them
        Assert.Equal(9, GamePaperDoll.HitTestColors.Length);
    }

    [Fact]
    public void The_upper_worn_item_is_the_one_with_the_higher_priority_and_the_player_answers_a_bare_part()
    {
        var shirt = new EquippedItem(0x80000001, 0x2, 0);
        var cuirass = new EquippedItem(0x80000002, 0x200, 0);
        var coat = new EquippedItem(0x80000003, 0x202, 8);
        Assert.Equal(0x80000002u, GamePaperDoll.UpperItemAt([shirt, cuirass], 514, Player)); // armour (0x200 counts as 127) over an undershirt (0x2, priority 0)
        Assert.Equal(0x80000002u, GamePaperDoll.UpperItemAt([cuirass, coat], 514, Player)); // 127 beats a priority of 8: the cuirass stays upper
        Assert.Equal(Player, GamePaperDoll.UpperItemAt([cuirass], 1, Player)); // nothing on the head: the player
        Assert.Equal(0u, GamePaperDoll.UpperItemAt([cuirass], 0, Player)); // retail's no-colour answer
    }

    // ------------------------------------------------------------------ the shell

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event;
        private uint _sequence = 100;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public UiLoginContext LoginContext => screen.PanelControllers!.Context;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public GameExaminePanel Examine => Assert.IsType<GameExaminePanel>(screen.PanelHost!.Examine);

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public GamePaperDoll Doll => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GamePaperDoll>());

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Wield(uint item, uint location) =>
            handler.OnWieldObject(Context, new GameEventHeader(Player, ++_event, 0x0023), new WieldObjectEvent(item, location));

        public void Answer(uint guid, AceAppraisal info) =>
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceVendorWriters.IdentifyResponse(Player, ++_sequence, guid, info), handler).Outcome);

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void RightClick(UiElement element)
        {
            var box = element.ScreenBox;
            rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: false);
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint? container = Player, uint? valid = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = container, ValidLocations = valid } };
    }

    private Game Enter(bool examineIcon = false)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, examineIcon: examineIcon);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var game = new Game(rig, screen, handler!);
        handler!.OnPlayerCreate(game.Context, new PlayerCreateMsg(Player));
        game.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        game.Create(Item(Kit, "Handy Healing Kit", 0x060011D0, 0x8000));
        game.Create(Item(Mace, "Mace", 0x060011D1, 0x1, valid: 0x00100000));
        game.Create(Item(Cuirass, "Leather Cuirass", 0x060011D2, 0x2, valid: 0x200));
        game.Create(Item(Npc, "Thelnoth Cort the Healer", 0x060011D3, 0x10, container: null));
        handler.OnPlayerDescription(
            game.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg { Attributes = [new AttributeDesc(1, 0, 100, 0)], Inventory = [new ContentDesc(Kit, 0), new ContentDesc(Mace, 0), new ContentDesc(Cuirass, 0)] });
        game.Frames(2);
        return game;
    }

    [RequiresLocalDatsFact]
    public void The_shell_has_the_examine_window_hidden_until_something_is_examined()
    {
        using var g = Enter();
        Assert.False(g.Examine.IsOpen);
        Assert.Equal(ExamineKind.None, g.Examine.Kind);
        Assert.Same(g.Examine, GameExaminePanel.Of(g.LoginContext));
    }

    [RequiresLocalDatsFact]
    public void A_right_click_on_a_pack_item_opens_the_panel_sends_the_identify_and_shows_the_answer()
    {
        using var g = Enter();
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(14);
        var cell = Assert.Single(Enumerable.Range(0, g.Page.PackList.CellCount).Select(i => g.Page.PackList.CellAt(i)!), c => c.ItemId == Kit);
        g.RightClick(cell);
        var examine = g.Examine;
        Assert.True(examine.IsOpen);
        Assert.Equal(Kit, examine.ShownGuid);
        Assert.Equal(ExamineKind.Item, examine.Kind);
        Assert.True(examine.Pending);
        Assert.Equal("Handy Healing Kit", examine.Title); // the object's name, until the answer
        Assert.Equal(1, examine.Opens);
        var sent = Assert.IsAssignableFrom<IdentifyHandle>(examine.LastIdentify);
        Assert.Equal(ActionSource.Ui, sent.Source);
        Assert.Equal(ActionKind.Identify, sent.Kind);

        g.Answer(Kit, KitAnswer()); // the server's 00C9
        Assert.False(examine.Pending);
        Assert.Equal(1, examine.Fills);
        Assert.Equal("Handy Healing Kit", examine.Title);
        Assert.Contains("Value: 1,200", examine.Lines);
        Assert.Contains("Restoration Bonus: 125%", examine.Lines);
        Assert.Equal(0x060011D0u, examine.IconId);
        var text = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemTextId));
        Assert.Contains("Value: 1,200", text.Text, StringComparison.Ordinal);
        var title = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.TitleId));
        Assert.Equal("Handy Healing Kit", title.Text);
        Assert.True(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId)!.Visible);
        Assert.False(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CreatureFieldId)!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_item_text_draws_a_raised_line_and_a_lowered_line_in_the_dats_raised_and_lowered_colours_D142()
    {
        // ItemExamineUI::AddItemInfo (11.6096 0x004AE020) appends each line with AppendTextWithFont in font 0 and the colour index; the constructor (0x004ABB70) makes 1 the raised font and 2 the lowered. The item text's own
        // colour list in the dat gives the colours: a raised line, a lowered line and a plain one are three different colours.
        using var g = Enter();
        var examine = g.Examine;
        examine.Open(Cuirass);
        g.Answer(Cuirass, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x2, [AppraisalIds.Value] = 500, [AppraisalIds.ArmorLevel] = 75 },
            Armor = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f],
            ArmorBits = (0x1 | 0x2, 0x1), // the level raised (Impenetrability), slashing lowered (a lure)
        });
        g.Frames(2);
        Assert.Contains(ExamineColor.Raised, examine.LineColors);
        var text = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemTextId));
        var all = text.Text;
        var glyphs = text.Glyphs.Glyphs;
        uint ColorAt(string line) => glyphs[all.IndexOf(line, StringComparison.Ordinal)].Color.ToColor32();
        var plain = ColorAt("Value: 500");
        var raised = ColorAt("Armor Level: 75");
        var lowered = ColorAt("Slashing: Average  (75)");
        Assert.NotEqual(plain, raised);
        Assert.NotEqual(plain, lowered);
        Assert.NotEqual(raised, lowered);
        Assert.Equal(raised, glyphs[all.IndexOf("Armor Level: 75", StringComparison.Ordinal) + "Armor Level: ".Length].Color.ToColor32()); // the whole line, label and value, as retail
        Assert.Equal(plain, ColorAt("Piercing: Average  (75)"));
    }

    [RequiresLocalDatsFact]
    public void An_inscribable_item_in_the_pack_shows_inscribe_here_and_sends_the_text_when_the_box_loses_the_focus_D137()
    {
        // ItemExamineUI::SetInscription, SetInscriptionEditableState, HandleInscriptionGainingFocus and HandleInscriptionLosingFocus (M5 7 D137); the write is IActionApi.SetInscription (GameAction 0x00BF).
        using var g = Enter();
        const uint Ring = 0x80000A07;
        const uint Loose = 0x80000A08;
        var ring = Item(Ring, "Gold Ring", 0x060011D0, 0x8);
        g.Create(ring with { Weenie = ring.Weenie with { ObjectDescriptionFlags = GameExaminePanel.InscribableFlag } });
        var loose = Item(Loose, "Silver Ring", 0x060011D0, 0x8, container: null);
        g.Create(loose with { Weenie = loose.Weenie with { ObjectDescriptionFlags = GameExaminePanel.InscribableFlag } });
        g.Frames(2);
        var examine = g.Examine;
        var box = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.InscriptionId));
        var background = Assert.IsAssignableFrom<UiElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.InscriptionBackgroundId));

        // No scribe: "<Inscribe here>", writable; the focus clears it and signs it with this character.
        examine.Open(Ring);
        g.Answer(Ring, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 100 } });
        Assert.True(examine.InscriptionShown);
        Assert.Equal(GameExaminePanel.InscribeHere, examine.InscriptionText);
        Assert.Equal(string.Empty, examine.SignatureText);
        Assert.True(examine.InscriptionEditable);
        g.Rig.Manager.SetFocusElement(box);
        Assert.Equal(string.Empty, examine.InscriptionText);
        Assert.Equal("--Tester", examine.SignatureText);
        box.SetText("For the Den");
        g.Rig.Manager.SetFocusElement(null);
        var sent = Assert.IsAssignableFrom<ActionHandle>(examine.LastInscription);
        Assert.Equal((ActionKind.SetInscription, ActionSource.Ui), (sent.Kind, sent.Source));
        Assert.Equal("Tester", examine.Scribe);
        Assert.Equal("For the Den", examine.InscriptionText);

        // Unchanged text sends nothing.
        g.Rig.Manager.SetFocusElement(box);
        g.Rig.Manager.SetFocusElement(null);
        Assert.Same(sent, examine.LastInscription);

        // Another character's inscription: shown with its signature, not writable; a press says who may change it.
        examine.Open(Ring);
        g.Answer(Ring, new AceAppraisal { Success = true, Strings = new Dictionary<uint, string> { [7] = "Mine", [8] = "Someone Else" } });
        Assert.Equal("Mine", examine.InscriptionText);
        Assert.Equal("--Someone Else", examine.SignatureText);
        Assert.False(examine.InscriptionEditable);
        background.BroadcastElementMessage(UiMessageIds.MouseDown, UiMouseActions.Left);
        Assert.Equal("Only Someone Else can change the inscription", examine.LastInscriptionNotice);

        // An inscribable item the player does not hold, and an item that is not inscribable.
        examine.Open(Loose);
        g.Answer(Loose, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 5 } });
        Assert.True(examine.InscriptionShown);
        Assert.False(examine.InscriptionEditable);
        background.BroadcastElementMessage(UiMessageIds.MouseDown, UiMouseActions.Left);
        Assert.Equal("Item must be in your inventory to inscribe.", examine.LastInscriptionNotice);
        examine.Open(Kit);
        g.Answer(Kit, KitAnswer());
        Assert.False(examine.InscriptionShown);
        background.BroadcastElementMessage(UiMessageIds.MouseDown, UiMouseActions.Left);
        Assert.Equal("This item is not inscribable.", examine.LastInscriptionNotice);
        examine.Close();
    }

    [RequiresLocalDatsFact]
    public void The_toolbars_examine_button_examines_the_selection_or_asks_for_a_target_that_the_next_click_names_S6()
    {
        // M5 7 S6 (D136): gmToolbarUI::ListenToElementMessage's 0x100001A5 calls ClientUISystem::ExamineObject with the selected object, or with none sets the examine target mode; a left click on an item in a
        // list then examines it (UIElement_ItemList::HandleTargetedUseLeftClick) and does not select it, and the press ends the mode on the next frame (ClientUISystem::OnAction, UseTime). Escape ends it too.
        using var g = Enter();
        var toolbar = Assert.IsType<GameToolbar>(g.Screen.ToolbarUi);
        var button = Assert.IsAssignableFrom<UiButton>(g.Screen.Root!.GetChildRecursive(GameToolbar.ExamineButtonId));
        var target = g.Rig.Manager.TargetMode;
        uint selected = 0;
        toolbar.SelectedSource = () => selected;

        // Nothing selected: the mode asks for a target; no message dialog (the old NotInM3 placeholder).
        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(1, toolbar.ExamineClicks);
        Assert.Equal(UiTargetModeKind.Examine, target.Mode);
        Assert.False(g.Examine.IsOpen);
        Assert.Empty(g.Rig.Manager.Dialogs.OpenDialogs);

        // The next click, on the healing kit in the pack: examined, not selected; the mode ends after the press.
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(14);
        Assert.Equal(UiTargetModeKind.None, target.Mode); // the click that opened the page was a press too: it ended the mode (as retail: any press)
        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(UiTargetModeKind.Examine, target.Mode);
        var list = g.Page.PackList;
        var selectedBefore = list.SelectedItemId;
        var cell = Assert.Single(Enumerable.Range(0, list.CellCount).Select(i => list.CellAt(i)!), c => c.ItemId == Kit);
        Assert.True(g.Rig.Click(cell));
        Assert.True(g.Examine.IsOpen);
        Assert.Equal(Kit, g.Examine.ShownGuid);
        Assert.Equal(Kit, target.LastTarget);
        Assert.Equal(selectedBefore, list.SelectedItemId); // HandleTargetedUseLeftClick selects nothing
        g.Frames(1);
        Assert.Equal(UiTargetModeKind.None, target.Mode);
        g.Examine.Close();

        // Escape ends a mode with nothing clicked.
        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(UiTargetModeKind.Examine, target.Mode);
        Assert.True(g.Rig.Manager.Input.KeyDown(DikScanCodes.Escape));
        g.Rig.Manager.Input.KeyUp(DikScanCodes.Escape);
        Assert.Equal(UiTargetModeKind.None, target.Mode);
        Assert.False(g.Examine.IsOpen);

        // A selection: the button examines it at once, no mode.
        selected = Mace;
        button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(UiTargetModeKind.None, target.Mode);
        Assert.True(g.Examine.IsOpen);
        Assert.Equal(Mace, g.Examine.ShownGuid);
        Assert.Equal("Mace", g.Examine.Title);
        g.Examine.Close();
    }

    [RequiresLocalDatsFact]
    public void The_item_window_draws_the_objects_composed_icon_with_its_overlay_and_the_Appraisals_icon_only_when_the_table_has_none()
    {
        // M4 4.1 v4 (m4-examine-icon): the floaty's layout has no icon element, so the panel makes one from the standalone layout and composes the object's icon in it (the pack cell's path). With --examine-icon on only (review R8).
        using var g = Enter(examineIcon: true);
        var plain = Item(0x80000B01, "Orb", 0x060011D0, 0x8000);
        g.Create(plain with { Weenie = plain.Weenie with { Header = plain.Weenie.Header | WeenieHeaderFlags.IconOverlay, IconOverlayId = 0x060011D5 } });
        var bare = Item(0x80000B02, "Plain Thing", 0, 0x80);
        g.Create(bare);
        g.Frames(2);
        var examine = g.Examine;
        Assert.NotNull(examine.IconElement);
        Assert.Equal(GameExaminePanel.ItemIconId, examine.IconElement!.ElementId);
        Assert.Same(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId), examine.IconElement.Parent);

        examine.Open(0x80000B01);
        g.Frames(3);
        Assert.Equal(0x060011D0u, examine.IconId);
        Assert.Equal(0x060011D5u, examine.Icon.OverlayId);
        Assert.Equal(0x8000u, examine.Icon.ItemType);
        Assert.False(examine.IconPending);
        Assert.True(examine.IconCompositions >= 1);
        Assert.NotNull(examine.IconElement.GeneratedImage);
        Assert.Equal(32, examine.IconElement.GeneratedImage!.Width);

        // The world table holds no icon for this object: the Appraisal's DataId 8 (property data) is the fallback it always was.
        examine.Open(0x80000B02);
        g.Answer(0x80000B02, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 5 }, Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Plain Thing" }, DataIds = new Dictionary<uint, uint> { [8] = 0x060011D6 } });
        g.Frames(3);
        Assert.Equal(0x060011D6u, examine.IconId);
        Assert.NotNull(examine.IconElement.GeneratedImage);

        examine.Close();
    }

    [RequiresLocalDatsFact]
    public void By_default_the_item_window_has_no_icon_as_retails_merged_layout_draws_it()
    {
        // Review R8: the icon is an addition to retail (SetAppraiseInfo 0x004B7ED0 skips it), so without --examine-icon on the panel makes no icon element and composes nothing.
        using var g = Enter();
        g.Frames(2);
        Assert.Null(g.Examine.IconElement);
        g.Examine.Open(Kit);
        g.Answer(Kit, KitAnswer());
        g.Frames(3);
        Assert.Equal("Handy Healing Kit", g.Examine.Title);
        Assert.Contains("Value: 1,200", g.Examine.Lines);
        Assert.Equal(0, g.Examine.IconCompositions);
        Assert.False(g.Examine.IconPending);
        Assert.DoesNotContain(g.Rig.Log.Lines, static l => l.Contains("TODO(m4-examine-icon)", StringComparison.Ordinal));
        g.Examine.Close();
    }

    [RequiresLocalDatsFact]
    public void An_answer_for_another_object_or_after_the_window_closed_changes_nothing()
    {
        using var g = Enter();
        g.Examine.Open(Kit);
        g.Answer(Mace, MaceAnswer());
        Assert.True(g.Examine.Pending);
        Assert.Equal(0, g.Examine.Fills);
        g.Examine.Close();
        Assert.False(g.Examine.IsOpen);
        g.Answer(Kit, KitAnswer());
        Assert.Equal(0, g.Examine.Fills);
    }

    [RequiresLocalDatsFact]
    public void The_close_button_closes_the_window()
    {
        using var g = Enter();
        g.Examine.Open(Kit);
        g.Frames(2);
        var close = g.Screen.Root!.GetChildRecursive(GameExaminePanel.CloseButtonId)!;
        Assert.True(g.Rig.Click(close));
        Assert.False(g.Examine.IsOpen);
        Assert.Equal(0u, g.Examine.ShownGuid); // the floaty hid itself and the message went on to the examination half, which stopped listening
        Assert.Equal(ExamineKind.None, g.Examine.Kind);
    }

    [RequiresLocalDatsFact]
    public void The_floaty_is_retails_class_unlocked_with_its_bars_shown_and_kept_inside_the_screen()
    {
        // gmFloatyExaminationUI (0x1000004C, M5 7 S4): its own runtime, no generic stand-in; PostInit's window id 7 and UpdateLockedStatus' unlocked look (no UI lock in Lifestone); MoveTo keeps it inside its parent.
        using var g = Enter();
        var floaty = Assert.IsType<UiFloatyExamination>(g.Examine.Window);
        Assert.False(floaty.IsStandIn);
        Assert.DoesNotContain(g.Rig.Log.Lines, static l => l.Contains("TODO(retailui-runtime): class 0x1000004C", StringComparison.Ordinal));
        Assert.Equal(7u, floaty.WindowKind);
        Assert.False(floaty.Locked);
        foreach (var id in UiFloatyExamination.BorderIds)
        {
            Assert.True(floaty.GetChildRecursive(id)!.Visible, $"border 0x{id:X8}");
        }

        foreach (var id in UiFloatyExamination.LockedBorderIds)
        {
            Assert.False(floaty.GetChildRecursive(id)!.Visible, $"locked border 0x{id:X8}");
        }

        floaty.UpdateLockedStatus(locked: true);
        Assert.True(floaty.GetChildRecursive(UiFloatyExamination.LockedBorderIds[0])!.Visible);
        Assert.False(floaty.GetChildRecursive(UiFloatyExamination.BorderIds[0])!.Visible);
        floaty.UpdateLockedStatus(locked: false);

        var parent = floaty.Parent!.Box;
        floaty.MoveTo(parent.Width + 500, parent.Height + 500);
        Assert.Equal((parent.Width - floaty.Box.Width, parent.Height - floaty.Box.Height), (floaty.Box.X0, floaty.Box.Y0));
        floaty.MoveTo(-40, -7);
        Assert.Equal((0, 0), (floaty.Box.X0, floaty.Box.Y0));
        floaty.MoveTo(180, 128);
        Assert.Equal((180, 128), (floaty.Box.X0, floaty.Box.Y0));
    }

    [RequiresLocalDatsFact]
    public void A_creature_answer_switches_to_the_creature_window_with_the_level_and_vitals()
    {
        using var g = Enter();
        g.Examine.Open(Npc);
        g.Answer(Npc, CreatureAnswer(success: true));
        var examine = g.Examine;
        Assert.Equal(ExamineKind.Creature, examine.Kind);
        Assert.Equal("Thelnoth Cort the Healer", examine.Title);
        Assert.Equal("8", examine.LevelText);
        Assert.Equal("Health: 55/60 (92 %)", examine.VitalLines[0]);
        var level = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.LevelTextId));
        Assert.Equal("8", level.Text);
        Assert.True(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CreatureFieldId)!.Visible);
        Assert.False(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId)!.Visible);

        // M5 7 D137 (lane L3 wired): the nine stat rows are drawn from the list's template, label and value, in retail's order; the creature sub-panel shows, the character one not.
        var stats = Assert.IsAssignableFrom<UiListBox>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.StatsListId));
        Assert.Equal(9, stats.Items.Count);
        string Label(int i) => Assert.IsAssignableFrom<UiTextElement>(stats.Items[i].GetChildRecursive(GameExaminePanel.RowLabelId)).Text;
        string Value(int i) => Assert.IsAssignableFrom<UiTextElement>(stats.Items[i].GetChildRecursive(GameExaminePanel.RowValueId)).Text;
        Assert.Equal(["Strength", "Endurance", "Coordination", "Quickness", "Focus", "Self", "Health", "Stamina", "Mana"], Enumerable.Range(0, 9).Select(Label));
        Assert.Equal("20", Value(0));
        Assert.Equal("55/60 (92 %)", Value(6));
        Assert.True(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CreaturePanelId)!.Visible);
        Assert.False(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CharacterPanelId)!.Visible);
        Assert.Equal(ExamineCreatureKind.Creature, examine.CreaturePage!.Kind);
        Assert.DoesNotContain(g.Rig.Log.Lines, static l => l.Contains("TODO(m5-examine-name-tables)", StringComparison.Ordinal)); // the dats' tables are wired: no placeholder name

        // A failed roll on the same creature: the values read "???" and the health its percentage, in colour 3 (L3's one source for the vitals).
        g.Examine.Open(Npc);
        g.Answer(Npc, new AceAppraisal { Success = false, Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 8 }, Creature = new AceCreatureProfile(37, 50) });
        Assert.Equal("???", Value(0));
        Assert.Equal("74 %", Value(6));
        Assert.Equal("???", Value(7));
        Assert.All(examine.CreaturePage!.Stats, static r => Assert.Equal(ExamineColor.Unassessed, r.Color));
        Assert.Equal(["Health: 74 %", "Stamina: ???", "Mana: ???"], examine.VitalLines);
        examine.Close();
    }

    [RequiresLocalDatsFact]
    public void The_creature_capture_asks_each_creature_the_fight_aims_at_retries_a_failed_roll_after_ACEs_five_seconds_and_stops_at_its_limit_D137()
    {
        // --examine-capture-creatures (the failed-roll live capture): the toolbar Inspect's path (UiTargetMode.ExamineObject), one Identify an ask, nothing waits.
        using var g = Enter();
        var log = new RecordingLogSink();
        using var capture = new ExamineCreatureCapture(g.Rig.Session, g.Rig.Manager.TargetMode, log, maxAnswers: 2);
        var actions = g.Rig.Session.Actions;

        capture.Step();
        Assert.Equal(0, capture.Asks); // nothing aimed at yet

        actions.Cast(27, Kit, ActionSource.Automation); // an item is no creature: not asked
        capture.Step();
        Assert.Equal(0, capture.Asks);

        actions.Cast(27, Npc, ActionSource.Automation);
        Assert.Equal(Npc, actions.LastAttackTarget);
        capture.Step();
        Assert.Equal(1, capture.Asks);
        Assert.True(g.Examine.IsOpen);
        Assert.Equal(Npc, g.Examine.ShownGuid);
        capture.Step();
        Assert.Equal(1, capture.Asks); // one ask until its answer

        Assert.Null(g.Examine.CaptureKind()); // pending: no proof frame yet
        g.Answer(Npc, new AceAppraisal { Success = false, Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 8 }, Creature = new AceCreatureProfile(37, 50) });
        Assert.Equal((1, 1), (capture.Answers, capture.Failed));
        Assert.Equal(GameExaminePanel.CaptureFailedRoll, g.Examine.CaptureKind()); // D138: the failed roll's frame
        Assert.Contains("Health 74 % (c3)", g.Examine.CaptureText(), StringComparison.Ordinal);
        Assert.Contains(log.Lines, static l => l.Contains("examine capture answer", StringComparison.Ordinal) && l.Contains("success=False", StringComparison.Ordinal));
        g.Rig.Run(2.0);
        capture.Step();
        Assert.Equal(1, capture.Asks); // ACE answers a repeat within 5 s as failed without a roll: wait
        g.Rig.Run(ExamineCreatureCapture.RetrySeconds);
        capture.Step();
        Assert.Equal(2, capture.Asks);

        g.Answer(Npc, CreatureAnswer(success: true));
        Assert.Equal((2, 1), (capture.Answers, capture.Failed));
        Assert.Equal(GameExaminePanel.CaptureCreature, g.Examine.CaptureKind()); // D138: the full page's frame
        Assert.Contains("level=8", g.Examine.CaptureText(), StringComparison.Ordinal);
        Assert.False(capture.Active); // the limit
        Assert.Contains(g.Rig.Log.Lines, static l => l.Contains("examine answer guid=", StringComparison.Ordinal) && l.Contains("success=True", StringComparison.Ordinal));
        Assert.Contains(g.Rig.Log.Lines, static l => l.Contains("examine shown guid=", StringComparison.Ordinal));

        // An item: a plain kit gives no proof frame; one with a set line does (the item-extras frame).
        g.Examine.Open(Kit);
        g.Answer(Kit, KitAnswer());
        Assert.Null(g.Examine.CaptureKind());
        g.Examine.Open(Mace);
        g.Answer(Mace, MaceAnswer() with { Ints = new Dictionary<uint, int>(MaceAnswer().Ints) { [265] = 13 } });
        Assert.Equal(GameExaminePanel.CaptureItemExtras, g.Examine.CaptureKind());
        Assert.Contains("Set: Soldier's", g.Examine.CaptureText(), StringComparison.Ordinal);
        g.Examine.Close();
        Assert.Null(g.Examine.CaptureKind());
    }

    [RequiresLocalDatsFact]
    public void The_figures_body_part_click_examines_the_worn_item_over_that_part_or_the_player()
    {
        using var g = Enter();
        g.Wield(Cuirass, 0x200);
        g.Frames(2);
        Assert.True(g.Doll.ExamineMask(514, g.Examine)); // the chest
        Assert.Equal(Cuirass, g.Examine.ShownGuid);
        Assert.Equal(ActionSource.Ui, g.Examine.LastIdentify!.Source);
        Assert.True(g.Doll.ExamineMask(1, g.Examine)); // the head: nothing there, so the player
        Assert.Equal(Player, g.Examine.ShownGuid);
        Assert.Equal(2, g.Examine.Opens);
        Assert.False(g.Doll.ExamineMask(0, g.Examine)); // no colour
        Assert.Equal(2, g.Examine.Opens);
    }

    [RequiresLocalDatsFact]
    public void The_click_map_image_exists_in_the_dats_and_holds_the_nine_hit_test_colours()
    {
        using var g = Enter();
        Assert.True(fixture.Dats.EnumMaps.TryResolve(EnumGroup.UiAsset, GamePaperDoll.ClickMapEnum, out var did)); // the same lookup Cg3DViewData.UiAssetDid makes
        Assert.NotEqual(0u, did);
        var images = g.Rig.Manager.Services.Images!;
        UiImage? image = null;
        for (var i = 0; i < 200 && images.TryGetImage(did, out image) != UiImageLookup.Ready; i++)
        {
            Thread.Sleep(10);
        }

        Assert.NotNull(image);
        var found = new HashSet<uint>();
        for (var p = 0; p < image!.Pixels.Length; p += 4)
        {
            var mask = GamePaperDoll.MaskOfColor(image.Pixels[p], image.Pixels[p + 1], image.Pixels[p + 2]);
            if (mask != 0)
            {
                found.Add(mask);
            }
        }

        Assert.Equal(9, found.Count);
    }
}
