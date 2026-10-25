using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;
using DatAnimData = DatReaderWriter.Types.AnimData;
using DatMotionData = DatReaderWriter.Types.MotionData;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;
using MotionCommand = DatReaderWriter.Enums.MotionCommand;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 P5b lane J2b: a travel seam that records every request and answers it with a leg of the means the test picks (Run, Recall or None are authoritative arrivals; Place is only the confirmation), after
/// <see cref="StepsToArrive"/> steps; it never sends a place. <see cref="FailPurpose"/> makes one purpose's leg fail.
/// </summary>
internal sealed class J2bRecordingTravel(Func<TravelPurpose, TravelMeans> means) : IJourneyTravel
{
    private J2bLeg? _leg;

    public List<TravelRequest> Requests { get; } = [];

    public int StepsToArrive { get; set; } = 2;

    public TravelPurpose? FailPurpose { get; set; }

    public int Disposed { get; private set; }

    public bool Busy => _leg is { Done: false };

    public IEnumerable<TravelPurpose> Purposes => Requests.Select(r => r.Purpose);

    public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
    {
        Requests.Add(request);
        _leg = new J2bLeg(this, means(request.Purpose), request.Purpose == FailPurpose);
        return _leg;
    }

    private sealed class J2bLeg(J2bRecordingTravel owner, TravelMeans means, bool fail) : ITravelLeg
    {
        private int _steps;

        public bool Done { get; private set; }

        public TravelMeans Means => means;

        public string? Failure { get; private set; }

        public string Fields => means == TravelMeans.Run ? "route_m=1.0 run_s=0.5" : string.Empty;

        public TravelStatus Step(IGameSessionView session, double now)
        {
            if (Done)
            {
                return Failure is null ? TravelStatus.Arrived : TravelStatus.Failed;
            }

            if (++_steps < owner.StepsToArrive)
            {
                return TravelStatus.Running;
            }

            Done = true;
            if (fail)
            {
                Failure = "travel:" + means.ToString().ToLowerInvariant() + ":blocked";
                return TravelStatus.Failed;
            }

            return TravelStatus.Arrived;
        }

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose()
        {
            Done = true;
            owner.Disposed++;
        }
    }
}

/// <summary>M4 P5b lane J2b: a controller over a settled player whose motion table plays Ready, walking and Sleeping (0x41000014) in NonCombat and Ready in the Magic stance: the lie-down's wire proof without the dats.</summary>
internal static class J2bLieDownKit
{
    public const uint Sleeping = 0x41000014;
    public const uint MagicStyle = 0x80000049;
    private const uint AnimReady = 0x03FF0031;
    private const uint AnimSleep = 0x03FF0032;
    private const uint AnimWalk = 0x03FF0033;

    public static (PlayerMovementController Controller, RecordingMovementSink Sink) Controller()
    {
        const uint style = MotionCommands.NonCombat;
        var world = MovementTestKit.WorldOf(MovementTestKit.Flat(MovementTestKit.Block));
        var player = new PhysicsObj(world, MovementTestKit.HumanSetup, id: 0x50000001, new TestPlayerWeenie(), Motion(style)) { LastMoveWasAutonomous = true };
        world.SetPlayer(player);
        Assert.True(player.EnterWorld(MovementTestKit.At(MovementTestKit.Block, 96f, 96f, 0f)));
        for (var i = 0; i < 60; i++)
        {
            if (world.UseTime(world.CurrentTime + (1.0 / 60.0)))
            {
                player.UpdateObject();
            }
        }

        var sink = new RecordingMovementSink();
        var controller = new PlayerMovementController(player, sink, MovementOptions.Default);
        sink.Clock = () => controller.Now;
        return (controller, sink);
    }

    private static MotionPlayer Motion(uint style)
    {
        var animations = new Anims().Add(AnimReady, 2).Add(AnimSleep, 2).Add(AnimWalk, 4);
        var table = new DatMotionTable { Id = 0x09FFFF31, DefaultStyle = (MotionCommand)style };
        table.StyleDefaults[(MotionCommand)style] = (MotionCommand)MotionCommands.Ready;
        table.Cycles[Key(style, MotionCommands.Ready)] = Data(AnimReady);
        table.Cycles[Key(style, Sleeping)] = Data(AnimSleep);
        table.Cycles[Key(style, MotionCommands.WalkForward)] = Data(AnimWalk, new Vector3(0, 3.12f, 0));
        table.StyleDefaults[(MotionCommand)MagicStyle] = (MotionCommand)MotionCommands.Ready;
        table.Cycles[Key(MagicStyle, MotionCommands.Ready)] = Data(AnimReady);
        return new MotionPlayer(MotionTableData.FromDat(table), animations);
    }

    private static int Key(uint style, uint motion) => unchecked((int)MotionTableData.Key(style, motion));

    private static DatMotionData Data(uint anim, Vector3 velocity = default) => new()
    {
        Anims = { new DatAnimData { AnimId = anim, LowFrame = 0, HighFrame = -1, Framerate = 30f } },
        Velocity = velocity,
    };

    private sealed class Anims : IAnimationSource
    {
        private readonly Dictionary<uint, AnimationData> _anims = [];

        public Anims Add(uint id, int frames)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(id);
                w.Write(0u);
                w.Write(1u);
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
                    w.Write(0u);
                }
            }

            _anims[id] = AnimationData.Decode(id, ms.ToArray());
            return this;
        }

        public AnimationData? GetAnimation(uint animId) => _anims.GetValueOrDefault(animId);
    }
}
