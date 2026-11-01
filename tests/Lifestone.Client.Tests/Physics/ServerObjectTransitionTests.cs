using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Physics;
using Lifestone.Client.Tests.Avatars;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using Lifestone.Data.Tests;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Physics;

/// <summary>
/// M5 item 5 t1 (owner decision D45): the avatars' steps run retail's transition through the server-object host's physics
/// object (<c>CPhysicsObj::transition</c> and <c>SetPositionInternal(transit)</c>, <see cref="PhysicsObj.TryTransitionTo"/>),
/// and their teleports and blips its sliding placement, so creatures collide with each other, with the player and with
/// Holtburg's lifestone, on the dats (Tusker Setup 0x02000964 on its motion table 0x0900000C). Each test also runs the
/// same scene with <c>--server-object-transitions=off</c> (<see cref="AvatarSet.ServerObjectTransitions"/>), which proves the
/// scene would overlap without them and that the switch restores the old path.
/// </summary>
public sealed class ServerObjectTransitionTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    internal const uint TuskerSetup = 0x02000964;
    internal const uint TuskerTable = 0x0900000C;
    internal const uint PlayerGuid = 0x50000001;
    private const uint HumanTable = 0x09000001;
    private const uint LifestoneGuid = 0x7A9B404F;
    private const uint LifestoneSetupId = 0x020002EE;
    private const double Frame = 1.0 / 60.0;
    private static readonly Vector3 LifestoneOrigin = new(81.3304f, 11.7974f, 94.005f);

    /// <summary>Where the player stands: the open row east of the lifestone (<c>ServerObjectPhysicsTests</c>' NPC row).</summary>
    private static readonly Vector3 PlayerSpot = new(104f, 20f, 0f);

    /// <summary>Open ground south of <see cref="PlayerSpot"/>, clear of Holtburg's building 0x01000C17, whose hull covers about x 100-118, y 26-44 (F5).</summary>
    private static readonly Vector3 OpenSpot = new(106f, 16f, 0f);

    [RequiresDatsFact]
    public void Two_tuskers_stuck_to_a_standing_player_end_apart_and_outside_its_cylinder()
    {
        var on = StuckPair(transitions: true);
        var off = StuckPair(transitions: false);
        output.WriteLine($"with transitions: {on.Apart:F2} m apart, nearest to the player {on.NearestToPlayer:F2} m (touching {on.Touching:F2} m, player {on.PlayerTouching:F2} m); transitions {on.Transitions}");
        output.WriteLine($"without: {off.Apart:F2} m apart, nearest to the player {off.NearestToPlayer:F2} m");
        Assert.True(off.Apart < on.Touching * 0.8f, $"without transitions the two are {off.Apart:F2} m apart: the scene does not press them together");
        Assert.Equal(0, off.Transitions);
        Assert.True(on.Transitions > 0);
        Assert.True(on.Apart >= on.Touching * 0.95f, $"with transitions the two tuskers overlap: {on.Apart:F2} m apart, {on.Touching:F2} m when touching");
        Assert.True(on.NearestToPlayer >= on.PlayerTouching * 0.95f, $"a tusker is inside the player's cylinder: {on.NearestToPlayer:F2} m, {on.PlayerTouching:F2} m when touching");
    }

    [RequiresDatsFact]
    public void Two_tuskers_interpolated_to_one_server_point_end_apart()
    {
        foreach (var transitions in new[] { true, false })
        {
            var scene = new Scene(fixture.Dats, transitions, withPlayer: false);
            var point = Ground(PlayerSpot + new Vector3(0f, 6f, 0f));
            var a = scene.Tusker(0x70000001, point + new Vector3(-5f, 0f, 0f), heading: 90f);
            var b = scene.Tusker(0x70000002, point + new Vector3(5f, 0f, 0f), heading: 270f);
            scene.Advance(10);
            Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
            Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000002, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
            scene.Advance(240);
            var apart = Horizontal(InHoltburg(a.Position), InHoltburg(b.Position));
            var touching = scene.Radius(0x70000001) + scene.Radius(0x70000002);
            output.WriteLine($"transitions {transitions}: {apart:F2} m apart (touching {touching:F2} m) at {a.Position.Origin} and {b.Position.Origin}; host transitions {scene.Host.Stats.Transitions}, placements {scene.Host.Stats.Placements} (refused {scene.Host.Stats.PlacementsRefused})");
            if (transitions)
            {
                Assert.True(apart >= touching * 0.95f, $"the two tuskers overlap at the server's point: {apart:F2} m apart");

                // 5-R6 and the lead's #9: the blocked one asks for its blip every frame (retail InterpolationManager::UseTime, 11.6096 0x00556B40, asks
                // again until a placement succeeds), and each refused sliding placement is timed beside the transitions (report only). The blips are refused
                // because the point is inside the hull of Holtburg's building 0x01000C17 (F5), not because the other tusker stands there.
                var cost = scene.Set.Stats.PhysicsCost;
                var stats = scene.Set.Stats;
                output.WriteLine(
                    $"placements {stats.Placements} over {stats.Advances} advances (refused {cost.PlacementsRefused}): {stats.PlacementTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, stats.Placements):F4} ms each, "
                    + $"{cost.PlacementMsMean:F4} ms per frame on average, {cost.PlacementMsMax:F3} ms at most; transitions {cost.TransitionMsMean:F4} ms per frame on average, {cost.TransitionMsMax:F3} ms at most; jumps {cost.CreatureJumps}, flips {cost.CreatureFlips}");
                Assert.True(cost.PlacementsRefused > 0, "the scene's blocked tusker asked for no blip: the placement timing proves nothing");
                Assert.True(stats.Placements >= cost.PlacementsRefused + 2, "the creates' and the blips' placements are not all counted");
                Assert.True(stats.PlacementTicks > 0 && cost.PlacementMsMax > 0, "the placements were not timed");
            }
            else
            {
                Assert.True(apart < 0.1f, $"without transitions both should reach the server's point: {apart:F2} m apart");
                Assert.Equal(0, scene.Host.Stats.Transitions);
            }
        }
    }

    /// <summary>
    /// 5-R3 (M5 item 5 F1): a tusker created on another tusker's spot enters with retail's sliding placement (<c>enter_world</c>, 11.6096 0x00516C70)
    /// and ends beside it; its body is drawn where its object stands, and the feed does not force the object back onto the other.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_created_on_another_slides_beside_it_and_its_body_stands_there()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        var spot = Ground(PlayerSpot + new Vector3(-4f, 0f, 0f));
        var a = scene.Tusker(0x70000001, spot, heading: 0f);
        var b = scene.Tusker(0x70000002, spot, heading: 0f);
        Assert.True(scene.Host.TryGetObject(0x70000002, out var obj));
        Assert.Equal(obj.Position.Frame.Origin, b.Position.Origin);
        scene.Advance(60);
        var apart = Horizontal(InHoltburg(a.Position), InHoltburg(b.Position));
        var touching = scene.Radius(0x70000001) + scene.Radius(0x70000002);
        output.WriteLine($"created on one spot: {apart:F2} m apart after a second (touching {touching:F2} m); placements {scene.Set.Stats.Placements}, forced {scene.Host.Stats.EntriesForced}");
        Assert.True(apart >= touching * 0.95f, $"the second tusker was created inside the first: {apart:F2} m apart");
        Assert.Equal(0, scene.Host.Stats.EntriesForced);
        Assert.Equal(obj.Position.Frame.Origin, b.Position.Origin);
    }

    [RequiresDatsFact]
    public void A_tusker_walking_into_holtburgs_lifestone_stops_at_it()
    {
        foreach (var transitions in new[] { true, false })
        {
            var scene = new Scene(fixture.Dats, transitions, withPlayer: false);
            scene.Host.Create(new ServerObjectDescription(LifestoneGuid, LifestoneSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0), Outdoor(LifestoneOrigin));
            var tusker = scene.Tusker(0x70000001, Ground(LifestoneOrigin + new Vector3(0f, 6f, 0f)), heading: 180f);
            scene.Walk(0x70000001, seconds: 6.0);
            var reached = InHoltburg(tusker.Position);
            var gap = Horizontal(reached, LifestoneOrigin);
            output.WriteLine($"transitions {transitions}: the tusker reached {reached}, {gap:F2} m from the lifestone's centre (its sphere 1.50 m, the tusker {scene.Radius(0x70000001):F2} m)");
            if (transitions)
            {
                Assert.True(reached.Y > LifestoneOrigin.Y && gap > 1.5f, $"the tusker walked into the lifestone: {gap:F2} m from its centre");
            }
            else
            {
                Assert.True(reached.Y < LifestoneOrigin.Y - 1f, "without transitions the tusker should walk through the lifestone: the test proves nothing");
            }
        }
    }

    [RequiresDatsFact]
    public void A_tusker_walking_into_the_player_stops_at_the_players_cylinder()
    {
        foreach (var transitions in new[] { true, false })
        {
            var scene = new Scene(fixture.Dats, transitions, withPlayer: true);
            var tusker = scene.Tusker(0x70000001, Ground(PlayerSpot + new Vector3(0f, 5f, 0f)), heading: 180f);
            scene.Walk(0x70000001, seconds: 5.0);
            var reached = InHoltburg(tusker.Position);
            var gap = Horizontal(reached, scene.PlayerObj!.Position.Origin);
            var touching = scene.Radius(0x70000001) + scene.PlayerRadius;
            output.WriteLine($"transitions {transitions}: the tusker reached {reached}, {gap:F2} m from the player (touching {touching:F2} m)");
            if (transitions)
            {
                Assert.True(reached.Y > PlayerSpot.Y && gap >= touching * 0.95f, $"the tusker walked into the player: {gap:F2} m");
            }
            else
            {
                Assert.True(reached.Y < PlayerSpot.Y - 1f, "without transitions the tusker should walk through the player: the test proves nothing");
            }
        }
    }

    /// <summary>
    /// The cost (D45): 20 tuskers in Holtburg around a standing player, ten walking across one another and ten stuck to the
    /// player by swings, for ten seconds of 60 Hz frames. Report only (a new judge, CLAUDE.md "Checks"): the transitions' time
    /// per frame against the 50 ms frame rule.
    /// </summary>
    [RequiresDatsFact]
    public void A_den_like_crowd_of_twenty_tuskers_reports_its_transition_cost_per_frame()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: true);
        var guids = new List<uint>();
        for (var i = 0; i < 20; i++)
        {
            var guid = 0x70000001u + (uint)i;
            var angle = i * (MathF.Tau / 20f);
            var spot = PlayerSpot + (new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (i % 2 == 0 ? 4f : 9f));
            var heading = (float)((Math.Atan2(-(spot.X - PlayerSpot.X), -(spot.Y - PlayerSpot.Y)) * 180.0 / Math.PI) + 360.0) % 360f;
            scene.Tusker(guid, Ground(spot), heading);
            guids.Add(guid);
        }

        scene.Advance(10);
        var walk = AvatarRig.Moving(MotionCommands.WalkForward);
        var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: PlayerGuid);
        double sumMs = 0;
        var frames = 0;
        long transitions = 0;
        for (var second = 0; second < 10; second++)
        {
            for (var i = 0; i < guids.Count; i++)
            {
                var movement = i % 2 == 0 ? swing : walk;
                Assert.Equal(AvatarApply.Applied, scene.Set.SetMovement(guids[i], isSelf: false, autonomous: false, movement, []));
            }

            for (var f = 0; f < 60; f++)
            {
                scene.Advance(1);
                sumMs += scene.Set.Stats.TransitionMsLastFrame;
                transitions += scene.Set.Stats.TransitionsLastFrame;
                frames++;
            }
        }

        var stats = scene.Set.Stats;
        output.WriteLine(
            $"20 tuskers, {frames} frames: transitions {sumMs / frames:F3} ms per frame on average, {stats.TransitionMsMax:F3} ms at most; {(double)transitions / frames:F1} transitions per frame (at most {stats.TransitionsMax}); against the 50 ms frame rule");
        Assert.True(transitions > frames, "the crowd ran fewer transitions than frames: it is not moving");
    }

    /// <summary>
    /// M5 item 5 F4 (the blips explained): a tusker free to walk across Holtburg's terrain ends no step short of its request (the ground clamp moves only
    /// the height), so it counts no short transit, names no obstacle, is never blipped and raises no held-back report.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_walking_freely_records_no_short_transits_and_no_blips()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        var start = PlayerSpot + new Vector3(-20f, -8f, 0f);
        var tusker = scene.Tusker(0x70000001, Ground(start), heading: 90f);
        scene.Walk(0x70000001, seconds: 6.0);
        var moved = Horizontal(InHoltburg(tusker.Position), start);
        output.WriteLine($"walked {moved:F1} m; short transits {tusker.ShortTransits}, obstacle {tusker.LastObstacle.Describe()}, blips {scene.Set.Stats.BlipCauses}, held back {scene.Set.Stats.HeldBackLogged + scene.Set.Stats.HeldBackCounted}");
        Assert.True(moved > 8f, $"the tusker walked only {moved:F1} m");
        Assert.True(scene.Set.Stats.Transitions > 100, $"only {scene.Set.Stats.Transitions} transitions ran");
        Assert.Equal(0, tusker.ShortTransits);
        Assert.Equal(AvatarObstacleKind.None, tusker.LastObstacle.Kind);
        Assert.Equal(0L, scene.Set.Stats.BlipCauses.Total);
        Assert.Equal(0L, scene.Set.Stats.HeldBackLogged + scene.Set.Stats.HeldBackCounted);
        Assert.DoesNotContain(scene.Log.Entries, e => e.Message.Contains("hero-creature-flicker", StringComparison.Ordinal) || e.Message.Contains("m5-creature-held-back", StringComparison.Ordinal));
    }

    /// <summary>
    /// A tusker interpolated to a server point inside another tusker is held back by that creature: its transitions end short with the creature as the
    /// obstacle (also on the frames it rests against it), and the blip it asks for every frame is refused (the point is inside the creature), so none is counted.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_held_by_another_tusker_names_the_creature_and_asks_for_its_blip()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        var point = Ground(PlayerSpot + new Vector3(0f, 6f, 0f));
        scene.Tusker(0x70000001, point, heading: 0f);
        var b = scene.Tusker(0x70000002, Ground(PlayerSpot + new Vector3(8f, 6f, 0f)), heading: 270f);
        scene.Advance(10);
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000002, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        scene.Advance(120);
        output.WriteLine($"held: short transits {b.ShortTransits}, obstacle {b.LastObstacle.Describe()}, behind {b.BehindMetres:F2} m, placements refused {scene.Set.Stats.PlacementsRefused}");
        Assert.True(b.ShortTransits > 0, "the blocked tusker recorded no short transit");
        Assert.Equal(AvatarObstacleKind.Creature, b.LastObstacle.Kind);
        Assert.Equal(0x70000001u, b.LastObstacle.Guid);
        Assert.Equal("creature:0x70000001", b.LastObstacle.Describe());
        Assert.Equal(0L, scene.Set.Stats.BlipCauses.Total);

        // F4 found the blip still refused with the blocker teleported 30 m away; F5 found why: the point (104, 26) is inside the hull of Holtburg's building 0x01000C17,
        // where no placement fits whatever stands there (A_placement_inside_a_buildings_hull_is_refused_for_any_object_and_open_ground_takes_it). On open ground the blip
        // slides beside the blocker and lands (A_held_tusker_blips_beside_its_blocker_and_reaches_the_point_once_the_blocker_is_gone).
    }

    /// <summary>A tusker interpolated to a point on the far side of the player stops at the player's cylinder: its transits end short with the player as the obstacle.</summary>
    [RequiresDatsFact]
    public void A_tusker_interpolated_past_the_player_is_held_by_the_player()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: true);
        var tusker = scene.Tusker(0x70000001, Ground(PlayerSpot + new Vector3(0f, -6f, 0f)), heading: 0f);
        scene.Advance(10);
        var far = Ground(PlayerSpot + new Vector3(0f, 7f, 0f));
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001, Outdoor(far), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        scene.Advance(120);
        output.WriteLine($"tusker at {tusker.Position.Origin}; short transits {tusker.ShortTransits}, obstacle {tusker.LastObstacle.Describe()}, behind {tusker.BehindMetres:F2} m, placements refused {scene.Set.Stats.PlacementsRefused}");
        Assert.True(tusker.ShortTransits > 0, "the tusker was not held back by the player");
        Assert.Equal(AvatarObstacleKind.Player, tusker.LastObstacle.Kind);
        Assert.Equal("player", tusker.LastObstacle.Describe());
        Assert.True(tusker.BehindMetres > 5f, $"behind {tusker.BehindMetres:F2} m");
    }

    /// <summary>
    /// A tusker walking into the player presses against it every frame: ten short transits in a row raise the held-back report
    /// (<c>TODO(m5-creature-held-back)</c>), a debug line naming the player, at most once per 5 s per creature.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_pressing_into_the_player_is_reported_held_back_once_per_five_seconds()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: true);
        var tusker = scene.Tusker(0x70000001, Ground(PlayerSpot + new Vector3(0f, 5f, 0f)), heading: 180f);
        scene.Walk(0x70000001, seconds: 8.0);
        var held = scene.Log.Entries.Where(e => e.Message.Contains("TODO(m5-creature-held-back)", StringComparison.Ordinal)).ToList();
        output.WriteLine($"short run {tusker.ShortRun}, short transits {tusker.ShortTransits}, obstacle {tusker.LastObstacle.Describe()}; held-back lines {held.Count} (counted {scene.Set.Stats.HeldBackCounted}): {string.Join(" | ", held.Select(h => h.Line))}");
        Assert.True(tusker.ShortRun >= AvatarBody.HeldBackFrames, $"the tusker was not held back {AvatarBody.HeldBackFrames} frames in a row: {tusker.ShortRun}");
        Assert.InRange(held.Count, 1, 2);
        Assert.Equal(held.Count, scene.Set.Stats.HeldBackLogged);
        Assert.True(scene.Set.Stats.HeldBackCounted > 0, "the repeats inside 5 s were not counted");
        Assert.All(held, h => Assert.Equal(LogSeverity.Debug, h.Severity));
        Assert.Equal("player", Convert.ToString(held[0].Fields.Single(f => f.Key == "blocked_by").Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// F5's cause of F4's refused blips: the held scene's point (104, 26) lies inside the hull of Holtburg's building 0x01000C17 (at (107.5, 36, 94), its BSP
    /// sphere 15 m), where every placement is refused whatever stands there: the building's <c>BSPTREE::placement_insert</c> (11.6096 0x0053A6C0) finds the sphere's
    /// centre in its solid and pushes it 1.98 m down through its floor, the terrain pushes it back, and the initial placement of <c>CTransition::find_placement_position</c>
    /// (0x0050CC40) never validates, so the sliding search is never reached. The same object places on open ground, and the refusal is not an object's: a placement that
    /// meets the fixed world only is refused there too.
    /// </summary>
    [RequiresDatsFact]
    public void A_placement_inside_a_buildings_hull_is_refused_for_any_object_and_open_ground_takes_it()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        scene.Tusker(0x70000001, Ground(OpenSpot), heading: 0f);
        Assert.True(scene.Host.TryGetObject(0x70000001, out var obj));
        var refused = new List<Vector3>();
        foreach (var at in new[] { new Vector3(104f, 26f, 0f), new Vector3(110f, 30f, 0f), new Vector3(108f, 36f, 0f) })
        {
            Assert.Equal(AvatarTransitionResult.NoValidPosition, scene.Host.Place(0x70000001, Outdoor(Ground(at)), out _));
            obj.FixedWorldOnly = true;
            Assert.Equal(SetPositionError.NoValidPosition, obj.SetPositionSimple(Outdoor(Ground(at)), sliding: true));
            obj.FixedWorldOnly = false;
            refused.Add(at);
        }

        var open = Ground(OpenSpot + new Vector3(3f, 0f, 0f));
        Assert.Equal(AvatarTransitionResult.Moved, scene.Host.Place(0x70000001, Outdoor(open), out var placed));
        output.WriteLine($"refused inside the hull at {string.Join(", ", refused)}; placed on open ground at {placed.Frame.Origin} (asked {open})");
        Assert.True(Vector3.Distance(placed.Frame.Origin, open) < 0.05f, $"the open placement moved: {placed.Frame.Origin}");
    }

    /// <summary>
    /// F4's held tusker on open ground (F5): held back by another tusker at its server point, the blip it then asks for (retail <c>InterpolationManager::UseTime</c>,
    /// 11.6096 0x00556B40) is retail's sliding <c>SetPositionSimple</c> (0x00516DB0, flags 0x1012: Teleport, Slide, SendPositionEvent), which slides it out of the
    /// other tusker and lands; sent to the point again once the blocker has been teleported 30 m away, it reaches the point, and no placement is refused.
    /// </summary>
    [RequiresDatsFact]
    public void A_held_tusker_blips_beside_its_blocker_and_reaches_the_point_once_the_blocker_is_gone()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        var point = Ground(OpenSpot);
        var a = scene.Tusker(0x70000001, point, heading: 0f);
        var b = scene.Tusker(0x70000002, Ground(OpenSpot + new Vector3(8f, 0f, 0f)), heading: 270f);
        scene.Advance(10);
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000002, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        scene.Advance(120);
        var apart = Horizontal(InHoltburg(a.Position), InHoltburg(b.Position));
        var touching = scene.Radius(0x70000001) + scene.Radius(0x70000002);
        var held = scene.Set.Stats.BlipCauses;
        output.WriteLine($"held: {apart:F2} m apart (touching {touching:F2} m); blips {held}; placements {scene.Set.Stats.Placements}, refused {scene.Set.Stats.PlacementsRefused}; obstacle {b.LastObstacle.Describe()}");
        foreach (var (line, _) in JumpLines(scene))
        {
            output.WriteLine(line);
        }

        Assert.True(held.Total >= 1, $"the held tusker's blip did not land beside its blocker: {held}");
        Assert.Equal(0L, scene.Set.Stats.PlacementsRefused);
        Assert.True(apart >= touching * 0.95f, $"the blip put the tusker inside its blocker: {apart:F2} m apart");

        var far = Ground(OpenSpot + new Vector3(-30f, 0f, 0f));
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001, Outdoor(far), teleportAdvanced: true, hasContact: true, placement: null, scene.Now));
        scene.Advance(5);
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000002, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        scene.Advance(120);
        var gap = Horizontal(InHoltburg(b.Position), point);
        output.WriteLine($"blocker gone: the tusker is {gap:F2} m from the point; blips {scene.Set.Stats.BlipCauses}; refused {scene.Set.Stats.PlacementsRefused}");
        Assert.True(gap < 0.1f, $"the tusker did not reach the point once its blocker was gone: {gap:F2} m");
        Assert.Equal(0L, scene.Set.Stats.PlacementsRefused);
    }

    /// <summary>
    /// A tusker interpolated onto the standing player's spot is held by the player's cylinder; its blip (the sliding placement, which meets the client's player as
    /// retail's does) lands beside the player, outside its cylinder, and is counted under the player.
    /// </summary>
    [RequiresDatsFact]
    public void A_blip_into_the_players_spot_slides_beside_the_player()
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: true, playerAt: Ground(OpenSpot));
        var player = scene.PlayerObj!.Position.Frame.Origin;
        var tusker = scene.Tusker(0x70000001, Ground(OpenSpot + new Vector3(0f, -7f, 0f)), heading: 0f);
        scene.Advance(10);
        Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001, Outdoor(player), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        scene.Advance(120);
        var gap = Horizontal(InHoltburg(tusker.Position), player);
        var touching = scene.Radius(0x70000001) + scene.PlayerRadius;
        var blips = scene.Set.Stats.BlipCauses;
        output.WriteLine($"the tusker is {gap:F2} m from the player (touching {touching:F2} m); blips {blips}; refused {scene.Set.Stats.PlacementsRefused}");
        Assert.True(blips.Player >= 1, $"no blip landed beside the player: {blips}");
        Assert.Equal(0L, scene.Set.Stats.PlacementsRefused);
        Assert.True(gap >= touching * 0.95f, $"the blip put the tusker inside the player's cylinder: {gap:F2} m");
    }

    /// <summary>
    /// A held crowd: six tuskers sent to one server point. On open ground each blip slides beside the others and lands, so the refused placements stay at what
    /// retail would refuse there (none); the same crowd inside the building's hull (F4's spot) is refused every frame, as retail's placement is there.
    /// </summary>
    [RequiresDatsFact]
    public void A_held_crowd_refuses_only_the_blips_that_cannot_fit()
    {
        var open = HeldCrowd(OpenSpot);
        var hull = HeldCrowd(new Vector3(104f, 26f, 0f));
        output.WriteLine($"open ground: blips {open.Blips}, refused {open.Refused} over {open.Frames} frames, nearest pair {open.Nearest:F2} m (touching {open.Touching:F2} m)");
        output.WriteLine($"inside the hull: blips {hull.Blips}, refused {hull.Refused} over {hull.Frames} frames");
        Assert.True(open.Blips.Total >= 1, "the open crowd asked for no blip: the test proves nothing");
        Assert.Equal(0L, open.Refused);
        Assert.True(open.Nearest >= open.Touching * 0.95f, $"two tuskers overlap: {open.Nearest:F2} m");
        Assert.True(hull.Refused > hull.Frames / 2, $"the crowd inside the hull was not refused as retail's placement is: {hull.Refused} in {hull.Frames} frames");
    }

    private (AvatarBlipCauses Blips, long Refused, int Frames, float Nearest, float Touching) HeldCrowd(Vector3 at)
    {
        var scene = new Scene(fixture.Dats, transitions: true, withPlayer: false);
        var point = Ground(at);
        var bodies = new List<AvatarBody>();
        for (var i = 0; i < 6; i++)
        {
            var angle = i * MathF.PI / 3f;
            bodies.Add(scene.Tusker(0x70000001 + (uint)i, Ground(at + new Vector3(6f * MathF.Cos(angle), 6f * MathF.Sin(angle), 0f)), heading: 0f));
        }

        scene.Advance(10);
        for (var i = 0; i < bodies.Count; i++)
        {
            Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001 + (uint)i, Outdoor(point), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        }

        const int frames = 240;
        scene.Advance(frames);
        var nearest = float.MaxValue;
        for (var i = 0; i < bodies.Count; i++)
        {
            for (var j = i + 1; j < bodies.Count; j++)
            {
                nearest = Math.Min(nearest, Horizontal(InHoltburg(bodies[i].Position), InHoltburg(bodies[j].Position)));
            }
        }

        return (scene.Set.Stats.BlipCauses, scene.Set.Stats.PlacementsRefused, frames, nearest, 2f * scene.Radius(0x70000001));
    }

    private static List<(string Line, Dictionary<string, string> Fields)> JumpLines(Scene scene) =>
        [.. scene.Log.Entries
            .Where(e => e.Message.Contains("TODO(hero-creature-flicker)", StringComparison.Ordinal))
            .Select(e => (e.Line, e.Fields.ToDictionary(f => f.Key, f => Convert.ToString(f.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)))];

    private (float Apart, float NearestToPlayer, float Touching, float PlayerTouching, long Transitions) StuckPair(bool transitions)
    {
        var scene = new Scene(fixture.Dats, transitions, withPlayer: true);
        var a = scene.Tusker(0x70000001, Ground(PlayerSpot + new Vector3(-1.1f, 4f, 0f)), heading: 180f);
        var b = scene.Tusker(0x70000002, Ground(PlayerSpot + new Vector3(1.1f, 4f, 0f)), heading: 180f);
        scene.Advance(10);
        var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: PlayerGuid);
        Assert.Equal(AvatarApply.Applied, scene.Set.SetMovement(0x70000001, isSelf: false, autonomous: false, swing, []));
        Assert.Equal(AvatarApply.Applied, scene.Set.SetMovement(0x70000002, isSelf: false, autonomous: false, swing, []));
        scene.Advance(40);
        Assert.Equal(PlayerGuid, a.Positions.GetStickyObjectId());
        Assert.Equal(PlayerGuid, b.Positions.GetStickyObjectId());
        var player = scene.PlayerObj!.Position.Origin;
        return (
            Horizontal(a.Position.Origin, b.Position.Origin),
            Math.Min(Horizontal(a.Position.Origin, player), Horizontal(b.Position.Origin, player)),
            scene.Radius(0x70000001) + scene.Radius(0x70000002),
            scene.Radius(0x70000001) + scene.PlayerRadius,
            scene.Host.Stats.Transitions);
    }

    private Vector3 Ground(Vector3 at) => ServerObjectPhysicsTests.OnGround(fixture.Dats, at);

    private static CellFrame Outdoor(Vector3 at) => ServerObjectPhysicsTests.OutdoorAt(at);

    /// <summary><paramref name="position"/>'s origin in Holtburg's landblock coordinates (a walker may cross into a neighbour).</summary>
    private static Vector3 InHoltburg(in CellFrame position) => position.Origin + LandDefs.GetBlockOffset(Outdoor(PlayerSpot).CellId, position.CellId);

    private static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));

    /// <summary>
    /// Holtburg's collision, the host over it, an avatar set from the dats feeding it, and optionally the player, stepped at
    /// <paramref name="frame"/> seconds a frame (60 Hz unless a test names its rate: <see cref="InterpolationRateTests"/>).
    /// </summary>
    internal sealed class Scene
    {
        public Scene(DatSet dats, bool transitions, bool withPlayer, PhysicsWorld? world = null, Vector3? playerAt = null, double frame = Frame)
        {
            FrameSeconds = frame;
            World = world ?? ServerObjectPhysicsTests.HoltburgWorld(dats);
            Host = new ServerObjectPhysicsHost(World, dats, log: NullLogSink.Instance);
            Set = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats)), Log) { ServerObjects = Host, ServerObjectTransitions = transitions };
            if (withPlayer)
            {
                var at = ServerObjectPhysicsTests.OutdoorAt(playerAt ?? ServerObjectPhysicsTests.OnGround(dats, PlayerSpot));
                PlayerObj = MovementTestKit.Player(World, at);
                PlayerRadius = MovementTestKit.HumanSetup.Radius;
                var placed = PlayerObj.Position;
                Assert.Equal(AvatarApply.Applied, Set.Create(AvatarStress.Object(0x02000001, HumanTable, isCreature: true) with { Guid = PlayerGuid, IsSelf = true, Position = placed, HasContact = true }, now: 0));
                Set.PlayerBody = new KinematicPlayerBody(Set.Player!, Set);
            }

            Viewer = new AvatarViewer(ServerObjectPhysicsTests.OutdoorAt(playerAt ?? PlayerSpot));
        }

        public RecordingLogSink Log { get; } = new();

        public PhysicsWorld World { get; }

        public ServerObjectPhysicsHost Host { get; }

        public AvatarSet Set { get; }

        public PhysicsObj? PlayerObj { get; }

        public float PlayerRadius { get; }

        public AvatarViewer Viewer { get; }

        public double Now { get; private set; }

        /// <summary>The seconds of one <see cref="Advance"/> frame.</summary>
        public double FrameSeconds { get; }

        public AvatarBody Tusker(uint guid, Vector3 at, float heading)
        {
            var create = AvatarStress.Object(TuskerSetup, TuskerTable, isCreature: true) with
            {
                Guid = guid,
                PhysicsState = AvatarStateBits.ReportCollisions | AvatarStateBits.Gravity,
                Position = new CellFrame(ServerObjectPhysicsTests.OutdoorAt(at).CellId, new AcFrame(at, PositionFrames.WithHeading(Quaternion.Identity, heading))),
                HasContact = true,
            };
            Assert.Equal(AvatarApply.Applied, Set.Create(create, Now));
            Assert.True(Host.TryGetObject(guid, out _), "the host keeps no physics object for the tusker");
            return Set.All.Single(x => x.Guid == guid).Body;
        }

        /// <summary>The widest sphere of the host's object for <paramref name="guid"/>, scaled.</summary>
        public float Radius(uint guid)
        {
            Assert.True(Host.TryGetObject(guid, out var obj));
            return obj.Setup!.Spheres.Max(s => s.Radius) * obj.Scale;
        }

        public void Walk(uint guid, double seconds)
        {
            Advance(10);
            Assert.Equal(AvatarApply.Applied, Set.SetMovement(guid, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []));
            Advance((int)Math.Round(seconds / FrameSeconds));
        }

        public void Advance(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Now += FrameSeconds;
                if (PlayerObj is not null && World.UseTime(Now))
                {
                    PlayerObj.UpdateObject();
                }

                Set.Advance(Now, Viewer, long.MaxValue);
            }
        }
    }
}

