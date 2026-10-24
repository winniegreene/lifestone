using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using DatAnimData = DatReaderWriter.Types.AnimData;
using DatMotionData = DatReaderWriter.Types.MotionData;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;
using MotionCommand = DatReaderWriter.Enums.MotionCommand;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// Synthetic worlds and players for the controller tests (no dats): flat or sloped terrain, the human Setup's collision,
/// a small NonCombat motion table (Ready, walk 3.12 m/s, run 4 m/s, Falling; TurnRight and SideStepRight modifiers) and a
/// sink that records what the controller sends.
/// </summary>
internal static class MovementTestKit
{
    /// <summary>A landblock far from anything the dat tests use.</summary>
    public static readonly LandblockCoord Block = new(0x40, 0x40);

    /// <summary>TurnRight's turn rate in the synthetic table, rad/s (clockwise seen from above: a negative Z spin).</summary>
    public const float TurnRate = MathF.PI / 2f;

    /// <summary>The human Setup 0x02000001's collision (checked against the dats in the Data tests).</summary>
    public static PhysicsSetup HumanSetup { get; } = PhysicsSetup.FromParts(
        [new PhysSphere(new Vector3(0, 0, 0.475f), 0.48f), new PhysSphere(new Vector3(0, 0, 1.35f), 0.48f)],
        stepUpHeight: 0.6f,
        stepDownHeight: 1.5f,
        height: 1.8f,
        radius: 0.48f,
        sortingSphere: new PhysSphere(new Vector3(0, 0, 0.9f), 1.0f),
        allowsFreeHeading: false);

    /// <summary>Terrain whose vertex (x, y) uses height index <paramref name="heightIndex"/>(x, y), all dry land.</summary>
    public static LandblockTerrain TerrainOf(LandblockCoord coord, Func<int, int, byte> heightIndex)
    {
        var words = new ushort[TerrainConstants.VertexCount];
        var heights = new byte[TerrainConstants.VertexCount];
        for (var x = 0; x < TerrainConstants.VerticesPerSide; x++)
        {
            for (var y = 0; y < TerrainConstants.VerticesPerSide; y++)
            {
                var index = TerrainConstants.VertexIndex(x, y);
                heights[index] = heightIndex(x, y);
                words[index] = 1 << 2;
            }
        }

        return new LandblockTerrain(coord, words, heights);
    }

    /// <summary>A height table where index i is i metres.</summary>
    public static float[] Heights { get; } = Enumerable.Range(0, 256).Select(i => (float)i).ToArray();

    /// <summary>A flat landblock at height 0.</summary>
    public static LandblockCollision Flat(LandblockCoord coord) => LandblockCollisionBuilder.FromTerrain(TerrainOf(coord, (_, _) => 0), Heights);

    /// <summary>
    /// A landblock whose vertex heights alternate 0 and 1 m along X (a corrugated slope), so walking east crosses terrain
    /// triangle edges with different planes inside one cell.
    /// </summary>
    public static LandblockCollision Corrugated(LandblockCoord coord) =>
        LandblockCollisionBuilder.FromTerrain(TerrainOf(coord, (x, _) => (byte)(x % 2)), Heights);

    /// <summary>A world with <paramref name="blocks"/> attached and its clock started at 0.</summary>
    public static PhysicsWorld WorldOf(params LandblockCollision[] blocks)
    {
        var world = new PhysicsWorld();
        foreach (var block in blocks)
        {
            world.Attach(block);
        }

        world.UseTime(0.0);
        return world;
    }

