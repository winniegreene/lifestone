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
        new NetRequest(),
        new PlacesRequest(),
        new PlacesRequest("0123456789abcdef"),
        new XpRequest(1),
        new XpRequest(ToolRequestParser.MaxXp, "max"),
        new XpRequest(4_286_609_098, "lvl126"),
        new XpRequest(int.MaxValue + 1L),
        new SpellLearnRequest([1]),
        new SpellLearnRequest([0, 4_294_967_295, 5]),
        new SpellLearnRequest([7, 7], "dup"),
        new SpellLearnRequest(Enumerable.Range(1, ToolRequestParser.MaxSpellIds).Select(i => (uint)i).ToArray(), "max"),
        new ItemRequest(1628),
        new ItemRequest(0, ToolRequestParser.MaxItemStack),
        new ItemRequest(4_294_967_295, 25, "i2"),
        new ItemRequest(57, null, "g1", 21),
        new ItemRequest(107, null, null, null, 0.66),
        new ItemRequest(3715, 1, "e", ToolRequestParser.MinItemPalette, 0),
        new ItemRequest(3715, null, null, ToolRequestParser.MaxItemPalette, 1),
        new PlacePoiRequest("Holtburg"),
        new PlacePoiRequest("Bandit Castle", "a2"),
        new PlacePoiRequest("Tou-Tou"),
        new PlacePoiRequest("Fort O'Brien 2", "a4"),
        new PlacePoiRequest(new string('n', ToolRequestParser.MaxNameLength)),
        new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f)),
        new PlaceLocRequest(new ToolLocation(0xA9B40001, 0f, 0f, -0.25f, 0.70710677f, 0f, 0f, -0.70710677f), "q"),
        new PlaceLocRequest(new ToolLocation(0x01D90108, -12.000001f, 1234.5679f, 1E-05f, 0.5f, 0.5f, 0.5f, 0.5f)),
        new PlaceLocRequest(new ToolLocation(0x00000040, 191.99998f, 191.99998f, 0f)),
        new QuestRequest(QuestAction.Stamp, "SentryTaskComplete"),
        new QuestRequest(QuestAction.Erase, "AcademyTokenGiven", "q2"),
        new QuestRequest(QuestAction.Has, "KillTask-BlightSpirit", "q3"),
        new QuestRequest(QuestAction.Erase, "_a-B_9"),
        new QuestRequest(QuestAction.Stamp, new string('q', ToolRequestParser.MaxQuestNameLength)),
        new CharPurgeRequest("Lifestone Throwaway"),
        new CharPurgeRequest("Tou-Tou O'Brien 2", "p2"),
        new CharPurgeRequest(new string('c', ToolRequestParser.MaxCharacterNameLength)),
        new CorpsesRotRequest(),
        new CorpsesRotRequest("c1"),
        new CorpsesRotRequest("0123456789abcdef"),
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
    [InlineData("corpses-rot", "@lifestone corpses rot #k1")]
    [InlineData("corpses-rot-bare", "@lifestone corpses rot")]
    [InlineData("spell", "@lifestone spell learn 1,2,3 #s1")]
    [InlineData("item", "@lifestone item 1628")]
    [InlineData("item-gold", "@lifestone item 57 palette=21 #g1")]
    [InlineData("item-shade", "@lifestone item 107 shade=0.66")]
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
            "corpses-rot" => new CorpsesRotRequest("k1"),
            "corpses-rot-bare" => new CorpsesRotRequest(),
            "spell" => new SpellLearnRequest([1, 2, 3], "s1"),
            "item" => new ItemRequest(1628),
            "item-gold" => new ItemRequest(57, null, "g1", 21),
            "item-shade" => new ItemRequest(107, null, null, null, 0.66),
            "loc" => new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f)),
            _ => new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 7.1f, 94.005f, 0.5f, 0.5f, 0.5f, 0.5f), "z"),
        };
        Assert.Equal(expected, ToolsWire.Request(request));
    }

    [Fact]
    public void An_item_palette_and_shade_parse_in_either_order_and_an_out_of_range_one_is_refused_with_a_reply()
    {
        Assert.Equal(new ItemRequest(57, null, "g", 21, 0.5), ToolRequestParser.Parse("item 57 shade=0.5 palette=21 #g").Request);
        Assert.Equal(new ItemRequest(57, 3, null, 21, null), ToolRequestParser.Parse("item 57 3 PALETTE=21").Request);
        Assert.Equal(new ItemRequest(57, null, null, null, 1), ToolRequestParser.Parse("item 57 shade=1").Request);
        var refused = ToolRequestParser.Parse("item 57 palette=94 #r");
        Assert.Null(refused.Request);
        Assert.Equal(ToolErrors.BadArgs, refused.Error!.Code);
        Assert.Contains("palette=<1 to 93>", refused.Error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ToolsWire.Request(new ItemRequest(57, null, null, 0)));
        Assert.Throws<ArgumentException>(() => ToolsWire.Request(new ItemRequest(57, null, null, 94)));
        Assert.Throws<ArgumentException>(() => ToolsWire.Request(new ItemRequest(57, null, null, 21, 1.5)));
        Assert.Throws<ArgumentException>(() => ToolsWire.Request(new ItemRequest(57, null, null, 21, double.NaN)));
    }

    [Theory]
    [InlineData("nEt", "net")]
    [InlineData("XP 5", "xp")]
    [InlineData("SPELL LEARN 1,2", "spell")]
    [InlineData("Place Loc 0xa9b40019 84.5 7.1 94", "place")]
    [InlineData("place loc A9B40019 84.5 7.1 94", "place")]
    [InlineData("  status   #a1  ", "status")]
    [InlineData("quest HAS X  #a1", "quest")]
    [InlineData("quest stamp SentryTaskComplete", "quest", QuestAction.Stamp, "SentryTaskComplete")]
    [InlineData("quest ERASE Sentry_Task-Complete(2)", "quest", QuestAction.Erase, "Sentry_Task-Complete(2)")]
    [InlineData("quest Has sentrytaskcomplete", "quest", QuestAction.Has, "sentrytaskcomplete")]
    [InlineData("Quest Erase sentrytaskcomplete", "quest", QuestAction.Erase, "sentrytaskcomplete")]
    public void Verbs_and_forms_match_ignoring_case_and_extra_spaces_and_a_quest_keeps_its_name_as_written(string raw, string verb, QuestAction? action = null, string? name = null)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.True(parse.Ok, parse.Error?.Message);
        Assert.Equal(verb, parse.Verb);
        Assert.Equal(verb, parse.Request.Verb);
        if (action is not null)
        {
            var request = Assert.IsType<QuestRequest>(parse.Request);
            Assert.Equal((action.Value, name), (request.Action, request.Name));
        }
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

    [Theory]
    [InlineData("corpses rot")]
    [InlineData("CORPSES ROT")]
    [InlineData("Corpses Rot")]
    [InlineData("  corpses   rot  ")]
    public void Corpses_rot_matches_ignoring_case_and_extra_spaces_and_takes_no_argument(string raw)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.True(parse.Ok, parse.Error?.Message);
        Assert.Equal(new CorpsesRotRequest(), parse.Request);
        Assert.Equal(("corpses", null), (parse.Verb, parse.Id));
        Assert.Equal("corpses", parse.Request.Verb);
        Assert.Equal(new CorpsesRotRequest("k9"), ToolRequestParser.Parse(raw + " #k9").Request);
    }

    [Theory]
    // Only the sub-verb rot, with nothing after it: the reply names the verb and echoes the id.
    [InlineData("corpses", "corpses", null)]
    [InlineData("corpses #k1", "corpses", "k1")]
    [InlineData("corpses purge #k2", "corpses", "k2")]
    [InlineData("corpses delete", "corpses", null)]
    [InlineData("corpses list", "corpses", null)]
    [InlineData("corpses rotten", "corpses", null)]
    [InlineData("corpses \"rot\"", "corpses", null)]
    [InlineData("corpses rot now #k3", "corpses", "k3")]
    [InlineData("corpses rot all", "corpses", null)]
    [InlineData("corpses rot 5", "corpses", null)]
    [InlineData("corpses rot \"BSD Mage\"", "corpses", null)]
    [InlineData("corpses rot rot", "corpses", null)]
    [InlineData("corpses rot BSD Mage #k4", "corpses", "k4")]
    [InlineData("corpse rot", "corpse", null)]
    public void Corpses_takes_rot_and_nothing_else(string raw, string verb, string? id)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.False(parse.Ok);
        Assert.Null(parse.Request);
        Assert.Equal((verb, id), (parse.Verb, parse.Id));
        Assert.Equal(verb == "corpses" ? ToolErrors.BadArgs : ToolErrors.UnknownVerb, parse.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(parse.Error.Message));
        Assert.All(parse.Error.Message, c => Assert.InRange(c, ' ', '~'));
        Assert.False(ToolsWire.TryParseRequest(raw, out var request, out var error));
        Assert.Null(request);
        Assert.Equal(parse.Error.Code, error.Code);
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
    [InlineData("teleport home #t1", ToolErrors.UnknownVerb, "teleport", "t1")]
    [InlineData("statu", ToolErrors.UnknownVerb)]
    [InlineData("\"status\"", ToolErrors.BadArgs)]
    // Extra words.
    [InlineData("status now #s", ToolErrors.BadArgs, "status", "s")]
    [InlineData("where 1", ToolErrors.BadArgs)]
    [InlineData("net \"x\"", ToolErrors.BadArgs)]
    [InlineData("places all", ToolErrors.BadArgs)]
    // Characters outside printable ASCII.
    [InlineData("status\t", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtbürg\"", ToolErrors.BadArgs)]
    // place poi.
    [InlineData("place", ToolErrors.BadArgs)]
    [InlineData("place poi", ToolErrors.BadArgs)]
    [InlineData("place there Holtburg", ToolErrors.BadArgs)]
    [InlineData("place \"poi\" Holtburg", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtburg #a7", ToolErrors.BadArgs, "place", "a7")]
    [InlineData("place poi Holtburg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holt\"burg", ToolErrors.BadArgs)]
    [InlineData("place poi Bandit Castle", ToolErrors.BadArgs)]
    [InlineData("place poi \"\"", ToolErrors.BadArgs)]
    [InlineData("place poi \" Holtburg\"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Holtburg \"", ToolErrors.BadArgs)]
    [InlineData("place poi \"Nowhere!\" #a7", ToolErrors.BadArgs, "place", "a7")]
    [InlineData("place poi \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", ToolErrors.BadArgs)]
    // xp.
    [InlineData("xp", ToolErrors.BadArgs)]
    [InlineData("xp 0 #a7", ToolErrors.BadArgs, "xp", "a7")]
    [InlineData("xp -1", ToolErrors.BadArgs)]
    [InlineData("xp 1.5", ToolErrors.BadArgs)]
    [InlineData("xp 1000000000000", ToolErrors.BadArgs)]
    [InlineData("xp 99999999999999999999999", ToolErrors.BadArgs)]
    [InlineData("xp \"5\"", ToolErrors.BadArgs)]
    [InlineData("xp 5 5", ToolErrors.BadArgs)]
    // place loc: the cell.
    [InlineData("place loc", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1 94 1 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40019 84.5 7.1 94 1 0 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4001G 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x0A9B40019 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc \"0xA9B40019\" 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40000 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40041 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B400FF 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B4FFFF 84.5 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xFFB40019 84.5 7.1 94", ToolErrors.BadArgs)]
    // place loc: an outdoor cell must hold x and y.
    [InlineData("place loc 0xA9B40019 84.5 30 94 #loc-2", ToolErrors.BadArgs, "place", "loc-2")]
    [InlineData("place loc 0xA9B40001 24 0 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40040 192 191 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0xA9B40001 -0.001 0 94", ToolErrors.BadArgs)]
    // place loc: the numbers.
    [InlineData("place loc 0xA9B40019 NaN 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 Infinity 7.1 94", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 1e40", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1,5 2 3", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 0x10 2 3", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 0 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 0.98 0 0 0", ToolErrors.BadArgs)]
    [InlineData("place loc 0x01D90108 1 2 3 1 0 0 NaN", ToolErrors.BadArgs)]
    // quest: the action and the name.
    [InlineData("quest", ToolErrors.BadArgs, "quest", null)]
    [InlineData("quest stamp", ToolErrors.BadArgs)]
    [InlineData("quest list", ToolErrors.BadArgs)]
    [InlineData("quest \"stamp\" SentryTaskComplete", ToolErrors.BadArgs)]
    [InlineData("quest stamp \"SentryTaskComplete\"", ToolErrors.BadArgs)]
    [InlineData("quest stamp Sentry Task", ToolErrors.BadArgs)]
    [InlineData("quest stamp Bad@Name #q7", ToolErrors.BadArgs, "quest", "q7")]
    [InlineData("QUEST erase * #q8", ToolErrors.BadArgs, "quest", "q8")]
    [InlineData("quest erase Sentry.Task", ToolErrors.BadArgs)]
    [InlineData("quest has SentryTäsk", ToolErrors.BadArgs)]
    [InlineData("quest has qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq", ToolErrors.BadArgs)]
    // char: the action, the name and its quotes.
    [InlineData("char", ToolErrors.BadArgs, "char", null)]
    [InlineData("char purge #c1", ToolErrors.BadArgs, "char", "c1")]
    [InlineData("CHAR delete Ann #c2", ToolErrors.BadArgs, "char", "c2")]
    [InlineData("char \"purge\" Ann", ToolErrors.BadArgs)]
    [InlineData("char purge Ann Bo", ToolErrors.BadArgs)]
    [InlineData("char purge \"\"", ToolErrors.BadArgs)]
    [InlineData("char purge \" Ann\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"Ann\"Bo", ToolErrors.BadArgs)]
    [InlineData("char purge \"An_n\"", ToolErrors.BadArgs)]
    [InlineData("char purge \"AnnAnnAnnAnnAnnAnnAnnAnnAnnAnnAnnAnn\"", ToolErrors.BadArgs)]
    // spell learn: the form and the id list.
    [InlineData("spell", ToolErrors.BadArgs)]
    [InlineData("spell learn", ToolErrors.BadArgs)]
    [InlineData("spell forget 1", ToolErrors.BadArgs)]
    [InlineData("spell learn \"1\"", ToolErrors.BadArgs)]
    [InlineData("spell learn 1 2", ToolErrors.BadArgs)]
    [InlineData("spell learn 1,,2", ToolErrors.BadArgs)]
    [InlineData("spell learn -1", ToolErrors.BadArgs)]
    [InlineData("spell learn abc", ToolErrors.BadArgs)]
    [InlineData("spell learn 4294967296", ToolErrors.BadArgs)]
    // item: the class id and the stack.
    [InlineData("item", ToolErrors.BadArgs)]
    [InlineData("item abc", ToolErrors.BadArgs)]
    [InlineData("item -1", ToolErrors.BadArgs)]
    [InlineData("item 4294967296", ToolErrors.BadArgs)]
    [InlineData("item \"1628\"", ToolErrors.BadArgs)]
    [InlineData("item 1628 0", ToolErrors.BadArgs)]
    [InlineData("item 1628 -1", ToolErrors.BadArgs)]
    [InlineData("item 1628 65536", ToolErrors.BadArgs)]
    [InlineData("item 1628 1.5", ToolErrors.BadArgs)]
    [InlineData("item 1628 5 5", ToolErrors.BadArgs)]
    // item palette and shade (P7, D43): ACE's PaletteTemplate runs 1 to 93 (Undef is 0); a shade is 0 to 1; each is given once, after the stack.
    [InlineData("item 57 palette=0", ToolErrors.BadArgs)]
    [InlineData("item 57 palette=94", ToolErrors.BadArgs)]
    [InlineData("item 57 palette=", ToolErrors.BadArgs)]
    [InlineData("item 57 palette=21 palette=22", ToolErrors.BadArgs)]
    [InlineData("item 57 shade=1.01", ToolErrors.BadArgs)]
    [InlineData("item 57 shade=-0.1", ToolErrors.BadArgs)]
    [InlineData("item 57 shade=", ToolErrors.BadArgs)]
    [InlineData("item 57 shade=0.3 shade=0.4", ToolErrors.BadArgs)]
    [InlineData("item 57 colour=3", ToolErrors.BadArgs)]
    // Ids.
    [InlineData("status #", ToolErrors.BadArgs)]
    [InlineData("status #a_1", ToolErrors.BadArgs, "status", null)]
    [InlineData("status #0123456789abcdefg", ToolErrors.BadArgs)]
    [InlineData("status #a1 #a2", ToolErrors.BadArgs)]
    [InlineData("#a1 status", ToolErrors.BadArgs, "", null)]
    [InlineData("#only", ToolErrors.BadArgs, "", "only")]
    [InlineData("Te_leport #a7", ToolErrors.UnknownVerb, "", "a7")]
    public void The_parser_rejects_with_the_right_code_and_still_yields_its_verb_and_id_for_the_reply(string raw, string code, string? verb = null, string? id = null)
    {
        var parse = ToolRequestParser.Parse(raw);
        Assert.False(parse.Ok);
        if (verb is not null)
        {
            // A row that names its verb names its id too (null: none is echoed).
            Assert.Equal((verb, id), (parse.Verb, parse.Id));
        }

        Assert.Null(parse.Request);
        Assert.Equal(code, parse.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(parse.Error.Message));
        Assert.All(parse.Error.Message, c => Assert.InRange(c, ' ', '~'));
        Assert.False(ToolsWire.TryParseRequest(raw, out var request, out var error));
        Assert.Null(request);
        Assert.Equal(code, error.Code);
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
        new XpRequest(ToolRequestParser.MaxXp + 1),
        new SpellLearnRequest([]),
        new SpellLearnRequest(Enumerable.Range(1, ToolRequestParser.MaxSpellIds + 1).Select(i => (uint)i).ToArray()),
        new ItemRequest(1628, 0),
        new ItemRequest(1628, ToolRequestParser.MaxItemStack + 1),
        new PlacePoiRequest(string.Empty),
        new PlacePoiRequest("Holt\"burg"),
        new PlacePoiRequest(new string('n', ToolRequestParser.MaxNameLength + 1)),
        new PlaceLocRequest(new ToolLocation(0xA9B40019, 84.5f, 30f, 94f)),
        new PlaceLocRequest(new ToolLocation(0xA9B40041, 84.5f, 7.1f, 94f)),
        new PlaceLocRequest(new ToolLocation(0x01D90108, float.NaN, 7.1f, 94f)),
        new PlaceLocRequest(new ToolLocation(0x01D90108, 1f, 2f, 3f, 2f, 0f, 0f, 0f)),
        new PlaceLocRequest(default),
        new QuestRequest(QuestAction.Stamp, "Sentry Task"),
        new QuestRequest(QuestAction.Erase, "SentryTaskComplete@1"),
        new QuestRequest(QuestAction.Has, new string('q', ToolRequestParser.MaxQuestNameLength + 1)),
        new QuestRequest((QuestAction)99, "SentryTaskComplete"),
        new CharPurgeRequest(" Ann"),
        new CharPurgeRequest("Ann_2"),
        new CharPurgeRequest(new string('c', ToolRequestParser.MaxCharacterNameLength + 1)),
        new CorpsesRotRequest("a_1"),
        new CorpsesRotRequest(string.Empty),
        new CorpsesRotRequest(new string('k', 17)),
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

        Assert.Equal(["status", "where", "net", "places", "xp", "place poi", "place loc", "quest stamp", "quest erase", "quest has", "char purge", "spell learn", "item", "corpses rot"], ToolsWire.Verbs);
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
