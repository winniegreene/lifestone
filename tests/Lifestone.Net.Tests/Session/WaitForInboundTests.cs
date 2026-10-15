using System.Net;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Sockets;
using Lifestone.Net.Tests.Sockets;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// <see cref="IAcSession.WaitForInboundAsync"/> keeps its contract when nothing can arrive: an idle or failed session,
/// or a socket whose receive loop has ended, is slept out for the whole wait (on the session's clock), so a host that
/// loops on Pump and the wait never spins. Only an infinite wait, which would never end, returns at once.
/// </summary>
public sealed class WaitForInboundTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task An_idle_session_sleeps_out_the_wait()
    {
        var time = new ManualTimeProvider();
        using var session = AcSession.Create(Options(new IPEndPoint(IPAddress.Loopback, 9000), time, _ => throw new InvalidOperationException("no socket expected")), new NullSink());

        var wait = session.WaitForInboundAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(wait.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(await wait.WaitAsync(Patience));

        Assert.False(await session.WaitForInboundAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await session.WaitForInboundAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).AsTask().WaitAsync(Patience));
        Assert.Equal(SessionPhase.Idle, session.Phase);
    }

    [Fact]
    public async Task An_idle_wait_ends_on_cancellation()
    {
        var time = new ManualTimeProvider();
        using var session = AcSession.Create(Options(new IPEndPoint(IPAddress.Loopback, 9000), time, _ => throw new InvalidOperationException("no socket expected")), new NullSink());
        using var cancel = new CancellationTokenSource();

        var wait = session.WaitForInboundAsync(TimeSpan.FromSeconds(5), cancel.Token).AsTask();
        Assert.False(wait.IsCompleted);
        cancel.Cancel();
        Assert.False(await wait.WaitAsync(Patience));
    }

    [Fact]
    public async Task A_failed_session_sleeps_out_the_wait()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script, o => o with { Retry = LoginRetryPolicy.Retail });
        rig.Login();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));

        var wait = rig.Session.WaitForInboundAsync(TimeSpan.FromSeconds(2), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        rig.Harness.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await wait.WaitAsync(Patience));
        Assert.False(await rig.Session.WaitForInboundAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).AsTask().WaitAsync(Patience));
    }

    /// <summary>
    /// The real socket's receive loop ends on a socket error, after which its own wait returns false at once; the
    /// session's wait still lasts its full length (the silence watchdog fails the session on a later pump).
    /// </summary>
    [Fact]
    public async Task A_socket_whose_receive_loop_faulted_is_slept_out_not_spun_on()
    {
        using var server = UdpLoopbackSocketTests.FakeServer.Open();
        var time = new ManualTimeProvider();
        UdpLoopbackSocket? socket = null;
        using var session = AcSession.Create(Options(server.Login, time, endpoint => socket = new UdpLoopbackSocket(endpoint, time)), new NullSink());
        using (var credentials = new AcCredentials(SessionRig.Account, SessionRig.Password))
        {
            session.Login(credentials);
        }

        Assert.NotNull(socket);
        socket.BreakForTests();
        await socket.ReceiveLoop.WaitAsync(Patience);
        Assert.NotNull(socket.Fault);

        var wait = session.WaitForInboundAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(wait.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(await wait.WaitAsync(Patience));

        Assert.False(await session.WaitForInboundAsync(Timeout.InfiniteTimeSpan, CancellationToken.None).AsTask().WaitAsync(Patience));
        Assert.Equal(SessionPhase.Connecting, session.Phase);
    }

    private static AcSessionOptions Options(IPEndPoint server, TimeProvider time, Func<IPEndPoint, IDatagramSocket> sockets) => new()
    {
        Server = server,
        Iterations = new DddIterations(2072, 982, 994, null),
        Time = time,
        SocketFactory = sockets,
    };

    /// <summary>A sink that ignores everything (every handler member has a no-op default).</summary>
    private sealed class NullSink : IAcSessionSink
    {
        public void OnSessionEvent(in SessionEvent sessionEvent)
        {
        }
    }
}
