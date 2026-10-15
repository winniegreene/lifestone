using System.Numerics;
using Lifestone.Client.Camera;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Camera;

/// <summary>
/// The chase camera's model against what <c>CameraSet</c> and <c>CameraManager::UpdateCamera</c> do (11.4186 PORTAL/smartbox, re-expressed): the
/// default and first-person offsets, zoom, orbit, raise and lower, the places it looks, the smoothing, the mouse look, the fade, and the sweep.
/// </summary>
public sealed class ChaseCameraModelTests
{
    private const uint Cell = 0x40400001;

    private static CellFrame Player(float x = 100f, float y = 100f, float z = 50f, float heading = 0f) =>
        new(Cell, new AcFrame(new Vector3(x, y, z), PositionFrames.WithHeading(Quaternion.Identity, heading)));

    private static Vector3 Forward(in CellFrame frame) => Vector3.Transform(Vector3.UnitY, frame.Orientation);

    private static void Near(Vector3 expected, Vector3 actual, int precision = 4)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }

    [Fact]
    public void The_constants_are_the_retail_ones()
    {
        Assert.Equal(1.5f, ChaseCameraModel.DefaultPivotZ);
        Assert.Equal(-2.5f, ChaseCameraModel.DefaultOffsetYFactor);
        Assert.Equal(0.75f, ChaseCameraModel.DefaultOffsetZFactor);
        Assert.Equal(0.18f, ChaseCameraModel.InHeadY);
        Assert.Equal(41f, ChaseCameraModel.AdjustmentSpeed);
        Assert.Equal(0.35f, ChaseCameraModel.Stiffness);
        Assert.Equal(8f * MathF.PI / 180f, ChaseCameraModel.AngleRadians, 6);
        Assert.Equal(0.8f, ChaseCameraModel.MouseLookLimit);
    }

    [Fact]
    public void A_new_camera_is_behind_and_above_the_pivot_looking_where_the_player_faces()
    {
        var model = new ChaseCameraModel();

        Assert.Equal(new Vector3(0f, -2.5f, 0.75f), model.ViewerOffset);
        Assert.Equal(new Vector3(0f, 0f, 1.5f), model.PivotOffset);
        Assert.Equal(CameraTargets.AlignWithPlane | CameraTargets.LookInDirection, model.Targets);
        Assert.False(model.InHead);
        var pose = model.Update(1.0, Player(), sweep: null);

        // The pivot is 1.5 m up; the camera 2.5 m behind it (south of a north-facing player) and 0.75 m higher.
        Assert.Equal(new Vector3(100f, 100f, 51.5f), pose.Pivot.Origin);
        Near(new Vector3(100f, 97.5f, 52.25f), pose.Viewer.Origin);
        Assert.Equal(0f, pose.PlayerTranslucency);
        var look = Forward(pose.Viewer);
        Assert.Equal(0f, look.X, 4);
        Assert.Equal(1f, look.Y, 4);
        Assert.Equal(0f, look.Z, 4);
    }

    [Fact]
    public void The_camera_turns_with_the_player()
    {
        var model = new ChaseCameraModel();

        var pose = model.Update(1.0, Player(heading: 90f), sweep: null);

        // Facing east: the camera is 2.5 m west of the pivot and looks east.
        Assert.Equal(97.5f, pose.Viewer.Origin.X, 3);
        Assert.Equal(100f, pose.Viewer.Origin.Y, 3);
        Assert.Equal(1f, Forward(pose.Viewer).X, 4);
    }

    [Fact]
    public void Zooming_in_shrinks_the_offset_by_one_minus_a_fifth_of_the_adjustment_and_stops_half_a_metre_from_the_pivot()
    {
        var model = new ChaseCameraModel();
        model.Closer(now: 10.0, keepGoing: false, modOverride: 1f);
        var step = 1f - (0.2f * (1f / 60f) * 41f); // the first call takes one frame time

        Near(new Vector3(0f, -2.5f, 0.75f) * step, model.ViewerOffset);

        for (var i = 0; i < 200; i++)
        {
            model.Closer(10.0 + (i * 0.05), keepGoing: false, modOverride: 1f);
        }

        Assert.InRange(model.ViewerOffset.Length(), 0.5f, 0.75f);
    }

    [Fact]
    public void Zooming_out_grows_the_offset_to_ten_metres_and_no_further()
    {
        var model = new ChaseCameraModel();
        model.Farther(10.0, keepGoing: false, modOverride: 1f);

        Near(new Vector3(0f, -2.5f, 0.75f) * (1f + (0.2f * (1f / 60f) * 41f)), model.ViewerOffset);
        for (var i = 0; i < 200; i++)
        {
            model.Farther(10.0 + (i * 0.05), keepGoing: false, modOverride: 1f);
        }

        Assert.InRange(MathF.Abs(model.ViewerOffset.Y), 8f, 10f);
    }

    [Fact]
    public void A_wheel_notch_is_a_zoom_with_an_override()
    {
        var model = new ChaseCameraModel();
        model.Closer(10.0, keepGoing: false, modOverride: 2f);

        // 1 / 2 = 0.5 of the adjustment: 1 - 0.2 * 0.5.
        Near(new Vector3(0f, -2.5f, 0.75f) * 0.9f, model.ViewerOffset);
    }

    [Fact]
    public void Rotating_the_camera_left_moves_it_to_the_players_left_and_makes_it_look_at_the_pivot()
    {
        var model = new ChaseCameraModel();
        model.Rotate(left: true, now: 10.0, keepGoing: false, modOverride: 11.25f, cameraKeys: true); // 11.25 * 8 degrees = 90 degrees

        Assert.Equal(-2.5f, model.ViewerOffset.X, 4);
        Assert.Equal(0f, model.ViewerOffset.Y, 4);
        Assert.Equal(0.75f, model.ViewerOffset.Z, 4);
        Assert.True((model.Targets & CameraTargets.LookAtPivot) != 0);
        var pose = model.Update(11.0, Player(), sweep: null);
        Assert.Equal(97.5f, pose.Viewer.Origin.X, 3);
        Assert.Equal(100f, pose.Viewer.Origin.Y, 3);
        Assert.InRange(Forward(pose.Viewer).X, 0.9f, 1f); // west of the player, looking east at the pivot (a little down)
    }

    [Fact]
    public void Rotating_right_is_the_other_way_and_a_small_turn_still_looks_forward()
    {
        var model = new ChaseCameraModel();
        model.Rotate(left: false, now: 10.0, keepGoing: false, modOverride: 11.25f, cameraKeys: true);
        Assert.Equal(2.5f, model.ViewerOffset.X, 4);

        var small = new ChaseCameraModel();
        small.Rotate(left: true, now: 10.0, keepGoing: false, modOverride: 2f, cameraKeys: true); // 16 degrees: x = -0.69
        Assert.InRange(small.ViewerOffset.X, -1f, 0f);
        Assert.Equal(CameraTargets.AlignWithPlane | CameraTargets.LookInDirection, small.Targets); // within a metre to the side, and not above 0.75: it still looks forward
    }

    [Fact]
    public void A_held_rotate_key_turns_the_camera_every_frame_by_the_adjustment_speed()
    {
        var model = new ChaseCameraModel();
        model.HoldRotate(left: true, start: true, now: 10.0);
        var first = model.ViewerOffset;
        model.Update(10.0 + (1.0 / 60.0), Player(), null);
        var second = model.ViewerOffset;
        model.HoldRotate(left: true, start: false, now: 10.05);
        model.Update(10.1, Player(), null);

        Assert.NotEqual(new Vector3(0f, -2.5f, 0.75f), first);
        Assert.NotEqual(first, second);
        Assert.Equal(second, model.ViewerOffset); // released: nothing more
        Assert.Equal(Vector3.Distance(Vector3.Zero, new Vector3(0f, -2.5f, 0.75f)), model.ViewerOffset.Length(), 3); // an orbit keeps the radius
    }

    [Fact]
    public void Raising_the_camera_goes_up_the_sphere_keeping_its_radius_and_stays_behind_the_pivot()
    {
        var model = new ChaseCameraModel();
        var radius = model.ViewerOffset.Length();
        for (var i = 0; i < 40; i++)
        {
            model.Raise(10.0 + (i * 0.02), keepGoing: false, modOverride: 1f);
        }

        Assert.True(model.ViewerOffset.Z > 0.75f);
        Assert.True(model.ViewerOffset.Y < 0f);
        Assert.Equal(radius, model.ViewerOffset.Length(), 3);
        Assert.True((model.Targets & CameraTargets.LookAtPivot) != 0);
    }

    [Fact]
    public void Lowering_the_camera_stops_at_the_height_of_the_pivot_within_one_point_two_metres()
    {
        var model = new ChaseCameraModel();
        for (var i = 0; i < 100; i++)
        {
            model.Lower(10.0 + (i * 0.02), keepGoing: false, modOverride: 1f);
        }

        // The offset is 2.6 m from the pivot: it may go below the pivot only while 1.2 m or more away horizontally.
        var horizontal = new Vector2(model.ViewerOffset.X, model.ViewerOffset.Y).Length();
        Assert.True(model.ViewerOffset.Z >= 0f || horizontal >= 1.2f);
        Assert.True(model.ViewerOffset.Z < 0.75f);
    }

    [Fact]
    public void First_person_puts_the_viewer_eighteen_centimetres_in_front_of_the_pivot_and_hides_the_player()
    {
        var model = new ChaseCameraModel();
        model.SetInHead();

        Assert.True(model.InHead);
        Assert.Equal(1f, model.TranslationalStiffness);
        Assert.Equal(CameraTargets.AlignWithPlane | CameraTargets.LookInDirection, model.Targets);
        var pose = model.Update(1.0, Player(), sweep: null);
        Near(new Vector3(100f, 100.18f, 51.5f), pose.Viewer.Origin);
        Assert.Equal(1f, pose.PlayerTranslucency);
        Assert.True(pose.InHead);
    }

    [Fact]
    public void First_person_follows_the_players_position_at_once_but_its_turn_with_the_rotational_stiffness()
    {
        var model = new ChaseCameraModel();
        model.SetInHead();
        model.Update(1.0, Player(), null);

        var pose = model.Update(1.0 + (1.0 / 60.0), Player(x: 110f, heading: 90f), null);

        Assert.Equal(110.18f, pose.Viewer.Origin.X, 3); // 18 cm east of the pivot, at once
        Assert.Equal(51.5f, pose.Viewer.Origin.Z, 3);
        Assert.InRange(Forward(pose.Viewer).X, 0.01f, 0.5f); // the view has only started to turn toward east
    }

    [Fact]
    public void Zooming_out_of_first_person_leaves_the_head_for_behind_and_above()
    {
        var model = new ChaseCameraModel();
        model.SetInHead();
        model.Closer(5.0, keepGoing: false, modOverride: 1f); // nothing happens in the head
        Assert.True(model.InHead);

        model.Farther(10.0, keepGoing: false, modOverride: 1f);

        Assert.False(model.InHead);
        Near(new Vector3(0f, -1.2f, 0.5f), model.ViewerOffset);
        Assert.Equal(0.35f, model.TranslationalStiffness);
    }

    [Fact]
    public void The_default_view_key_brings_the_camera_back_from_first_person_and_from_an_orbit()
    {
        var model = new ChaseCameraModel();
        model.Rotate(true, 10.0, false, 5f, true);
        model.SetInHead();

        model.SetDefaultOffsets();

        Near(new Vector3(0f, -2.5f, 0.75f), model.ViewerOffset);
        Assert.Equal(CameraTargets.AlignWithPlane | CameraTargets.LookInDirection, model.Targets);
        Assert.Equal(0.35f, model.TranslationalStiffness);
    }

    [Fact]
    public void The_viewer_moves_a_fraction_of_the_way_to_where_it_wants_to_be_each_frame()
    {
        var model = new ChaseCameraModel();
        var first = model.Update(1.0, Player(), null);

        // The player runs 10 m east; one frame at 60 Hz later the camera has covered stiffness * dt * 10 = 5.83% of the way.
        var dt = 1.0 / 60.0;
        var second = model.Update(1.0 + dt, Player(x: 110f), null);

        var expected = first.Viewer.Origin.X + (10f * 0.35f * (float)dt * 10f);
        Assert.Equal(expected, second.Viewer.Origin.X, 3);
        Assert.Equal(first.Viewer.Origin.Y, second.Viewer.Origin.Y, 3);
    }

    [Fact]
    public void A_snap_places_the_viewer_at_once()
    {
        var model = new ChaseCameraModel();
        model.Update(1.0, Player(), null);
        model.Snap();

        var pose = model.Update(1.01, Player(x: 500f), null);

        Assert.Equal(500f, pose.Viewer.Origin.X, 3);
    }

    [Fact]
    public void The_sweep_stops_the_camera_and_the_next_frame_starts_from_where_it_stopped()
    {
        var model = new ChaseCameraModel();
        var calls = 0;
        ViewerSweep wall = (in CellFrame from, in CellFrame to) =>
        {
            calls++;
            var stopped = to with { Frame = to.Frame with { Origin = new Vector3(to.Origin.X, 99f, to.Origin.Z) } };
            return new ViewerSweepResult(stopped, Blocked: to.Origin.Y < 99f);
        };

        var pose = model.Update(1.0, Player(), wall);
        var next = model.Update(1.0 + (1.0 / 60.0), Player(), wall);

        Assert.True(pose.Blocked);
        Assert.Equal(99f, pose.Viewer.Origin.Y);
        Assert.True(pose.Desired.Origin.Y < 99f);
        Assert.Equal(2, calls);
        Assert.Equal(model.Viewer, next.Viewer);
        Assert.InRange(next.Desired.Origin.Y, 98.9f, 99f); // lerped from the stopped position toward 97.5: the fraction is small
    }

    [Fact]
    public void The_player_fades_when_the_viewer_comes_within_forty_five_centimetres_of_the_pivot()
    {
        var model = new ChaseCameraModel();
        model.Update(1.0, Player(), null);
        var pivot = new Vector3(100f, 100f, 51.5f);
        ViewerSweep close(float distance) => (in CellFrame from, in CellFrame to) =>
            new ViewerSweepResult(to with { Frame = to.Frame with { Origin = pivot + new Vector3(0f, -distance, 0f) } }, Blocked: true);

        model.Update(1.1, Player(), close(0.3f)); // sets the viewer 0.3 m from the pivot
        var halfway = model.Update(1.2, Player(), null);
        model.Update(1.3, Player(), close(0.1f));
        var gone = model.Update(1.4, Player(), null);
        model.Update(1.5, Player(), close(1f));
        var solid = model.Update(1.6, Player(), null);

        Assert.InRange(halfway.PlayerTranslucency, 0.5f, 0.7f); // 1 - (0.3 - 0.2) / 0.25 = 0.6
        Assert.Equal(1f, gone.PlayerTranslucency);
        Assert.Equal(0f, solid.PlayerTranslucency);
    }

    [Fact]
    public void Mouse_look_acts_after_five_events_in_one_direction_and_resets_on_a_pause()
    {
        var model = new ChaseCameraModel();
        var start = model.ViewerOffset;
        model.ToggleMouseLook(true);
        for (var i = 0; i < 5; i++)
        {
            model.MouseLook(30f, 0f, 10.0 + (i * 0.01));
        }

        Assert.Equal(start, model.ViewerOffset);
        model.MouseLook(30f, 0f, 10.06); // the sixth

        Assert.True(model.ViewerOffset.X > 0f); // mouse right: the camera orbits to the right
        var turned = model.ViewerOffset;
        model.MouseLook(0f, 0f, 10.07);
        model.MouseLook(30f, 0f, 10.08);
        Assert.Equal(turned, model.ViewerOffset);
    }

    [Fact]
    public void Mouse_look_down_lowers_and_up_raises_the_camera()
    {
        var up = new ChaseCameraModel();
        var down = new ChaseCameraModel();
        for (var i = 0; i < 12; i++)
        {
            up.MouseLook(0f, -15f, 10.0 + (i * 0.01));
            down.MouseLook(0f, 15f, 10.0 + (i * 0.01));
        }

        Assert.True(up.ViewerOffset.Z > 0.75f);
        Assert.True(down.ViewerOffset.Z < 0.75f);
    }

    [Fact]
    public void In_first_person_the_mouse_turns_the_player_and_the_vertical_mouse_pitches_the_view()
    {
        var model = new ChaseCameraModel();
        model.SetInHead();
        var turns = new List<(bool Clockwise, float Speed)>();
        model.SteerRequested += (clockwise, speed) => turns.Add((clockwise, speed));
        for (var i = 0; i < 8; i++)
        {
            model.MouseLook(30f, 0f, 10.0 + (i * 0.01)); // mouse right: the player turns right
        }

        Assert.NotEmpty(turns);
        Assert.All(turns, t => Assert.True(t.Clockwise));
        Assert.All(turns, t => Assert.InRange(t.Speed, 0f, 1.5f));
        Assert.True(model.SteeringRequested);
        model.EndFrame();
        Assert.False(model.SteeringRequested);
        Assert.True(model.InHead); // turning the player does not leave the head

        for (var i = 0; i < 40; i++)
        {
            model.MouseLook(0f, -15f, 11.0 + (i * 0.01));
        }

        Assert.Equal(-0.8f, model.Direction.Z / model.Direction.Length(), 1); // raised: the pitch component falls to its limit, as the decompile reads
    }

    [Fact]
    public void The_overhead_and_map_views_are_loud_unbuilt_and_told_once()
    {
        var model = new ChaseCameraModel();
        var told = new List<string>();
        model.Unbuilt += told.Add;

        model.ToggleLookDown();
        model.ToggleLookDown();
        model.ToggleMapMode();

        Assert.Equal(2, told.Count);
        Assert.All(told, t => Assert.StartsWith("TODO(g5-camera-", t));
        Assert.Equal(new Vector3(0f, -2.5f, 0.75f), model.ViewerOffset);
    }

    [Fact]
    public void The_slope_alignment_is_a_loud_unbuilt_told_once_at_the_first_frame()
    {
        var model = new ChaseCameraModel();
        var told = new List<string>();
        model.Unbuilt += told.Add;

        model.Update(1.0, Player(), null);
        model.Update(1.1, Player(), null);

        Assert.Single(told);
        Assert.StartsWith("TODO(g5-camera-slope)", told[0]);
    }

    [Fact]
    public void An_update_allocates_nothing_once_warm()
    {
        var model = new ChaseCameraModel();
        var player = Player();
        for (var i = 0; i < 50; i++)
        {
            model.Update(1.0 + (i / 60.0), player, null);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 50; i < 450; i++)
        {
            model.Update(1.0 + (i / 60.0), player, null);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
