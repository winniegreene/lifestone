using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// How the <c>places</c> reply is built from ACE's point-of-interest rows (server/mod/Lifestone.ServerMod/Core/
/// PlacesBuilder.cs, compiled here by link; m2-plan SM3): one entry per portal weenie with all its names, in ordinal
/// name order whatever the culture, raw destinations in wire order, and a weenie without a destination left out and
/// named.
/// </summary>
public sealed class PlacesBuilderTests
{
    private static readonly ToolLocation Holtburg = new(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.078459f);

    private static readonly ToolLocation Arqas = new(0x8F58003B, 175.3f, 66.1f, 0.005f, -0.70710677f, 0f, 0f, 0.70710677f);

    [Fact]
    public void One_entry_per_portal_weenie_with_all_its_names_in_ordinal_order()
    {
        // Out of order on purpose. Ordinal puts upper case before lower case ("Yaraq" < "aerlinthe"), which no culture's
        // collation does, so the order cannot change with the server's culture.
        PoiRow[] rows =
        [
            new("Holtburg", 100),
            new("aerlinthe", 300),
            new("Arqas", 200),
            new("Yaraq", 400),
            new("Al-Arqas", 200),
            new("Cragstone", 100),
        ];
        var asked = new List<uint>();
        var build = PlacesBuilder.Build(rows, wcid =>
        {
            asked.Add(wcid);
            return wcid == 200 ? Arqas : Holtburg;
        });

        var places = build.Data.Places;
        Assert.Equal([200u, 100u, 400u, 300u], places.Select(p => p.Wcid));
        Assert.Equal(["Al-Arqas", "Arqas"], places[0].Names);
        Assert.Equal(["Cragstone", "Holtburg"], places[1].Names);
        Assert.Equal(["Yaraq"], places[2].Names);
        Assert.Equal(["aerlinthe"], places[3].Names);
        Assert.Equal(4, build.Data.Count);
        Assert.Equal(6, build.Data.Names);

        // ACE is asked once per weenie, not once per name.
        Assert.Equal([200u, 100u, 400u, 300u], asked);
        Assert.Empty(build.Skipped);
        Assert.Null(build.Note);
        Assert.Equal("4 places (6 names)", build.Sentence);
    }

    [Fact]
    public void Destinations_are_written_raw_in_wire_order()
    {
        var build = PlacesBuilder.Build([new PoiRow("Holtburg", 100)], _ => Holtburg);
        var entry = Assert.Single(build.Data.Places);
        Assert.Equal(new PoiEntry(entry.Names, 100, "0xA9B40019", 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.078459f), entry);

        // And the reply carries it through the wire unchanged.
        var text = ToolsWire.Reply(ToolReply.Success(new PlacesRequest("p1"), build.Data, build.Note), build.Sentence);
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out var chat));
        Assert.Equal("Lifestone: 1 places (1 names)", chat);
        var read = reply.DataAs<PlacesData>()!;
        Assert.Equal("0xA9B40019", read.Places[0].Cell);
        Assert.Equal(-0.078459f, read.Places[0].Qz);
    }

    [Fact]
    public void A_weenie_without_a_destination_is_left_out_and_named()
    {
        PoiRow[] rows = [new("Holtburg", 100), new("Lost Town", 999), new("Old Lost Town", 999), new("Arqas", 200)];
        var build = PlacesBuilder.Build(rows, wcid => wcid == 999 ? null : wcid == 200 ? Arqas : Holtburg);

        Assert.Equal([200u, 100u], build.Data.Places.Select(p => p.Wcid));
        Assert.Equal(2, build.Data.Count);
        Assert.Equal(2, build.Data.Names);
        var skip = Assert.Single(build.Skipped);
        Assert.Equal(999u, skip.Wcid);
        Assert.Equal(["Lost Town", "Old Lost Town"], skip.Names);
        Assert.Equal("left out, with no destination in ACE: Lost Town, Old Lost Town", build.Note);
        Assert.Equal("2 places (2 names); left out, with no destination in ACE: Lost Town, Old Lost Town", build.Sentence);
    }

    [Fact]
    public void Rows_with_the_same_name_keep_the_callers_order()
    {
        // AceFacts.PointsOfInterest gives rows by name, then by row id; the sort is stable, so that order stands.
        var build = PlacesBuilder.Build([new PoiRow("Twin", 2), new PoiRow("Twin", 1)], _ => Holtburg);
        Assert.Equal([2u, 1u], build.Data.Places.Select(p => p.Wcid));
    }

    [Fact]
    public void No_rows_give_an_empty_list()
    {
        var build = PlacesBuilder.Build([], _ => throw new InvalidOperationException("no weenie to look up"));
        Assert.Empty(build.Data.Places);
        Assert.Equal("0 places (0 names)", build.Sentence);
    }

    [Fact]
    public void A_row_without_a_name_is_refused_loudly()
    {
        var e = Assert.Throws<ArgumentException>(() => PlacesBuilder.Build([new PoiRow("Holtburg", 100), new PoiRow(string.Empty, 7)], _ => Holtburg));
        Assert.Contains("portal weenie 7", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_lookup_is_not_swallowed()
    {
        // PlacesVerb answers 'failed' for it (ToolCall.Crashed); the builder never guesses a destination.
        Assert.Throws<InvalidOperationException>(() => PlacesBuilder.Build([new PoiRow("Holtburg", 100)], _ => throw new InvalidOperationException("database gone")));
    }
}
