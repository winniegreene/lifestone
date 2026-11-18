using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary>
/// M6 item 14 (<c>m6-live-position</c>): <see cref="LivePositions"/>, the one answer to where a creature stands now: its body when the session draws bodies and the body can be trusted, else the server's last report, and
/// which of the two answered. Over a scripted body source; nothing here needs the dats, a server or the avatars.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class LivePositionsTests
{
    private const uint Den = 0x01050375; // an EnvCell: retail's blip distance is 20 m there
    private const uint Holtburg = 0xA9B40010; // a land cell: 100 m
    private const uint Tusker = 0x80001001;

    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogSink _log = new();
    private readonly Bodies _bodies = new();

    [Fact]
    public void A_session_that_draws_no_bodies_answers_from_the_servers_report_and_says_so()
    {
        var o = Make(Tusker, At(Den, 100, 25));

        var read = LivePositions.ReportsOnly.Read(o);

        Assert.False(LivePositions.ReportsOnly.HasBodies);
        Assert.Equal(At(Den, 100, 25), read.At);
        Assert.Equal(LiveSource.ServerReport, read.Source);
        Assert.Equal(LiveFallback.NoBodies, read.Fallback);
        Assert.Null(read.Body);
        Assert.Equal(At(Den, 100, 25), LivePositions.ReportsOnly.At(o));
        Assert.Equal(0, LivePositions.ReportsOnly.Fallbacks); // the shared instance keeps no count
    }

    [Fact]
    public void A_placed_body_answers_and_the_reading_carries_both_records()
    {
        var live = Live();
        var o = Make(Tusker, At(Den, 100, 25)); // the server's last report: round the bend
        _bodies.Set(Tusker, new LiveBody(At(Den, 100, 21), InWorld: true, Hidden: false, ServerPositionPending: false)); // the body: beside her

        var read = live.Read(o);

        Assert.True(live.HasBodies);
        Assert.Equal(At(Den, 100, 21), read.At);
        Assert.Equal(LiveSource.Body, read.Source);
        Assert.Equal(LiveFallback.None, read.Fallback);
        Assert.Equal(At(Den, 100, 21), read.Body);
        Assert.Equal(At(Den, 100, 25), read.Report);
        Assert.Equal(1, live.BodyAnswers);
        Assert.Equal(0, live.Fallbacks);
        Assert.Empty(_log.Lines);
    }

    [Fact]
    public void An_object_the_server_gives_no_position_stands_nowhere_whatever_a_body_shows()
    {
        var live = Live();
        var o = Make(Tusker, null);
        _bodies.Set(Tusker, new LiveBody(At(Den, 100, 21), InWorld: true, Hidden: false, ServerPositionPending: false));

        var read = live.Read(o);

        Assert.Null(read.At);
        Assert.Equal(LiveSource.None, read.Source);
        Assert.Null(live.At(o));
        Assert.Equal(0, live.BodyAnswers);
        Assert.Equal(0, live.Fallbacks);
    }

    [Fact]
    public void With_no_body_kept_the_servers_report_answers_and_the_fall_back_is_logged_once_past_the_create_grace()
    {
        var live = Live();
        var young = Make(Tusker, At(Den, 100, 25));
        Assert.Equal(LiveFallback.NoBody, live.Read(young).Fallback);
        Assert.Equal(At(Den, 100, 25), live.At(young));
        Assert.Empty(_log.Lines); // its create may still wait in the adapter's queue: counted, not logged
        Assert.Equal(2, live.FallbacksFor(LiveFallback.NoBody));

        _time.Advance(TimeSpan.FromSeconds(LivePositions.CreateGraceSeconds + 1));
        var read = live.Read(young);

        Assert.Equal(LiveSource.ServerReport, read.Source);
        Assert.Equal(LiveFallback.NoBody, read.Fallback);
        Assert.Null(read.Body);
        var line = Assert.Single(_log.Lines);
        Assert.Contains("[live-position] Tusker 0x80001001 is taken to stand where the server last reported it, 0x01050375 [100 25 0], not where its body is drawn: no body is kept for it", line, StringComparison.Ordinal);
        Assert.Contains("(m6-live-position; fall backs so far 3, this reason 3)", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false, LiveFallback.NotInWorld, "its body stands in no cell of its own (not placed, or held)")]
    [InlineData(true, true, false, LiveFallback.Hidden, "its body is Hidden, the screen draws nothing there")]
    [InlineData(true, false, true, LiveFallback.ServerPositionPending, "its body at 0x01050375 [100 21 0], 4 m off, has not taken the server's last position (a placement refused, a position without contact, or a blip due)")]
    public void A_body_that_is_not_placed_hidden_or_behind_the_server_does_not_answer(bool inWorld, bool hidden, bool pending, LiveFallback reason, string why)
    {
        var live = Live(seconds: 5);
        var o = Make(Tusker, At(Den, 100, 25));
        _bodies.Set(Tusker, new LiveBody(At(inWorld ? Den : 0, 100, 21), inWorld, hidden, pending));

        var read = live.Read(o);

        Assert.Equal(At(Den, 100, 25), read.At);
        Assert.Equal(LiveSource.ServerReport, read.Source);
        Assert.Equal(reason, read.Fallback);
        Assert.Equal(1, live.FallbacksFor(reason));
        Assert.Equal(0, live.BodyAnswers);
        Assert.Contains(why, Assert.Single(_log.Lines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den, 19.5f, true)] // a dungeon cell: 20 m
    [InlineData(Den, 20.5f, false)]
    [InlineData(Holtburg, 99f, true)] // outdoors: 100 m
    [InlineData(Holtburg, 101f, false)]
    public void A_body_farther_from_the_report_than_retails_blip_distance_does_not_answer(uint cell, float apart, bool bodyAnswers)
    {
        var live = Live(seconds: 5);
        var o = Make(Tusker, At(cell, 10, 10));
        _bodies.Set(Tusker, new LiveBody(At(cell, 10 + apart, 10), InWorld: true, Hidden: false, ServerPositionPending: false));

        var read = live.Read(o);

        Assert.Equal(LandDefs.IsEnvCell(cell) ? 20f : 100f, LivePositions.BoundMetres(cell));
        Assert.Equal(bodyAnswers ? LiveSource.Body : LiveSource.ServerReport, read.Source);
        Assert.Equal(bodyAnswers ? LiveFallback.None : LiveFallback.TooFar, read.Fallback);
        Assert.Equal(bodyAnswers ? At(cell, 10 + apart, 10) : At(cell, 10, 10), read.At);
        Assert.Equal(At(cell, 10 + apart, 10), read.Body); // both records, whichever answered
        if (!bodyAnswers)
        {
            Assert.Contains($"is {apart:0.#} m from the report, over retail's blip distance of {LivePositions.BoundMetres(cell):0} m there", Assert.Single(_log.Lines), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_fall_back_is_logged_once_per_object_in_ten_seconds_and_always_counted()
    {
        var live = Live(seconds: 5);
        var o = Make(Tusker, At(Den, 100, 25));
        _bodies.Set(Tusker, new LiveBody(At(Den, 100, 21), InWorld: true, Hidden: true, ServerPositionPending: false));

        for (var i = 0; i < 50; i++)
        {
            live.Read(o);
        }

        Assert.Single(_log.Lines);
        Assert.Equal(50, live.Fallbacks);

        _time.Advance(TimeSpan.FromSeconds(LivePositions.FallbackLogSeconds));
        live.Read(o);
        Assert.Equal(2, _log.Lines.Count);
    }

    [Fact]
    public void The_players_own_object_is_never_asked_of_the_bodies()
    {
        var live = Live();
        var self = Make(0x50000001, At(Den, 100, 20), isSelf: true);
        _bodies.Set(0x50000001, new LiveBody(At(Den, 100, 28), InWorld: true, Hidden: false, ServerPositionPending: false));

        var read = live.Read(self);

        Assert.Equal(At(Den, 100, 20), read.At);
        Assert.Equal(LiveSource.ServerReport, read.Source);
        Assert.Equal(0, _bodies.Lookups);
    }

    [Fact]
    public void The_tables_creature_queries_judge_by_the_live_position_when_given_it_and_by_the_report_when_not()
    {
        var live = Live();
        var table = new WorldObjectTable();
        table.Set(Make(Tusker, At(Den, 100, 28))); // reported 8 m off; its body is 1 m off
        table.Set(Make(Tusker + 1, At(Den, 100, 23))); // reported 3 m off; its body walked 12 m off, into the next cell
        _bodies.Set(Tusker, new LiveBody(At(Den, 100, 21), InWorld: true, Hidden: false, ServerPositionPending: false));
        _bodies.Set(Tusker + 1, new LiveBody(At(Den + 1, 100, 32), InWorld: true, Hidden: false, ServerPositionPending: false));
        var here = At(Den, 100, 20);
        uint[] wcids = [100];

        Assert.True(table.NearestLivingCreatureNamed("Tusker", here, 0, out var guid));
        Assert.Equal(Tusker + 1, guid); // the report's answer, as before
        Assert.True(table.NearestLivingCreatureNamed("Tusker", here, 0, out guid, live));
        Assert.Equal(Tusker, guid);

        Assert.True(table.NearestAttackableCreature(here, 5f, 0, out guid));
        Assert.Equal(Tusker + 1, guid);
        Assert.True(table.NearestAttackableCreature(here, 5f, 0, out guid, live));
        Assert.Equal(Tusker, guid);

        Assert.True(table.NearestCreature(wcids, here, 5f, null, null, out guid));
        Assert.Equal(Tusker + 1, guid);
        Assert.True(table.NearestCreature(wcids, here, 5f, null, null, out guid, live));
        Assert.Equal(Tusker, guid);
        Assert.True(table.NearestCreature(wcids, here, 50f, cell => cell == Den + 1, null, out guid, live)); // the cell filter sees the cell the body stands in
        Assert.Equal(Tusker + 1, guid);
        Assert.False(table.NearestCreature(wcids, here, 50f, cell => cell == Den + 1, null, out _));
    }

    [Fact]
    public void A_session_answers_from_the_servers_report_until_a_host_gives_it_bodies_and_again_when_they_are_taken_away()
    {
        using var session = GameSession.Offline(_ => { }, _time, _log);
        IGameSessionView view = session;
        Assert.Same(LivePositions.ReportsOnly, view.Live);

        session.UseBodies(_bodies);
        Assert.True(view.Live.HasBodies);
        Assert.Same(_bodies, view.Live.Bodies);
        Assert.Contains(_log.Lines, l => l.Contains("live-position: bodies are drawn; a creature stands where its body is, else where the server last reported it (m6-live-position)", StringComparison.Ordinal));

        session.UseBodies(null);
        Assert.Same(LivePositions.ReportsOnly, view.Live);
        Assert.Contains(_log.Lines, l => l.Contains("live-position: no bodies are drawn; a creature stands where the server last reported it (m6-live-position)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_read_allocates_nothing()
    {
        var live = Live();
        var table = new WorldObjectTable();
        for (uint i = 0; i < 50; i++)
        {
            table.Set(Make(Tusker + i, At(Den, 100, 21 + i)));
            _bodies.Set(Tusker + i, new LiveBody(At(Den, 101, 21 + i), InWorld: true, Hidden: false, ServerPositionPending: false));
        }

        var here = At(Den, 100, 20);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                table.NearestAttackableCreature(here, 80f, 0, out _, live);
                foreach (var o in table.All)
                {
                    live.At(o);
                }
            }
        });
        Assert.True(live.BodyAnswers > 0);
    }

    private LivePositions Live(double seconds = 0)
    {
        var clock = new SessionClock(_time);
        _time.Advance(TimeSpan.FromSeconds(seconds)); // past the create grace of an object created at 0
        return new LivePositions(_bodies, clock, _log);
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, CellFrame? position, bool isSelf = false)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Tusker",
            WeenieClassId = 100,
            IconId = 0x06000001,
            ItemType = 0x10,
            ObjectDescriptionFlags = WorldObjectTable.AttackableFlag,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf, createdAt: 0) { Position = position };
    }

    private sealed class Bodies : ILiveBodies
    {
        private readonly Dictionary<uint, LiveBody> _bodies = [];

        public int Lookups { get; private set; }

        public void Set(uint guid, LiveBody body) => _bodies[guid] = body;

        public bool TryGetBody(uint guid, out LiveBody body)
        {
            Lookups++;
            return _bodies.TryGetValue(guid, out body);
        }
    }
}
