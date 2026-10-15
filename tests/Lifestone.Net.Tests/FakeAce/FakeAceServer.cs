using System.Net;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// A fake ACE server for tests (m2-plan §3.3 N4): the login port and the world port on an <see cref="InMemoryNetwork"/>,
/// the session map, and the per-tick work of ACE's <c>NetworkManager</c> and <c>WorldManager</c>, written from
/// docs/protocol.md §1 to §4 and the ACE behaviours the plan adds (never from OpenAC's ACE models). Everything runs on
/// the caller's thread when <see cref="Tick"/> is called; time comes only from the given <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Tick"/> does what ACE does between two world-loop passes: it takes every datagram waiting on both
/// ports (ACE's <c>NetworkManager.ProcessPacket</c>), runs the work that was waiting on a delay (the account lookup,
/// the database write that flips a session to AuthConnectResponse), then runs every session's outbound tick (bundling,
/// acks, time syncs, logoff, termination) and drops the sessions whose termination finished.</para>
/// <para>The fake never logs. It sees test passwords only, compares them and keeps them nowhere else.</para>
/// </remarks>
internal sealed class FakeAceServer
{
    /// <summary>ACE's server id, the header Id of every server packet.</summary>
    public const ushort ServerId = 0x000B;

    private readonly InMemoryNetwork.ServerEndpoint _login;
    private readonly InMemoryNetwork.ServerEndpoint _world;
    private readonly FakeAceSession?[] _sessions;
    private readonly List<(long Due, Action Work)> _delayed = [];
    private readonly Random _random;
    private uint _nextCharacterGuid = 0x50000001;

    /// <summary>A server on <paramref name="network"/> at <paramref name="loginEndPoint"/> (default 127.0.0.1:9000) and Port + 1.</summary>
    public FakeAceServer(InMemoryNetwork network, TimeProvider time, FakeAceScript script, IPEndPoint? loginEndPoint = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Script = script ?? throw new ArgumentNullException(nameof(script));
        LoginEndPoint = loginEndPoint ?? new IPEndPoint(IPAddress.Loopback, 9000);
        WorldEndPoint = new IPEndPoint(LoginEndPoint.Address, LoginEndPoint.Port + 1);
        _login = network.BindServer(LoginEndPoint);
        _world = network.BindServer(WorldEndPoint);
        _sessions = new FakeAceSession?[script.MaximumSessions];
        _random = new Random(script.Seed);
        StartTimestamp = time.GetTimestamp();
        foreach (var character in script.Accounts.Values.SelectMany(a => a.Characters))
        {
            _nextCharacterGuid = Math.Max(_nextCharacterGuid, character.Guid + 1);
        }
    }

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>The script.</summary>
    public FakeAceScript Script { get; }

    /// <summary>The login port (client to server traffic, ConnectRequest and early rejections).</summary>
    public IPEndPoint LoginEndPoint { get; }

    /// <summary>The world port (ConnectResponse in; everything after it out).</summary>
    public IPEndPoint WorldEndPoint { get; }

    /// <summary>What the fake counted.</summary>
    public FakeAceCounters Counters { get; } = new();

    /// <summary>The live sessions (in the session map).</summary>
    public IEnumerable<FakeAceSession> Sessions => _sessions.Where(s => s is not null)!;

    /// <summary>Every session ever created, live or dropped, in creation order.</summary>
    public List<FakeAceSession> History { get; } = [];

    /// <summary>The timestamp the server started at.</summary>
    internal long StartTimestamp { get; }

    /// <summary>ACE's <c>PortalYearTicks</c>: the in-game clock in seconds.</summary>
    internal double PortalYearTicks => Script.PortalYearStart + Time.GetElapsedTime(StartTimestamp).TotalSeconds;

    /// <summary>One world-loop pass (see the remarks).</summary>
    public void Tick()
    {
        while (_login.TryDequeue(out var from, out var datagram))
        {
            ProcessDatagram(onWorldPort: false, from, datagram);
        }

        while (_world.TryDequeue(out var from, out var datagram))
        {
            ProcessDatagram(onWorldPort: true, from, datagram);
        }

        RunDueWork();

        for (var i = 0; i < _sessions.Length; i++)
        {
            var session = _sessions[i];
            if (session is null)
            {
                continue;
            }

            session.TickOutbound();
            if (session.TerminationComplete)
            {
                _sessions[i] = null;
            }
        }
    }

    /// <summary>A graceful shutdown: every session is terminated with F659 0x04 (ACE <c>DisconnectAllSessionsForShutdown</c>).</summary>
    public void Shutdown()
    {
        foreach (var session in Sessions)
        {
            session.Terminate(FakeAceTermination.ServerShuttingDown, (AceWireWriters.CharacterError(0x04), FakeAceSession.UiQueue));
        }
    }

