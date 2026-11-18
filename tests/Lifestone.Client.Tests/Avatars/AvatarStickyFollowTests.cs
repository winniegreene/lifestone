using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M6 item 14 (<c>m6-live-position</c>): what the body the screen draws does for a monster in melee while the player walks and the server sends no position, measured offline on the avatar rig (no game, no server, no
/// collision host), and what the avatar set answers the session's <see cref="LivePositions"/> for it (<see cref="AvatarSet.TryGetBody"/>).
/// </summary>
/// <remarks>
/// <para><b>The stick.</b> A swing's UpdateMotion carries the sticky object (ACE's <c>Monster_Melee.cs:373-374</c>; the server sticks its own object too, <c>Monster_Melee.cs:70-71</c>), and a MoveTo with the Sticky flag
/// sticks on arrival. For one second (<see cref="StickyManager.StickySeconds"/>) the body is stepped towards the player's live body, 0.3 m short of touching, whatever the server last reported.</para>
/// <para><b>The walk back</b> (<c>TODO(m6-sticky-walk-back)</c>, what the owner sees). ACE sends one position with the first swing after a chase (<c>Monster_Melee.cs:342-346</c>) and none while the monster sticks. When
/// that position is still queued in the interpolation as the stick ends (it is, unless the body stood within 0.05 m of it), retail's interpolation walks the body back to it at twice its run speed, away from the
/// player the server's monster is still beside; the next swing's stick brings the body to her again and, with nothing queued any more, it stays. So between a stick's end and the next swing the body can stand at the
/// server's stale report: there both records are wrong together, and only a blow tells (the fight's struck proof and its far-blow line).</para>
/// </remarks>
public sealed class AvatarStickyFollowTests
{
    private const uint Player = 0x50000001;
    private const uint Creature = 7;
    private const double Frame = 1.0 / 60.0;

    private readonly RecordingLogSink _log = new();
    private double _now;

    [Fact]
    public void A_creature_stuck_to_the_walking_player_follows_her_with_no_server_position_and_stays_when_the_stick_ends()
    {
        var (set, player, creature) = PlayerAndCreature();
        var start = creature.Position.Origin;

        // The swing, with no position before it: the stick begins.
        Swing(set);
        Walk(set, player, seconds: 1.0, metresPerSecond: 6f); // she walks 6 m east in the stick's second
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) < 1.5f, $"the creature is {Vector3.Distance(creature.Position.Origin, player.Position.Origin):F2} m from her while stuck");
        Assert.True(Vector3.Distance(creature.Position.Origin, start) > 4f);

        Walk(set, player, seconds: 3.0, metresPerSecond: 0f); // the stick has ended; nothing is queued, so nothing calls it back
        Assert.Equal(0u, creature.Positions.GetStickyObjectId());
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) < 1.5f, "the creature left her after the stick ended");

        // What the session's live positions are told: the body, placed, drawn, in step with the server.
        Assert.True(set.TryGetBody(Creature, out var body));
        Assert.Equal(creature.Position, body.Position);
        Assert.True(body.InWorld);
        Assert.False(body.Hidden);
        Assert.False(body.ServerPositionPending);
        Assert.False(set.TryGetBody(0x12345678, out _));
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    [InlineData(5f)]
    public void With_the_swings_server_position_still_queued_the_body_walks_back_to_it_when_the_stick_ends_and_the_next_swing_brings_it_to_stay(float off)
    {
        var (set, player, creature) = PlayerAndCreature();

        // ACE's order at the first swing after a chase: the position, then the swing, in one tick. The server's spot lies a little beside the body's (off metres west).
        var reported = AvatarRig.At(creature.Position.Origin - new Vector3(off, 0, 0), 180f);
        Assert.Equal(AvatarApply.Applied, set.Move(Creature, reported, teleportAdvanced: false, hasContact: true, placement: null, _now));
        Swing(set);
        Walk(set, player, seconds: 1.0, metresPerSecond: 6f);
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) < 1.5f, "the creature did not follow her while stuck");
        Assert.Single(creature.Positions.Interpolation.Queue); // the swing's position is still queued

        // The stick ends: the interpolation takes the body back to the server's last position, 6 m behind her, and there it stands.
        Walk(set, player, seconds: 2.0, metresPerSecond: 0f);
        Assert.True(Vector3.Distance(creature.Position.Origin, reported.Origin) < 0.1f, $"the creature is {Vector3.Distance(creature.Position.Origin, reported.Origin):F2} m from the server's position");
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) > 4.5f);
        Assert.True(set.TryGetBody(Creature, out var back));
        Assert.False(back.ServerPositionPending); // the body is where the server last said: nothing marks it as wrong

        // The next swing (ACE sends no position with it): the stick brings the body to her, and with nothing queued it stays.
        Swing(set);
        Walk(set, player, seconds: 1.5, metresPerSecond: 0f);
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) < 1.5f, "the next swing did not bring the creature back to her");
        Walk(set, player, seconds: 3.0, metresPerSecond: 0f);
        Assert.True(Vector3.Distance(creature.Position.Origin, player.Position.Origin) < 1.5f, "the creature left her again");
        Assert.True(Vector3.Distance(creature.Position.Origin, reported.Origin) > 4.5f); // metres from the server's report: the gap the fight's measure logs
    }

    [Fact]
    public void A_body_that_ignored_a_position_without_contact_is_marked_until_it_takes_the_next_one()
    {
        var (set, player, creature) = PlayerAndCreature();
        var at = creature.Position;

        // Retail ignores a position without contact (CPhysicsObj::MoveOrTeleport): the body stays, and no longer knows where the server has the object.
        Assert.Equal(AvatarApply.Ignored, set.Move(Creature, AvatarRig.At(at.Origin + new Vector3(0, 6, 3), 180f), teleportAdvanced: false, hasContact: false, placement: null, _now));
        Walk(set, player, seconds: 0.5, metresPerSecond: 0f);
        Assert.Equal(at.Origin, creature.Position.Origin);
        Assert.True(creature.ServerPositionPending);
        Assert.True(set.TryGetBody(Creature, out var body));
        Assert.True(body.ServerPositionPending);

        Assert.Equal(AvatarApply.Applied, set.Move(Creature, AvatarRig.At(at.Origin + new Vector3(0, 2, 0), 180f), teleportAdvanced: false, hasContact: true, placement: null, _now));
        Assert.False(creature.ServerPositionPending);
    }

    /// <summary>The player at (30, 20) with a kinematic body, and a creature at melee distance north of it facing it; both settled.</summary>
    private (AvatarSet Set, KinematicPlayerBody Player, AvatarBody Creature) PlayerAndCreature()
    {
        var set = new AvatarSet(AvatarRig.Sources(), _log);
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(Player, new Vector3(30, 20, 0)) with { IsSelf = true }, now: 0));
        var player = new KinematicPlayerBody(set.Player!, set);
        set.PlayerBody = player;
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(Creature, new Vector3(30, 21.3f, 0), heading: 180f), now: 0));
        Walk(set, player, seconds: 10 * Frame, metresPerSecond: 0f);
        return (set, player, set.All[1].Body);
    }

    /// <summary>A swing as ACE broadcasts it: an interpreted motion that sticks the creature to the player.</summary>
    private static void Swing(AvatarSet set)
    {
        var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: Player);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(Creature, isSelf: false, autonomous: false, swing, []));
    }

    /// <summary>Steps the player east at <paramref name="metresPerSecond"/> and the set, at 60 Hz, for <paramref name="seconds"/>.</summary>
    private void Walk(AvatarSet set, KinematicPlayerBody player, double seconds, float metresPerSecond)
    {
        var frames = (int)Math.Round(seconds / Frame);
        for (var i = 0; i < frames; i++)
        {
            _now += Frame;
            if (metresPerSecond != 0f)
            {
                var at = player.Position;
                player.Place(at with { Frame = at.Frame with { Origin = at.Origin + new Vector3(metresPerSecond * (float)Frame, 0, 0) } });
            }

            player.Advance(_now);
            set.Advance(_now, new AvatarViewer(player.Position), long.MaxValue);
        }
    }
}
