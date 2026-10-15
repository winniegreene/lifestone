using System.Numerics;
using Lifestone.Client.Play;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="StandInSessions.PlayerVisits"/>: the scripted visit of the world that play mode's checks run on without a server. It goes through the
/// session's own reducer, so these tests read what a live session would publish, and run <see cref="PlayPresentationFlow"/> over it.
/// </summary>
public sealed class StandInVisitTests
{
    private const uint Holtburg = 0xA9B40019;
    private const uint Academy = 0x860201AD;

    [Fact]
    public void The_visit_creates_the_objects_it_lists_beside_the_player()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 4.0,
            Objects = [new StandInObject(0x80000A01, "Guard", new Vector3(3f, 0f, 0f)), new StandInObject(0x80000A02, "Statue", new Vector3(0f, 4f, 0f), MotionTable: 0)],
        };
        var time = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
            },
            time,
            new RecordingLogSink());
        var created = new List<(uint Guid, bool Self)>();
        session.Events.Subscribe((in ObjectCreated e) => created.Add((e.Guid, e.IsSelf)));
        for (var t = 0.0; t < 1.0; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        Assert.Equal([(StandInSessions.FirstCharacterGuid, true), (0x80000A01u, false), (0x80000A02u, false)], created);
        Assert.True(session.State.Objects.TryGet(0x80000A01, out var guard));
        Assert.Equal(visit.Origin + new Vector3(3f, 0f, 0f), guard.Position!.Value.Origin);
        Assert.Equal(StandInVisit.HumanMotionTable, guard.Physics.MotionTableId);
        Assert.True(session.State.Objects.TryGet(0x80000A02, out var statue));
        Assert.Null(statue.Physics.MotionTableId);
        Assert.Equal(Holtburg, guard.Position!.Value.CellId);
    }

    [Fact]
    public void The_visit_enters_hidden_unhides_teleports_and_logs_off_through_the_reducer()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 1.0,
            UnhideAfter = 4.0,
            Teleports = [new StandInTeleport(2.0, Academy, new Vector3(61.5f, -21.5f, 0.005f))],
            LogOffAfter = 3.0,
            LogOffDelay = 0.5,
        };
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
            },
            time,
            log);
        var events = new List<string>();
        session.Events.Subscribe((in PlayerCreated e) => events.Add("created"));
        session.Events.Subscribe((in ObjectCreated e) => events.Add($"object self={e.IsSelf} inWorld={e.InWorld}"));
        session.Events.Subscribe((in SelfTravelChanged e) => events.Add($"travel {e.Travel}"));
        session.Events.Subscribe((in ObjectMoved e) => events.Add($"moved {e.Verdict} 0x{e.Position.CellId:X8}"));
        session.Events.Subscribe((in SessionPhaseChanged e) => events.Add($"phase {e.Phase}"));

        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                time.Advance(TimeSpan.FromSeconds(0.1));
                session.Tick(session.Clock.Now);
            }
        }

        Assert.Equal(SessionPhase.CharacterSelect, session.State.Phase);
        Run(1.2);
        Assert.Equal(SessionPhase.InWorld, session.State.Phase);
        Assert.Equal(StandInSessions.FirstCharacterGuid, session.State.Self.Guid);
        Assert.True(session.State.Self.Hidden);
        Assert.Equal(Holtburg, session.State.Self.Position!.Value.CellId);
        Assert.Equal(SelfTravel.Arriving, session.State.Self.Travel);
        Assert.True(session.State.Objects.TryGet(StandInSessions.FirstCharacterGuid, out var player));
        Assert.Equal(StandInVisit.HumanSetup, player.Physics.SetupId);
        Assert.Equal(StandInVisit.HumanMotionTable, player.Physics.MotionTableId);

        Run(4.0);
        Assert.False(session.State.Self.Hidden);
        Assert.Equal(SelfTravel.Present, session.State.Self.Travel);

        Run(2.2);
        Assert.Equal(Academy, session.State.Self.Position!.Value.CellId);
        Assert.Contains($"moved SelfTeleport 0x{Academy:X8}", events);

        Run(4.0);
        Assert.Equal(SessionPhase.CharacterSelect, session.State.Phase);
        Assert.False(session.State.Objects.TryGet(StandInSessions.FirstCharacterGuid, out _));
        Assert.Equal(SelfTravel.NotInWorld, session.State.Self.Travel);
        Assert.Equal(["phase EnteringWorld", "created", "phase InWorld"], events.Where(e => e is "created" || e.StartsWith("phase ", StringComparison.Ordinal)).Take(3));
        Assert.Contains(events, e => e == "object self=True inWorld=True");
    }

    [Fact]
    public void Play_mode_s_flow_follows_the_visit_to_the_end()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.5,
            UnhideAfter = 2.0,
            Teleports = [new StandInTeleport(1.0, Academy, new Vector3(61.5f, -21.5f, 0.005f))],
            LogOffAfter = 1.0,
            LogOffDelay = 0.3,
        };
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
            },
            time,
            log);
        var calls = new List<string>();
        using var flow = new PlayPresentationFlow(session, new Recorder(calls), new PlayFlowOptions(), log);
        for (var t = 0.0; t < 8.0; t += 0.1)
        {
            time.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }

        Assert.Equal(
            [$"arriving 0x{StandInSessions.FirstCharacterGuid:X8}", "state", "present", $"teleported 0x{Academy:X8}", "left LoggedOff"],
            calls);
        Assert.Equal(PlayStage.CharacterList, flow.Stage);
    }

    [Fact]
    public void A_live_session_s_reducer_is_refused()
    {
        using var h = new LiveHarness();
        Assert.Throws<ArgumentException>(() => StandInSessions.PlayerVisits(h.Session.Reducer, new StandInVisit()));
    }

    private sealed class Recorder(List<string> calls) : IPlayWorld
    {
        public void PlayerArriving(WorldObject player, bool recreated) => calls.Add($"arriving 0x{player.Guid:X8}");

        public void PlayerPresent() => calls.Add("present");

        public void PlayerTeleported(in ObjectMoved move) => calls.Add($"teleported 0x{move.Position.CellId:X8}");

        public void PlayerPositioned(in ObjectMoved move) => calls.Add("positioned");

        public void PlayerStateChanged(in ObjectStateChanged change) => calls.Add("state");

        public void PlayerMotionChanged(in ObjectMotionChanged change) => calls.Add("motion");

        public void PlayerDescChanged(in ObjectDescChanged change) => calls.Add("desc");

        public void PlayerLeft(PlayLeaveReason reason) => calls.Add($"left {reason}");
    }
}
