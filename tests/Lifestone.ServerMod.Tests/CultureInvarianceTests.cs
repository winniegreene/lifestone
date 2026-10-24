using System.Globalization;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The wire text does not depend on the current culture (m2-plan SM1). ACE runs the mod's command on its world thread,
/// which takes the machine's culture (ACE sets en-US on its main thread only, Program.cs:56), and the client runs in
/// the player's. Each case below is computed under the invariant culture and again under German, Turkish and Saudi Arabic,
/// which differ in decimal separators, digit grouping, the minus sign and the casing of I.
/// </summary>
public sealed class CultureInvarianceTests
{
    public static TheoryData<string> Cultures => ["de-DE", "tr-TR", "ar-SA"];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_cultures_really_differ_from_the_invariant_one(string name)
    {
        // Guards the tests below: in invariant-globalization mode every culture would format like the invariant one.
        var culture = CultureInfo.GetCultureInfo(name);
        Assert.NotEqual("1.5", 1.5f.ToString(culture));
        if (name == "tr-TR")
        {
            Assert.NotEqual("poi", "POI".ToLower(culture));
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Requests_are_written_the_same_in_every_culture(string name)
    {
        var expected = Invariant(() => ToolRequestParserTests.Requests.Select(ToolsWire.Request).ToArray());
        var actual = Under(name, () => ToolRequestParserTests.Requests.Select(ToolsWire.Request).ToArray());
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Requests_are_read_the_same_in_every_culture(string name)
    {
        string[] texts =
        [
            "place loc 0xA9B40019 84.5 7.1 94.005",
            "place loc 0x01D90108 -12.000001 1234.5679 1E-05 0.5 0.5 0.5 0.5 #q",
            "PLACE POI \"Bandit Castle\"",
            "place Poi Holtburg",
            "PLACES",
            "xp 999999999999",
            "STATUS #I1",
            "WHERE",
            "place LOC 0xa9b40001 0.25 0.5 -1.5",
            "QUEST STAMP SentryTaskComplete #I1",
            "quest Has KillTask-BlightSpirit",
        ];
        foreach (var text in texts)
        {
            var expected = Invariant(() => ToolRequestParser.Parse(text));
            var actual = Under(name, () => ToolRequestParser.Parse(text));
            Assert.True(expected.Ok, text + ": " + expected.Error?.Message);
            Assert.Equal(expected, actual);
        }

        // Comma decimals are never numbers, whatever the culture says.
        Assert.False(Under(name, () => ToolRequestParser.Parse("place loc 0x01D90108 1,5 2 3")).Ok);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Replies_are_written_and_read_the_same_in_every_culture(string name)
    {
        var expected = Invariant(EveryReply);
        var actual = Under(name, EveryReply);
        Assert.Equal(expected, actual);

        foreach (var text in actual)
        {
            ToolsWireTests.AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
            var parse = Under(name, () => ToolsWire.ParseReply(text));
            Assert.Equal(ToolReplyKind.Reply, parse.Kind);
        }

        var xp = Under(name, () => ToolsWire.ParseReply(actual[4]).Reply!.DataAs<XpData>());
        ToolsWireTests.AssertSame(ToolsWireTests.Xp, xp);
        var place = Under(name, () => ToolsWire.ParseReply(actual[3]).Reply!.DataAs<PlaceData>());
        ToolsWireTests.AssertSame(ToolsWireTests.PlaceLoc, place);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Cells_are_the_same_in_every_culture(string name)
    {
        Assert.Equal("0xA9B40019", Under(name, () => ToolsWire.FormatCell(0xA9B40019)));
        Assert.Equal(0xA9B40019u, Under(name, () => ToolsWire.TryParseCell("0xa9b40019", out var cell) ? cell : 0));
    }

    private static string[] EveryReply() =>
    [
        ToolsWire.Reply(ToolReply.Success("s", ToolVerbs.Status, ToolsWireTests.Status), ToolsWireTests.Status.Describe()),
        ToolsWire.Reply(ToolReply.Success("w", ToolVerbs.Where, ToolsWireTests.Where), ToolsWireTests.Where.Describe()),
        ToolsWire.Reply(ToolReply.Success("p", ToolVerbs.Place, ToolsWireTests.PlacePoi), ToolsWireTests.PlacePoi.Describe()),
        ToolsWire.Reply(ToolReply.Success("l", ToolVerbs.Place, ToolsWireTests.PlaceLoc), ToolsWireTests.PlaceLoc.Describe()),
        ToolsWire.Reply(ToolReply.Success("x", ToolVerbs.Xp, ToolsWireTests.Xp), ToolsWireTests.Xp.Describe()),
        ToolsWire.Reply(ToolReply.Success("n", ToolVerbs.Net, ToolsWireTests.Net), ToolsWireTests.Net.Describe()),
        ToolsWire.Reply(ToolReply.Success("a", ToolVerbs.Places, ToolsWireTests.Places), ToolsWireTests.Places.Describe()),
        ToolsWire.Reply(ToolReply.Failure("e", ToolVerbs.Xp, new ToolError(ToolErrors.BadArgs, "xp takes one whole number")), "xp takes one whole number"),
        ToolsWire.Reply(ToolReply.Success("q", ToolVerbs.Quest, ToolsWireTests.QuestStamped), ToolsWireTests.QuestStamped.Describe()),
        ToolsWire.Reply(ToolReply.Success("r", ToolVerbs.Quest, ToolsWireTests.QuestErased), ToolsWireTests.QuestErased.Describe()),
        ToolsWire.Reply(ToolReply.Success("c", ToolVerbs.Char, ToolsWireTests.CharPurged), ToolsWireTests.CharPurged.Describe()),
    ];

    private static T Invariant<T>(Func<T> action) => Under(CultureInfo.InvariantCulture.Name, action);

    /// <summary>Runs <paramref name="action"/> with this thread's culture and UI culture set to <paramref name="name"/>, then restores them.</summary>
    private static T Under<T>(string name, Func<T> action)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
