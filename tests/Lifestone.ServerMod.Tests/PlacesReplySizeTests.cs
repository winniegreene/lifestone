using Lifestone.Data.World.Data;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// The largest reply, <c>places</c>, fits ACE's String16L (a u16 length, written in code page 1252) with room to spare:
/// built from the real points of interest the client embeds (ACE-World-Database v0.9.296, 56 destinations and 62
/// names), and again with every number at its longest JSON spelling and every name at the longest length a request
/// accepts.
/// </summary>
public sealed class PlacesReplySizeTests
{
    /// <summary>A float whose shortest round-trip text is as long as a float's gets: <c>-1.1754944E-38</c>.</summary>
    private const float LongestFloat = -1.1754944E-38f;

    [Fact]
    public void The_real_places_reply_fits_a_String16L_and_is_ascii()
    {
        var places = AcePointsOfInterest.All;
        Assert.Equal(56, places.Count);
        Assert.Equal(62, places.Sum(p => p.PoiNames.Count));

        var data = new PlacesData(
            places.Count,
            places.Sum(p => p.PoiNames.Count),
            [.. places.Select(p => new PoiEntry(
                p.PoiNames, p.Wcid, ToolsWire.FormatCell(p.CellId), p.Origin.X, p.Origin.Y, p.Origin.Z,
                p.Rotation.W, p.Rotation.X, p.Rotation.Y, p.Rotation.Z))]);
        var text = ToolsWire.Reply(ToolReply.Success("0123456789abcdef", ToolVerbs.Places, data), data.Describe());

        Assert.InRange(text.Length, 1, ToolsWire.MaxReplyLength);
        ToolsWireTests.AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out _));
        var back = reply.DataAs<PlacesData>()!;
        Assert.Equal(56, back.Places.Count);
        Assert.Equal(places.SelectMany(p => p.PoiNames), back.Places.SelectMany(p => p.Names));
        Assert.True(places.SelectMany(p => p.PoiNames).All(ToolRequestParser.IsValidPoiName), "every real name must be one a place poi request can carry");
    }

    [Fact]
    public void The_worst_case_places_reply_fits_a_String16L()
    {
        var places = AcePointsOfInterest.All;
        Assert.Equal(LongestFloat.ToString(System.Globalization.CultureInfo.InvariantCulture).Length, "-1.1754944E-38".Length);

        var data = new PlacesData(
            int.MaxValue,
            int.MaxValue,
            [.. places.Select(p => new PoiEntry(
                [.. p.PoiNames.Select(n => n.PadRight(ToolRequestParser.MaxNameLength, 'n'))], uint.MaxValue, ToolsWire.FormatCell(uint.MaxValue),
                LongestFloat, LongestFloat, LongestFloat, LongestFloat, LongestFloat, LongestFloat, LongestFloat))]);
        var text = ToolsWire.Reply(ToolReply.Success("0123456789abcdef", ToolVerbs.Places, data), data.Describe());

        // About 14,800 characters (the real reply is about 7,200), under a quarter of the limit: no paging is needed.
        Assert.InRange(text.Length, 1, ToolsWire.MaxReplyLength / 2);
        ToolsWireTests.AssertPrintableAscii(text.Replace("\n", string.Empty, StringComparison.Ordinal));
    }
}
