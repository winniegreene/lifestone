using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 2.1 s2b item 1: the player's body plays the cast the server sends (ACE's UpdateMotion 0xF74C per wind-up and for the talisman's gesture, stance Magic, <c>Player_Magic.cs</c> 598-610, 977-990;
/// <c>EnqueueMotionMagic</c>) on the real dats: the player's avatar and the physics player body of the explore mode (<see cref="OfflineWalker"/>, the body the game binds, with the avatar's hook forwarding),
/// fed the stand-in's own sequence through <see cref="AvatarSet.SetMovement"/> as a non-autonomous UpdateMotion of the player (retail <c>CPhysics::SetObjectMovement</c>, 11.4186 0x00509690, 11.6096 0x0050A160).
/// Nothing needed fixing: the stance is taken (<c>CMotionInterp::DoInterpretedMotion</c> through <c>MovementManager::unpack_movement</c>), the command class maps (<see cref="MotionCommandTable.FromWire"/>), the motion
/// is not dropped (the server's movement is not autonomous: <see cref="Lifestone.Data.Physics.Objects.PhysicsObj.LastMoveWasAutonomous"/>), and the held item follows the hand. Skipped without the dats.
/// </summary>
public sealed class PlayerCastMotionTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Wand = 0x80000009;
    private const uint HumanSetup = 0x02000001;
    private const uint HumanMotionTable = 0x09000001;

    /// <summary>Revitalize Self VI: a profile spell that is not FastCast (the profile's creature buffs all are), so it has a wind-up, the gesture, an effect at 2.09 s and a target effect on the player.</summary>
    private const uint NonFastSpell = 1182;

    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [RequiresDatsFact]
    public void A_non_fast_cast_plays_on_the_players_body_in_the_magic_stance_wind_up_then_gesture_at_double_speed_then_ready()
    {
        var rig = Rig();
        var plan = Plan();
        var windup = plan.Steps.Single(s => s.Kind == CastStepKind.Windup);
        var gesture = plan.Steps.Single(s => s.Kind == CastStepKind.CastGesture);
        var ready = plan.Steps.Single(s => s.Kind == CastStepKind.ReturnToReady);
        var parts = rig.Set.Player!.Parts!;
        Assert.NotEqual(MotionCommands.Magic, parts.State.Style);

        var applied = new List<AvatarApply>();
        var sawParticlesInWindup = false;
        var sawGestureSubstate = false;
        var gestureSpeed = 0f;
        rig.Play(plan, applied.Add, (clock, set) =>
        {
            if (clock > windup.StartTime + 0.5 && clock < gesture.StartTime)
            {
                sawParticlesInWindup |= set.Player!.Body.Effects.Particles.Count > 0;
            }

            if (clock > gesture.StartTime + 0.1 && clock < ready.StartTime && parts.State.Substate == gesture.Motion)
            {
                sawGestureSubstate = true;
                gestureSpeed = rig.Walker.Motion.Interpreted.ForwardSpeed;
            }

            if (clock > windup.StartTime + 0.05 && clock < gesture.StartTime)
            {
                Assert.Equal(MotionCommands.Magic, parts.State.Style); // the stance went to Magic with the first motion and stays through the cast
            }
        });

        output.WriteLine($"{plan.Spell.Name}: windup 0x{windup.Motion:X8} at {windup.StartTime:0.00}, gesture 0x{gesture.Motion:X8} at {gesture.StartTime:0.00}, ready at {ready.StartTime:0.00}; hooks heard {rig.Sink.Kinds.Count}");
        Assert.All(applied, a => Assert.Equal(AvatarApply.Applied, a));
        Assert.Equal(3, applied.Count);
        Assert.Equal(3, rig.Set.Stats.Motions);
        Assert.Equal(0, rig.Set.Stats.MotionFailures);
        Assert.Equal(0, rig.Set.Stats.PlayerMotionsWithoutBody);
        Assert.False(rig.Walker.Physics.LastMoveWasAutonomous); // the server's movement: not dropped as the client's own echo
        Assert.True(sawParticlesInWindup, "the wind-up animation's hand particles never started");
        Assert.True(sawGestureSubstate, "the talisman's gesture never became the substate");
        Assert.Equal(2f, gestureSpeed); // ACE's CastSpeed
        Assert.Contains(rig.Sink.Kinds, k => k is nameof(SoundTweakedHook) or nameof(SoundHook) or nameof(SoundTableHook)); // the gesture animations carry sound hooks
        Assert.Equal(MotionCommands.Magic, parts.State.Style);
        Assert.Equal(MotionCommands.Ready, parts.State.Substate); // back to Ready (the Magic stance's, the motion table's 0x41000003)
    }

    [RequiresDatsFact]
    public void The_held_wand_stays_in_the_hand_and_follows_it_through_the_whole_cast()
    {
        var rig = Rig();
        var plan = Plan();
        var player = rig.Set.Player!.Body;
        var location = player.Setup.HoldingLocations.ContainsKey(1) ? 1 : player.Setup.HoldingLocations.Keys.First();
        Assert.Equal(AvatarApply.Applied, rig.Set.Create(new AvatarCreate { Guid = Wand, SetupId = HumanSetup, Position = AvatarRig.At(new Vector3(31, 30, 0)) }, now: 0));
        Assert.Equal(AvatarApply.Applied, rig.Set.SetParent(Wand, Player, location, 0));
        Assert.True(rig.Set.TryGet(Wand, out var wand));
        var held = wand!.Body;
        Assert.Same(player, held.Parent);

        var hands = new HashSet<Vector3>();
        var frames = 0;
        rig.Play(plan, _ => { }, (_, _) =>
        {
            frames++;
            Assert.Same(player, held.Parent); // no motion detached it
            Assert.Single(player.Children);
            var expected = AcFrame.Combine(player.PartFrames[held.ParentPartIndex], held.HoldingFrame);
            AvatarAssert.Near(expected.Origin, held.AnchorFrame.Origin);
            hands.Add(player.PartFrames[held.ParentPartIndex].Origin - player.AnchorFrame.Origin);
        });

        output.WriteLine($"{frames} frames, the hand part visited {hands.Count} places relative to the body");
        Assert.True(hands.Count > 10, "the hand never moved relative to the body: the gesture did not play on the held item's parent");
        Assert.True(held.InCell);
    }

    private CastPlan Plan() => StandInCastPlanner.FromDats(fixture.Dats).Plan(SpellTableData.Load(fixture.Dats).Spells[NonFastSpell]);

    private Rigged Rig()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var set = new AvatarSet(sources);
        Assert.Equal(AvatarApply.Applied, set.Create(
            new AvatarCreate
            {
                Guid = Player,
                IsSelf = true,
                IsCreature = true,
                SetupId = HumanSetup,
                MotionTableId = HumanMotionTable,
                PhysicsState = AvatarStateBits.Gravity,
                Position = AvatarRig.At(new Vector3(84, 7, 94)),
            },
            now: 0));
        var builder = new LandblockCollisionBuilder(dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        var walker = OfflineWalker.Create(dats, world, new RecordingLogSink(), parts: set.Player!.Parts, id: Player);
        walker.Physics.Events = new PlayerPhysicsEvents(set.Player); // the hooks of the player's animations reach its avatar's effects, as the game's rig wires it
        set.PlayerBody = walker.Body;
        var sink = new KindSink();
        set.EffectEvents = sink;
        world.UseTime(0.0);
        var at = new CellFrame(CellIds.LandCellAt(LandblockCoord.Holtburg, 96f, 20f)!.Value, new AcFrame(new Vector3(96f, 20f, 95f), Quaternion.Identity));
        Assert.True(walker.Place(at));
        var rigged = new Rigged(set, walker, sink);
        for (var i = 0; i < 60; i++)
        {
            rigged.Frame();
        }

        Assert.True(walker.OnGround);
        return rigged;
    }

    private sealed class KindSink : IEffectEventSink
    {
        public List<string> Kinds { get; } = [];

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId) => Kinds.Add(effectEvent.Hook.GetType().Name);
    }

    private sealed class Rigged(AvatarSet set, OfflineWalker walker, KindSink sink)
    {
        private double _now;

        public AvatarSet Set => set;

        public OfflineWalker Walker => walker;

        public KindSink Sink => sink;

        public void Frame()
        {
            _now += 1.0 / 60.0;
            walker.Tick(_now);
            set.Advance(_now, Viewer, long.MaxValue);
        }

        /// <summary>The stand-in's sequence for <paramref name="plan"/>, one UpdateMotion per motion step at its time (stance Magic, the speed and turn speed ACE sends), 60 frames a second until half a second after the end.</summary>
        public void Play(CastPlan plan, Action<AvatarApply> onApplied, Action<double, AvatarSet> afterFrame)
        {
            var steps = plan.Steps.Where(s => s.Kind != CastStepKind.SpellEffects).ToList();
            var next = 0;
            for (var clock = 0.0; clock < plan.EndTime + 0.5; clock += 1.0 / 60.0)
            {
                while (next < steps.Count && steps[next].StartTime <= clock)
                {
                    var step = steps[next++];
                    var stance = MotionCommandTable.ToWire(MotionCommands.Magic);
                    var state = new ServerMotionState
                    {
                        MovementType = MovementType.Interpreted,
                        Stance = stance,
                        Style = stance,
                        Forward = MotionCommandTable.ToWire(step.Motion),
                        ForwardSpeed = step.Speed != 1f ? step.Speed : null,
                        TurnSpeed = step.Kind == CastStepKind.ReturnToReady ? null : StandInWorld.CastTurnSpeed,
                    };
                    onApplied(set.SetMovement(Player, isSelf: true, autonomous: false, AvatarWire.ToMovement(state), []));
                }

                Frame();
                afterFrame(clock, set);
            }
        }
    }
}