    /// <summary>A position in <paramref name="coord"/>'s land cell under (x, y), facing north.</summary>
    public static CellFrame At(LandblockCoord coord, float x, float y, float z) =>
        new(CellIds.LandCellAt(coord, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    /// <summary>A motion player over the synthetic table.</summary>
    public static MotionPlayer SyntheticMotion()
    {
        var animations = new FakeAnimations().Add(AnimReady, 2).Add(AnimWalk, 4).Add(AnimRun, 4).Add(AnimFall, 2).Add(AnimTurn, 4).Add(AnimSide, 4);
        var style = MotionCommands.NonCombat;
        var table = new DatMotionTable { Id = 0x09FFFF02, DefaultStyle = (MotionCommand)style };
        table.StyleDefaults[(MotionCommand)style] = (MotionCommand)MotionCommands.Ready;
        table.Cycles[Key(style, MotionCommands.Ready)] = Data(AnimReady);
        table.Cycles[Key(style, MotionCommands.WalkForward)] = Data(AnimWalk, new Vector3(0, 3.12f, 0));
        table.Cycles[Key(style, MotionCommands.RunForward)] = Data(AnimRun, new Vector3(0, 4f, 0));
        table.Cycles[Key(style, MotionCommands.Falling)] = Data(AnimFall);
        table.Modifiers[Key(style, MotionCommands.TurnRight)] = Data(AnimTurn, omega: new Vector3(0, 0, -TurnRate));
        table.Modifiers[Key(style, MotionCommands.SideStepRight)] = Data(AnimSide, new Vector3(1.25f, 0, 0));
        return new MotionPlayer(MotionTableData.FromDat(table), animations);
    }

    /// <summary>
    /// The world's player: a human with the synthetic motion table and <paramref name="weenie"/>, entered at
    /// <paramref name="at"/> and settled for a second.
    /// </summary>
    public static PhysicsObj Player(PhysicsWorld world, in CellFrame at, TestPlayerWeenie? weenie = null)
    {
        var player = new PhysicsObj(world, HumanSetup, id: 0x50000001, weenie ?? new TestPlayerWeenie(), SyntheticMotion())
        {
            LastMoveWasAutonomous = true,
        };
        world.SetPlayer(player);
        Assert.True(player.EnterWorld(at), $"placement at {at}");
        for (var i = 0; i < 60; i++)
        {
            if (world.UseTime(world.CurrentTime + (1.0 / 60.0)))
            {
                player.UpdateObject();
            }
        }

        Assert.True(player.HasContact && player.OnWalkable, $"not on the ground at {player.Position}");
        return player;
    }

    /// <summary>A controller over a settled player in a flat world, with a recording sink.</summary>
    public static (PlayerMovementController Controller, RecordingMovementSink Sink, PhysicsWorld World) FlatController(
        float x = 96f,
        float y = 96f,
        TestPlayerWeenie? weenie = null,
        MovementOptions? options = null,
        params LandblockCollision[] blocks)
    {
        var world = WorldOf(blocks.Length == 0 ? [Flat(Block)] : blocks);
        var player = Player(world, At(Block, x, y, 0f), weenie);
        var sink = new RecordingMovementSink();
        var controller = new PlayerMovementController(player, sink, options ?? MovementOptions.Default);
        sink.Clock = () => controller.Now;
        return (controller, sink, world);
    }

    /// <summary>Ticks <paramref name="controller"/> at <paramref name="fps"/> for <paramref name="seconds"/>, calling <paramref name="perFrame"/> before each frame; answers the clock.</summary>
    public static double Run(PlayerMovementController controller, double seconds, double fps = 60.0, Action<double>? perFrame = null)
    {
        var now = controller.Now;
        var frames = (int)Math.Round(seconds * fps);
        for (var i = 0; i < frames; i++)
        {
            now += 1.0 / fps;
            perFrame?.Invoke(now);
            controller.Tick(now);
        }

        return now;
    }

    private const uint AnimReady = 0x03FF0011;
    private const uint AnimWalk = 0x03FF0012;
    private const uint AnimRun = 0x03FF0013;
    private const uint AnimFall = 0x03FF0014;
    private const uint AnimTurn = 0x03FF0015;
    private const uint AnimSide = 0x03FF0016;

    private static int Key(uint style, uint motion) => unchecked((int)MotionTableData.Key(style, motion));

    private static DatMotionData Data(uint anim, Vector3 velocity = default, Vector3 omega = default) => new()
    {
        Anims = { new DatAnimData { AnimId = anim, LowFrame = 0, HighFrame = -1, Framerate = 30f } },
        Velocity = velocity,
        Omega = omega,
    };

    /// <summary>Raw one-part animations with no root motion and no hooks.</summary>
    private sealed class FakeAnimations : IAnimationSource
    {
        private readonly Dictionary<uint, AnimationData> _anims = [];

        public FakeAnimations Add(uint id, int frames)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(id);
                w.Write(0u); // flags: no pos frames
                w.Write(1u); // parts
                w.Write((uint)frames);
                for (var f = 0; f < frames; f++)
                {
                    w.Write(0f);
                    w.Write(0f);
                    w.Write(0f);
                    w.Write(1f);
                    w.Write(0f);
                    w.Write(0f);
                    w.Write(0f);
                    w.Write(0u); // no hooks
                }
            }

            _anims[id] = AnimationData.Decode(id, ms.ToArray());
            return this;
        }

        public AnimationData? GetAnimation(uint animId) => _anims.GetValueOrDefault(animId);
    }
}