    /// <summary>Runs <paramref name="work"/> on the first tick at or after <paramref name="delay"/> from now.</summary>
    internal void After(TimeSpan delay, Action work)
    {
        var due = Time.GetTimestamp() + (long)(delay.TotalSeconds * Time.TimestampFrequency);
        _delayed.Add((due, work));
    }

    /// <summary>The live session logged in to <paramref name="account"/>, if any (ACE <c>NetworkManager.Find</c>).</summary>
    internal FakeAceSession? FindByAccount(string account) =>
        Sessions.SingleOrDefault(s => s.AccountName is not null && string.Equals(s.AccountName, account, StringComparison.Ordinal));

    /// <summary>The live session whose player is <paramref name="guid"/>, if any.</summary>
    internal FakeAceSession? FindByPlayer(uint guid) => Sessions.FirstOrDefault(s => s.PlayerGuid == guid);

    /// <summary>A fresh player GUID.</summary>
    internal uint NewPlayerGuid() => _nextCharacterGuid++;

    /// <summary>Random bytes from the script's seed (ISAAC seeds and cookies).</summary>
    internal uint NextRandomUInt32()
    {
        Span<byte> bytes = stackalloc byte[4];
        _random.NextBytes(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    /// <summary>Sends a datagram from the login or the world port.</summary>
    internal void SendFrom(bool worldPort, IPEndPoint to, ReadOnlySpan<byte> datagram)
    {
        Counters.S2CPackets++;
        (worldPort ? _world : _login).Send(to, datagram);
    }

    private void RunDueWork()
    {
        var now = Time.GetTimestamp();
        while (true)
        {
            var index = _delayed.FindIndex(d => d.Due <= now);
            if (index < 0)
            {
                return;
            }

            var work = _delayed[index].Work;
            _delayed.RemoveAt(index);
            work();
        }
    }

    /// <summary>ACE's <c>NetworkManager.ProcessPacket</c>.</summary>
    private void ProcessDatagram(bool onWorldPort, IPEndPoint from, byte[] datagram)
    {
        var packet = ClientPacketParser.TryParse(datagram);
        if (packet is null)
        {
            Counters.Unparsable++;
            return;
        }

        if (onWorldPort)
        {
            if (packet.ConnectResponseCookie is { } cookie)
            {
                var session = Sessions.FirstOrDefault(s =>
                    s.State == FakeAceSessionState.AuthConnectResponse && s.ConnectionCookie == cookie && s.EndPointC2S.Address.Equals(from.Address));
                if (session is null)
                {
                    Counters.ConnectResponsesIgnored++;
                    return;
                }

                session.OnConnectResponse(from);
            }
            else if (!(packet.Id == 0 && packet.Has(ClientPacketParser.CicmdCommand)))
            {
                Counters.RejectedOnWorldPort++;
            }

            return;
        }

        if (packet.Has(ClientPacketParser.LoginRequest))
        {
            if (Sessions.Count(s => s.AccountName is not null) >= _sessions.Length)
            {
                SendLoginRequestReject(from, 0x15);
                return;
            }

            var session = Sessions.SingleOrDefault(s => s.EndPointC2S.Equals(from)) ?? CreateSession(from);
            if (session is null)
            {
                SendLoginRequestReject(from, 0x15);
                return;
            }

            if (session.State == FakeAceSessionState.AuthConnectResponse)
            {
                // The ConnectRequest was lost and the client started over from the same endpoint: ACE removes the session
                // ("Bad handshake") and still hands it the packet, which its state check then ignores.
                Counters.BadHandshakes++;
                Remove(session);
            }

            session.ProcessPacket(packet);
            return;
        }

        var target = packet.Id < _sessions.Length ? _sessions[packet.Id] : null;
        if (target is null || !target.EndPointC2S.Equals(from))
        {
            Counters.Unsolicited++;
            return;
        }

        target.ProcessPacket(packet);
    }

    private FakeAceSession? CreateSession(IPEndPoint from)
    {
        for (ushort i = 0; i < _sessions.Length; i++)
        {
            if (_sessions[i] is null)
            {
                var session = new FakeAceSession(this, from, i);
                _sessions[i] = session;
                History.Add(session);
                return session;
            }
        }

        return null;
    }

    private void Remove(FakeAceSession session)
    {
        if (_sessions[session.ClientId] == session)
        {
            _sessions[session.ClientId] = null;
        }
    }

    /// <summary>ACE's <c>SendLoginRequestReject</c>: a throwaway session sends a ConnectRequest, then the error.</summary>
    private void SendLoginRequestReject(IPEndPoint from, uint error)
    {
        var temporary = new FakeAceSession(this, from, (ushort)(_sessions.Length + 1));
        temporary.SendLoginRequestReject(error);
    }
}