/// <summary>
/// The transitions' allocations (D45): a walking crowd of 60 humans with host objects on flat ground, colliding as they go,
/// and a standing one, allocate nothing per advance once warm (the pattern of <c>AvatarAllocationTests</c>).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ServerObjectTransitionAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_crowd_stepped_through_its_host_objects_allocates_nothing(bool walking)
    {
        var world = MovementTestKit.WorldOf(MovementTestKit.Flat(LandblockCoord.Holtburg));
        var host = new ServerObjectPhysicsHost(world, id => id == AvatarRig.Human ? MovementTestKit.HumanSetup : null);
        var set = new AvatarSet(AvatarRig.Sources()) { ServerObjects = host };
        // A ring of 60 humans 8 m around the middle of one outdoor cell, each facing the middle: walking, they meet there and
        // keep pressing into one another, so every frame runs transitions in the cells the crowd warmed up in.
        var middle = new Vector3(108f, 108f, 0f);
        var cell = CellIds.LandCellAt(LandblockCoord.Holtburg, middle.X, middle.Y)!.Value;
        var center = AvatarRig.At(middle, cell: cell);
        var crowd = new List<AvatarCreate>();
        for (var i = 0; i < 60; i++)
        {
            var angle = i * (MathF.Tau / 60f);
            var spot = middle + (new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * 8f);
            var heading = (float)((Math.Atan2(middle.X - spot.X, middle.Y - spot.Y) * 180.0 / Math.PI) + 360.0) % 360f;
            crowd.Add(AvatarRig.HumanAt(0x7E000001u + (uint)i, spot, heading, cell) with { HasContact = true });
        }

        foreach (var create in crowd)
        {
            Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        }

        Assert.Equal(60, host.Count);
        if (walking)
        {
            var walk = AvatarRig.Moving(MotionCommands.WalkForward);
            foreach (var create in crowd)
            {
                Assert.Equal(AvatarApply.Applied, set.SetMovement(create.Guid, isSelf: false, autonomous: false, walk, []));
            }
        }

        var viewer = new AvatarViewer(center);
        var now = 0.0;
        long bytes = 0;
        var judged = 0;
        long transitions = 0;
        var ticks = 0L;
        var where = new (int Frame, long Bytes)[8];
        var allocating = 0;
        for (var frame = 1; frame <= 720; frame++)
        {
            now += 1.0 / 60.0;
            var start = Stopwatch.GetTimestamp();
            var window = AllocationWindow.Open();
            set.Advance(now, viewer, long.MaxValue);
            var (b, gcInside) = window.Close();
            ticks += Stopwatch.GetTimestamp() - start;
            if (frame <= 360 || gcInside)
            {
                continue;
            }

            transitions += set.Stats.TransitionsLastFrame;
            if (b > 0 && allocating < where.Length)
            {
                where[allocating++] = (frame, b);
            }

            bytes += b;
            judged++;
        }

        output.WriteLine($"{(walking ? "walking" : "standing")}: {judged} judged frames, {bytes} bytes, {(double)transitions / Math.Max(1, judged):F1} transitions per frame, {ticks * 1000.0 / Stopwatch.Frequency / 720:F3} ms per advance, transitions at most {set.Stats.TransitionMsMax:F3} ms");
        output.WriteLine("allocating frames: " + string.Join(", ", where.Take(allocating).Select(w => $"{w.Frame} ({w.Bytes} B)")));
        Assert.True(judged > 300, $"only {judged} frames were judged (GCs inside the others)");
        Assert.Equal(0L, bytes);
        if (walking)
        {
            Assert.True(transitions > judged * 30, $"only {transitions} transitions in {judged} frames: the crowd is not walking through its host objects");
        }
    }
}
