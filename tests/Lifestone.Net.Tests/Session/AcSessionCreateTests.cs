using System.Net;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// <see cref="AcSession.Create"/>: the loopback gate runs first, before any other check and before any socket factory
/// is touched (S8); then the options are validated; the session starts idle and binds to the thread that first pumps
/// it. Every operation that needs a connection refuses an idle session.
/// </summary>
public sealed class AcSessionCreateTests
{
    private static readonly DddIterations EndOfRetail = new(2072, 982, 994, 497);

    /// <summary>One address stands for all: the gate runs before the factory whatever the address; which addresses it refuses is LoopbackPolicyTests'.</summary>
    public static TheoryData<string> NonLoopbackServers => ["10.0.0.5"];

    [Theory]
    [MemberData(nameof(NonLoopbackServers))]
    public void A_non_loopback_server_is_refused_before_the_socket_factory_is_touched(string address)
    {
        var factory = new ThrowingSocketFactory();
        var options = new AcSessionOptions
        {
            Server = new IPEndPoint(IPAddress.Parse(address), 9000),
            Iterations = EndOfRetail,
            SocketFactory = factory.Open,
        };

        Assert.Throws<LoopbackRequiredException>(() => AcSession.Create(options, new NullSink()));
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public void The_gate_runs_before_every_other_check()
    {
        var factory = new ThrowingSocketFactory();
        var options = new AcSessionOptions
        {
            Server = new IPEndPoint(IPAddress.Parse("10.0.0.5"), 0),
            Iterations = default,
            ClientVersion = "",
            Timeouts = SessionTimeouts.Default with { Silence = TimeSpan.Zero },
            Retry = LoginRetryPolicy.Default with { Attempts = 0 },
            SocketFactory = factory.Open,
        };

        Assert.Throws<LoopbackRequiredException>(() => AcSession.Create(options, null!));
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public void A_loopback_server_gives_an_idle_session_without_opening_a_socket()
    {
        var factory = new ThrowingSocketFactory();
        using var session = AcSession.Create(Options(factory), new NullSink());

        Assert.Equal(SessionPhase.Idle, session.Phase);
        Assert.Null(session.Failure);
        Assert.Null(session.CharacterList);
        Assert.Null(session.PlayerGuid);
        Assert.Equal(default, session.Link);
        Assert.Equal(default, session.Pump(TimeSpan.Zero));
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public void The_session_keeps_the_gates_copy_of_the_server_endpoint()
    {
        var server = new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 9000);
        using var session = AcSession.Create(Options(new ThrowingSocketFactory()) with { Server = server }, new NullSink());

        server.Address = IPAddress.Parse("10.0.0.5");
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), ((SessionMachine)session).Options.Server);
    }

    public static TheoryData<string> BadOptions => ["port 0", "port 65535", "portal 0", "language 0", "cell -1", "high-res 0", "version empty", "version non-ascii", "silence 0", "attempts 0", "delay negative", "in-world wait with no delay", "in-world wait negative"];

    [Theory]
    [MemberData(nameof(BadOptions))]
    public void Unusable_options_are_refused(string which)
    {
        var options = Options(new ThrowingSocketFactory());
        options = which switch
        {
            "port 0" => options with { Server = new IPEndPoint(IPAddress.Loopback, 0) },
            "port 65535" => options with { Server = new IPEndPoint(IPAddress.Loopback, 65535) },
            "portal 0" => options with { Iterations = EndOfRetail with { Portal = 0 } },
            "language 0" => options with { Iterations = EndOfRetail with { Language = 0 } },
            "cell -1" => options with { Iterations = EndOfRetail with { Cell = -1 } },
            "high-res 0" => options with { Iterations = EndOfRetail with { HighRes = 0 } },
            "version empty" => options with { ClientVersion = "" },
            "version non-ascii" => options with { ClientVersion = "18é02" },
            "silence 0" => options with { Timeouts = SessionTimeouts.Default with { Silence = TimeSpan.Zero } },
            "attempts 0" => options with { Retry = LoginRetryPolicy.Default with { Attempts = 0 } },
            "delay negative" => options with { Retry = LoginRetryPolicy.Default with { Delay = TimeSpan.FromSeconds(-1) } },
            "in-world wait with no delay" => options with { Retry = LoginRetryPolicy.Default with { CharacterInWorldDelay = TimeSpan.Zero } },
            "in-world wait negative" => options with { Retry = LoginRetryPolicy.Default with { CharacterInWorldWait = TimeSpan.FromSeconds(-1) } },
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
        };

        Assert.ThrowsAny<ArgumentException>(() => AcSession.Create(options, new NullSink()));
    }

    [Fact]
    public void Cell_iteration_0_and_no_high_res_dat_are_accepted()
    {
        using var session = AcSession.Create(Options(new ThrowingSocketFactory()) with { Iterations = new DddIterations(2072, 0, 994, null) }, new NullSink());
        Assert.Equal(SessionPhase.Idle, session.Phase);
    }

