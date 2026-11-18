using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The hub walk's hand-over to ACE's walk-to-use (<see cref="UseHandOver.Refusal"/>; the owner's go-ahead of 2026-11-18: hand a portal walk to the game's own walk once the way is clear): a straight line on one open floor hands over, and a line that crosses
/// another portal's keep-off disc, a closed door or a level change does not. On a flat floor of the test's own, no dats.
/// </summary>
public sealed class UseHandOverTests
{
    private static readonly Vector2 Body = new(100f, 100f);

    private static readonly Vector2 Portal = new(120f, 100f);

    private static float? Flat(Vector2 at, float z) => 0f;

    private static string? Judge(PortalKeepOff? keep = null, uint? door = null, Func<Vector2, float, float?>? floor = null, float toZ = 0f, bool creature = false, bool walls = true, float speed = 9f) =>
        UseHandOver.Refusal(Body, 0f, Portal, toZ, speed, creature, keep, null, door, floor ?? Flat, () => walls);

    [Fact]
    public void A_clear_straight_line_on_one_floor_hands_over()
    {
        Assert.Null(Judge());
    }

    [Fact]
    public void Its_own_portals_disc_at_the_lines_end_is_no_refusal()
    {
        var keep = new PortalKeepOff([new KeptPortal("Own", Portal, 0f, Outdoor: false, Own: true)], ownClearance: 2.8f);

        Assert.Null(Judge(keep));
    }

    [Fact]
    public void A_line_through_another_portals_keep_off_disc_is_refused()
    {
        var keep = new PortalKeepOff([new KeptPortal("Own", Portal, 0f, Outdoor: false, Own: true), new KeptPortal("Mite Maze", new Vector2(110f, 101.5f), 0f, Outdoor: false)]);

        Assert.Equal("other-portal:Mite_Maze", Judge(keep));
    }

    [Fact]
    public void A_portal_a_storey_away_from_the_line_is_no_refusal()
    {
        var keep = new PortalKeepOff([new KeptPortal("Below", new Vector2(110f, 100f), -6f, Outdoor: false)]);

        Assert.Null(Judge(keep));
    }

    [Fact]
    public void A_closed_door_on_the_line_is_refused()
    {
        Assert.Equal("closed-door:0x78A0209C", Judge(door: 0x78A0209C));
    }

    [Fact]
    public void A_ledge_or_a_stair_on_the_line_is_a_level_change_and_refused()
    {
        // A drop of more than a step halfway along, and a target a storey down.
        Assert.Equal("level-change", Judge(floor: (at, z) => at.X > 110f ? -1f : 0f));
        Assert.Equal("level-change", Judge(toZ: -6f));
        Assert.Null(Judge(floor: (at, z) => (at.X - 100f) * 0.02f, toZ: 0.4f)); // a gentle slope within a step a sample stays one floor
    }

    [Fact]
    public void Ground_no_room_holds_or_a_static_stands_on_is_refused()
    {
        Assert.Equal("off-floor", Judge(floor: (at, z) => at.X is > 108f and < 109f ? null : 0f));
    }

    [Fact]
    public void Walls_or_door_frames_the_body_meets_a_hostile_creature_and_a_line_past_the_servers_timeout_are_refused()
    {
        Assert.Equal("walls", Judge(walls: false));
        Assert.Equal("creature", Judge(creature: true));
        Assert.Equal("too-far", Judge(speed: 1f)); // 20 m at 1 m/s is past ACE's 15 s MoveTo timeout
    }
}
