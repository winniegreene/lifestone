using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The <c>@lifestone</c> request grammar (server/mod/Shared/ToolRequestParser.cs; m2-plan §4.12, SM1): every request
/// <see cref="ToolsWire.Request"/> writes parses back to an equal request, and every malformed one is refused with the
/// right code, its id still echoed.
/// </summary>
public sealed class ToolRequestParserTests
{
    /// <summary>One request of every verb and form, with and without ids, rotations and edge values.</summary>
    internal static readonly ToolRequest[] Requests =
    [
        new StatusRequest(),
        new StatusRequest("a1"),
        new WhereRequest(),
        new WhereRequest("where-7"),
        new NetRequest(),
        new NetRequest("N"),
        new PlacesRequest(),
        new PlacesRequest("0123456789abcdef"),
        new XpRequest(1),
        new XpRequest(1000, "x"),
        new XpRequest(ToolRequestParser.MaxXp, "max"),
        new PlacePoiRequest("Holtburg"),
        new PlacePoiRequest("Bandit Castle", "a2"),
        new PlacePoiRequest("Al-Arqas", "a3"),
        new PlacePoiRequest("Tou-Tou"),
        new PlacePoiRequest("Fort O'Brien 2", "a4"),
        new PlacePoiRequest(new string('n', ToolRequestParser.MaxNameLength)),
        new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f)),
        new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f), "loc1"),
        new PlaceLocRequest(new ToolLocation(0xA9B40001, 0f, 0f, -0.25f, 0.70710677f, 0f, 0f, -0.70710677f), "q"),
        new PlaceLocRequest(new ToolLocation(0x01D90108, -12.000001f, 1234.5679f, 1E-05f, 0.5f, 0.5f, 0.5f, 0.5f)),
        new PlaceLocRequest(new ToolLocation(0x00000040, 191.99998f, 191.99998f, 0f)),
        new PlaceLocRequest(new ToolLocation(0xFEFE0001, 0f, 23.999998f, 300f)),
        new QuestRequest(QuestAction.Stamp, "SentryTaskComplete"),
        new QuestRequest(QuestAction.Stamp, "SentryTaskComplete", "q1"),
        new QuestRequest(QuestAction.Erase, "AcademyTokenGiven", "q2"),
        new QuestRequest(QuestAction.Has, "KillTask-BlightSpirit", "q3"),
        new QuestRequest(QuestAction.Has, "BurFlagged(Gem)"),
        new QuestRequest(QuestAction.Erase, "_a-B_9"),
        new QuestRequest(QuestAction.Stamp, new string('q', ToolRequestParser.MaxQuestNameLength)),
        new CharPurgeRequest("Lifestone Throwaway"),
        new CharPurgeRequest("Lifestone Throwaway", "p1"),
        new CharPurgeRequest("Ann"),
        new CharPurgeRequest("Tou-Tou O'Brien 2", "p2"),
        new CharPurgeRequest(new string('c', ToolRequestParser.MaxCharacterNameLength)),
    ];

    public static TheoryData<ToolRequest> EveryRequest => [.. Requests];

    [Theory]
    [MemberData(nameof(EveryRequest))]
    public void Request_builds_and_parses_back_for_every_verb(ToolRequest request)
    {
        var text = ToolsWire.Request(request);
        Assert.StartsWith("@lifestone ", text, StringComparison.Ordinal);
        Assert.All(text, c => Assert.InRange(c, ' ', '~'));

        var parse = ToolRequestParser.Parse(text["@lifestone ".Length..]);
        Assert.True(parse.Ok, parse.Error?.Message);
        Assert.Equal(request, parse.Request);
        Assert.Equal(request.Verb, parse.Verb);
        Assert.Equal(request.Id, parse.Id);

        Assert.True(ToolsWire.TryParseRequest(text["@lifestone ".Length..], out var again, out var error));
        Assert.Null(error);
        Assert.Equal(request, again);
    }

    [Theory]
    [InlineData("status", "@lifestone status")]
    [InlineData("xp", "@lifestone xp 1000 #a1")]
    [InlineData("poi", "@lifestone place poi \"Holtburg\" #a1")]
    [InlineData("loc", "@lifestone place loc 0xA9B40019 84.5 7.1 94.005")]
    [InlineData("loc-q", "@lifestone place loc 0xA9B40019 84.5 7.1 94.005 0.5 0.5 0.5 0.5 #z")]
    [InlineData("quest-stamp", "@lifestone quest stamp SentryTaskComplete #a1")]
    [InlineData("quest-erase", "@lifestone quest erase SentryTaskComplete")]
    [InlineData("quest-has", "@lifestone quest has BurFlagged(Gem) #h")]
    [InlineData("char-purge", "@lifestone char purge \"Lifestone Throwaway\" #p1")]
    [InlineData("char-purge-one-word", "@lifestone char purge \"Ann\"")]
    public void Requests_have_one_canonical_text(string which, string expected)
    {
        ToolRequest request = which switch
        {
            "status" => new StatusRequest(),
            "xp" => new XpRequest(1000, "a1"),
            "poi" => new PlacePoiRequest("Holtburg", "a1"),
            "quest-stamp" => new QuestRequest(QuestAction.Stamp, "SentryTaskComplete", "a1"),
            "quest-erase" => new QuestRequest(QuestAction.Erase, "SentryTaskComplete"),
            "quest-has" => new QuestRequest(QuestAction.Has, "BurFlagged(Gem)", "h"),
            "char-purge" => new CharPurgeRequest("Lifestone Throwaway", "p1"),
            "char-purge-one-word" => new CharPurgeRequest("Ann"),
            "loc" => new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f)),
            _ => new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f, 0.5f, 0.5f, 0.5f, 0.5f), "z"),
        };
        Assert.Equal(expected, ToolsWire.Request(request));
    }

    [Theory]
    [InlineData("STATUS", "status")]
    [InlineData("Where", "where")]
    [InlineData("nEt", "net")]
    [InlineData("PLACES", "places")]
    [InlineData("XP 5", "xp")]
    [InlineData("PLACE POI Holtburg", "place")]
    [InlineData("Place Loc 0xa9b40019 84.5 7.1 94", "place")]
    [InlineData("place loc A9B40019 84.5 7.1 94", "place")]
    [InlineData("  status   #a1  ", "status")]
    [InlineData("QUEST STAMP SentryTaskComplete", "quest")]
    [InlineData("Quest Erase sentrytaskcomplete", "quest")]
    [InlineData("quest HAS X  #a1", "quest")]
    public void Verbs_and_forms_match_ignoring_case_and_extra_spaces(string raw, string verb)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.True(parse.Ok, parse.Error?.Message);
        Assert.Equal(verb, parse.Verb);
        Assert.Equal(verb, parse.Request.Verb);
    }

    [Theory]
    [InlineData("quest stamp SentryTaskComplete", QuestAction.Stamp, "SentryTaskComplete")]
    [InlineData("quest ERASE Sentry_Task-Complete(2)", QuestAction.Erase, "Sentry_Task-Complete(2)")]
    [InlineData("quest Has sentrytaskcomplete", QuestAction.Has, "sentrytaskcomplete")]
    public void A_quest_request_keeps_the_name_as_written_and_reads_the_action_ignoring_case(string raw, QuestAction action, string name)
    {
        var request = Assert.IsType<QuestRequest>(ToolRequestParser.Parse(raw).Request);
        Assert.Equal((action, name), (request.Action, request.Name));
    }

    [Fact]
    public void An_unquoted_one_word_name_is_accepted_and_a_quoted_name_keeps_its_spaces()
    {
        Assert.Equal(new PlacePoiRequest("Holtburg"), ToolRequestParser.Parse("place poi Holtburg").Request);
        Assert.Equal(new PlacePoiRequest("Bandit Castle", "b"), ToolRequestParser.Parse("place poi \"Bandit Castle\" #b").Request);
        Assert.Equal(new PlacePoiRequest("Bandit  Castle"), ToolRequestParser.Parse("place poi \"Bandit  Castle\"").Request);
    }

    [Fact]
    public void A_character_name_goes_in_quotes_and_an_unquoted_one_word_name_is_accepted()
    {
        Assert.Equal(new CharPurgeRequest("Lifestone Throwaway", "p"), ToolRequestParser.Parse("char purge \"Lifestone Throwaway\" #p").Request);
        Assert.Equal(new CharPurgeRequest("Ann"), ToolRequestParser.Parse("char purge Ann").Request);
        Assert.Equal(new CharPurgeRequest("Ann"), ToolRequestParser.Parse("CHAR Purge \"Ann\"").Request);
        Assert.Equal(new CharPurgeRequest("Ann  Bo"), ToolRequestParser.Parse("char purge \"Ann  Bo\"").Request);
        Assert.Equal("char", ToolRequestParser.Parse("char purge Ann").Verb);

        // An unquoted name with a space is two words: refused with the quoting hint.
        var two = ToolRequestParser.Parse("char purge Lifestone Throwaway #p");
        Assert.False(two.Ok);
        Assert.Equal((ToolErrors.BadArgs, "char", "p"), (two.Error.Code, two.Verb, two.Id));
        Assert.Contains("double quotes", two.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_name_has_the_documented_alphabet_and_length()
    {
        foreach (var name in new[] { "Ann", "Lifestone Throwaway", "Tou-Tou", "O'Brien", "Ann 2", new string('c', ToolRequestParser.MaxCharacterNameLength) })
        {
            Assert.True(ToolRequestParser.IsValidCharacterName(name), name);
        }

        foreach (var name in new[] { string.Empty, " ", " Ann", "Ann ", "An\"n", "Ann_2", "Ann.", "Ann@", "Änn", new string('c', ToolRequestParser.MaxCharacterNameLength + 1) })
        {
            Assert.False(ToolRequestParser.IsValidCharacterName(name), name);
        }

        Assert.False(ToolRequestParser.IsValidCharacterName(null));
    }

    [Theory]
    // No verb, an unknown verb, a quoted verb.
    [InlineData("", ToolErrors.BadArgs)]
    [InlineData("   ", ToolErrors.BadArgs)]
    [InlineData("teleport", ToolErrors.UnknownVerb)]
    [InlineData("statu", ToolErrors.UnknownVerb)]
    [InlineData("status_", ToolErrors.UnknownVerb)]
    [InlineData("\"status\"", ToolErrors.BadArgs)]
    // Extra words.
    [InlineData("status now", ToolErrors.BadArgs)]
    [InlineData("where 1", ToolErrors.BadArgs)]
    [InlineData("net \"x\"", ToolErrors.BadArgs)]
    [InlineData("places all", ToolErrors.BadArgs)]
    // Characters outside printable ASCII.
    [InlineData("status\t", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtbürg\"", ToolErrors.BadArgs)]
    [InlineData("status\n", ToolErrors.BadArgs)]
    // place poi.
    [InlineData("place", ToolErrors.BadArgs)]
    [InlineData("place poi", ToolErrors.BadArgs)]
    [InlineData("place there Holtburg", ToolErrors.BadArgs)]
    [InlineData("place \"poi\" Holtburg", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtburg", ToolErrors.BadArgs)]
    [InlineData("place poi Holtburg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holt\"burg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holt\"burg", ToolErrors.BadArgs)]
    [InlineData("place poi Bandit Castle", ToolErrors.BadArgs)]
    [InlineData("place poi \"\"", ToolErrors.BadArgs)]
    [InlineData("place poi \" Holtburg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtburg \"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holt.burg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holt_burg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", ToolErrors.BadArgs)]
    // xp.
    [InlineData("xp", ToolErrors.BadArgs)]
    [InlineData("xp 0", ToolErrors.BadArgs)]
    [InlineData("xp -1", ToolErrors.BadArgs)]
    [InlineData("xp +5", ToolErrors.BadArgs)]
    [InlineData("xp 1e3", ToolErrors.BadArgs)]
    [InlineData("xp 1,000", ToolErrors.BadArgs)]
    [InlineData("xp 1.5", ToolErrors.BadArgs)]
    [InlineData("xp 1000000000000", ToolErrors.BadArgs)]
    [InlineData("xp 99999999999999999999999", ToolErrors.BadArgs)]
    [InlineData("xp \"5\"", ToolErrors.BadArgs)]
    [InlineData("xp 5 5", ToolErrors.BadArgs)]
    [InlineData("xp five", ToolErrors.BadArgs)]
    // place loc: the cell.
    [InlineData("place loc", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1 94 1 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1 94 1 0 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4001G 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x0A9B40019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x-9B40019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc \"0xA9B40019\" 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40000 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40041 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B400FF 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4FFFE 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4FFFF 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xFFB40019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9FF0108 84.5 7.1 94", ToolErrors.BadArgs)]
    // place loc: an outdoor cell must hold x and y.
    [InlineData("place loc 0xA9B40019 84.5 30 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40001 24 0 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40040 192 191 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40001 -0.001 0 94", ToolErrors.BadArgs)]
    // place loc: the numbers.
    [InlineData("place loc 0xA9B40019 NaN 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 Infinity 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 -Infinity 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 1e40", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1,5 2 3", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 0x10 2 3", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 2 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 0 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 0.98 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 1 0 0 NaN", ToolErrors.BadArgs)]
    // quest: the action and the name.
    [InlineData("quest", ToolErrors.BadArgs)]
    [InlineData("quest stamp", ToolErrors.BadArgs)]
    [InlineData("quest erase", ToolErrors.BadArgs)]
    [InlineData("quest has", ToolErrors.BadArgs)]
    [InlineData("quest list", ToolErrors.BadArgs)]
    [InlineData("quest bestow SentryTaskComplete", ToolErrors.BadArgs)]
    [InlineData("quest \"stamp\" SentryTaskComplete", ToolErrors.BadArgs)]
    [InlineData("quest stamp \"SentryTaskComplete\"", ToolErrors.BadArgs)]
    [InlineData("quest stamp Sentry Task", ToolErrors.BadArgs)]
    [InlineData("quest stamp \"Sentry Task\"", ToolErrors.BadArgs)]
    [InlineData("quest stamp SentryTaskComplete 2", ToolErrors.BadArgs)]
    [InlineData("quest stamp SentryTaskComplete@2", ToolErrors.BadArgs)]
    [InlineData("quest stamp @", ToolErrors.BadArgs)]
    [InlineData("quest erase *", ToolErrors.BadArgs)]
    [InlineData("quest erase Sentry.Task", ToolErrors.BadArgs)]
    [InlineData("quest erase Sentry'Task", ToolErrors.BadArgs)]
    [InlineData("quest erase Sentry\"Task", ToolErrors.BadArgs)]
    [InlineData("quest has Sentry/Task", ToolErrors.BadArgs)]
    [InlineData("quest has Sentry\\Task", ToolErrors.BadArgs)]
    [InlineData("quest has SentryTäsk", ToolErrors.BadArgs)]
    [InlineData("quest has qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq", ToolErrors.BadArgs)]
    // char: the action, the name and its quotes.
    [InlineData("char", ToolErrors.BadArgs)]
    [InlineData("char purge", ToolErrors.BadArgs)]
    [InlineData("char list", ToolErrors.BadArgs)]
    [InlineData("char delete Ann", ToolErrors.BadArgs)]
    [InlineData("char \"purge\" Ann", ToolErrors.BadArgs)]
    [InlineData("char purge Ann Bo", ToolErrors.BadArgs)]
    [InlineData("char purge \"Ann\" \"Bo\"", ToolErrors.BadArgs)]
    [InlineData("char purge Lifestone Throwaway", ToolErrors.BadArgs)]
    [InlineData("char purge \"\"", ToolErrors.BadArgs)]
    [InlineData("char purge \" Ann\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"Ann \"", ToolErrors.BadArgs)]
    [InlineData("char purge \"Ann", ToolErrors.BadArgs)]
    [InlineData("char purge Ann\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"Ann\"Bo", ToolErrors.BadArgs)]
    [InlineData("char purge \"An_n\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"An@n\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"AnnAnnAnnAnnAnnAnnAnnAnnAnnAnnAnnAnn\"", ToolErrors.BadArgs)]
    [InlineData("char purge *", ToolErrors.BadArgs)]
    // Ids.
    [InlineData("status #", ToolErrors.BadArgs)]
    [InlineData("status #a_1", ToolErrors.BadArgs)]
    [InlineData("status #0123456789abcdefg", ToolErrors.BadArgs)]
    [InlineData("status #a1 #a2", ToolErrors.BadArgs)]
    [InlineData("#a1 status", ToolErrors.BadArgs)]
    [InlineData("status ##a1", ToolErrors.BadArgs)]
    public void The_parser_rejects_with_the_right_code(string raw, string code)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.False(parse.Ok);
        Assert.Null(parse.Request);
        Assert.Equal(code, parse.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(parse.Error.Message));
        Assert.All(parse.Error.Message, c => Assert.InRange(c, ' ', '~'));
        Assert.False(ToolsWire.TryParseRequest(raw, out var request, out var error));
        Assert.Null(request);
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("xp 0 #a7", "xp", "a7")]
    [InlineData("place poi \"Nowhere!\" #a7", "place", "a7")]
    [InlineData("place loc 0xA9B40019 84.5 30 94 #loc-2", "place", "loc-2")]
    [InlineData("teleport home #t1", "teleport", "t1")]
    [InlineData("status now #s", "status", "s")]
    [InlineData("#only", "", "only")]
    [InlineData("place poi \"Holtburg #a7", "place", "a7")]
    [InlineData("Te_leport #a7", "", "a7")]
    [InlineData("status #a_1", "status", null)]
    [InlineData("quest stamp Bad@Name #q7", "quest", "q7")]
    [InlineData("QUEST erase * #q8", "quest", "q8")]
    [InlineData("quest", "quest", null)]
    [InlineData("char purge #c1", "char", "c1")]
    [InlineData("CHAR delete Ann #c2", "char", "c2")]
    [InlineData("char", "char", null)]
    public void A_refused_request_still_yields_its_verb_and_id_for_the_reply(string raw, string verb, string? id)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.False(parse.Ok);
        Assert.Equal(verb, parse.Verb);
        Assert.Equal(id, parse.Id);
    }

    [Fact]
    public void A_null_tail_is_a_missing_verb()
    {
        var parse = ToolRequestParser.Parse(null);
        Assert.Equal(ToolErrors.BadArgs, parse.Error?.Code);
        Assert.Contains(ToolsWire.Usage, parse.Error!.Message, StringComparison.Ordinal);
    }

    public static TheoryData<ToolRequest> UnsendableRequests => new()
    {
        new StatusRequest("a_1"),
        new StatusRequest(string.Empty),
        new NetRequest(new string('a', 17)),
        new XpRequest(0),
        new XpRequest(-5),
        new XpRequest(ToolRequestParser.MaxXp + 1),
        new PlacePoiRequest(string.Empty),
        new PlacePoiRequest("Holt\"burg"),
        new PlacePoiRequest("Holtbürg"),
        new PlacePoiRequest(new string('n', ToolRequestParser.MaxNameLength + 1)),
        new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 30f, 94f)),
        new PlaceLocRequest(new ToolLocation(0xA9B40041, 84.5f, 7.1f, 94f)),
        new PlaceLocRequest(new ToolLocation(0x01D90108, float.NaN, 7.1f, 94f)),
        new PlaceLocRequest(new ToolLocation(0x01D90108, 1f, 2f, 3f, 2f, 0f, 0f, 0f)),
        new PlaceLocRequest(default),
        new QuestRequest(QuestAction.Stamp, string.Empty),
        new QuestRequest(QuestAction.Stamp, "Sentry Task"),
        new QuestRequest(QuestAction.Erase, "SentryTaskComplete@1"),
        new QuestRequest(QuestAction.Erase, "*"),
        new QuestRequest(QuestAction.Has, "Sentry\"Task"),
        new QuestRequest(QuestAction.Has, "SentryTäsk"),
        new QuestRequest(QuestAction.Has, new string('q', ToolRequestParser.MaxQuestNameLength + 1)),
        new QuestRequest((QuestAction)99, "SentryTaskComplete"),
        new QuestRequest(QuestAction.Has, "SentryTaskComplete", "bad_id"),
        new CharPurgeRequest(string.Empty),
        new CharPurgeRequest(" Ann"),
        new CharPurgeRequest("Ann "),
        new CharPurgeRequest("An\"n"),
        new CharPurgeRequest("Ann_2"),
        new CharPurgeRequest(new string('c', ToolRequestParser.MaxCharacterNameLength + 1)),
        new CharPurgeRequest("Ann", "bad_id"),
    };

    [Theory]
    [MemberData(nameof(UnsendableRequests))]
    public void A_request_the_mod_would_refuse_cannot_be_written(ToolRequest request)
    {
        Assert.Throws<ArgumentException>(() => ToolsWire.Request(request));
    }

    [Fact]
    public void The_usage_names_every_verb()
    {
        foreach (var verb in ToolsWire.Verbs)
        {
            Assert.Contains(verb, ToolsWire.Usage, StringComparison.Ordinal);
        }

        Assert.Equal(["status", "where", "net", "places", "xp", "place poi", "place loc", "quest stamp", "quest erase", "quest has", "char purge"], ToolsWire.Verbs);
    }

    [Fact]
    public void A_quest_name_is_one_word_of_the_characters_ACE_s_quests_use()
    {
        // ACE's world database (v0.9.296): 4,237 quest names, at most 45 characters, every one letters, digits, '_', '-',
        // '(' and ')' except one with an apostrophe, which the wire does not carry.
        foreach (var name in new[] { "SentryTaskComplete", "KillTask-HarvestReaperCompletions", "BurFlagged(Permanent)", "a", "_", "9" })
        {
            Assert.True(ToolRequestParser.IsValidQuestName(name), name);
        }

        foreach (var name in new[] { string.Empty, " ", "a b", "a@b", "a@", "a*", "a'b", "a\"b", "a.b", "a/b", "a:b", "a,b", "\u00E4" })
        {
            Assert.False(ToolRequestParser.IsValidQuestName(name), name);
        }

        Assert.False(ToolRequestParser.IsValidQuestName(null));
        Assert.True(ToolRequestParser.IsValidQuestName(new string('a', ToolRequestParser.MaxQuestNameLength)));
        Assert.False(ToolRequestParser.IsValidQuestName(new string('a', ToolRequestParser.MaxQuestNameLength + 1)));
        Assert.Equal(["stamp", "erase", "has"], Enum.GetValues<QuestAction>().Select(ToolRequestParser.QuestActionWord));
    }
}
