using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

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
        // The swing comes three frames after the position: where the interpolation's 5-frame progress check falls decides whether the stick's end blips
        // (two phases of five) or slides back at twice the run speed (the other three).
        Assert.Equal(AvatarApply.Applied, set.Move(Creature, AvatarRig.At(new Vector3(27.5f, 23f, 0), 180f), teleportAdvanced: false, hasContact: true, placement: null, _now));
        Advance(set, frames: 3);
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

    private void Advance(AvatarSet set, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            _now += Frame;
            set.Advance(_now, Viewer, long.MaxValue);
        }
    }
}
