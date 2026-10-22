using System.Numerics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A reducer on a manual clock with every event type recorded in publish order (boxed copies: tests only), and builders
/// for the server messages the reducer takes. Handler exceptions are rethrown so a failing assertion inside a handler
/// fails the test.
/// </summary>
internal sealed class ReducerHarness
{
    /// <summary>The player's object id in these tests.</summary>
    public const uint PlayerGuid = 0x50000001;

    public ReducerHarness(bool track = true)
    {
        Clock = new SessionClock(Time);
        Bus = new EventBus(Log) { RethrowHandlerExceptions = true };
        Reducer = new GameStateReducer(State, Bus, Clock, Log);
        if (track)
        {
            TrackEveryEvent();
        }
    }

    public ManualTimeProvider Time { get; } = new();

    public SessionClock Clock { get; }

    public RecordingLogSink Log { get; } = new();

    public EventBus Bus { get; }

    public GameState State { get; } = new();

    public GameStateReducer Reducer { get; }

    /// <summary>Every published event, in order.</summary>
    public List<object> Events { get; } = [];

    /// <summary>A context for a message received now.</summary>
    public InboundContext Context => new(0, MessageQueue.SmartBox, Time.GetTimestamp());

    /// <summary>The events of type <typeparamref name="T"/>, in order.</summary>
    public List<T> Of<T>() => [.. Events.OfType<T>()];

    /// <summary>The names of the events published so far, in order.</summary>
    public List<string> Names() => [.. Events.Select(e => e.GetType().Name)];

    /// <summary>Forgets the recorded events.</summary>
    public void ClearEvents() => Events.Clear();

    /// <summary>A session event now.</summary>
    public void Phase(SessionPhase from, SessionPhase to) => Reducer.OnSessionEvent(SessionEvent.PhaseChanged(Time.GetTimestamp(), from, to));

    /// <summary>F746 then the player's own CreateObject (hidden, at <paramref name="cell"/>), the way ACE enters the world.</summary>
    public WorldObject EnterWorld(uint cell = 0x860201AD, PhysicsTimestamps? stamps = null)
    {
        Reducer.OnPlayerCreate(Context, new PlayerCreateMsg(PlayerGuid));
        Reducer.OnCreateObject(
            Context,
            Create(PlayerGuid, stamps ?? Stamps(), Position(cell), state: PhysicsState.Hidden | PhysicsState.Gravity | PhysicsState.IgnoreCollisions | PhysicsState.EdgeSlide, name: "Tester"),
            isUpdate: false);
        Assert.True(State.Objects.TryGet(PlayerGuid, out var player));
        return player;
    }

    /// <summary>Nine stamps, all <paramref name="all"/> unless named.</summary>
    public static PhysicsTimestamps Stamps(
        ushort all = 1,
        ushort? position = null,
        ushort? movement = null,
        ushort? state = null,
        ushort? vector = null,
        ushort? teleport = null,
        ushort? serverControl = null,
        ushort? forcePosition = null,
        ushort? objDesc = null,
        ushort? instance = null) =>
        new(position ?? all, movement ?? all, state ?? all, vector ?? all, teleport ?? all, serverControl ?? all, forcePosition ?? all, objDesc ?? all, instance ?? all);

    /// <summary>A position in <paramref name="cell"/>.</summary>
    public static AcPosition Position(uint cell, float x = 10, float y = 20, float z = 30) => new(cell, new Vector3(x, y, z), Quaternion.Identity);

    /// <summary>A CreateObject.</summary>
    public static CreateObjectMsg Create(
        uint guid,
        PhysicsTimestamps stamps,
        AcPosition? position = null,
        ObjectLink? parent = null,
        PhysicsState state = PhysicsState.None,
        ServerMotionState? movement = null,
        string name = "Thing",
        ModelData? model = null,
        uint setup = 0x02000001,
        Vector3? velocity = null,
        uint itemType = 0)
    {
        var flags = PhysicsDescFlags.CSetup;
        if (position is not null)
        {
            flags |= PhysicsDescFlags.Position;
        }

        if (parent is not null)
        {
            flags |= PhysicsDescFlags.Parent;
        }

        if (movement is not null)
        {
            flags |= PhysicsDescFlags.Movement;
        }

        var physics = new PhysicsDesc
        {
            Flags = flags,
            State = state,
            Position = position,
            Parent = parent,
            Movement = movement,
            SetupId = setup,
            Velocity = velocity,
            Timestamps = stamps,
        };
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 1, IconId = 0x06000001, ItemType = itemType, ObjectDescriptionFlags = 0 };
        return new CreateObjectMsg(guid, model ?? ModelData.Empty, physics, weenie);
    }

    /// <summary>An UpdatePosition.</summary>
    public static UpdatePositionMsg Move(
        uint guid,
        ushort instance,
        ushort position,
        ushort teleport,
        ushort forcePosition,
        uint cell = 0x860201AD,
        float x = 11,
        bool grounded = true) =>
        new(guid, grounded ? PositionFlags.Grounded : PositionFlags.None, Position(cell, x), null, null, instance, position, teleport, forcePosition);

    /// <summary>A motion of type 0 with a forward command.</summary>
    public static ServerMotionState Motion(ushort forward = 0x0005) => new() { MovementType = MovementType.Interpreted, Forward = forward };

    private void TrackEveryEvent()
    {
        Track<SessionPhaseChanged>();
        Track<ConnectionProgress>();
        Track<CharacterListChanged>();
        Track<CharacterSelected>();
        Track<CharacterCreateResult>();
        Track<CharacterErrorReceived>();
        Track<Booted>();
        Track<PlayerCreated>();
        Track<ObjectCreated>();
        Track<ObjectUpdated>();
        Track<ObjectDeleted>();
        Track<ObjectMoved>();
        Track<ObjectMotionChanged>();
        Track<ObjectVectorChanged>();
        Track<ObjectStateChanged>();
        Track<ObjectParented>();
        Track<ObjectPickedUp>();
        Track<ObjectDescChanged>();
        Track<ObjectSoundPlayed>();
        Track<ObjectScriptPlayed>();
        Track<PlayerTeleportAnnounced>();
        Track<SelfTravelChanged>();
        Track<ServerTimeUpdated>();
        Track<ChatReceived>();
        Track<ToolReplyReceived>();
        Track<ToolsUnavailable>();
        Track<UseDone>();
        Track<HealthUpdated>();
        Track<QualitiesChanged>();
        Track<LogOffRequested>();
        Track<EnchantmentChanged>();
        Track<SpellbookChanged>();
        Track<InventoryChanged>();
        Track<InventoryFailed>();
        Track<ContainerOpened>();
        Track<ContainerClosed>();
        Track<ContainerContentsChanged>();
        Track<CombatEvent>();
        Track<CombatModeChanged>();
        Track<AutoTargetRequested>();
        Track<PlayerDied>();
        Track<PlayerKilledSomething>();
        Track<MessageDropped>();
        Track<UnknownMessage>();
    }

    private void Track<T>()
        where T : struct, IGameEvent => Bus.Subscribe((in T e) => Events.Add(e));
}
