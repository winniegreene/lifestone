using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// One server object's stepping (AV7, <see cref="AvatarBody"/>): retail <c>update_object</c>'s clock, the
/// <c>UpdatePositionInternal</c> order, contact from the Grounded flag, <c>MoveOrTeleport</c>, the anchor landblock and
/// the ground probe.
/// </summary>
public sealed class AvatarBodyTests
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void An_object_without_motion_steps_like_the_spell_viewers_actor()
    {
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        var position = AvatarRig.At(new Vector3(50, 60, 10), heading: 30f);
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(1, default) with { SetupId = AvatarRig.Lamp, MotionTableId = 0, IsCreature = false, Position = position }, now: 0));
        var body = set.All[0].Body;

        var lamp = sources.GetSetup(AvatarRig.Lamp)!;
        var driver = new DefaultAnimationPlayer(sources.Animations, lamp.DefaultAnimationId);
        var actor = new EffectActor(new EffectObject(sources.Effects, new RetailRandom(1)), driver, lamp.Parts.Count, position.Frame, 1f, now: 0);

        // Irregular frames, a 0.5 s gap (split into quanta of 0.2 s and a remainder) and a 3 s gap (skipped).
        double[] times = [0.016, 0.040, 0.041, 0.075, 0.575, 0.6, 3.6, 3.65, 3.7];
        foreach (var now in times)
        {
            set.Advance(now, Viewer, long.MaxValue);
            actor.AdvanceTo(now);
            for (var i = 0; i < lamp.Parts.Count; i++)
            {
                AvatarAssert.Near(actor.PartFrames[i].Origin, body.PartFrames[i].Origin);
            }
        }
    }

    [Fact]
    public void Root_motion_moves_a_walker_on_walkable_ground_scaled_by_its_scale()
    {
        var (set, body) = Walker(scale: 2f);
        var start = body.Position.Origin;
        Step(set, seconds: 1.0);
        var moved = body.Position.Origin - start;
        Assert.InRange(moved.Y, 2 * 3.12f * 0.85f, 2 * 3.12f * 1.01f); // the link plays first, then the 3.12 m/s cycle, doubled
        Assert.Equal(0f, moved.X, 4);
    }

    [Fact]
    public void Root_motion_is_zeroed_off_walkable_ground()
    {
        // No gravity: leaving the ground sets no velocity, and the walk keeps playing (contact_allows_move) in place.
        var (set, body) = Walker(state: AvatarStateBits.ReportCollisions);
        Step(set, seconds: 0.5);
        body.SetContact(false);
        var start = body.Position.Origin;
        Step(set, seconds: 1.0);
        Assert.Equal(start, body.Position.Origin);
        Assert.Equal(MotionCommands.WalkForward, body.Parts!.State.Substate);
    }

    [Fact]
    public void Walking_across_a_landblock_seam_changes_the_cell_but_not_the_anchor_so_frames_stay_continuous()
    {
        var (set, body) = Walker(origin: new Vector3(12, 186, 0)); // cell 0x...0F of 0xA9B4 (x 0-24, y 168-192)
        var anchor = body.AnchorCellId;
        var previous = body.AnchorFrame.Origin;
        var crossed = false;
        for (var i = 0; i < 120; i++)
        {
            Step(set, seconds: 1.0 / 30.0);
            var now = body.AnchorFrame.Origin;
            // Root motion comes in whole keyframes (0, 1 or 2 per step); a seam would show as a jump of a landblock.
            Assert.True(Vector3.Distance(previous, now) < 0.5f, $"the drawn frame jumped from {previous} to {now} at step {i}: cell 0x{body.Position.CellId:X8} origin {body.Position.Origin} anchor 0x{body.AnchorCellId:X8}");
            previous = now;
            crossed |= (body.Position.CellId >> 16) != (AvatarRig.HoltburgCell >> 16);
        }

        Assert.True(crossed, $"still in 0x{body.Position.CellId:X8} at {body.Position.Origin}");
        Assert.True(body.Position.CellId >> 16 == 0xA9B5u, $"in 0x{body.Position.CellId:X8} at {body.Position.Origin}");
        Assert.Equal(anchor, body.AnchorCellId);
        AvatarAssert.Near(body.AnchorFrame.Origin, body.Position.Origin + AvatarAssert.Block(anchor, body.Position.CellId));
    }

    [Fact]
    public void A_teleport_places_the_object_and_rebases_the_anchor()
    {
        var (set, body) = Walker();
        var far = new CellFrame(0x7D640021, new AcFrame(new Vector3(30, 40, 50), Quaternion.Identity));
        Assert.True(body.MoveOrTeleport(far, teleportAdvanced: true, hasContact: true, placement: null));
        Assert.Equal(far, body.Position);
        Assert.Equal(far.CellId, body.AnchorCellId);
        Assert.False(body.IsInterpolating);
    }

    [Fact]
    public void A_position_without_contact_is_ignored_as_retail_does()
    {
        var (set, body) = Walker(state: AvatarStateBits.ReportCollisions);
        var before = body.Position;
        Assert.False(body.MoveOrTeleport(AvatarRig.At(before.Origin + new Vector3(5, 0, 0)), teleportAdvanced: false, hasContact: false, placement: null));
        Assert.Equal(before, body.Position);
        Assert.False(body.IsInterpolating);
        Assert.False(body.HasContact);
    }

    [Fact]
    public void A_nearby_position_interpolates_and_the_object_takes_the_servers_cell_on_arrival()
    {
        var (set, body) = Walker(forward: MotionCommands.Ready);
        var target = new CellFrame(0xA9B40105, new AcFrame(body.Position.Origin + new Vector3(3, 0, 0), body.Position.Orientation));
        Assert.True(body.MoveOrTeleport(target, teleportAdvanced: false, hasContact: true, placement: null));
        Assert.True(body.IsInterpolating);
        Step(set, seconds: 1.5);
        Assert.False(body.IsInterpolating);
        Assert.Equal(target.CellId, body.Position.CellId);
        Assert.True(PositionFrames.Distance(target, body.Position) < 0.1f);
    }

    [Fact]
    public void An_object_without_spheres_never_moves_and_blips_to_a_server_position_once_its_interpolation_fails()
    {
        // Retail UpdateObjectInternal: with CPartArray::GetNumSphere 0 the old origin goes back into the new frame, so the
        // interpolation's steps never move the lamp. The first progress check passes against BIG_DISTANCE, the second
        // finds no progress: the node fails, and the position manager blips the lamp to it in the same update. The checks count update steps at retail's
        // 30 Hz (D74, M5 item 5 F7), two of these 60 Hz frames each.
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        var start = AvatarRig.At(new Vector3(50, 50, 0));
        set.Create(new AvatarCreate { Guid = 1, SetupId = AvatarRig.Lamp, PhysicsState = AvatarStateBits.ReportCollisions | AvatarStateBits.AlignPath, Position = start }, now: 0);
        var body = set.All[0].Body;
        var initial = body.Position;
        Assert.Empty(body.Setup.Spheres);
        Assert.True(body.Active);

        var target = AvatarRig.At(new Vector3(53, 50, 0));
        Assert.True(body.MoveOrTeleport(target, teleportAdvanced: false, hasContact: true, placement: null));
        Assert.True(body.IsInterpolating);
        var frames = 0;
        while (body.IsInterpolating)
        {
            Assert.Equal(initial.Origin, body.Position.Origin); // not moved
            Assert.True(Math.Abs(Quaternion.Dot(initial.Orientation, body.Position.Orientation)) > 0.99999f); // AlignPath did not turn it
            Assert.Equal(Vector3.Zero, body.Velocity);
            Assert.True(++frames <= 60, "the lamp should have blipped within a second");
            Step(set, seconds: 1.0 / 60.0);
        }

        Assert.Equal(2 * InterpolationManager.ProgressCheckFrames * 2, frames);
        Assert.True(PositionFrames.Distance(target, body.Position) < 1e-4f, $"at {body.Position}");
    }

    [Fact]
    public void An_object_without_spheres_turns_but_neither_its_velocity_nor_the_ground_probe_moves_it()
    {
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        var ground = new FlatGround(7.5f);
        set.GroundProbe = ground;
        var start = AvatarRig.At(new Vector3(50, 50, 0));
        set.Create(
            new AvatarCreate
            {
                Guid = 1,
                SetupId = AvatarRig.Lamp,
                PhysicsState = AvatarStateBits.ReportCollisions | AvatarStateBits.AlignPath,
                Position = start,
                Velocity = new Vector3(2, 0, 0),
                Omega = new Vector3(0, 0, MathF.PI / 2f),
            },
            now: 0);
        var body = set.All[0].Body;
        Step(set, seconds: 1.0);

        Assert.Equal(start.Origin, body.Position.Origin);
        Assert.Equal(Vector3.Zero, body.Velocity);
        Assert.Equal(0, ground.Calls);
        var turned = 2.0 * Math.Acos(Math.Min(1.0, Math.Abs(Quaternion.Dot(start.Orientation, body.Position.Orientation))));
        Assert.InRange(turned, Math.PI / 2 - 0.05, Math.PI / 2 + 0.05); // only the angular velocity acted
    }

    [Fact]
    public void A_position_96_m_or_more_from_the_player_is_taken_at_once()
    {
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(10, 10, 0)) with { IsSelf = true }, now: 0);
        set.Create(AvatarRig.HumanAt(2, new Vector3(150, 150, 0)), now: 0);
        set.Advance(0.1, Viewer, long.MaxValue); // measures the player distance (about 198 m)
        var body = set.All[1].Body;
        Assert.False(body.Active);
        var target = AvatarRig.At(new Vector3(152, 150, 0));
        Assert.Equal(AvatarApply.Applied, set.Move(2, target, teleportAdvanced: false, hasContact: true, placement: null, now: 0.1));
        Assert.Equal(target.Origin, body.Position.Origin);
        Assert.False(body.IsInterpolating);
    }

    [Fact]
    public void An_object_beyond_96_m_is_not_stepped_until_it_comes_back()
    {
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(10, 10, 0)) with { IsSelf = true }, now: 0);
        set.Create(AvatarRig.HumanAt(2, new Vector3(150, 150, 0)), now: 0);
        set.SetMovement(2, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []);
        var body = set.All[1].Body;
        var start = body.Position;
        Step(set, seconds: 1.0);
        Assert.Equal(start, body.Position);
        Assert.Equal(0, body.Steps);

        set.Player!.Body.Place(AvatarRig.At(new Vector3(140, 140, 0)));
        Step(set, seconds: 1.0);
        Assert.True(body.Steps > 0);
        Assert.NotEqual(start, body.Position);
    }

    [Fact]
    public void A_static_object_without_a_motion_table_is_never_stepped_even_near_the_player()
    {
        // Retail: no movement manager was made before set_description set Static, and prepare_to_enter_world does not
        // activate a Static object; nor is a server object a static animating object (it was made dynamic).
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        var start = AvatarRig.At(new Vector3(50, 50, 0));
        set.Create(
            AvatarRig.HumanAt(1, default) with
            {
                SetupId = AvatarRig.Lamp,
                MotionTableId = 0,
                IsCreature = false,
                HasContact = true,
                PhysicsState = AvatarStateBits.Static | AvatarStateBits.ReportCollisions,
                Position = start,
            },
            now: 0);
        var body = set.All[0].Body;
        Assert.False(body.Active);
        var frame = body.Sequence.CurrentFrameIndex;

        Assert.True(body.MoveOrTeleport(AvatarRig.At(new Vector3(53, 50, 0)), teleportAdvanced: false, hasContact: true, placement: null));
        Assert.True(body.IsInterpolating);
        Step(set, seconds: 2.0);
        Assert.Equal(start, body.Position);
        Assert.Equal(0, body.Steps);
        Assert.False(body.Active);
        Assert.Equal(frame, body.Sequence.CurrentFrameIndex); // its default animation does not play either
    }

    [Fact]
    public void A_static_object_with_a_motion_table_is_active_until_it_is_once_out_of_range()
    {
        // Retail: MakeMovementManager set the active bit before the described state made the object Static; set_active(1)
        // then refuses it, so once the 96 m rule clears the bit, it stays clear.
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(10, 10, 0)) with { IsSelf = true }, now: 0);
        set.Create(AvatarRig.HumanAt(2, new Vector3(20, 20, 0)) with { PhysicsState = AvatarStateBits.Static | AvatarStateBits.Gravity, HasContact = true }, now: 0);
        var body = set.All[1].Body;
        Assert.True(body.Active);
        Step(set, seconds: 0.5);
        var steps = body.Steps;
        Assert.True(steps > 0);

        set.Player!.Body.Place(AvatarRig.At(new Vector3(150, 150, 0)));
        Step(set, seconds: 0.5);
        Assert.False(body.Active);
        set.Player.Body.Place(AvatarRig.At(new Vector3(10, 10, 0)));
        Step(set, seconds: 0.5);
        Assert.False(body.Active);
        Assert.Equal(steps, body.Steps);
    }

    [Fact]
    public void The_ground_probe_clamps_a_grounded_object_to_the_ground()
    {
        var (set, body) = Walker();
        var ground = new FlatGround(7.5f);
        set.GroundProbe = ground;
        Step(set, seconds: 0.2);
        Assert.Equal(7.5f, body.Position.Origin.Z);
        Assert.True(ground.Calls > 0);
    }

    [Fact]
    public void The_pose_version_changes_with_the_keyframe_not_with_every_update()
    {
        var sources = AvatarRig.Sources();
        var set = new AvatarSet(sources);
        set.Create(AvatarRig.HumanAt(1, new Vector3(50, 50, 0)) with { SetupId = AvatarRig.Lamp, MotionTableId = 0, IsCreature = false }, now: 0);
        var body = set.All[0].Body;
        var versions = new List<int>();
        var keyframes = new List<int>();
        for (var i = 1; i <= 48; i++)
        {
            set.Advance(i / 120.0, Viewer, long.MaxValue);
            versions.Add(body.PoseVersion);
            keyframes.Add(body.Sequence.CurrentFrameIndex);
        }

        for (var i = 1; i < versions.Count; i++)
        {
            Assert.Equal(keyframes[i] != keyframes[i - 1], versions[i] != versions[i - 1]);
        }

        Assert.True(versions.Distinct().Count() >= 6);
    }

    private (AvatarSet Set, AvatarBody Body) Walker(
        float scale = 1f, uint state = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions, Vector3? origin = null, uint forward = MotionCommands.WalkForward)
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(1, origin ?? new Vector3(50, 50, 0)) with { Scale = scale, PhysicsState = state }, now: 0);
        if (forward != MotionCommands.Ready)
        {
            Assert.Equal(AvatarApply.Applied, set.SetMovement(1, isSelf: false, autonomous: false, AvatarRig.Moving(forward), []));
        }

        return (set, set.All[0].Body);
    }

    private double _clock;

    private void Step(AvatarSet set, double seconds)
    {
        var end = _clock + seconds;
        while (_clock < end - 1e-9)
        {
            _clock += 1.0 / 60.0;
            set.Advance(_clock, Viewer, long.MaxValue);
        }
    }
}