    [Fact]
    public void Null_options_or_sink_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AcSession.Create(null!, new NullSink()));
        Assert.Throws<ArgumentNullException>(() => AcSession.Create(Options(new ThrowingSocketFactory()), null!));
    }

    [Fact]
    public void The_first_pump_binds_the_session_to_its_thread()
    {
        using var session = AcSession.Create(Options(new ThrowingSocketFactory()), new NullSink());
        session.Pump(TimeSpan.Zero);

        var refusals = new List<Exception?>();
        var other = new Thread(() =>
        {
            using var credentials = new AcCredentials("lifestone-test", "unused");
            refusals.Add(Record.Exception(() => session.Pump(TimeSpan.Zero)));
            refusals.Add(Record.Exception(() => session.Login(credentials)));
            refusals.Add(Record.Exception(() => session.SendUse(1)));
        });
        other.Start();
        other.Join();

        Assert.Equal(3, refusals.Count);
        Assert.All(refusals, e => Assert.IsType<InvalidOperationException>(e));
        Assert.Equal(default, session.Pump(TimeSpan.Zero));
    }

    [Fact]
    public void A_negative_budget_is_refused()
    {
        using var session = AcSession.Create(Options(new ThrowingSocketFactory()), new NullSink());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Pump(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Dispose_closes_the_session_and_later_calls_are_refused()
    {
        var session = AcSession.Create(Options(new ThrowingSocketFactory()), new NullSink());
        session.Dispose();
        session.Dispose();

        Assert.Equal(SessionPhase.Closed, session.Phase);
        Assert.Throws<ObjectDisposedException>(() => session.Pump(TimeSpan.Zero));
        using var credentials = new AcCredentials("lifestone-test", "unused");
        Assert.Throws<ObjectDisposedException>(() => session.Login(credentials));
    }

    [Fact]
    public void A_create_request_is_validated_before_anything_is_sent()
    {
        using var session = AcSession.Create(Options(new ThrowingSocketFactory()), new NullSink());
        var request = new CharacterCreateRequest(
            Heritage: 1, Gender: 1, Appearance: default, TemplateOption: 0, Strength: 10, Endurance: 10, Coordination: 10,
            Quickness: 10, Focus: 10, Self: 10, Slot: 0, ClassId: 1, SkillAdvancementClasses: [.. new uint[54]], Name: "Lifestone Smoke", StartArea: 0);

        Assert.Throws<ArgumentException>(() => session.CreateCharacter(request));
    }

    /// <summary>
    /// The N5, N6 and N7 operations refuse an idle session with InvalidOperationException, and open nothing. No operation
    /// is a loud stub any more: the N7 world sends were the last.
    /// </summary>
    [Fact]
    public async Task Operations_that_need_a_connection_refuse_an_idle_session()
    {
        var factory = new ThrowingSocketFactory();
        using var session = AcSession.Create(Options(factory), new NullSink());

        Action[] operations =
        [
            () => session.DeleteCharacter(0),
            () => session.RestoreCharacter(1),
            () => session.EnterWorld(1),
            () => session.SendLoginComplete(),
            () => session.SendMoveToState(RawMotionStateWire.Default, default, default, true, false),
            () => session.SendAutonomousPosition(default, default, true),
            () => session.SendJump(0.5f, default, default, default),
            () => session.SendUse(1),
            () => session.SendQueryHealth(1),
            () => session.SendTalk("hello"),
            () => session.SendTell("someone", "hello"),
            session.LogOff,
        ];

        Assert.All(operations, o => Assert.Throws<InvalidOperationException>(o));
        Assert.Equal(0, factory.Calls);
        Assert.False(await session.WaitForInboundAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SessionPhase.Idle, session.Phase);
    }

    /// <summary>
    /// The session's own handler forwards every message to the sink: it must implement every member of
    /// IInboundMessageHandler itself, or a message added to the interface later would stop at the default no-op.
    /// </summary>
    [Fact]
    public void The_sessions_handler_forwards_every_handler_member()
    {
        var inbound = typeof(SessionMachine).GetNestedType("SessionInbound", System.Reflection.BindingFlags.NonPublic)!;
        var map = inbound.GetInterfaceMap(typeof(IInboundMessageHandler));
        var notForwarded = map.TargetMethods.Where(m => m.DeclaringType != inbound).Select(m => m.Name).ToList();

        Assert.True(map.InterfaceMethods.Length > 30, $"only {map.InterfaceMethods.Length} handler members found");
        Assert.True(notForwarded.Count == 0, "not forwarded to the sink: " + string.Join(", ", notForwarded));
    }

    private static AcSessionOptions Options(ThrowingSocketFactory factory) => new()
    {
        Server = new IPEndPoint(IPAddress.Loopback, 9000),
        Iterations = EndOfRetail,
        Time = new ManualTimeProvider(),
        SocketFactory = factory.Open,
    };

    /// <summary>A socket factory that fails the test if it is ever called.</summary>
    private sealed class ThrowingSocketFactory
    {
        public int Calls { get; private set; }

        public IDatagramSocket Open(IPEndPoint server)
        {
            Calls++;
            throw new InvalidOperationException($"the socket factory must not be called (asked for {server})");
        }
    }

    /// <summary>A sink that ignores everything (every handler member has a no-op default).</summary>
    private sealed class NullSink : IAcSessionSink
    {
        public void OnSessionEvent(in SessionEvent sessionEvent)
        {
        }
    }
}
