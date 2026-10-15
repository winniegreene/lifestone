using System.Net;
using Lifestone.Net.Sockets;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// Virtual time around the fake server (m2-plan §3.3 N4): a <see cref="ManualTimeProvider"/>, an
/// <see cref="InMemoryNetwork"/> (with optional loss) and a <see cref="FakeAceServer"/> on 127.0.0.1:9000/9001. Each
/// <see cref="Step"/> runs one server tick, then every participant (a raw test client now; the session's pump from N5
/// on), then moves time forward by <see cref="TickLength"/>. Nothing waits on a real clock, so a two-minute soak takes
/// as long as its work.
/// </summary>
internal sealed class SessionHarness
{
    /// <summary>The default step: 5 ms, ACE's minimum time between bundles.</summary>
    public static readonly TimeSpan DefaultTick = TimeSpan.FromMilliseconds(5);

    private readonly List<Action> _participants = [];

    /// <summary>A harness for <paramref name="script"/> (a fresh default one when null), with optional loss.</summary>
    public SessionHarness(FakeAceScript? script = null, LossModel? clientToServer = null, LossModel? serverToClient = null, TimeSpan? tick = null)
    {
        Time = new ManualTimeProvider();
        Network = new InMemoryNetwork(Time, clientToServer, serverToClient);
        Script = script ?? new FakeAceScript();
        Server = new FakeAceServer(Network, Time, Script);
        TickLength = tick ?? DefaultTick;
    }

    /// <summary>The only clock.</summary>
    public ManualTimeProvider Time { get; }

    /// <summary>The network.</summary>
    public InMemoryNetwork Network { get; }

    /// <summary>The fake server's script.</summary>
    public FakeAceScript Script { get; }

    /// <summary>The fake server.</summary>
    public FakeAceServer Server { get; }

    /// <summary>How far one step moves time.</summary>
    public TimeSpan TickLength { get; }

    /// <summary>False stops the server's ticks (a killed server: it neither reads nor sends); true by default.</summary>
    public bool ServerAlive { get; set; } = true;

    /// <summary>The login endpoint a session is created for.</summary>
    public IPEndPoint ServerEndPoint => Server.LoginEndPoint;

    /// <summary>A socket factory for <c>AcSessionOptions.SocketFactory</c>: in-memory sockets on this network.</summary>
    public Func<IPEndPoint, IDatagramSocket> SocketFactory => endpoint => Network.OpenClientSocket(endpoint);

    /// <summary>Adds work that runs after every server tick (a client's receive and send).</summary>
    public void AddParticipant(Action participant) => _participants.Add(participant ?? throw new ArgumentNullException(nameof(participant)));

    /// <summary>One server tick, then the participants, then time moves on by <see cref="TickLength"/>.</summary>
    public void Step()
    {
        if (ServerAlive)
        {
            Server.Tick();
        }

        foreach (var participant in _participants)
        {
            participant();
        }

        Time.Advance(TickLength);
    }

    /// <summary>Steps until <paramref name="duration"/> of virtual time has passed.</summary>
    public void RunFor(TimeSpan duration)
    {
        var end = Time.Elapsed + duration;
        while (Time.Elapsed < end)
        {
            Step();
        }
    }

    /// <summary>Steps until <paramref name="condition"/> holds (true) or <paramref name="timeout"/> of virtual time passes (false).</summary>
    public bool RunUntil(Func<bool> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var end = Time.Elapsed + timeout;
        while (!condition())
        {
            if (Time.Elapsed >= end)
            {
                return false;
            }

            Step();
        }

        return true;
    }
}
