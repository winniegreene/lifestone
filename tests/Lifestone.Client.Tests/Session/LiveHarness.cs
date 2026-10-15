using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A live <see cref="GameSession"/> built by <see cref="SessionBootstrap"/>'s internal entry with every gate passing on
/// stand-in files (the test account's sentinel password), a manual clock, a log that records every line, an in-memory
/// recording, and the <see cref="ScriptedAceSession"/> in place of the network session (a new one on each reconnect).
/// </summary>
internal sealed class LiveHarness : IDisposable
{
    /// <summary>The frame step <see cref="Run"/> uses, in seconds.</summary>
    public const double Frame = 0.05;

    private readonly List<ScriptedAceSession> _servers = [];

    public LiveHarness(bool record = true, SessionOptions? options = null, byte[]? secrets = null, Action<ScriptedAceSession>? configure = null)
    {
        Recording = record ? new MemoryStream() : null;
        Session = SessionBootstrap.CreateLiveWith(
            options ?? new SessionOptions { Account = "test" },
            secrets ?? SessionTestData.SecretsV2(),
            SessionTestData.PidsReady(),
            () => SessionTestData.EndOfRetail,
            Time,
            Log,
            Recording,
            Sockets.Open,
            new RecordingHeader { Runner = "test", CreatedUtc = Time.GetUtcNow(), Scenario = "harness" },
            (netOptions, sink, tap) =>
            {
                NetOptions = netOptions;
                var server = new ScriptedAceSession(sink, tap, Time);
                configure?.Invoke(server);
                _servers.Add(server);
                return server;
            });
    }

    public ManualTimeProvider Time { get; } = new();

    public RecordingLogSink Log { get; } = new();

    public SocketFactorySpy Sockets { get; } = new();

    public MemoryStream? Recording { get; }

    public GameSession Session { get; }

    /// <summary>The network session's options the bootstrap built (the last one).</summary>
    public Lifestone.Net.Session.AcSessionOptions? NetOptions { get; private set; }

    /// <summary>The current scripted server.</summary>
    public ScriptedAceSession Server => _servers[^1];

    /// <summary>Every scripted server created, in order (one per reconnect).</summary>
    public IReadOnlyList<ScriptedAceSession> Servers => _servers;

    public GameState State => Session.State;

    public IActionApi Actions => Session.Actions;

    /// <summary>Advances the clock by one frame and ticks the session, <paramref name="seconds"/> worth of frames.</summary>
    public void Run(double seconds = Frame)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds / Frame));
        for (var i = 0; i < frames; i++)
        {
            Time.Advance(TimeSpan.FromSeconds(Frame));
            Session.Tick(Session.Clock.Now);
        }
    }

    /// <summary>Ticks frame by frame until <paramref name="condition"/> holds; false after <paramref name="maxSeconds"/>.</summary>
    public bool RunUntil(Func<bool> condition, double maxSeconds = 30)
    {
        for (var t = 0.0; t <= maxSeconds; t += Frame)
        {
            if (condition())
            {
                return true;
            }

            Run();
        }

        return condition();
    }

    /// <summary>Logs in, enters as Amy, sends LoginComplete and waits until the player is present.</summary>
    public void Present()
    {
        var login = Actions.Login();
        Assert.True(RunUntil(() => login.IsCompleted), "login did not complete");
        Assert.Equal(ActionOutcomeKind.Confirmed, login.Outcome!.Value.Kind);
        var enter = Actions.EnterWorld(ScriptedAceSession.Amy, ActionSource.Host);
        Assert.True(RunUntil(() => enter.IsCompleted), "enter did not complete");
        Assert.Equal(ActionOutcomeKind.Confirmed, enter.Outcome!.Value.Kind);
        var complete = Actions.LoginComplete();
        Assert.True(RunUntil(() => complete.IsCompleted), "LoginComplete did not complete");
        Assert.Equal(ActionOutcomeKind.Confirmed, complete.Outcome!.Value.Kind);
        Assert.Equal(SelfTravel.Present, State.Self.Travel);
    }

    /// <summary>The recording's bytes so far (the session must be disposed for the trailer).</summary>
    public byte[] RecordingBytes() => Recording?.ToArray() ?? [];

    public void Dispose() => Session.Dispose();
}
