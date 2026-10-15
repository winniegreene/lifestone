using System.Text.Json;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The reply half of the wire (server/mod/Shared/ToolsWire.cs; m2-plan §4.12): two lines of printable ASCII, a typed
/// JSON payload that round-trips, every error code, and a client-side classifier that never mistakes chat for a reply
/// or throws on a bad one.
/// </summary>
public sealed class ToolsWireTests
{
    internal static readonly StatusData Status = new(
        1, "0.1.0", "95ef426d412459ff619a12cc18f82bcf72c1c2a8", "9c510128f6ce0469ae1345265ba4d569023c0acdd13bd3a42f83ccd46ea94367",
        "127.0.0.1", 9000, false, "Player", "lifestone", "Lifestone Smoke", "Open", ["status", "net"]);

    internal static readonly WhereData Where = new(
        "0xA9B40019", 84.5f, 7.1f, 94.005f, 0.70710677f, 0f, 0f, -0.70710677f, "0xA9B4FFFF", "0xA9B40019", false, 3, 1);

    internal static readonly PlaceData PlacePoi = new("poi", "Holtburg", "0xA9B40019", 84.5f, 7.1f, 94.005f, 1f, 0f, 0f, 0f);

    internal static readonly PlaceData PlaceLoc = new("loc", null, "0x01D90108", -12.000001f, 1234.5679f, 1E-05f, 0.5f, 0.5f, 0.5f, 0.5f);

    internal static readonly XpData Xp = new(1000, 1000, 1_234_567, 1000, 5);

    internal static readonly QuestData QuestStamped = new("stamp", "SentryTaskComplete", false, 0, true, 1);

    internal static readonly QuestData QuestErased = new("erase", "SentryTaskComplete", true, 1, false, 0);

    internal static readonly CharPurgeData CharPurged = new("Lifestone Throwaway", 1, 1, 12);

    internal static readonly NetData Net = new(123_456, 234_567, 2, 17, 4);

    internal static readonly PlacesData Places = new(2, 3,
    [
        new PoiEntry(["Holtburg"], 1234, "0xA9B40019", 84.5f, 7.1f, 94.005f, 1f, 0f, 0f, 0f),
        new PoiEntry(["Al-Arqas", "Arqas"], 5678, "0x8F58003B", 175.3f, 66.1f, 0.005f, -0.70710677f, 0f, 0f, 0.70710677f),
    ]);

    [Fact]
    public void A_status_reply_is_two_lines_of_ascii_and_round_trips()
    {
        var reply = ToolReply.Success(new StatusRequest("a1"), Status);
        var text = ToolsWire.Reply(reply, Status.Describe());

        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(ToolsWire.ChatPrefix, lines[0], StringComparison.Ordinal);
        Assert.StartsWith(ToolsWire.ReplyPrefix, lines[1], StringComparison.Ordinal);
        AssertPrintableAscii(lines[0]);
        AssertPrintableAscii(lines[1]);

        Assert.True(ToolsWire.TryParseReply(text, out var parsed, out var chatLine));
        Assert.Equal(lines[0], chatLine);
        Assert.Equal((1, "a1", "status", true, (string?)null, (string?)null), (parsed.V, parsed.Id, parsed.Verb, parsed.Ok, parsed.Error, parsed.Message));
        AssertSame(Status, parsed.DataAs<StatusData>());
    }

    [Fact]
    public void Every_data_type_round_trips()
    {
        AssertSame(Status, RoundTrip(Status, ToolVerbs.Status, Status.Describe()));
        AssertSame(Where, RoundTrip(Where, ToolVerbs.Where, Where.Describe()));
        AssertSame(PlacePoi, RoundTrip(PlacePoi, ToolVerbs.Place, PlacePoi.Describe()));
        AssertSame(PlaceLoc, RoundTrip(PlaceLoc, ToolVerbs.Place, PlaceLoc.Describe()));
        AssertSame(Xp, RoundTrip(Xp, ToolVerbs.Xp, Xp.Describe()));
        AssertSame(QuestStamped, RoundTrip(QuestStamped, ToolVerbs.Quest, QuestStamped.Describe()));
        AssertSame(QuestErased, RoundTrip(QuestErased, ToolVerbs.Quest, QuestErased.Describe()));
        AssertSame(CharPurged, RoundTrip(CharPurged, ToolVerbs.Char, CharPurged.Describe()));
        AssertSame(Net, RoundTrip(Net, ToolVerbs.Net, Net.Describe()));
        AssertSame(Places, RoundTrip(Places, ToolVerbs.Places, Places.Describe()));
    }

