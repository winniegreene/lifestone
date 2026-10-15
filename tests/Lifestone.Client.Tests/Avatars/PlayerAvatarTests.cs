using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The player seam (AV10): the player's avatar draws what the player body moves and animates, the body hosts the
/// interpreter on the avatar's part array, and the player's server movements reach it while its autonomous echoes do not.
/// </summary>
public sealed class PlayerAvatarTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void The_kinematic_body_walks_the_players_avatar_and_the_avatar_draws_its_pose()
    {
        var (set, body) = Bound(AvatarRig.Sources(), AvatarRig.HumanAt(Player, new Vector3(30, 30, 0)) with { IsSelf = true });
        var avatar = set.Player!;
        Assert.Same(avatar.Parts, body.Motion.Parts);
        Assert.Equal(WeenieError.None, body.Motion.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
        for (var i = 1; i <= 60; i++)
        {
            body.Advance(i / 60.0);
            set.Advance(i / 60.0, Viewer, long.MaxValue);
        }

        Assert.True(body.Position.Origin.Y > 31.5f);
        Assert.Equal(body.Position, avatar.Body.Position);
        Assert.Equal(MotionCommands.WalkForward, avatar.Parts!.State.Substate);
        AvatarAssert.Near(avatar.Body.PartFrames[0].Origin, Data.Animation.PartPose.Compose(avatar.Body.AnchorFrame, avatar.Parts.Pose, 1f, 3)[0].Origin);
        Assert.True(avatar.Body.Velocity.Y > 2f); // measured from the body's moves: what voyeurs predict with
    }

    [Fact]
    public void The_players_server_movement_reaches_the_body_and_its_autonomous_echo_does_not()
    {
        var (set, body) = Bound(AvatarRig.Sources(), AvatarRig.HumanAt(Player, new Vector3(30, 30, 0)) with { IsSelf = true });
        Assert.Equal(AvatarApply.Ignored, set.SetMovement(Player, isSelf: true, autonomous: true, AvatarRig.Moving(MotionCommands.WalkForward), []));
        Assert.Equal(MotionCommands.Ready, body.Motion.Interpreted.ForwardCommand);

        Assert.Equal(AvatarApply.Applied, set.SetMovement(Player, isSelf: true, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []));
        Assert.Equal(MotionCommands.WalkForward, body.Motion.Interpreted.ForwardCommand);
        Assert.False(body.MovementIsAutonomous);
    }

    [Fact]
    public void The_player_draws_level_0_at_any_distance_and_first_person_hides_the_body()
    {
        var sources = AvatarRig.Sources();
        var (set, body) = Bound(sources, AvatarRig.HumanAt(Player, new Vector3(30, 30, 0)) with { IsSelf = true });
        set.Create(AvatarRig.HumanAt(7, new Vector3(31, 30, 0)), now: 0);
        var far = new AvatarViewer(AvatarRig.At(new Vector3(30, 30, 0), cell: 0x40400019)); // tens of km away
        set.Advance(0.1, far, long.MaxValue);
        var player = set.Frame.Avatars[0];
        Assert.True(player.IsPlayer);
        Assert.True(player.Visible);
        var parts = set.Frame.Parts.Slice(player.PartStart, player.PartCount);
        for (var i = 0; i < parts.Length; i++)
        {
            Assert.Equal(set.Player!.Body.Appearance.Parts[i].Chain.NearestGfxObjId, parts[i].GfxObjId);
            Assert.Equal(DegradeMode.Normal, parts[i].Mode);
        }

        set.Player!.FirstPerson = true;
        set.Advance(0.2, far, long.MaxValue);
        Assert.False(set.Frame.Avatars[0].Visible);
        Assert.True(set.Frame.Avatars[1].Visible);
    }

    [Fact]
    public void A_server_moveto_for_the_player_turns_and_walks_it_there()
    {
        var (set, body) = Bound(AvatarRig.Sources(), AvatarRig.HumanAt(Player, new Vector3(30, 30, 0)) with { IsSelf = true });
        var target = AvatarRig.At(new Vector3(30, 36, 0));
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards,
            DistanceToObject = 0.6f,
        };
        var moveTo = new WireMovement(
            WireMovementType.MoveToPosition, MotionCommandTable.ToWire(MotionCommands.NonCombat), false, false, default, MoveTo: new WireMoveTo(0, target, parameters, 1f));
        Assert.Equal(AvatarApply.Applied, set.SetMovement(Player, isSelf: true, autonomous: false, moveTo, []));
        Assert.True(body.Movement.IsMovingTo);
        for (var i = 1; i <= 240 && body.Movement.IsMovingTo; i++)
        {
            body.Advance(i / 60.0);
        }

        Assert.False(body.Movement.IsMovingTo);
        Assert.True(PositionFrames.Distance(target, body.Position) <= 0.6f + 0.15f);
        Assert.Equal(MotionCommands.Ready, body.Motion.Interpreted.ForwardCommand);
    }

    /// <summary>
    /// Every DoD-7 state through the human motion table (0x09000001) on the human Setup (0x02000001) with the kinematic
    /// body: ready, walk, run, turn, sidestep, jump, fall, land.
    /// </summary>
    [RequiresDatsFact]
    public void Every_movement_state_plays_through_the_human_motion_table()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var (set, body) = Bound(sources, new AvatarCreate
        {
            Guid = Player,
            IsSelf = true,
            IsCreature = true,
            SetupId = 0x02000001,
            MotionTableId = 0x09000001,
            PhysicsState = AvatarStateBits.Gravity,
            Position = AvatarRig.At(new Vector3(84, 7, 94)),
        });
        var parts = set.Player!.Parts!;
        var p = new MovementParameters();
        var now = 0.0;
        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 1.0 / 60.0)
            {
                now += 1.0 / 60.0;
                body.Advance(now);
                set.Advance(now, Viewer, long.MaxValue);
            }
        }

        Assert.Equal(MotionCommands.Ready, parts.State.Substate);
        body.Motion.DoMotion(MotionCommands.WalkForward, p);
        Run(0.5);
        Assert.Equal(MotionCommands.WalkForward, parts.State.Substate);
        body.Motion.SetHoldRun(true, cancelMoveTo: false);
        Run(0.5);
        Assert.Equal(MotionCommands.RunForward, parts.State.Substate);
        body.Motion.StopMotion(MotionCommands.WalkForward, p);
        body.Motion.SetHoldRun(false, cancelMoveTo: false);
        Run(0.5);
        Assert.Equal(MotionCommands.Ready, parts.State.Substate);

        // Standing, a turn and a sidestep are the motion table's substates (moving, they would be modifiers).
        var heading = body.Heading;
        body.Motion.DoMotion(MotionCommands.TurnRight, p);
        Run(0.5);
        Assert.Equal(MotionCommands.TurnRight, parts.State.Substate);
        Assert.NotEqual(heading, body.Heading);
        body.Motion.StopMotion(MotionCommands.TurnRight, p);
        body.Motion.DoMotion(MotionCommands.SideStepRight, p);
        Run(0.5);
        Assert.Equal(MotionCommands.SideStepRight, parts.State.Substate);
        body.Motion.StopMotion(MotionCommands.SideStepRight, p);
        Run(0.5);
        Assert.Equal(MotionCommands.Ready, parts.State.Substate);

        Assert.Equal(WeenieError.None, body.Motion.Jump(0.5f, out _));
        Assert.False(body.OnWalkable);
        Run(0.2);
        Assert.Equal(MotionCommands.Falling, parts.State.Substate);
        body.SetContact(true);
        Run(0.5);
        Assert.Equal(MotionCommands.Ready, parts.State.Substate);
    }

    private static (AvatarSet Set, KinematicPlayerBody Body) Bound(AvatarSources sources, AvatarCreate create)
    {
        var set = new AvatarSet(sources);
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        var body = new KinematicPlayerBody(set.Player!, set);
        body.Place(create.Position!.Value);
        set.PlayerBody = body;
        return (set, body);
    }
}
