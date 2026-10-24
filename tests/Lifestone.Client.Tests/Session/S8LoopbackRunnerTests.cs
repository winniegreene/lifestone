using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A0's S8 (docs/automation-vision.md): every runner that could reach a server refuses a non-loopback endpoint before
/// anything exists that could contact it. For each runner and each refused endpoint: the run is refused, the injected
/// socket factory is never invoked (so no socket can exist), nothing past the gate runs, and one WARNING names the runner.
/// The rows grow as runners land: session creation (ST2); the Action API's automation actions and movement intents
/// (ST10, ST11) and the player tools (ST12), which refuse through their handle rather than an exception; the headless
/// runner (ST15, which gates before it creates the session). The game's SessionHost and the scenarios have no row of their own, by
/// design: the SessionHost creates its session only through SessionBootstrap.CreateLive (the first row; ArchitectureTests'
/// Only_the_SessionBootstrap_opens_a_network_session pins that no other type opens one), and a scenario runs inside a runner above.
/// SessionBootstrap is the single choke point; LoopbackPolicy.Require in the socket and AcSession.Create back it up.
/// </summary>
public sealed class S8LoopbackRunnerTests
{
    /// <summary>The runners: each runs once against an endpoint and returns what it threw (or null).</summary>
    private static readonly IReadOnlyDictionary<string, Func<Probe, Exception?>> Runners = new Dictionary<string, Func<Probe, Exception?>>
    {
        [SessionBootstrap.Runner] = probe => Record.Exception(() => SessionBootstrap.CreateLiveWith(
            new SessionOptions { Server = probe.EndPoint },
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            probe.Iterations,
            new ManualTimeProvider(),
            probe.Log,
            recording: null,
            probe.Sockets.Open).Dispose()),
        [HeadlessRunner.RunnerName] = probe => Record.Exception(() => HeadlessRunner.StartWith(
            new HeadlessRunOptions { Session = new SessionOptions { Server = probe.EndPoint, Account = "test" } },
            new LoginScenario(),
            SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            probe.Iterations,
            () => HeadlessTestWorld.Assets,
            _ => new HeadlessTestWorld(),
            new ManualTimeProvider(),
            probe.Log,
            recording: null,
            probe.Sockets.Open,
            networkFactory: null).Dispose()),
    };

    /// <summary>The runners that refuse through a handle: each acts once in a session created for the endpoint.</summary>
    private static readonly IReadOnlyDictionary<string, Func<ActionHarness, ActionHandle>> HandleRunners = new Dictionary<string, Func<ActionHarness, ActionHandle>>
    {
        ["IActionApi.Use (automation)"] = h => h.Api.Use(0x80000100, ActionSource.Automation),
        ["IActionApi.Talk (automation)"] = h => h.Api.Talk("hello", ActionSource.Automation),
        ["IActionApi.LogOff (automation)"] = h => h.Api.LogOff(ActionSource.Automation),
        ["IActionApi.MoveIntent (automation)"] = h => h.Api.Move.Command(Lifestone.Data.Animation.MotionCommands.WalkForward, true, ActionSource.Automation),
        ["PlayerTools.WhereAmI"] = h => h.Api.Tools.WhereAmI(ActionSource.Ui),
        ["PlayerTools.AddXp"] = h => h.Api.Tools.AddXp(100, ActionSource.Automation),
    };

