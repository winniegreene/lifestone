using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 AJ (<c>m6-body-apart</c>): the rule that the body and the server still move together. The numbers are gate A's climb, 2026-11-06: the server's last position 0xF6820033 (145.7, 49.9), the body held by the leash at 0xF7820003 (2.29, 50.12),
/// 48.6 m east over the landblock edge.
/// </summary>
public sealed class BodyApartWatchTests
{
    private const uint RefugeCell = 0xF6820033;
    private const uint PlateauCell = 0xF7820003;
    private const uint RoomCell = 0xA9B40100;

    private static CellFrame Frame(uint cell, float x, float y, float z = 58) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static readonly CellFrame Server = Frame(RefugeCell, 145.7f, 49.855f);
    private static readonly CellFrame Held = Frame(PlateauCell, 2.29f, 50.118f);

    [Fact]
    public void The_climbs_gap_is_found_once_the_hold_is_over_and_not_before()
    {
        var watch = new BodyApartWatch();
        Assert.Null(watch.Step(Server, Held, 100));
        Assert.True(watch.Apart);
        Assert.Null(watch.Step(Server, Held, 100 + BodyApartWatch.HoldSeconds - 0.5));

        var found = watch.Step(Server, Held, 100 + BodyApartWatch.HoldSeconds);

        Assert.NotNull(found);
        Assert.Equal(48.6, found!.Value.Metres, 1);
        Assert.Equal(10, found.Value.Limit);
        Assert.Equal(BodyApartWatch.HoldSeconds, found.Value.HeldSeconds);
        Assert.Equal(Server, found.Value.Server);
        Assert.Equal(Held, found.Value.Body);
    }

    [Fact]
    public void A_gap_that_lasts_is_found_again_each_hold()
    {
        var watch = new BodyApartWatch();
        watch.Step(Server, Held, 0);
        Assert.NotNull(watch.Step(Server, Held, BodyApartWatch.HoldSeconds));
        Assert.Null(watch.Step(Server, Held, BodyApartWatch.HoldSeconds + 1));
        Assert.NotNull(watch.Step(Server, Held, 2 * BodyApartWatch.HoldSeconds));
    }

    [Fact]
    public void A_new_position_from_the_server_starts_the_hold_again_however_wide_the_gap()
    {
        // A fast runner between two echoes, or a server slow to answer: it is still talking.
        var watch = new BodyApartWatch();
        for (var t = 0; t < 60; t += 10)
        {
            Assert.Null(watch.Step(Frame(RefugeCell, 145.7f + t, 49.855f), Held, t));
        }
    }

    [Fact]
    public void Inside_the_limit_there_is_no_gap_and_the_hold_starts_again()
    {
        var watch = new BodyApartWatch();
        watch.Step(Server, Held, 0);
        Assert.Null(watch.Step(Server, Frame(RefugeCell, 150f, 49.855f), 14));
        Assert.False(watch.Apart);
        Assert.Equal(4.3, watch.LastMetres, 1);
        Assert.Null(watch.Step(Server, Held, 16));
        Assert.Null(watch.Step(Server, Held, 16 + BodyApartWatch.HoldSeconds - 1));
    }

    [Fact]
    public void No_server_position_or_no_placed_body_is_no_reading()
    {
        var watch = new BodyApartWatch();
        watch.Step(Server, Held, 0);
        Assert.Null(watch.Step(null, Held, 14));
        Assert.False(watch.Apart);
        Assert.True(double.IsNaN(watch.LastMetres));
        Assert.Null(watch.Step(Server, Held, 15));
        Assert.Null(watch.Step(Server, null, 16));
        Assert.Null(watch.Step(Server, Held, 29));
    }

    [Fact]
    public void A_room_has_the_constraints_shorter_limit()
    {
        Assert.Equal(10, BodyApartWatch.Limit(RefugeCell));
        Assert.Equal(5, BodyApartWatch.Limit(RoomCell));

        var server = Frame(RoomCell, 80, 20);
        var body = Frame(RoomCell, 86, 20);
        var watch = new BodyApartWatch();
        watch.Step(server, body, 0);
        Assert.True(watch.Apart);
        Assert.NotNull(watch.Step(server, body, BodyApartWatch.HoldSeconds));
    }
}
