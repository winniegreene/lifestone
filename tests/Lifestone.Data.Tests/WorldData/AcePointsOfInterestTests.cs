using System.Numerics;
using Lifestone.Data.World.Data;

namespace Lifestone.Data.Tests.WorldData;

/// <summary>The embedded AGPL extract of ACE's points of interest (tools/data/extract_ace_poi.py).</summary>
public sealed class AcePointsOfInterestTests
{
    [Fact]
    public void The_extract_is_embedded_and_complete()
    {
        var all = AcePointsOfInterest.All;
        Assert.Equal(56, all.Count);
        Assert.Equal(62, all.Sum(p => p.PoiNames.Count)); // ACE has 62 point-of-interest rows
        Assert.Equal(all.Count, all.Select(p => p.Wcid).Distinct().Count());
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
        Assert.All(all, p => Assert.Equal(1f, p.Rotation.Length(), 4));
    }

    [Fact]
    public void Holtburg_is_aces_town_portal_destination()
    {
        var holtburg = Assert.Single(AcePointsOfInterest.All, p => p.DisplayName == "Holtburg");
        Assert.Equal(42820u, holtburg.Wcid);
        Assert.Equal("Portal to Holtburg", holtburg.WeenieName);
        Assert.Equal(0xA9B40019u, holtburg.CellId);
        Assert.Equal(new Vector3(84f, 7.1f, 94f), holtburg.Origin);
        Assert.Equal(new Quaternion(0f, 0f, -0.078459f, 0.996917f), holtburg.Rotation);
    }

    [Fact]
    public void Aliases_are_grouped_per_destination()
    {
        var swank = Assert.Single(AcePointsOfInterest.All, p => p.DisplayName == "Hotel Swank!");
        Assert.Equal(["Hotel", "Swank", "HotelSwank", "Hotel Swank"], swank.PoiNames);
        Assert.Equal(0x018A024Fu, swank.CellId);
    }

    [Fact]
    public void Bad_documents_fail_loudly()
    {
        Assert.Throws<InvalidDataException>(() => AcePointsOfInterest.Parse("{\"destinations\": []}"));
        Assert.Throws<InvalidDataException>(() => AcePointsOfInterest.Parse(
            "{\"destinations\": [{\"displayName\": \"X\", \"cell\": \"0xZZ\", \"origin\": [0,0,0], \"rotationWxyz\": [1,0,0,0]}]}"));
        Assert.Throws<InvalidDataException>(() => AcePointsOfInterest.Parse(
            "{\"destinations\": [{\"displayName\": \"X\", \"cell\": \"0x01\", \"origin\": [0,0], \"rotationWxyz\": [1,0,0,0]}]}"));
    }
}
