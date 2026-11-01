using Lifestone.Client.RetailUi.Panels;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The examine window's set line (M5 7 polish, D137; <see cref="ExamineSetLines"/>): retail's names by set id from <c>Appraisal_ShowSet</c>'s table (11.6096 0x004AEC00, its jump table at 0x004AF204), the ids it leaves without a line,
/// and the addition's pieces worn and spells by tier from the SpellTable's sets, over hand-made tiers and, in the dat test, the user's portal (skipped without the dats).
/// </summary>
public sealed class ExamineSetLinesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static SpellInfo Spell(uint id, string name) => new()
    {
        Id = id, Name = name, Description = string.Empty, School = default, IconId = 0, Category = 0, Flags = default, BaseMana = 0, BaseRangeConstant = 0, BaseRangeMod = 0, Power = 0, SpellEconomyMod = 0,
        FormulaVersion = 0, ComponentLoss = 0, MetaSpellType = default, MetaSpellId = 0, EncryptedFormula = [], Formula = [], CasterEffect = default, TargetEffect = default, FizzleEffect = default,
        RecoveryInterval = 0, RecoveryAmount = 0, DisplayOrder = 0, NonComponentTargetType = 0, ManaMod = 0,
    };

    private static SpellInfo? Names(uint id) => id switch
    {
        1 => Spell(1, "Initiate's Blessing"),
        2 => Spell(2, "Adept's Blessing"),
        3 => Spell(3, "Knight's Blessing"),
        4 => Spell(4, "Minor Focus"),
        _ => null,
    };

    /// <summary>A set like Dedication (30): tiers at 2, 4 and 6 pieces, with gaps.</summary>
    private static IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>> Sets() => new Dictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>>
    {
        [30] = new Dictionary<uint, IReadOnlyList<uint>> { [6] = [3, 4], [2] = [1], [4] = [2] },
    };

    // ------------------------------------------------------------------ retail: the set line

    [Theory]
    [InlineData(4, "Carraida's Benediction")]
    [InlineData(13, "Soldier's")]
    [InlineData(14, "Adept's")]
    [InlineData(26, "Flame Proof")]
    [InlineData(30, "Dedication")]
    [InlineData(35, "Sigil of Defense")]
    [InlineData(41, "Heroic Destroyer")]
    [InlineData(49, "Weave of Alchemy")]
    [InlineData(83, "Weave of Assess Creature")]
    [InlineData(89, "Shou-jen Shozoku")]
    [InlineData(105, "Minor Shivering Clouded Spirit")]
    [InlineData(118, "Blackfire Stinging Shrouded Soul")]
    [InlineData(130, "Shimmering Shadows")]
    public void Retails_table_names_the_set(int id, string name) => Assert.Equal(name, ExamineSetLines.SetName(id));

    [Theory]
    [InlineData(53)]
    [InlineData(67)]
    [InlineData(74)]
    [InlineData(75)]
    [InlineData(79)]
    public void The_light_weapon_cloak_ids_share_one_name(int id) => Assert.Equal("Weave of Light Weapons", ExamineSetLines.SetName(id));

    [Theory]
    [InlineData(54)]
    [InlineData(57)]
    [InlineData(77)]
    public void The_missile_cloak_ids_share_one_name(int id) => Assert.Equal("Weave of Missile Weapons", ExamineSetLines.SetName(id));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(34)]
    [InlineData(42)]
    [InlineData(48)]
    [InlineData(131)]
    [InlineData(140)]
    [InlineData(-5)]
    public void An_id_retails_table_falls_through_has_no_name(int id) => Assert.Null(ExamineSetLines.SetName(id));

    [Fact]
    public void The_table_covers_ids_4_to_130()
    {
        Assert.Equal(4, ExamineSetLines.FirstNamedSet);
        Assert.Equal(130, ExamineSetLines.LastNamedSet);
    }

    [Fact]
    public void The_set_line_is_Set_and_the_name()
    {
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 16 });
        Assert.Equal("Set: Defender's", ExamineSetLines.SetLine(a));
        Assert.Null(ExamineSetLines.SetLine(ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [19] = 100 })));
    }

    // ------------------------------------------------------------------ the addition: pieces worn and the set's spells

    [Fact]
    public void The_count_is_the_worn_items_of_the_same_set_and_unappraised_ones_are_unknown()
    {
        Assert.Equal((3, 0), ExamineSetLines.CountWorn(14, [14, 14, 13, 0, 14]));
        Assert.Equal((1, 2), ExamineSetLines.CountWorn(14, [null, 14, null, 15]));
        Assert.Equal((0, 0), ExamineSetLines.CountWorn(14, []));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, 2u)]
    [InlineData(3, 2u)]
    [InlineData(4, 4u)]
    [InlineData(5, 4u)]
    [InlineData(6, 6u)]
    [InlineData(9, 6u)]
    public void The_tier_in_force_is_the_highest_at_or_below_the_count(int count, uint? tier) => Assert.Equal(tier, ExamineSetLines.ActiveTier(Sets()[30], count));

    [Fact]
    public void Worn_lines_give_the_count_and_each_tier_in_order_with_the_one_in_force_marked()
    {
        var log = new RecordingLogSink();
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 30 });
        Assert.Equal(
            ["Pieces worn: 5", "2 pieces: Initiate's Blessing", "4 pieces: Adept's Blessing (active)", "6 pieces: Knight's Blessing, Minor Focus"],
            ExamineSetLines.WornLines(a, 5, Sets(), Names, log));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void Below_the_lowest_tier_no_tier_is_marked()
    {
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 30 });
        var lines = ExamineSetLines.WornLines(a, 1, Sets(), Names, new RecordingLogSink());
        Assert.Equal("Pieces worn: 1", lines[0]);
        Assert.DoesNotContain(lines, l => l.EndsWith(" (active)", StringComparison.Ordinal));
    }

    [Fact]
    public void Unappraised_worn_items_make_the_count_a_floor_with_a_named_todo_and_no_mark()
    {
        var log = new RecordingLogSink();
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 30 });
        var lines = ExamineSetLines.WornLines(a, 2, Sets(), Names, log, unknownPieces: 1);
        Assert.Equal("Pieces worn: 2 or more", lines[0]);
        Assert.DoesNotContain(lines, l => l.EndsWith(" (active)", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-set-unappraised)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_levelled_set_lists_its_tiers_by_level_without_a_count_and_names_its_todo()
    {
        var log = new RecordingLogSink();
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 30, [ExamineSetLines.ItemXpStyle] = 1 });
        Assert.Equal(
            ["Level 2: Initiate's Blessing", "Level 4: Adept's Blessing", "Level 6: Knight's Blessing, Minor Focus"],
            ExamineSetLines.WornLines(a, 5, Sets(), Names, log));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-set-levels)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spell_the_table_lacks_shows_its_number_with_a_named_todo()
    {
        var log = new RecordingLogSink();
        var sets = new Dictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>> { [13] = new Dictionary<uint, IReadOnlyList<uint>> { [1] = [1, 999] } };
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 13 });
        Assert.Equal(["Pieces worn: 1", "1 piece: Initiate's Blessing, Spell 999 (active)"], ExamineSetLines.WornLines(a, 1, sets, Names, log));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-spell-name)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_set_the_spell_table_lacks_gives_the_count_alone_with_a_named_todo()
    {
        var log = new RecordingLogSink();
        var a = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 14 });
        Assert.Equal(["Pieces worn: 2"], ExamineSetLines.WornLines(a, 2, Sets(), Names, log));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m5-examine-set-table)", StringComparison.Ordinal));
    }

    [Fact]
    public void No_worn_lines_without_a_set_retail_names()
    {
        var log = new RecordingLogSink();
        Assert.Empty(ExamineSetLines.WornLines(ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [19] = 1 }), 3, Sets(), Names, log));
        Assert.Empty(ExamineSetLines.WornLines(ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 140 }), 3, Sets(), Names, log));
        Assert.Empty(log.Lines);
    }

    // ------------------------------------------------------------------ the dats

    [RequiresDatsFact]
    public void The_portals_set_table_has_every_set_retail_names_and_gives_real_tiers()
    {
        var table = SpellTableData.Load(fixture.Dats);
        for (var id = ExamineSetLines.FirstNamedSet; id <= ExamineSetLines.LastNamedSet; id++)
        {
            if (ExamineSetLines.SetName(id) is null)
            {
                continue;
            }

            Assert.True(table.SpellSets.TryGetValue((uint)id, out var tiers), $"set {id} ({ExamineSetLines.SetName(id)}) is not in the SpellTable");
            Assert.All(tiers.Values.SelectMany(static s => s), s => Assert.True(table.Spells.ContainsKey(s), $"set {id}: spell {s}"));
        }

        SpellInfo? Lookup(uint id) => table.Spells.TryGetValue(id, out var info) ? info : null;
        var log = new RecordingLogSink();

        // Dedication (30), the society armour: tiers at 2, 4, 6, 8 and 9 pieces; five worn use the 4-piece tier.
        var dedication = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 30 });
        Assert.Equal(
            ["Pieces worn: 5", "2 pieces: Society Initiate's Blessing", "4 pieces: Society Adept's Blessing (active)", "6 pieces: Society Knight's Blessing", "8 pieces: Society Lord's Blessing", "9 pieces: Society Master's Blessing"],
            ExamineSetLines.WornLines(dedication, 5, table.SpellSets, Lookup, log));

        // Archer's (15): three worn are the Apprentice tier.
        var archers = ExamineRatingLinesTests.Answered(new Dictionary<uint, int> { [ExamineSetLines.EquipmentSetId] = 15 });
        var lines = ExamineSetLines.WornLines(archers, 3, table.SpellSets, Lookup, log);
        Assert.Equal("Pieces worn: 3", lines[0]);
        Assert.Equal("2 pieces: Novice Archer's Missile Weapon Aptitude", lines[1]);
        Assert.Equal("3 pieces: Apprentice Archer's Missile Weapon Aptitude (active)", lines[2]);
        Assert.Single(lines, static l => l.EndsWith(" (active)", StringComparison.Ordinal));
        Assert.Empty(log.Lines);
    }
}