    [Theory]
    [MemberData(nameof(ErrorCodes))]
    public void Every_error_code_round_trips(string code)
    {
        var text = ToolsWire.Reply(ToolReply.Failure("e1", ToolVerbs.Place, new ToolError(code, "that did not work")), "that did not work");
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out var chatLine));
        Assert.Equal("Lifestone: that did not work", chatLine);
        Assert.False(reply.Ok);
        Assert.Equal(code, reply.Error);
        Assert.Equal("that did not work", reply.Message);
        Assert.Null(reply.Data);
        Assert.Null(reply.DataAs<PlaceData>());
        Assert.Equal(new ToolError(code, "that did not work"), reply.AsError());
    }

    public static TheoryData<string> ErrorCodes => [.. ToolErrors.All];

    [Fact]
    public void The_error_codes_are_the_contracts()
    {
        Assert.Equal(
            [
                "bad-args", "unknown-verb", "not-found", "invalid-cell", "busy", "not-in-world", "not-loopback", "needs-player", "failed",
                "graduated", "own-character", "unknown-character", "character-online", "not-pending-deletion",
            ],
            ToolErrors.All);
    }

    [Fact]
    public void The_json_is_camel_case_and_leaves_nulls_out()
    {
        var text = ToolsWire.Reply(ToolReply.Success(null, ToolVerbs.Status, Status with { Character = null }), "ok");
        var json = text[(text.IndexOf('\n', StringComparison.Ordinal) + 1 + ToolsWire.ReplyPrefix.Length)..];
        Assert.StartsWith("{\"v\":1,\"verb\":\"status\",\"ok\":true,\"data\":{\"wire\":1,\"mod\":\"0.1.0\",", json, StringComparison.Ordinal);
        Assert.Contains("\"aceServerDllSha256\":\"9c510128", json, StringComparison.Ordinal);
        Assert.Contains("\"autoPromotion\":false", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"character\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", json, StringComparison.Ordinal);

        var failure = ToolsWire.Reply(ToolReply.Failure("f", "xp", new ToolError(ToolErrors.BadArgs, "no")), "no");
        Assert.EndsWith("{\"v\":1,\"id\":\"f\",\"verb\":\"xp\",\"ok\":false,\"error\":\"bad-args\",\"message\":\"no\"}", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_reply_carries_the_counts_before_and_after()
    {
        var text = ToolsWire.Reply(ToolReply.Success("q1", ToolVerbs.Quest, QuestStamped), QuestStamped.Describe());
        Assert.EndsWith(
            "{\"v\":1,\"id\":\"q1\",\"verb\":\"quest\",\"ok\":true,\"data\":{\"action\":\"stamp\",\"name\":\"SentryTaskComplete\",\"heldBefore\":false,\"before\":0,\"held\":true,\"after\":1}}",
            text,
            StringComparison.Ordinal);

        Assert.True(ToolsWire.TryParseReply(text, out var reply, out _));
        var data = reply.DataAs<QuestData>()!;
        Assert.True(data.Changed());
        Assert.False((QuestStamped with { Before = 1, HeldBefore = true, After = 1 }).Changed());
        Assert.True(QuestErased.Changed());
        Assert.False((QuestErased with { HeldBefore = false, Before = 0 }).Changed());
    }

    [Fact]
    public void Non_ascii_is_escaped_in_the_json_and_replaced_in_the_sentence()
    {
        var data = PlacePoi with { Name = "Holtbürg→<&>" };
        var text = ToolsWire.Reply(ToolReply.Success("n", ToolVerbs.Place, data), "teleporting to Holtbürg\r\nnow");
        AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.Equal(2, text.Split('\n').Length);
        Assert.Contains("\\u00FC", text, StringComparison.Ordinal);

        Assert.True(ToolsWire.TryParseReply(text, out var reply, out var chatLine));
        Assert.Equal("Lifestone: teleporting to Holtb?rg??now", chatLine);
        Assert.Equal("Holtbürg→<&>", reply.DataAs<PlaceData>()!.Name);
    }

    [Fact]
    public void Nan_and_infinity_do_not_break_a_reply()
    {
        var text = ToolsWire.Reply(ToolReply.Success(null, ToolVerbs.Where, Where with { X = float.NaN, Z = float.NegativeInfinity }), "odd");
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out _));
        var where = reply.DataAs<WhereData>()!;
        Assert.True(float.IsNaN(where.X));
        Assert.Equal(float.NegativeInfinity, where.Z);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("You say, \"hello\"")]
    [InlineData("Unknown command: lifestone")]
    [InlineData("Lifestone: hello")]
    [InlineData("Lifestone: hello\nsecond line")]
    [InlineData("Lifestone: hello\n #lifestone/1 {}")]
    [InlineData("#lifestone/1 {\"v\":1,\"verb\":\"status\",\"ok\":true}")]
    public void Ordinary_chat_is_not_a_reply(string? text)
    {
        Assert.False(ToolsWire.TryParseReply(text, out var reply, out var chatLine));
        Assert.Null(reply);
        Assert.Null(chatLine);
        Assert.Equal(ToolReplyKind.NotAReply, ToolsWire.ParseReply(text).Kind);
    }

    [Fact]
    public void The_unknown_command_answer_means_the_mod_is_missing()
    {
        Assert.True(ToolsWire.IsModMissing("Unknown command: lifestone"));
        Assert.Equal(ToolsWire.UnknownCommandText, "Unknown command: " + ToolsWire.Command);
        Assert.False(ToolsWire.IsModMissing("Unknown command: lifestones"));
        Assert.False(ToolsWire.IsModMissing("Lifestone: Unknown command: lifestone"));
        Assert.False(ToolsWire.IsModMissing(null));

        // ACE's ChatMessageType values (ACE.Entity/Enum/ChatMessageType.cs): Broadcast 0x00 for replies, Help 0x0F for
        // the unknown-command answer (GameActionTalk.cs:86).
        Assert.Equal(0x00, ToolsWire.ReplyChatType);
        Assert.Equal(0x0F, ToolsWire.UnknownCommandChatType);
    }

    [Theory]
    [InlineData("Lifestone: hi\n#lifestone/2 {\"v\":2,\"verb\":\"status\",\"ok\":true}", "#lifestone/2")]
    [InlineData("Lifestone: hi\n#lifestone/10 {}", "#lifestone/10")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"v\":2,\"verb\":\"status\",\"ok\":true}", "version 2")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"verb\":\"status\",\"ok\":true}", "version 0")]
    public void Another_wire_version_is_reported_not_shown_as_chat(string text, string problem)
    {
        var parse = ToolsWire.ParseReply(text);
        Assert.Equal(ToolReplyKind.UnsupportedVersion, parse.Kind);
        Assert.Equal("Lifestone: hi", parse.ChatLine);
        Assert.Contains(problem, parse.Problem, StringComparison.Ordinal);
        Assert.Contains("unsupported wire version", parse.Problem, StringComparison.Ordinal);
        Assert.False(ToolsWire.TryParseReply(text, out _, out _));
    }

    [Theory]
    [InlineData("Lifestone: hi\n#lifestone/1 ", "does not parse")]
    [InlineData("Lifestone: hi\n#lifestone/1 {", "does not parse")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"v\":\"1\",\"verb\":\"status\",\"ok\":true}", "does not parse")]
    [InlineData("Lifestone: hi\n#lifestone/1 [1,2]", "does not parse")]
    [InlineData("Lifestone: hi\n#lifestone/1 null", "null")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"v\":1,\"ok\":true}", "no verb")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"v\":1,\"verb\":\"xp\",\"ok\":false}", "no error code")]
    [InlineData("Lifestone: hi\n#lifestone/1 {\"v\":1,\"verb\":\"xp\",\"ok\":true}\nthird", "more than two lines")]
    [InlineData("Someone: hi\n#lifestone/1 {\"v\":1,\"verb\":\"xp\",\"ok\":true}", "first line")]
    public void A_malformed_reply_is_reported_and_never_throws(string text, string problem)
    {
        var parse = ToolsWire.ParseReply(text);
        Assert.Equal(ToolReplyKind.Malformed, parse.Kind);
        Assert.Contains(problem, parse.Problem, StringComparison.Ordinal);
        Assert.All(parse.Problem!, c => Assert.InRange(c, ' ', '~'));
        Assert.False(ToolsWire.TryParseReply(text, out _, out _));
    }

    [Fact]
    public void Unknown_json_fields_are_ignored()
    {
        const string Text = "Lifestone: hi\n#lifestone/1 {\"v\":1,\"later\":[1,{\"x\":2}],\"verb\":\"net\",\"ok\":true,\"data\":{\"c2SPackets\":5,\"extra\":true}}";
        Assert.True(ToolsWire.TryParseReply(Text, out var reply, out _));
        Assert.Equal(5, reply.DataAs<NetData>()!.C2SPackets);
    }

    [Fact]
    public void A_reply_longer_than_a_String16L_is_refused()
    {
        var huge = new PlacesData(1, 1, [new PoiEntry([new string('n', ToolsWire.MaxReplyLength)], 1, "0x00000001", 0, 0, 0, 1, 0, 0, 0)]);
        Assert.Throws<ArgumentException>(() => ToolsWire.Reply(ToolReply.Success(null, ToolVerbs.Places, huge), "too long"));
    }

    [Fact]
    public void A_reply_stays_below_the_String16L_length_escape()
    {
        // A String16L length word of 0xFFFF is an escape for a 32-bit length in retail's string unpack (and in readers
        // that follow it), so the longest reply is 0xFFFE characters: exactly that is written, one more is refused.
        Assert.Equal(0xFFFE, ToolsWire.MaxReplyLength);
        var reply = ToolReply.Success(new NetRequest("a1"), Net);
        var fixedPart = ToolsWire.Reply(reply, string.Empty).Length;

        var longest = ToolsWire.Reply(reply, new string('s', ToolsWire.MaxReplyLength - fixedPart));
        Assert.Equal(65_534, longest.Length);
        Assert.True(ToolsWire.TryParseReply(longest, out var parsed, out _));
        Assert.Equal("a1", parsed.Id);

        Assert.Throws<ArgumentException>(() => ToolsWire.Reply(reply, new string('s', ToolsWire.MaxReplyLength - fixedPart + 1)));
    }

    [Fact]
    public void A_reply_must_be_version_one()
    {
        Assert.Throws<ArgumentException>(() => ToolsWire.Reply(new ToolReply(2, null, "status", true, null, null, null), "x"));
    }

    [Fact]
    public void Data_of_an_unregistered_type_is_refused_loudly()
    {
        Assert.Throws<NotSupportedException>(() => ToolReply.Success(null, "status", new Unregistered(1)));
    }

    [Fact]
    public void Cells_are_written_as_0x_and_eight_upper_case_digits()
    {
        Assert.Equal("0xA9B40019", ToolsWire.FormatCell(0xA9B40019));
        Assert.Equal("0x00000001", ToolsWire.FormatCell(1));
        Assert.True(ToolsWire.TryParseCell("0xa9b40019", out var cell));
        Assert.Equal(0xA9B40019u, cell);
        Assert.True(ToolsWire.TryParseCell("A9B40019", out cell));
        Assert.Equal(0xA9B40019u, cell);
        Assert.False(ToolsWire.TryParseCell("0xA9B4001", out _));
        Assert.False(ToolsWire.TryParseCell("+A9B40019", out _));
        Assert.False(ToolsWire.TryParseCell(" A9B40019", out _));
        Assert.False(ToolsWire.TryParseCell(null, out _));
    }

    [Fact]
    public void The_sentences_read_well()
    {
        Assert.Equal(
            "mod 0.1.0 (wire 1) on ACE 95ef426d at 127.0.0.1:9000; world Open; access Player; auto-promotion off; verbs: status, net",
            Status.Describe());
        Assert.Equal("you are in cell 0xA9B40019 at (84.50, 7.10, 94.01)", Where.Describe());
        Assert.Equal("teleporting to Holtburg, cell 0xA9B40019 at (84.50, 7.10, 94.01)", PlacePoi.Describe());
        Assert.Equal("teleporting to cell 0x01D90108 at (-12.00, 1234.57, 0.00)", PlaceLoc.Describe());
        Assert.Equal("added 1,000 experience (asked 1,000); total 1,234,567, unassigned 1,000, level 5", Xp.Describe());
        Assert.Equal(
            "since ACE started: 123,456 packets in (2 bad checksums, 17 resend requests), 234,567 out (4 resend requests)",
            Net.Describe());
        Assert.Equal("2 places (3 names)", Places.Describe());
        Assert.Equal("quest SentryTaskComplete stamped: added, completions 1", QuestStamped.Describe());
        Assert.Equal("quest X stamped: completions 2 to 3", new QuestData("stamp", "X", true, 2, true, 3).Describe());
        Assert.Equal("quest X is already at its maximum (1 completions); unchanged", new QuestData("stamp", "X", true, 1, true, 1).Describe());
        Assert.Equal("quest SentryTaskComplete erased (it had 1 completions)", QuestErased.Describe());
        Assert.Equal("quest X was not held; nothing erased", new QuestData("erase", "X", false, 0, false, 0).Describe());
        Assert.Equal("you have quest X: 4 completions", new QuestData("has", "X", true, 4, true, 4).Describe());
        Assert.Equal("you do not have quest X", new QuestData("has", "X", false, 0, false, 0).Describe());
        Assert.Equal("purged Lifestone Throwaway: 1 character record, 1 player biota, 12 possessions", CharPurged.Describe());
        Assert.Equal("purged X: 1 character record, 1 player biota, 1 possession", new CharPurgeData("X", 1, 1, 1).Describe());
        Assert.Equal("purged X: 0 character records, 1 player biota, 0 possessions", new CharPurgeData("X", 0, 1, 0).Describe());
    }

    private static T RoundTrip<T>(T data, string verb, string sentence)
        where T : class
    {
        var text = ToolsWire.Reply(ToolReply.Success("id-" + verb, verb, data), sentence);
        AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out var chatLine));
        Assert.Equal(ToolsWire.ChatPrefix + sentence, chatLine);
        Assert.Equal("id-" + verb, reply.Id);
        Assert.Equal(verb, reply.Verb);
        Assert.True(reply.Ok);
        return reply.DataAs<T>()!;
    }

    /// <summary>Equal by value, lists included (records compare lists by reference).</summary>
    internal static void AssertSame<T>(T expected, T? actual)
        where T : class
    {
        Assert.NotNull(actual);
        Assert.Equal(
            JsonSerializer.Serialize(expected, ToolsWireJsonContext.Default.GetTypeInfo(typeof(T))!),
            JsonSerializer.Serialize(actual, ToolsWireJsonContext.Default.GetTypeInfo(typeof(T))!));
        Assert.Equivalent(expected, actual, strict: true);
    }

    internal static void AssertPrintableAscii(string text) => Assert.All(text, c => Assert.InRange(c, ' ', '~'));

    private sealed record Unregistered(int Value);
}