    public static TheoryData<string, string> HandleRefusedRows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var runner in HandleRunners.Keys)
            {
                foreach (var endPoint in SessionTestData.RefusedIpById.Keys)
                {
                    rows.Add(runner, endPoint);
                }
            }

            return rows;
        }
    }

    public static TheoryData<string, string> HandleAllowedRows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var runner in HandleRunners.Keys)
            {
                foreach (var endPoint in SessionTestData.AllowedById.Keys)
                {
                    rows.Add(runner, endPoint);
                }
            }

            return rows;
        }
    }

    public static TheoryData<string, string> RefusedRows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var runner in Runners.Keys)
            {
                foreach (var endPoint in SessionTestData.RefusedIpById.Keys)
                {
                    rows.Add(runner, endPoint);
                }
            }

            return rows;
        }
    }

    public static TheoryData<string, string> AllowedRows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var runner in Runners.Keys)
            {
                foreach (var endPoint in SessionTestData.AllowedById.Keys)
                {
                    rows.Add(runner, endPoint);
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(RefusedRows))]
    public void Runners_refuse_a_non_loopback_endpoint_before_any_socket(string runner, string endPoint)
    {
        var probe = new Probe(SessionTestData.RefusedIpById[endPoint]);
        var thrown = Runners[runner](probe);

        var refused = Assert.IsType<LoopbackRequiredException>(thrown);
        Assert.Equal(0, probe.Sockets.Calls);
        Assert.Equal(0, probe.IterationCalls);
        var warning = Assert.Single(probe.Log.Entries);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.Contains(runner, warning.Message, StringComparison.Ordinal);
        Assert.Contains(refused.Refusal, warning.Message, StringComparison.Ordinal);
        Assert.All(SessionTestData.Sentinels, s => Assert.False(probe.Log.AnyLineContains(s)));
    }

    [Theory]
    [MemberData(nameof(AllowedRows))]
    public void Runners_pass_the_gate_for_a_loopback_endpoint_without_opening_a_socket(string runner, string endPoint)
    {
        var probe = new Probe(SessionTestData.AllowedById[endPoint]);
        var thrown = Runners[runner](probe);

        // Past the gate: an idle session, no socket until the login.
        Assert.Null(thrown);
        Assert.Equal(0, probe.Sockets.Calls);
        Assert.DoesNotContain(probe.Log.Entries, e => e.Message.Contains("loopback gate", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(HandleRefusedRows))]
    public void Automation_and_the_tools_refuse_a_non_loopback_server_through_their_handle(string runner, string endPoint)
    {
        var h = new ActionHarness(server: SessionTestData.RefusedIpById[endPoint]);
        h.Present();
        var controller = new SpyController();
        h.Api.Move.Bind(controller, false);
        var before = h.Log.Entries.Count;

        var handle = HandleRunners[runner](h);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.Empty(h.Net.Sends);
        Assert.Equal(0, controller.Calls);
        var warning = Assert.Single(h.Log.Entries.Skip(before), e => e.Severity == LogSeverity.Warning);
        Assert.Contains(runner, warning.Message, StringComparison.Ordinal);
        Assert.Contains(handle.Outcome.Value.Detail!, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HandleAllowedRows))]
    public void Automation_and_the_tools_pass_the_gate_for_a_loopback_server(string runner, string endPoint)
    {
        var h = new ActionHarness(server: SessionTestData.AllowedById[endPoint]);
        h.Present();
        h.Api.Move.Bind(new SpyController(), false);
        var handle = HandleRunners[runner](h);

        Assert.NotEqual(ActionOutcomeKind.Refused, handle.Outcome?.Kind);
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("loopback gate", StringComparison.Ordinal));
    }

    [Fact]
    public void The_table_covers_every_runner_built_so_far()
    {
        Assert.Contains(SessionBootstrap.Runner, Runners.Keys);
        Assert.Contains(HeadlessRunner.RunnerName, Runners.Keys);
        Assert.Contains(HandleRunners.Keys, k => k.StartsWith("IActionApi.", StringComparison.Ordinal));
        Assert.Contains(HandleRunners.Keys, k => k.StartsWith("PlayerTools.", StringComparison.Ordinal));
        Assert.True(SessionTestData.RefusedIpById.Count >= 8);
    }

    /// <summary>A movement controller that counts what reaches it.</summary>
    private sealed class SpyController : Lifestone.Client.Movement.IPlayerMovementIntents
    {
        public int Calls { get; private set; }

        public Lifestone.Data.World.CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) => Calls++;

        public void SetHoldRun(bool on) => Calls++;

        public void SetHoldSidestep(bool on) => Calls++;

        public void ToggleAutoRun() => Calls++;

        public void CommenceJump(double now) => Calls++;

        public void DoJump(double now) => Calls++;
    }

    /// <summary>One run's spies: the socket factory, the dat check and the log.</summary>
    internal sealed class Probe(IPEndPoint endPoint)
    {
        public IPEndPoint EndPoint { get; } = endPoint;

        public SocketFactorySpy Sockets { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public int IterationCalls { get; private set; }

        public DddIterations Iterations()
        {
            IterationCalls++;
            return SessionTestData.EndOfRetail;
        }
    }
}