/// <summary>The player's weenie in the controller tests: a non-PK player with a settable run rate and jump speed.</summary>
internal sealed class TestPlayerWeenie : IPhysicsWeenie
{
    /// <summary>The run rate: the fighters' live one (2.47, 9.6-9.9 m/s; P5a research and live round 1), not the old convenience of 1.5. A test that needs another sets it.</summary>
    public float RunRate { get; set; } = 2.47f;

    public float JumpVelocity { get; set; } = 5f;

    public bool CanJumpNow { get; set; } = true;

    public bool IsCreature => true;

    public bool IsThePlayer => true;

    public bool IsPlayer => true;

    public bool IsPK => false;

    public bool IsPKLite => false;

    public bool IsImpenetrable => false;

    public bool TryGetRunRate(out float rate)
    {
        rate = RunRate;
        return true;
    }

    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        velocityZ = JumpVelocity * extent;
        return true;
    }

    public bool CanJump(float extent) => CanJumpNow;

    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        staminaCost = 0;
        return true;
    }
}

/// <summary>A movement sink that records every call, with the controller's clock.</summary>
internal sealed class RecordingMovementSink : IMovementSink
{
    /// <summary>What was sent.</summary>
    public enum Kind
    {
        MoveToState,
        AutonomousPosition,
        Jump,
    }

    /// <summary>One call: what, when, where, the contact flag, and the copied raw state or jump.</summary>
    public readonly record struct Sent(Kind Kind, double Time, CellFrame Position, bool Contact, uint Forward, uint Turn, uint SideStep, HoldKey HoldKey, bool StandingLongJump, float Extent, Vector3 Velocity);

    public List<Sent> Calls { get; } = [];

    public Func<double> Clock { get; set; } = () => 0.0;

    public IEnumerable<Sent> Of(Kind kind) => Calls.Where(c => c.Kind == kind);

    public int Count(Kind kind) => Calls.Count(c => c.Kind == kind);

    public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) =>
        Calls.Add(new Sent(Kind.MoveToState, Clock(), position, contact, raw.ForwardCommand, raw.TurnCommand, raw.SideStepCommand, raw.CurrentHoldKey, standingLongJump, 0f, default));

    public void AutonomousPosition(in CellFrame position, bool contact) =>
        Calls.Add(new Sent(Kind.AutonomousPosition, Clock(), position, contact, 0, 0, 0, HoldKey.Invalid, false, 0f, default));

    public void Jump(float extent, Vector3 velocity, in CellFrame position) =>
        Calls.Add(new Sent(Kind.Jump, Clock(), position, false, 0, 0, 0, HoldKey.Invalid, false, extent, velocity));
}
