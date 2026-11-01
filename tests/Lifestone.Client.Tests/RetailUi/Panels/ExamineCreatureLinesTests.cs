using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The creature window's lines (M5 7, D137): <see cref="ExamineCreatureLines"/> from appraisals laid out as the bundled ACE writes them (a monster, a failed roll with the health-only profile ACE sends, a player), and each line kind
/// alone. The appraisals are scripted 00C9 answers through the reducer; no creature is spawned.
/// </summary>
public sealed class ExamineCreatureLinesTests
{
    private const uint Monster = 0x80000B01;
    private const uint Person = 0x50000B02;

    /// <summary>Stand-in tables shaped like the dats' (enum names as the maps spell them, a title token and its string).</summary>
    private sealed class FakeNames : IExamineNames
    {
        public List<(uint Map, int Value)> Asked { get; } = [];

        public string? EnumName(uint mapId, int value)
        {
            Asked.Add((mapId, value));
            return (mapId, value) switch
            {
                (0x10000001, 1) => "Male",
                (0x10000002, 1) => "Aluvian",
                (0x10000002, 10) => "Penumbraen",
                (0x10000005, 13) => "Olthoi_Soldier",
                (0x10000005, 31) => "Human",
                (0x10000006, 1) => "ID_CharacterTitle_Adventurer",
                _ => null,
            };
        }

        public string? StringText(uint tableEnum, string token) =>
            tableEnum == ExamineCreatureLines.CharacterTitleStringTable && token == "ID_CharacterTitle_Adventurer" ? "Adventurer" : null;
    }

    private static ExamineCreatureContext Context(uint flags = 0, int viewer = 0, IExamineNames? names = null, bool noNames = false) =>
        new("Lifestone Tester", flags, viewer, noNames ? null : names ?? new FakeNames());

