using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M5 item 5, d2: the doors' leftovers. Every runner a journey or the fight builds gets the session's door feed (<see cref="SessionDoorFeed"/>): the two journey hosts hand it the doors' Setup extents
/// (<see cref="IJourneyLegBody.SetupExtent"/>, closing <c>TODO(door-extent-host)</c>), and <c>FightWalk</c>, <c>GameFightWalk</c> and <c>WayfindScenario</c> set it where they build their runner. The scripted case
/// below is that wiring on the scripted body: the same feed over the session's object table, a door that stays shut until the Use goes out, and the station's <see cref="DoorWatch"/> reading it.
/// </summary>
public sealed class DoorHostTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint DoorGuid = 0x7A9B4015;
    private const float DoorY = 65f;

    private static WorldObject MakeDoor()
    {
        var block = MovementTestKit.Block.LandBlockId;
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = "door", WeenieClassId = 412, IconId = 0x06000001, ItemType = 0, ObjectDescriptionFlags = DoorCrossings.DoorFlag };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(DoorGuid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0)
        {
            State = PhysicsState.None,
            Position = new CellFrame(block | 0x29, new AcFrame(new Vector3(50f, DoorY, 0f), Quaternion.Identity)),
        };
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>The session's door feed over the object table, with the doorway given (the unit rig has no tile and no rooms): what the real feed does for the fight walks and the wayfind leg.</summary>
    private sealed class SessionFeedWithDoorway(SessionDoorFeed inner) : INavDoorFeed
    {
        public int Uses { get; private set; }

        public void Fill(List<NavDoor> into) => inner.Fill(into);

        public bool TryGet(uint guid, out NavDoor door) => inner.TryGet(guid, out door);

        public ActionHandle? Use(uint guid)
        {
            Uses++;
            return inner.Use(guid);
        }

        public IReadOnlyList<DoorCrossing> Crossings(IReadOnlyList<NavDoor> doors, INavPathSource paths)
        {
            var block = MovementTestKit.Block.LandBlockId;
            return [new DoorCrossing(doors[0], block, block | 0x29, new Vector3(50f, DoorY, 0f), new Vector2(0f, 1f), null, 0f)];
        }
    }

    [Fact]
    public void A_runner_given_the_session_door_feed_stops_before_a_closed_door_uses_it_and_the_watch_reads_it_open()
    {
        var h = new ActionHarness(); // linked and present, so the Use goes out to the fake server (S5Rig's harness is offline and refuses it)
        h.Present();
        var rig = (H: h, Body: new S5KinematicBody(50f, 50f, 0f), Paths: new S5LinePaths());
        rig.H.Api.Move.Bind(rig.Body, false);
        var door = MakeDoor();
        rig.H.State.Objects.Set(door);
        var view = new View(rig.H);
        using var watch = new DoorWatch(view);
        var log = new RecordingLogSink();
        var feed = new SessionFeedWithDoorway(new SessionDoorFeed(view, log));
        using var runner = new NavRunner(rig.H.Api.Move, rig.Body, rig.Paths, log: log) { Doors = feed };
        runner.Start(S5Rig.To(50f, 80f), rig.Body.Now);
        double? usedAt = null;
        float? stoppedY = null;

        while (rig.Body.Now < 40 && !runner.IsDone)
        {
            rig.Body.Advance(S5Rig.Dt);
            runner.Step(rig.Body.Now);
            rig.H.Tick(S5Rig.Dt);
            if (usedAt is null && feed.Uses > 0)
            {
                usedAt = rig.Body.Now; // the Use went out through the session's actions
                stoppedY = rig.Body.Xy.Y;
            }

            if (usedAt is { } at && rig.Body.Now >= at + 0.5 && (door.State & PhysicsState.Ethereal) == 0)
            {
                door.State |= PhysicsState.Ethereal; // the server's word: ACE's Door.Open sets Ethereal at once
                rig.H.R.Bus.Publish(new ObjectStateChanged(DoorGuid, false, PhysicsState.None, door.State));
            }
        }

        Assert.True(runner.State == NavRunState.Arrived, $"{runner.State}: {runner.FailureReason}; {string.Join(" | ", log.Lines.TakeLast(6))}");
        Assert.Equal(1, runner.DoorUses);
        Assert.Equal(1, runner.DoorsOpened);
        Assert.InRange(stoppedY!.Value, DoorY - runner.Options.DoorStandOffMetres - 0.6f, DoorY - runner.Options.DoorStandOffMetres + 0.3f); // stopped before the doorway
        Assert.InRange(rig.Body.Xy.Y, 79f, 80.7f); // and walked on through to the goal
        Assert.Equal(1, watch.Opened); // the watch saw the door the walk Used (through the session's feed) gain Ethereal
        Assert.Equal("yes", watch.Field);
        Assert.Contains(log.Lines, l => l.Contains("purpose=door", StringComparison.Ordinal));
    }

    [Fact]
    public void A_door_already_open_is_found_open_and_never_used_so_the_watch_reads_no()
    {
        var h = new ActionHarness();
        h.Present();
        var rig = (H: h, Body: new S5KinematicBody(50f, 50f, 0f), Paths: new S5LinePaths());
        rig.H.Api.Move.Bind(rig.Body, false);
        var door = MakeDoor();
        door.State |= PhysicsState.Ethereal; // ACE (or another lane) opened it before the walk got there
        rig.H.State.Objects.Set(door);
        var view = new View(rig.H);
        using var watch = new DoorWatch(view);
        var log = new RecordingLogSink();
        var feed = new SessionFeedWithDoorway(new SessionDoorFeed(view, log));
        using var runner = new NavRunner(rig.H.Api.Move, rig.Body, rig.Paths, log: log) { Doors = feed };
        runner.Start(S5Rig.To(50f, 80f), rig.Body.Now);

        while (rig.Body.Now < 40 && !runner.IsDone)
        {
            rig.Body.Advance(S5Rig.Dt);
            runner.Step(rig.Body.Now);
            rig.H.Tick(S5Rig.Dt);
        }

        Assert.True(runner.State == NavRunState.Arrived, $"{runner.State}: {runner.FailureReason}");
        Assert.Equal(0, runner.DoorUses);
        Assert.Equal(0, feed.Uses);
        Assert.Equal(1, runner.DoorsFoundOpen);
        Assert.Equal(0, runner.DoorsOpened);
        Assert.Equal("no", watch.Field); // the walk opened nothing
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_headless_journey_hosts_leg_body_returns_the_real_door_extent_from_the_dats_built_once()
    {
        using var host = new HeadlessJourneyRunnerHost(fixture.Dats, new RecordingLogSink());

        var extent = host.JourneyLegBody.SetupExtent;

        Assert.NotNull(extent);
        Assert.Same(extent, host.JourneyLegBody.SetupExtent); // built once per host, not per leg
        Assert.InRange(extent!(0x020019FF)!.Value, 0.8f, 1.0f); // the Aluvian house door's selection sphere (0.89 m), not its 0.14 m physics radius
        Assert.Null(extent(0x02FFFFFF));
    }
}
