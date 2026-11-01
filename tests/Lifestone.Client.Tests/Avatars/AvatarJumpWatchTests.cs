using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The server objects' jump and flip detector (<see cref="AvatarJumpWatch"/>, M4 hero visual bugs: a creature by the looting Archer drawn in two places on
/// alternate frames, docs/milestones/m4-hero-visual-bugs.md), and the one mechanism the offline probes reproduced: a creature stuck to the client's player
/// far from where the server last put it blips back there when its stick ends.
/// </summary>
public sealed class AvatarJumpWatchTests
{
    private const uint Player = 0x50000001;
    private const uint Creature = 7;
    private const double Frame = 1.0 / 60.0;

    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(30, 20, 2)));

    private readonly RecordingLogSink _log = new();
    private double _now;

    [Fact]
    public void A_creature_stuck_to_the_player_away_from_the_servers_position_blips_back_when_its_stick_ends_and_the_jump_is_logged()
    {
        var (set, creature) = PlayerAndCreature();

        // The server's creature stands 2.5 m west of the client's (ACE's own sticky, with its collisions, put it elsewhere); the swing sticks the
        // client's creature to the client's player, which the server has east of where the client stands it.
        // The swing comes eight frames after the position: where the interpolation's progress check falls decides whether the stick's end blips or slides
        // back at twice the run speed. Since D74 (M5 item 5 F7) the check comes every five update steps at retail's 30 Hz, ten of these 60 Hz frames, so
        // three phases of ten blip (7, 8 and 9 frames; before D74 the check was every five drawn frames and three frames blipped).
        Assert.Equal(AvatarApply.Applied, set.Move(Creature, AvatarRig.At(new Vector3(27.5f, 23f, 0), 180f), teleportAdvanced: false, hasContact: true, placement: null, _now));
        Advance(set, frames: 8);
        var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: Player);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(Creature, isSelf: false, autonomous: false, swing, []));
        Advance(set, frames: 20);
        Assert.NotNull(creature.Positions.Sticky);
        Assert.True(creature.Positions.Sticky!.Initialized);
        Assert.Equal(0, set.Stats.CreatureJumps);
        var stuck = creature.Position.Origin;

        // The stick lasts a second (StickyManager.StickySeconds); the first frames after it the interpolation measures no progress over the stuck frames and
        // blips to the server's position: a 2.5 m jump in one frame, which the watch names with the blip as its cause.
        Advance(set, frames: 60);
        Assert.Equal(0u, creature.Positions.GetStickyObjectId());
        Assert.True(Vector3.Distance(creature.Position.Origin, new Vector3(27.5f, 23f, 0)) < 0.05f, $"the creature is at {creature.Position.Origin}, not at the server's position");
        Assert.True(Vector3.Distance(stuck, creature.Position.Origin) > 2f);
        Assert.Equal(1, set.Stats.CreatureJumps);
        var line = Assert.Single(_log.Entries, e => e.Message.Contains("TODO(hero-creature-flicker)", StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Warning, line.Severity);
        Assert.Contains(line.Fields, f => f.Key == "kind" && (string?)f.Value == nameof(AvatarJumpKind.RootJump));
        Assert.Contains(line.Fields, f => f.Key == "placed" && (string?)f.Value == nameof(AvatarPlacement.Blip));

        // M5 item 5 F4: the line names what held the creature back; without a host no transition ran, so nothing did, and the blip is counted as unexplained.
        Assert.Contains(line.Fields, f => f.Key == "short_transits" && Equals(0, f.Value));
        Assert.Contains(line.Fields, f => f.Key == "blocked_by" && (string?)f.Value == "none");
        Assert.Contains(line.Fields, f => f.Key == "step_refused" && Equals(false, f.Value));
        var behind = float.Parse((string)line.Fields.Single(f => f.Key == "behind_m").Value!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(behind > 1.5f, $"behind {behind} m");
        Assert.Equal(1L, set.Stats.BlipCauses.None);
        Assert.Equal(1L, set.Stats.BlipCauses.Total);
        Assert.Equal(set.Stats.BlipCauses, set.Stats.PhysicsCost.Blips);
    }

    /// <summary>
    /// The fix of m4-hero-visual-bugs §1.3 (M5 item 5 t1, D45): with the server-object host's transitions, two creatures stuck to the
    /// player stop at each other instead of overlapping, so the client's sticky spots are where a server that collides them
    /// (ACE runs retail's collisions) puts them, and the end of the stick no longer snaps them. A first run finds the collided
    /// spots (it stands in for the server); a second sends them as the server's positions before the swings, and after the
    /// sticks end neither creature moves more than the detector's metre in a frame. Retail's sticky and interpolation rules are
    /// unchanged; the case without a host above still proves the detector.
    /// </summary>
    [Fact]
    public void Two_creatures_stuck_to_the_player_with_the_hosts_transitions_end_apart_and_do_not_snap_when_their_sticks_end()
    {
        var probe = HostedPair(server: null);
        var apart = Vector3.Distance(probe.Spots[0].Origin, probe.Spots[1].Origin);
        Assert.True(apart >= 0.95f * 0.96f, $"the two creatures overlap while stuck: {apart:F2} m apart (0.96 m touching)");

        var run = HostedPair(server: probe.Spots);
        Assert.Equal(0, run.Set.Stats.CreatureJumps);
        Assert.Equal(0, run.Set.Stats.CreatureFlips);
        Assert.True(run.LargestStep < 1f, $"a creature moved {run.LargestStep:F2} m in one frame after its stick ended");
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("hero-creature-flicker", StringComparison.Ordinal));
        for (var i = 0; i < 2; i++)
        {
            Assert.True(Vector3.Distance(run.Ends[i], probe.Spots[i].Origin) < 0.5f, $"creature {i} ended at {run.Ends[i]}, not at the server's {probe.Spots[i].Origin}");
        }
    }

    [Fact]
    public void A_teleport_is_not_a_jump()
    {
        var (set, creature) = PlayerAndCreature();
        Assert.Equal(AvatarApply.Applied, set.Move(Creature, AvatarRig.At(new Vector3(60, 60, 0)), teleportAdvanced: true, hasContact: true, placement: null, _now));
        Advance(set, frames: 10);
        Assert.Equal(new Vector3(60, 60, 0), creature.Position.Origin);
        Assert.Equal(0, set.Stats.CreatureJumps);
        Assert.Equal(0, set.Stats.CreatureFlips);
    }

    [Fact]
    public void Two_places_on_alternate_frames_are_a_flip()
    {
        var (set, creature) = PlayerAndCreature();
        var a = creature.Position;
        var b = AvatarRig.At(a.Origin + new Vector3(1.5f, 0, 0));
        creature.Place(b);
        Advance(set, frames: 1);
        creature.Place(a);
        Advance(set, frames: 1);
        Assert.Equal(1, set.Stats.CreatureFlips);
        Assert.Contains(_log.Entries, e => e.Fields.Any(f => f.Key == "kind" && (string?)f.Value == nameof(AvatarJumpKind.RootFlip)));
    }

    [Fact]
    public void A_creature_walking_and_turning_is_not_reported()
    {
        var (set, creature) = PlayerAndCreature();
        Assert.Equal(AvatarApply.Applied, set.SetMovement(Creature, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []));
        var start = creature.Position.Origin;
        Advance(set, frames: 120);
        Assert.True(Vector3.Distance(start, creature.Position.Origin) > 4f, "the creature did not walk");
        Assert.Equal(AvatarApply.Applied, set.SetMovement(Creature, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward, turn: MotionCommands.TurnRight), []));
        Advance(set, frames: 120);
        Assert.Equal(0, set.Stats.CreatureJumps);
        Assert.Equal(0, set.Stats.CreatureFlips);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("hero-creature-flicker", StringComparison.Ordinal));
    }

    /// <summary>The player at (30, 20) with a kinematic body, and a creature 3 m north of it facing it; both settled.</summary>
    private (AvatarSet Set, AvatarBody Creature) PlayerAndCreature()
    {
        var set = new AvatarSet(AvatarRig.Sources(), _log);
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(Player, new Vector3(30, 20, 0)) with { IsSelf = true }, now: 0));
        set.PlayerBody = new KinematicPlayerBody(set.Player!, set);
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(Creature, new Vector3(30, 23, 0), heading: 180f), now: 0));
        Advance(set, frames: 10);
        return (set, set.All[1].Body);
    }

    /// <summary>
    /// The player (a physics object in a flat Holtburg and its avatar) at (30, 20) and two creatures 3 m north of it, 1.2 m apart,
    /// with host objects (the human's two spheres, 0.48 m); with <paramref name="server"/> each is first sent its server position;
    /// then both swing, stuck to the player. Answers where they stand stuck, and after their sticks end the largest one-frame step
    /// and where they end.
    /// </summary>
    private (AvatarSet Set, CellFrame[] Spots, float LargestStep, Vector3[] Ends) HostedPair(CellFrame[]? server)
    {
        var world = MovementTestKit.WorldOf(MovementTestKit.Flat(LandblockCoord.Holtburg));
        var player = MovementTestKit.Player(world, HoltburgAt(new Vector3(30, 20, 0)));
        Assert.Equal(Player, player.Id);
        var host = new ServerObjectPhysicsHost(world, id => id == AvatarRig.Human ? MovementTestKit.HumanSetup : null);
        var set = new AvatarSet(AvatarRig.Sources(), _log) { ServerObjects = host };
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(Player, player.Position.Origin, cell: player.Position.CellId) with { IsSelf = true }, now: 0));
        set.PlayerBody = new KinematicPlayerBody(set.Player!, set);
        uint[] guids = [Creature, Creature + 1];
        Vector3[] starts = [new(29.4f, 23f, 0f), new(30.6f, 23f, 0f)];
        for (var i = 0; i < 2; i++)
        {
            var at = HoltburgAt(starts[i], 180f);
            Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(guids[i], at.Origin, 180f, at.CellId) with { HasContact = true }, _now));
        }

        var bodies = set.All.Where(a => !a.IsPlayer).Select(a => a.Body).ToArray();
        Advance(set, frames: 10);
        if (server is not null)
        {
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal(AvatarApply.Applied, set.Move(guids[i], server[i], teleportAdvanced: false, hasContact: true, placement: null, _now));
            }

            Advance(set, frames: 3);
        }

        var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: Player);
        foreach (var guid in guids)
        {
            Assert.Equal(AvatarApply.Applied, set.SetMovement(guid, isSelf: false, autonomous: false, swing, []));
        }

        Advance(set, frames: 20);
        Assert.All(bodies, b => Assert.Equal(Player, b.Positions.GetStickyObjectId()));
        Assert.True(set.Stats.Transitions > 0, "no transition ran: the host is not stepping the creatures");
        var spots = bodies.Select(b => b.Position).ToArray();
        var largest = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            var before = bodies.Select(b => b.Position.Origin).ToArray();
            Advance(set, frames: 1);
            for (var i = 0; i < bodies.Length; i++)
            {
                largest = Math.Max(largest, Vector3.Distance(before[i], bodies[i].Position.Origin));
            }
        }

        Assert.All(bodies, b => Assert.Equal(0u, b.Positions.GetStickyObjectId()));
        return (set, spots, largest, bodies.Select(b => b.Position.Origin).ToArray());
    }

    private static CellFrame HoltburgAt(Vector3 origin, float heading = 0f) =>
        AvatarRig.At(origin, heading, CellIds.LandCellAt(LandblockCoord.Holtburg, origin.X, origin.Y)!.Value);

    private void Advance(AvatarSet set, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            _now += Frame;
            set.Advance(_now, Viewer, long.MaxValue);
        }
    }
}