    private static Appraisal Answered(AceAppraisal info, uint guid)
    {
        var r = new WorldActionRig();
        r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, info));
        Assert.True(r.H.State.Appraisals.TryGet(guid, out var appraisal));
        return appraisal;
    }

    private static Appraisal Creature(bool success = true, Dictionary<uint, int>? ints = null, AceCreatureProfile? profile = null, Dictionary<uint, string>? strings = null, uint[]? armor = null)
    {
        var all = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 30, [ExamineCreatureLines.CreatureTypeId] = 13 };
        foreach (var (k, v) in ints ?? [])
        {
            all[k] = v;
        }

        return Answered(
            new AceAppraisal
            {
                Success = success,
                Ints = all,
                Strings = strings ?? new Dictionary<uint, string> { [AppraisalIds.Name] = "Olthoi Soldier" },
                Creature = profile ?? new AceCreatureProfile(250, 300) { Attributes = [200, 180, 150, 160, 50, 40, 300, 20, 360, 40] },
                ArmorLevels = armor,
            },
            Monster);
    }

    // ------------------------------------------------------------------ the three cases

    [Fact]
    public void A_monster_shows_its_level_type_attributes_vitals_with_buffs_and_its_ratings_in_the_creature_order()
    {
        var log = new RecordingLogSink();
        var a = Creature(
            ints: new() { [307] = 10, [314] = 5, [308] = 4, [386] = 15, [387] = 3, [381] = 2, [350] = 7 },
            profile: new AceCreatureProfile(250, 300) { Attributes = [200, 180, 150, 160, 50, 40, 300, 20, 360, 40], Buffs = (0x1 | 0x20 | 0x40, 0x1 | 0x40) });

        var page = ExamineCreatureLines.Build(a, Context(), log)!;

        Assert.Equal(ExamineCreatureKind.Creature, page.Kind);
        Assert.Equal("30", page.Level);
        Assert.Equal("Olthoi Soldier", page.CreatureTypeName);
        Assert.Null(page.FullName);
        Assert.Equal(string.Empty, page.AllegianceNameText);
        Assert.Equal(
            [
                new ExamineRow("Strength", "200", ExamineColor.Raised), new ExamineRow("Endurance", "180", ExamineColor.Normal), new ExamineRow("Coordination", "160", ExamineColor.Normal),
                new ExamineRow("Quickness", "150", ExamineColor.Normal), new ExamineRow("Focus", "50", ExamineColor.Normal), new ExamineRow("Self", "40", ExamineColor.Lowered),
                new ExamineRow("Health", "250/300 (83 %)", ExamineColor.Raised), new ExamineRow("Stamina", "300/360", ExamineColor.Normal), new ExamineRow("Mana", "20/40", ExamineColor.Normal),
            ],
            page.Stats);
        Assert.Equal(
            [
                ExamineRow.Blank,
                new ExamineRow("Dmg/CritDmg", "Rating: 10/5", 0), new ExamineRow("Dmg/CritDmg", "Resist: 4/0", 0), new ExamineRow("Overpower %", "+15/-3", 0),
                new ExamineRow("PK Dmg/Res", "Rating: 2/0", 0), new ExamineRow("DoT/Life:", "Resist: 7/0", 0),
                ExamineRow.Blank,
            ],
            page.Info);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void A_failed_roll_shows_the_level_question_marks_and_the_health_percent_in_the_unassessed_colour_and_no_ratings()
    {
        // ACE: a failed roll still sends the profile, health only (flags 0: CreatureProfile.cs:47-55), the full tables, no ratings (AppraiseInfo.cs:594-597) and no armour levels.
        var a = Creature(success: false, profile: new AceCreatureProfile(250, 300));

        var page = ExamineCreatureLines.Build(a, Context(), new RecordingLogSink())!;

        Assert.False(a.Success);
        Assert.Equal("30", page.Level);
        Assert.Equal(
            [
                new ExamineRow("Strength", "???", 3), new ExamineRow("Endurance", "???", 3), new ExamineRow("Coordination", "???", 3), new ExamineRow("Quickness", "???", 3), new ExamineRow("Focus", "???", 3),
                new ExamineRow("Self", "???", 3), new ExamineRow("Health", "83 %", 3), new ExamineRow("Stamina", "???", 3), new ExamineRow("Mana", "???", 3),
            ],
            page.Stats);
        Assert.Empty(page.Info);
        Assert.Equal("Olthoi Soldier", page.CreatureTypeName);
    }

    [Fact]
    public void A_player_shows_the_character_texts_and_rows_in_retail_order_ending_with_the_unenchantable_key()
    {
        var log = new RecordingLogSink();
        var a = Answered(
            new AceAppraisal
            {
                Success = true,
                Ints = new Dictionary<uint, int>
                {
                    [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 126, [ExamineCreatureLines.CharacterTitleId] = 1, [ExamineCreatureLines.GenderId] = 1, [ExamineCreatureLines.HeritageId] = 1,
                    [ExamineCreatureLines.FactionBitsId] = 2, [288] = 450, [ExamineCreatureLines.AllegianceRankId] = 3, [307] = 6, [381] = 4, [386] = 9,
                    [ExamineCreatureLines.AgeId] = 93_784, [ExamineCreatureLines.DeathsId] = 0, [ExamineCreatureLines.TitlesEarnedId] = 12, [ExamineCreatureLines.EnlightenmentId] = 1,
                },
                Strings = new Dictionary<uint, string>
                {
                    [AppraisalIds.Name] = "Lifestone Tester", [ExamineCreatureLines.AllegianceNameId] = "Order of the Stone", [ExamineCreatureLines.MonarchId] = "King Ardent", [ExamineCreatureLines.PatronId] = "Thane Brisk",
                    [ExamineCreatureLines.FellowshipNameId] = "Den Crawlers", [ExamineCreatureLines.DateOfBirthId] = "10/1/2026",
                },
                Creature = new AceCreatureProfile(400, 400) { Attributes = [100, 100, 100, 100, 200, 250, 300, 600, 300, 600] },
                ArmorLevels = [120, 300, 300, 0, 0, 10_099, 0, 0, 0],
            },
            Person);

        var page = ExamineCreatureLines.Build(a, Context(viewer: 2), log)!;

        Assert.Equal(ExamineCreatureKind.Character, page.Kind);
        Assert.Equal("126", page.Level);
        Assert.Equal("Baron Lifestone Tester", page.FullName);
        Assert.Equal("Male Aluvian", page.HeritageText);
        Assert.Equal("Adventurer", page.ProfessionText);
        Assert.Equal("Non-Player Killer", page.PlayerKillerText);
        Assert.Equal("Order of the Stone", page.AllegianceNameText);
        Assert.Null(page.CreatureTypeName);
        Assert.Equal(new ExamineRow("Health", "400/400 (100 %)", 0), page.Stats[6]);
        Assert.Equal(
            [
                new ExamineRow("Society:", "Eldrytch Web ~ Knight", ExamineColor.Raised),
                new ExamineRow("Monarch:", "King Ardent", 0), new ExamineRow("Patron:", "Thane Brisk", 0),
                ExamineRow.Blank, new ExamineRow("Head/Chest/Groin", "AL: 120/300/300", 0), new ExamineRow("Bicep/Wrist/Hand", "AL: 0/0/*100", 0), new ExamineRow("Thigh/Shin/Foot", "AL: 0/0/0", 0),
                ExamineRow.Blank, new ExamineRow("Dmg/CritDmg", "Rating: 6/0", 0), new ExamineRow("PK Dmg/Res", "Rating: 4/0", 0), new ExamineRow("Overpower %", "+9/-0", 0), ExamineRow.Blank,
                new ExamineRow("Fellowship:", "Den Crawlers", 0), new ExamineRow("Arrived in Dereth:", "10/1/2026", 0), new ExamineRow("Time in Dereth:", "1d 2h 3m 4s", 0),
                new ExamineRow("Deaths:", "Has never died", 0), new ExamineRow("Titles Earned:", "12", 0), new ExamineRow("Enlightenment:", "1", 0),
                new ExamineRow("* = Unenchantable", string.Empty, 0),
            ],
            page.Info);
        Assert.Empty(log.Lines);
    }

    // ------------------------------------------------------------------ the window and the level

    [Fact]
    public void The_window_is_picked_by_the_creature_profile_then_the_template_or_title_id()
    {
        Assert.Equal(ExamineCreatureKind.Creature, ExamineCreatureLines.KindOf(Creature()));
        Assert.Equal(ExamineCreatureKind.Character, ExamineCreatureLines.KindOf(Creature(strings: new() { [AppraisalIds.Name] = "Someone", [ExamineCreatureLines.TemplateId] = "Adventurer" })));
        var item = Answered(new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x1 } }, Monster);
        Assert.Null(ExamineCreatureLines.KindOf(item));
        Assert.Null(ExamineCreatureLines.Build(item, Context(), new RecordingLogSink()));
    }

    [Theory]
    [InlineData(30, "30")]
    [InlineData(0, "???")]
    [InlineData(-4, "???")]
    public void The_level_is_the_number_or_question_marks(int level, string expected) =>
        Assert.Equal(expected, ExamineCreatureLines.LevelText(Creature(ints: new() { [AppraisalIds.Level] = level })));

    // ------------------------------------------------------------------ the attribute list

    [Fact]
    public void A_zero_attribute_or_vital_reads_question_marks_and_the_health_percent_rounds_like_MulDiv()
    {
        var rows = ExamineCreatureLines.StatRows(Creature(profile: new AceCreatureProfile(1, 200) { Attributes = [0, 10, 10, 10, 10, 10, 0, 5, 9, 10] }));
        Assert.Equal("???", rows[0].Value);
        Assert.Equal("1/200 (1 %)", rows[6].Value); // 0.5 rounds away from 0
        Assert.Equal("???", rows[7].Value); // current stamina 0
        Assert.Equal("5/10", rows[8].Value);
    }

    [Fact]
    public void A_failed_roll_at_a_third_of_health_rounds_down()
    {
        var rows = ExamineCreatureLines.StatRows(Creature(success: false, profile: new AceCreatureProfile(1, 3)));
        Assert.Equal(new ExamineRow("Health", "33 %", ExamineColor.Unassessed), rows[6]);
    }

    // ------------------------------------------------------------------ the rating rows

    [Fact]
    public void No_rating_above_zero_gives_no_rows_and_a_crit_rating_alone_shows_the_damage_row()
    {
        Assert.Empty(ExamineCreatureLines.RatingRows(Creature(), character: false));
        Assert.Equal(
            [ExamineRow.Blank, new ExamineRow("Dmg/CritDmg", "Rating: 0/0", 0), ExamineRow.Blank],
            ExamineCreatureLines.RatingRows(Creature(ints: new() { [313] = 3 }), character: false));
        Assert.Equal(
            [ExamineRow.Blank, new ExamineRow("DoT/Life:", "Resist: 0/8", 0), ExamineRow.Blank],
            ExamineCreatureLines.RatingRows(Creature(ints: new() { [351] = 8 }), character: true));
    }

    // ------------------------------------------------------------------ the armour rows

    [Fact]
    public void Armour_rows_need_a_level_above_zero()
    {
        Assert.Empty(ExamineCreatureLines.ArmorRows(Creature()));
        Assert.Empty(ExamineCreatureLines.ArmorRows(Creature(armor: new uint[9])));
        Assert.Equal(4, ExamineCreatureLines.ArmorRows(Creature(armor: [0, 0, 0, 0, 0, 0, 0, 0, 9_999])).Count);
        Assert.Equal("AL: 0/0/*0", ExamineCreatureLines.ArmorRows(Creature(armor: [0, 0, 0, 0, 0, 0, 0, 0, 9_999]))[3].Value);
    }

    // ------------------------------------------------------------------ the society and allegiance rows

    [Theory]
    [InlineData(1, 287, 50, 1, "Celestial Hand ~ Initiate", ExamineColor.Raised)]
    [InlineData(1, 287, 101, 4, "Celestial Hand ~ Adept", ExamineColor.Lowered)]
    [InlineData(4, 289, 1000, 0, "Radiant Blood ~ Lord", ExamineColor.Normal)]
    [InlineData(4, 289, 1500, 2, "Radiant Blood ~ Master", ExamineColor.Lowered)]
    [InlineData(2, 288, 1501, 2, "Eldrytch Web", ExamineColor.Raised)]
    [InlineData(8, 288, 50, 7, "???", ExamineColor.Normal)]
    public void The_society_row_names_the_society_its_rank_word_and_the_viewers_standing(int bits, int rankId, int rank, int viewer, string expected, int color)
    {
        var row = ExamineCreatureLines.SocietyRow(Creature(ints: new() { [ExamineCreatureLines.FactionBitsId] = bits, [(uint)rankId] = rank }), viewer);
        Assert.Equal(new ExamineRow("Society:", expected, color), row);
    }

    [Fact]
    public void A_viewer_without_a_society_reads_zero_and_draws_the_row_plain()
    {
        Assert.Equal(0, ExamineCreatureLines.ViewerFactionBitsOf(null));
        Assert.Equal(new ExamineRow("Society:", "Celestial Hand", 0), ExamineCreatureLines.SocietyRow(Creature(ints: new() { [ExamineCreatureLines.FactionBitsId] = 1 }), 0));
        Assert.Null(ExamineCreatureLines.SocietyRow(Creature(), 1));
    }

    [Fact]
    public void The_allegiance_rows_show_a_monarchs_followers_a_shared_monarch_and_patron_or_a_monarch_alone()
    {
        Assert.Equal([new ExamineRow("Alleg. Monarch:", "1 Follower", 0)], ExamineCreatureLines.AllegianceRows(Creature(ints: new() { [ExamineCreatureLines.AllegianceFollowersId] = 1 })));
        Assert.Equal([new ExamineRow("Alleg. Monarch:", "0 Followers", 0)], ExamineCreatureLines.AllegianceRows(Creature()));
        Assert.Equal(
            [new ExamineRow("Monarch/Patron:", "King Ardent", 0)],
            ExamineCreatureLines.AllegianceRows(Creature(strings: new() { [ExamineCreatureLines.MonarchId] = "King Ardent", [ExamineCreatureLines.PatronId] = "King Ardent" })));
        Assert.Equal([new ExamineRow("Monarch:", "King Ardent", 0)], ExamineCreatureLines.AllegianceRows(Creature(strings: new() { [ExamineCreatureLines.MonarchId] = "King Ardent" })));
    }

    // ------------------------------------------------------------------ the character rows and texts

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(59, "59s")]
    [InlineData(3_600, "1h 0s")]
    [InlineData(2_592_000 + 86_400 + 61, "1mo 1d 1m 1s")]
    public void The_time_in_Dereth_drops_zero_parts_but_the_seconds(int seconds, string expected) => Assert.Equal(expected, ExamineCreatureLines.DeltaTimeText(seconds));

    [Fact]
    public void Deaths_chess_and_fishing_rows_show_their_numbers()
    {
        var rows = ExamineCreatureLines.CharacterRows(Creature(ints: new() { [ExamineCreatureLines.DeathsId] = 3, [ExamineCreatureLines.ChessRankId] = 1400, [ExamineCreatureLines.FishingSkillId] = 25 }));
        Assert.Equal([new ExamineRow("Chess Rank:", "1400", 0), new ExamineRow("Fishing Skill:", "25", 0), new ExamineRow("Deaths:", "3", 0)], rows);
    }

    [Theory]
    [InlineData(0x20u, "Player Killer")]
    [InlineData(0x02000000u, "Player Killer Lite")]
    [InlineData(0x02000020u, "Player Killer")]
    [InlineData(0x8u, "Non-Player Killer")]
    public void The_player_killer_text_follows_the_objects_description_bits(uint flags, string expected) => Assert.Equal(expected, ExamineCreatureLines.PlayerKillerText(flags));

    [Fact]
    public void A_player_killers_page_reads_the_standing_from_the_context()
    {
        var page = ExamineCreatureLines.Build(Creature(ints: new() { [ExamineCreatureLines.CharacterTitleId] = 1 }), Context(flags: 0x28), new RecordingLogSink())!;
        Assert.Equal("Player Killer", page.PlayerKillerText);
    }

    [Theory]
    [InlineData(3, 1, 1, "Baron")]
    [InlineData(3, 1, 2, "Baroness")]
    [InlineData(10, 2, 2, "Sultana")]
    [InlineData(9, 3, 2, "Jo-ou")]
    [InlineData(1, 4, 2, "Dame")]
    [InlineData(6, 10, 2, "Void Lady")]
    [InlineData(10, 6, 2, "Primus")]
    [InlineData(10, 7, 1, "Tah")]
    [InlineData(10, 8, 1, "Tiatus")]
    [InlineData(9, 9, 2, "Ipharsia")]
    [InlineData(10, 11, 1, "Annointed")]
    public void The_allegiance_title_is_the_clients_table_for_the_heritage_and_gender(int rank, int heritage, int gender, string expected) =>
        Assert.Equal(expected, ExamineCreatureLines.AllegianceTitle(rank, heritage, gender));

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(11, 1, 1)]
    [InlineData(3, 12, 1)]
    [InlineData(3, 1, 0)]
    public void No_title_outside_the_ranks_heritages_and_genders_the_tables_cover(int rank, int heritage, int gender) =>
        Assert.Null(ExamineCreatureLines.AllegianceTitle(rank, heritage, gender));

    [Theory]
    [InlineData(2, "Male Gharu'ndim")]
    [InlineData(5, "Male Umbraen")]
    [InlineData(13, "Male Olthoi")]
    [InlineData(10, "Male Penumbraen")]
    public void Three_heritages_use_the_clients_own_spelling_and_the_rest_the_dat_map(int heritage, string expected)
    {
        var names = new FakeNames();
        var page = ExamineCreatureLines.Build(
            Creature(ints: new() { [ExamineCreatureLines.CharacterTitleId] = 1, [ExamineCreatureLines.GenderId] = 1, [ExamineCreatureLines.HeritageId] = heritage }), Context(names: names), new RecordingLogSink())!;
        Assert.Equal(expected, page.HeritageText);
        Assert.Equal(heritage == 10, names.Asked.Contains((0x10000002, heritage)));
    }

    [Fact]
    public void Without_name_tables_the_names_are_visible_placeholders_with_a_named_todo()
    {
        var log = new RecordingLogSink();
        var player = Creature(ints: new() { [ExamineCreatureLines.CharacterTitleId] = 1, [ExamineCreatureLines.GenderId] = 1, [ExamineCreatureLines.HeritageId] = 1 });

        var page = ExamineCreatureLines.Build(player, Context(noNames: true), log)!;

        Assert.Equal("Gender and heritage: not in Lifestone M5", page.HeritageText);
        Assert.Equal("Character title: not in Lifestone M5", page.ProfessionText);
        Assert.Equal("Creature type: not in Lifestone M5", ExamineCreatureLines.Build(Creature(), Context(noNames: true), log)!.CreatureTypeName);
        Assert.True(log.AnyLineContains("TODO(m5-examine-name-tables)"));
    }

    [RequiresDatsFact]
    public void The_dat_tables_name_a_creature_type_with_spaces_for_underscores()
    {
        using var dats = Lifestone.Data.Dat.DatSet.Open();
        var tables = new ExamineNameTables(new Lifestone.Data.Dat.EnumMapperNames(dats), null);
        var page = ExamineCreatureLines.Build(Creature(ints: new() { [ExamineCreatureLines.CreatureTypeId] = 9 }), Context(names: tables), new RecordingLogSink())!;
        Assert.Equal("Phyntos Wasp", page.CreatureTypeName);
        Assert.Null(tables.StringText(ExamineCreatureLines.CharacterTitleStringTable, "ID_CharacterTitle_Adventurer"));
    }

    [Fact]
    public void Without_a_heritage_the_heritage_text_names_the_creature_type()
    {
        var page = ExamineCreatureLines.Build(
            Creature(ints: new() { [ExamineCreatureLines.CharacterTitleId] = 9, [ExamineCreatureLines.GenderId] = 1, [ExamineCreatureLines.CreatureTypeId] = 31 }, strings: new() { [ExamineCreatureLines.TemplateId] = "Wanderer" }),
            Context(),
            new RecordingLogSink())!;
        Assert.Equal("Male Human", page.HeritageText);
        Assert.Equal("Wanderer", page.ProfessionText); // title 9 is not in the table: retail falls back to PropertyString 5
    }
}
