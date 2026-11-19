using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 sprint (live 2026-11-18 20:30:09 UTC, level 9 in Dryreach: the logistics walk back to the outskirts was planned from Dryreach as if she stood in Holtburg, and the wayfinder waited 30 s for tiles no host attaches):
/// <see cref="WayHomeRule.StandsNear"/> says where a walk between a ground and its home may start, by the ground's own facts; <see cref="NavRunner.StreamableWhileStanding"/> is the set of tiles the hosts attach while the
/// body stands, so a wait for tiles outside it is not made.
/// </summary>
public sealed class WayHomeNearTests
{
    [Fact]
    public void A_walk_between_the_outskirts_and_Holtburg_starts_in_Holtburgs_landblock_or_the_grounds_never_in_Dryreach()
    {
        var ground = GroundCatalog.TryGet("holtburg-outskirts")!;
        Assert.True(WayHomeRule.StandsNear(ground, ground.Home.LifestoneCell));
        Assert.True(WayHomeRule.StandsNear(ground, WayHomeRule.FirstCell(ground)!.Cell));
        Assert.False(WayHomeRule.StandsNear(ground, 0xDA75016Du)); // Dryreach, where the walk of 2026-11-18 was planned from
    }

    [Fact]
    public void A_standing_body_in_Dryreach_streams_its_ring_and_two_landblocks_toward_Holtburg_not_Holtburg()
    {
        static Vector2 Centre(uint landblock) => new(((landblock >> 24) + 0.5f) * 192f, (((landblock >> 16) & 0xFF) + 0.5f) * 192f);
        var set = NavRunner.StreamableWhileStanding(Centre(0xDA750000u), Centre(0xA8B50000u));
        Assert.Contains(0xDA750000u, set);
        Assert.Contains(0xD9740000u, set); // the ring
        Assert.DoesNotContain(0xA8B50000u, set);
        Assert.DoesNotContain(0xA9B40000u, set);
        Assert.Equal(9 + HeadlessStreamer.AheadLandblocks, set.Count);
    }
}
