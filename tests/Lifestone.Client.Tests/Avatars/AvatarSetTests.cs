using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The avatar set (AV8): the reducer's classified events applied with retail's object semantics, no queue and no stamp
/// gate of its own; unknown objects and ignored messages counted and noticed.
/// </summary>
public sealed class AvatarSetTests
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void A_create_builds_the_object_in_the_world_in_its_motion_tables_default_state()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(7, new Vector3(40, 50, 2)), now: 0));
        var avatar = set.All.Single();
        Assert.True(avatar.Body.InWorld);
        Assert.Equal(new Vector3(40, 50, 2), avatar.Body.Position.Origin);
        Assert.NotNull(avatar.Body.Movement);
        Assert.Equal(MotionCommands.Ready, avatar.Body.Parts!.State.Substate);
        Assert.Equal(MotionCommands.NonCombat, avatar.Body.Motion!.InqStyle());
        Assert.Equal([new AvatarNotice(AvatarNoticeKind.Created, 7)], Drain(set));
        Assert.Equal(1, set.Stats.Created);
    }

    [Fact]
    public void A_create_with_an_unknown_setup_fails_loudly()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        Assert.Equal(AvatarApply.Failed, set.Create(AvatarRig.HumanAt(7, default) with { SetupId = 0x02FFFFFF }, now: 0));
        Assert.Equal(0, set.Count);
        var notice = Assert.Single(Drain(set));
        Assert.Equal(AvatarNoticeKind.CreateFailed, notice.Kind);
        Assert.Contains("0x02FFFFFF", notice.Detail, StringComparison.Ordinal);
        Assert.Equal(1, set.Stats.CreateFailures);
    }

    [Fact]
    public void A_recreate_replaces_the_avatar_and_a_delete_removes_it()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Apply(new ObjectCreated(7, IsSelf: false, InWorld: true, Recreated: false), AvatarRig.HumanAt(0, new Vector3(10, 10, 0)), now: 0);
        var first = set.All.Single().Body;
        set.Apply(new ObjectCreated(7, IsSelf: false, InWorld: true, Recreated: true), AvatarRig.HumanAt(0, new Vector3(20, 10, 0)), now: 1);
        var second = set.All.Single().Body;
        Assert.NotSame(first, second);
        Assert.Equal(new Vector3(20, 10, 0), second.Position.Origin);
        Assert.Equal(1, set.Stats.Recreated);

        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectDeleted(7)));
        Assert.Equal(0, set.Count);
        Assert.Equal(AvatarApply.UnknownObject, set.Apply(new ObjectDeleted(7)));
        Assert.Contains(new AvatarNotice(AvatarNoticeKind.UnknownObject, 7), Drain(set));
    }

    [Fact]
    public void An_inventory_item_is_created_but_not_drawn()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword }, now: 0);
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(1, set.Count);
        Assert.False(set.All[0].Body.InCell);
        Assert.Equal(0, set.Frame.Avatars.Length);
    }

    [Fact]
    public void A_child_created_before_its_parent_waits_for_it()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        var sword = set.All[0].Body;
        Assert.Null(sword.Parent);
        Assert.False(sword.InCell);

        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        Assert.Same(set.All[1].Body, sword.Parent);
        Assert.True(sword.InCell);
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(2, set.Frame.Avatars.Length);
    }

    [Fact]
    public void A_parent_event_to_a_location_the_parent_lacks_fails_and_changes_nothing()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, Position = AvatarRig.At(new Vector3(31, 30, 0)) }, now: 0);
        Drain(set);
        Assert.Equal(AvatarApply.Failed, set.Apply(new ObjectParented(9, 7, Location: 2, Placement: 0)));
        var sword = set.All[1].Body;
        Assert.Null(sword.Parent);
        Assert.True(sword.InWorld);
        Assert.Equal(AvatarNoticeKind.ParentFailed, Assert.Single(Drain(set)).Kind);
    }

    [Fact]
    public void A_remote_motion_plays_and_moves_the_object()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        var body = set.All[0].Body;
        var motion = new ServerMotionState { MovementType = MovementType.Interpreted, Stance = MotionCommandTable.ToWire(MotionCommands.NonCombat), Forward = MotionCommandTable.ToWire(MotionCommands.WalkForward) };
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectMotionChanged(7, IsSelf: false, Autonomous: false, motion), now: 0));
        Assert.Equal(MotionCommands.WalkForward, body.Motion!.Interpreted.ForwardCommand);
        Assert.False(body.MovementIsAutonomous);
        for (var i = 1; i <= 30; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.True(body.Position.Origin.Y > 31f);
        Assert.Equal(1, set.Stats.Motions);
    }

    [Fact]
    public void A_motion_with_an_unknown_low_word_is_not_applied_and_is_reported()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        Drain(set);
        var motion = new ServerMotionState { MovementType = MovementType.Interpreted, Stance = MotionCommandTable.ToWire(MotionCommands.NonCombat), Forward = 0xFFF0 };
        Assert.Equal(AvatarApply.Failed, set.Apply(new ObjectMotionChanged(7, false, false, motion), now: 0));
        Assert.Equal(MotionCommands.Ready, set.All[0].Body.Motion!.Interpreted.ForwardCommand);
        var notice = Assert.Single(Drain(set));
        Assert.Equal(AvatarNoticeKind.MotionFailed, notice.Kind);
        Assert.Contains("0xFFF0", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_players_autonomous_echo_is_ignored_and_without_a_body_its_server_motion_too()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(30, 30, 0)) with { IsSelf = true }, now: 0);
        var walk = AvatarRig.Moving(MotionCommands.WalkForward);
        Assert.Equal(AvatarApply.Ignored, set.SetMovement(0x50000001, isSelf: true, autonomous: true, walk, []));
        Assert.Equal(AvatarApply.Ignored, set.SetMovement(0x50000001, isSelf: true, autonomous: false, walk, []));
        Assert.Equal(1, set.Stats.AutonomousEchoesIgnored);
        Assert.Equal(1, set.Stats.PlayerMotionsWithoutBody);
        Assert.Null(set.Player!.Body.Motion); // the player's interpreter is the player body's
        Assert.NotNull(set.Player.Parts);
    }

    [Fact]
    public void Remote_positions_interpolate_and_the_players_own_are_left_to_its_body()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(10, 10, 0)) with { IsSelf = true }, now: 0);
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        var moved = new ObjectMoved(7, AvatarRig.At(new Vector3(32, 30, 0)), PositionVerdict.Remote, false, HasContact: true, null, null);
        Assert.Equal(AvatarApply.Applied, set.Apply(moved, now: 0));
        Assert.True(set.All[1].Body.IsInterpolating);

        // Without a body the set places the player; with one, the body owns the player's positions.
        var self = new ObjectMoved(0x50000001, AvatarRig.At(new Vector3(12, 10, 0)), PositionVerdict.SelfEcho, false, true, null, null);
        Assert.Equal(AvatarApply.Applied, set.Apply(self, now: 0));
        Assert.Equal(new Vector3(12, 10, 0), set.Player!.Body.Position.Origin);
        set.PlayerBody = new KinematicPlayerBody(set.Player, set);
        Assert.Equal(AvatarApply.Ignored, set.Apply(self with { Position = AvatarRig.At(new Vector3(14, 10, 0)) }, now: 0));
        Assert.Equal(1, set.Stats.SelfMovesLeftToBody);
    }

    [Fact]
    public void An_objdesc_change_applies_from_the_default_and_bumps_the_appearance()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        var body = set.All[0].Body;
        var version = body.Appearance.Version;
        var model = new ModelData(null, [], [], [new Lifestone.Net.Types.AnimPartChange(1, 0x01FF0099)]);
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectDescChanged(7, model)));
        Assert.True(body.Appearance.Version > version);
        Assert.Equal(0x01FF0099u, body.Appearance.Parts[1].Chain.RootGfxObjId);
    }

    [Fact]
    public void Effect_events_without_a_sink_are_counted_as_dropped()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.All[0].Body.Effects.ExecuteHook(new SoundHook(AnimHookDirection.Both, 0x0A000001));
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(1, set.Stats.EffectEventsDropped);
    }

    [Fact]
    public void A_target_id_finds_the_player_body_when_it_can_be_targeted()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(10, 10, 0)) with { IsSelf = true }, now: 0);
        Assert.Same(set.Player!.Body, set.FindObject(0x50000001));
        var body = new KinematicPlayerBody(set.Player, set);
        set.PlayerBody = body;
        Assert.Same(body, set.FindObject(0x50000001));
        Assert.Null(set.FindObject(0x12345678));
    }

    [Fact]
    public void The_budget_stops_the_round_robin_and_every_avatar_gets_its_turn_within_three_frames()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        for (uint i = 1; i <= 40; i++)
        {
            set.Create(AvatarRig.HumanAt(i, new Vector3(i, 50, 0)), now: 0);
        }

        var lastSteps = set.All.Select(a => a.Body.Steps).ToArray();
        var turns = new int[40];
        for (var frame = 1; frame <= 3; frame++)
        {
            // A deadline already passed: retail's round-robin checks it every eighth avatar, so 8 run per frame...
            set.Advance(frame / 30.0, Viewer, deadlineTimestamp: 0);
            Assert.False(set.Stats.LastFrameComplete);
            Assert.Equal(8, set.Stats.SteppedLastFrame);
        }

        // ...and with the budget restored the next frames finish the round.
        set.Advance(4 / 30.0, Viewer, long.MaxValue);
        Assert.True(set.Stats.LastFrameComplete);
        for (var i = 0; i < 40; i++)
        {
            turns[i] = (int)(set.All[i].Body.Steps - lastSteps[i]);
            Assert.True(turns[i] > 0, $"avatar {i} was never stepped");
        }
    }

    [Fact]
    public void An_object_with_a_motion_table_and_no_spheres_is_counted_and_its_setup_is_named_once()
    {
        var log = new RecordingLogSink();
        var set = new AvatarSet(AvatarRig.Sources(), log);
        for (uint guid = 1; guid <= 2; guid++)
        {
            set.Create(new AvatarCreate { Guid = guid, SetupId = AvatarRig.Lamp, MotionTableId = AvatarRig.Table, Position = AvatarRig.At(new Vector3(50 + guid, 50, 0)) }, now: 0);
        }

        set.Create(AvatarRig.HumanAt(3, new Vector3(60, 50, 0)), now: 0);
        Assert.Equal(2, set.Stats.SpherelessMotionObjects);
        Assert.Single(log.Lines, line => line.Contains("TODO(avatars)", StringComparison.Ordinal) && line.Contains("no spheres", StringComparison.Ordinal));
    }

    [Fact]
    public void Notices_nobody_drains_are_capped_and_the_overflow_is_counted_and_logged_once()
    {
        // dod-6-7-6: play mode never drains the list, so it grew for the whole session.
        var log = new RecordingLogSink();
        var set = new AvatarSet(AvatarRig.Sources(), log);
        for (var i = 0; i < AvatarSet.MaxNotices + 50; i++)
        {
            Assert.Equal(AvatarApply.UnknownObject, set.Apply(new ObjectDeleted(1000u + (uint)i)));
        }

        var notices = Drain(set);
        Assert.Equal(AvatarSet.MaxNotices, notices.Count);
        Assert.Equal(50, set.Stats.NoticesDropped);
        Assert.Equal(1000u + AvatarSet.MaxNotices + 49, notices[^1].Guid); // the newest are kept, the oldest dropped
        Assert.Single(log.Lines, line => line.Contains("notice list is full", StringComparison.Ordinal));
    }

    private static List<AvatarNotice> Drain(AvatarSet set)
    {
        var notices = new List<AvatarNotice>();
        set.DrainNotices(notices);
        return notices;
    }
}
