using System.Buffers.Binary;
using System.Net;
using Lifestone.Net.Crypto;
using NetAuthType = Lifestone.Net.Packets.LoginRequest.NetAuthType;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>The states of a fake session (ACE's <c>SessionState</c>).</summary>
internal enum FakeAceSessionState
{
    /// <summary>Waiting for (or working on) the LoginRequest.</summary>
    AuthLoginRequest,

    /// <summary>The ConnectRequest went out and the database write finished: a ConnectResponse is accepted now.</summary>
    AuthConnectResponse,

    /// <summary>Character select.</summary>
    AuthConnected,

    /// <summary>In the world.</summary>
    WorldConnected,

    /// <summary>Terminating: flushing for two seconds, then silent.</summary>
    TerminationStarted,
}

/// <summary>
/// One client session of the <see cref="FakeAceServer"/>: ACE's <c>Session</c> and <c>NetworkSession</c> as
/// docs/protocol.md §2 to §4 describe them, plus the scripted character-select and world handlers. Each rule cites the
/// ACE code it reproduces (ACEmulator/ACE @95ef426d, read, not copied).
/// </summary>
/// <remarks>
/// <para><b>Client to server.</b> Every packet's checksum is verified first (an encrypted one through ACE's key window,
/// <see cref="AceCryptoSystemOracle"/>), so a duplicate encrypted packet costs a CRC error before its sequence is even
/// looked at. Then: a cleartext NAK is serviced (cached packets resent, the rest refused with a RejectRetransmit);
/// Disconnect terminates; a sequence at or below the last one is dropped, except an ack-only packet equal to it;
/// a sequence ahead is buffered, and NAKed (at most once a second, at most 115 ids) when it is two or more ahead; more
/// than 256 ahead terminates. Ack-only packets do not move the last sequence. Messages are handled strictly in
/// fragment-sequence order from 1 (<c>NetworkSession.ProcessPacket</c>, <c>ProcessFragment</c>).</para>
/// <para><b>Server to client.</b> One bundle per message group, at most one bundle per 5 ms, groups in order 0 to 11;
/// one fragment counter from 0; a tail fragment first when it fits; UI-queue messages strictly in order. ConnectRequest
/// is sequence 0, the first encrypted packet 2; ack-only and NAK packets reuse the current sequence; a cleartext
/// RejectRetransmit takes a new sequence and no key. Packets from sequence 2 on (NAKs excepted) are cached 120 s. Acks
/// go out every 2 s (the first 2 s after the session starts), time syncs every 20 s from the ConnectResponse on
/// (<c>NetworkSession.Update</c>, <c>SendBundle</c>, <c>FlushPackets</c>).</para>
/// <para><b>Deviation.</b> ACE queues each client message for the world thread; the fake handles it at once, inside
/// the tick that received it. Nothing on the wire depends on the difference.</para>
/// </remarks>
internal sealed class FakeAceSession
{
    /// <summary>Message groups (docs/protocol.md §2.3).</summary>
    public const ushort InvalidQueue = 0, DatabaseQueue = 5, UiQueue = 9, SmartboxQueue = 10;

    /// <summary>The newly logged-in player's physics state: Hidden | IgnoreCollisions | Gravity | EdgeSlide.</summary>
    public const uint EnteringPhysicsState = 0x4000 | 0x10 | 0x400 | 0x400000;

    private const int GroupCount = 12;
    private const int MaxPacketPayload = 464;
    private const int MaxFragmentData = 448;
    private const int MaxNakIds = 115;

    private static readonly TimeSpan MinimumTimeBetweenBundles = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan TimeBetweenTimeSync = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TimeBetweenAck = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CachedPacketRetention = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CachedPacketPruneInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingResponseInterval = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan RequestForRetransmitInterval = TimeSpan.FromSeconds(1);

    private readonly FakeAceServer _server;
    private readonly Bundle[] _bundles = new Bundle[GroupCount];
    private readonly Dictionary<uint, ClientPacket> _outOfOrderPackets = [];
    private readonly Dictionary<uint, MessageBuffer> _partialFragments = [];
    private readonly Dictionary<uint, byte[]> _outOfOrderFragments = [];
    private readonly Dictionary<uint, ServerPacket> _cachedPackets = [];
    private readonly Queue<ServerPacket> _packetQueue = new();
    private readonly IsaacRandom _isaacServer;

    private uint _packetSequence = uint.MaxValue;  // ACE's UIntSequence(false): the first NextValue is 0
    private uint _fragmentSequence;
    private uint _lastReceivedPacketSequence = 1;
    private uint _lastReceivedFragmentSequence;
    private bool _seedsDiscarded;
    private bool _sendResync;
    private long _nextSend;
    private long _nextResync;
    private long _nextAck;
    private long _lastPrune;
    private long? _lastRequestForRetransmit;
    private long _timeoutAt;
    private long? _terminationEnd;
    private long? _logOffRequested;
    private long? _nextPingResponse;
    private bool _released;
    private uint _physicsState;
    private ushort _instanceSequence = 1;
    private ushort _positionSequence;
    private ushort _stateSequence;
    private FakeAccount? _account;

    /// <summary>A session for a client at <paramref name="endPoint"/> in slot <paramref name="clientId"/>.</summary>
    public FakeAceSession(FakeAceServer server, IPEndPoint endPoint, ushort clientId)
    {
        _server = server;
        EndPointC2S = endPoint;
        ClientId = clientId;
        ServerSeed = server.NextRandomUInt32();
        ClientSeed = server.NextRandomUInt32();
        ConnectionCookie = ((ulong)server.NextRandomUInt32() << 32) | server.NextRandomUInt32();
        CryptoClient = new AceCryptoSystemOracle(ClientSeed);
        _isaacServer = new IsaacRandom(BitConverter.GetBytes(ServerSeed));
        for (var i = 0; i < GroupCount; i++)
        {
            _bundles[i] = new Bundle();
        }

        var now = Now;
        _nextSend = now;
        _nextResync = now;
        _nextAck = now + Ticks(TimeBetweenAck);
        _lastPrune = now;
        _timeoutAt = now + Ticks(server.Script.AuthTimeout);
    }

    /// <summary>The state.</summary>
    public FakeAceSessionState State { get; private set; }

    /// <summary>Where the client sends from (the LoginRequest's source).</summary>
    public IPEndPoint EndPointC2S { get; }

    /// <summary>Where the server sends to once the ConnectResponse arrived.</summary>
    public IPEndPoint? EndPointS2C { get; private set; }

    /// <summary>The client id (the slot in the session map; the header Id of the client's later packets).</summary>
    public ushort ClientId { get; }

    /// <summary>The cookie of the ConnectRequest.</summary>
    public ulong ConnectionCookie { get; }

    /// <summary>The seed of the server-to-client keystream.</summary>
    public uint ServerSeed { get; }

    /// <summary>The seed of the client-to-server keystream.</summary>
    public uint ClientSeed { get; }

    /// <summary>The client-to-server key window.</summary>
    public AceCryptoSystemOracle CryptoClient { get; }

    /// <summary>The account name once the login passed (the string F658 carries); null before.</summary>
    public string? AccountName { get; private set; }

    /// <summary>The account's characters as this session lists them.</summary>
    public List<FakeCharacter> Characters { get; } = [];

    /// <summary>The player in the world, if any.</summary>
    public uint? PlayerGuid { get; private set; }

    /// <summary>True from entering the world (or a teleport) until LoginComplete: positions are ignored meanwhile.</summary>
    public bool Teleporting { get; private set; }

    /// <summary>The next GameEvent sequence (ACE <c>GameEventSequence</c>).</summary>
    public uint GameEventSequence { get; private set; }

    /// <summary>Why the session is terminating, once it is.</summary>
    public FakeAceTermination? TerminationReason { get; private set; }

    /// <summary>True once termination finished (the server then forgets the session).</summary>
    public bool TerminationComplete { get; private set; }

    /// <summary>The last client packet sequence processed in order (starts at 1).</summary>
    public uint LastReceivedPacketSequence => _lastReceivedPacketSequence;

    /// <summary>The last client message (fragment) sequence handled (starts at 0).</summary>
    public uint LastReceivedFragmentSequence => _lastReceivedFragmentSequence;

    /// <summary>The server's current packet sequence.</summary>
    public uint CurrentPacketSequence => _packetSequence;

    /// <summary>Every client message a handler took, opcode first, in the order handled.</summary>
    public List<byte[]> HandledMessages { get; } = [];

    /// <summary>The positions accepted from MoveToState and AutonomousPosition.</summary>
    public List<AcePosition> AcceptedPositions { get; } = [];

    /// <summary>The objects the client used (GameAction 0x0036).</summary>
    public List<uint> UsedObjects { get; } = [];

    /// <summary>Jumps read (GameAction 0xF61B).</summary>
    public int JumpsAccepted { get; private set; }

    /// <summary>The name of the character in the world, for the speech and tell replies.</summary>
    private string PlayerName => Characters.SingleOrDefault(c => c.Guid == PlayerGuid)?.Name ?? string.Empty;

    /// <summary>The player's current position.</summary>
    public AcePosition PlayerPosition { get; private set; }

    private long Now => _server.Time.GetTimestamp();

    private FakeAceScript Script => _server.Script;

    private FakeAceCounters Counters => _server.Counters;

    // ---- Client to server ----

    /// <summary>ACE's <c>Session.ProcessPacket</c>: the state check, then <c>NetworkSession.ProcessPacket</c>.</summary>
    public void ProcessPacket(ClientPacket packet)
    {
        if (!CheckState(packet) || _released)
        {
            return;
        }

        Counters.C2SPackets++;
        if (!ClientPacketParser.VerifyCrc(packet, CryptoClient))
        {
            Counters.C2SCrcErrors++;
            return;
        }

        if (packet.Has(ClientPacketParser.RequestRetransmit) && !packet.Has(ClientPacketParser.EncryptedChecksum))
        {
            var uncached = packet.RetransmitRequests.Where(s => !Retransmit(s)).ToList();
            if (uncached.Count > 0)
            {
                var reject = new ServerPacket { Flags = ClientPacketParser.RejectRetransmit };
                reject.Optional.AddRange(BitConverter.GetBytes(uncached.Count));
                foreach (var sequence in uncached)
                {
                    reject.Optional.AddRange(BitConverter.GetBytes(sequence));
                }

                _packetQueue.Enqueue(reject);
            }

            Counters.C2SRequestsForRetransmit++;
            return;
        }

        if (packet.Has(ClientPacketParser.Disconnect))
        {
            Terminate(FakeAceTermination.PacketHeaderDisconnect);
            return;
        }

        if (packet.Has(ClientPacketParser.NetErrorDisconnect))
        {
            Terminate(FakeAceTermination.ClientSentNetworkErrorDisconnect);
            return;
        }

        _timeoutAt = Now + Ticks(State == FakeAceSessionState.AuthLoginRequest ? Script.AuthTimeout : Script.SessionTimeout);

        if (packet.Sequence <= _lastReceivedPacketSequence && packet.Sequence != 0
            && !(packet.Flags == ClientPacketParser.AckSequence && packet.Sequence == _lastReceivedPacketSequence))
        {
            Counters.DuplicatesDropped++;
            return;
        }

        var desired = _lastReceivedPacketSequence + 1;
        if (packet.Sequence > desired)
        {
            if (_outOfOrderPackets.TryAdd(packet.Sequence, packet))
            {
                Counters.OutOfOrderBuffered++;
            }
            else
            {
                Counters.OutOfOrderSlotTaken++;
                if (_outOfOrderPackets[packet.Sequence].Flags == ClientPacketParser.AckSequence && packet.Fragments.Count > 0)
                {
                    Counters.DataLostBehindAckOnly++;
                }
            }

            if (desired + 2 <= packet.Sequence
                && (_lastRequestForRetransmit is not { } last || Now - last > Ticks(RequestForRetransmitInterval)))
            {
                DoRequestForRetransmission(packet.Sequence);
            }

            return;
        }

        HandleOrderedPacket(packet);
        while (_outOfOrderPackets.Remove(_lastReceivedPacketSequence + 1, out var next))
        {
            HandleOrderedPacket(next);
        }

        CheckOutOfOrderFragments();
    }

    /// <summary>The ConnectResponse matched this session (<c>NetworkManager.ProcessPacket</c>, world port).</summary>
    public void OnConnectResponse(IPEndPoint from)
    {
        EndPointS2C = from;
        State = FakeAceSessionState.AuthConnected;
        _sendResync = true;

        // AuthenticationHandler.HandleConnectResponse.
        if (!Script.WorldOpen)
        {
            Terminate(FakeAceTermination.WorldClosed, (AceWireWriters.CharacterError(0x15), UiQueue));
            return;
        }

        RefreshCharacters();
        EnqueueSend((CharacterListBody(), UiQueue), (ServerNameBody(), UiQueue));
        EnqueueSend((AceWireWriters.DddInterrogation(Script.AllowHighResDat), DatabaseQueue));
    }

    private bool CheckState(ClientPacket packet)
    {
        const uint all = ClientPacketParser.AckSequence | ClientPacketParser.TimeSync | ClientPacketParser.EchoRequest | ClientPacketParser.Flow;
        if (packet.Has(ClientPacketParser.LoginRequest) && State != FakeAceSessionState.AuthLoginRequest)
        {
            return false;
        }

        if (packet.Has(ClientPacketParser.ConnectResponse) && State != FakeAceSessionState.AuthConnectResponse)
        {
            return false;
        }

        // ACE's HasFlag of four flags at once: true only when all four are set.
        return !((packet.Flags & all) == all && State == FakeAceSessionState.AuthLoginRequest);
    }

    private void HandleOrderedPacket(ClientPacket packet)
    {
        if (packet.EchoRequestClientTime is { } clientTime)
        {
            _bundles[InvalidQueue].ClientTime = clientTime;
            _bundles[InvalidQueue].Encrypted = true;
        }

        if (packet.Has(ClientPacketParser.AckSequence))
        {
            foreach (var cached in _cachedPackets.Keys.Where(k => k < packet.AckSequence).ToList())
            {
                _cachedPackets.Remove(cached);
            }
        }

        if (packet.Has(ClientPacketParser.LoginRequest))
        {
            HandleLoginRequest(packet);
            return;
        }

        foreach (var fragment in packet.Fragments)
        {
            ProcessFragment(fragment);
        }

        if (packet.Sequence != 0 && packet.Flags != ClientPacketParser.AckSequence)
        {
            _lastReceivedPacketSequence = packet.Sequence;
        }
    }

    private void ProcessFragment(ClientFragment fragment)
    {
        byte[]? message = null;
        if (fragment.Count != 1)
        {
            if (_partialFragments.TryGetValue(fragment.Sequence, out var buffer))
            {
                buffer.Add(fragment);
                if (buffer.Complete)
                {
                    message = buffer.TryGetMessage();
                    _partialFragments.Remove(fragment.Sequence);
                }
            }
            else
            {
                var created = new MessageBuffer(fragment.Count);
                created.Add(fragment);
                _partialFragments.Add(fragment.Sequence, created);
            }
        }
        else if (fragment.Data.Length >= 4)
        {
            message = fragment.Data;
        }

        if (message is null)
        {
            return;
        }

        if (fragment.Sequence == _lastReceivedFragmentSequence + 1)
        {
            HandleFragment(message);
        }
        else
        {
            _outOfOrderFragments.TryAdd(fragment.Sequence, message);
        }
    }

    private void CheckOutOfOrderFragments()
    {
        while (_outOfOrderFragments.Remove(_lastReceivedFragmentSequence + 1, out var message))
        {
            HandleFragment(message);
        }
    }

    private void HandleFragment(byte[] message)
    {
        HandleClientMessage(message);
        _lastReceivedFragmentSequence++;
    }

    /// <summary>ACE's <c>DoRequestForRetransmission</c>.</summary>
    private void DoRequestForRetransmission(uint received)
    {
        var desired = _lastReceivedPacketSequence + 1;
        var bottom = desired + 1;
        if (received < bottom || received - bottom > AceCryptoSystemOracle.MaximumEffortLevel)
        {
            Terminate(FakeAceTermination.AbnormalSequenceReceived);
            return;
        }

        var needed = new List<uint> { desired };
        for (var sequence = bottom; sequence < received; sequence++)
        {
            if (!_outOfOrderPackets.ContainsKey(sequence))
            {
                needed.Add(sequence);
                if (needed.Count >= MaxNakIds)
                {
                    break;
                }
            }
        }

        var nak = new ServerPacket { Flags = ClientPacketParser.RequestRetransmit };
        nak.Optional.AddRange(BitConverter.GetBytes((uint)needed.Count));
        foreach (var sequence in needed)
        {
            nak.Optional.AddRange(BitConverter.GetBytes(sequence));
        }

        _packetQueue.Enqueue(nak);
        _lastRequestForRetransmit = Now;
        Counters.S2CRequestsForRetransmit++;
    }

    // ---- Login (AuthenticationHandler) ----

    private void HandleLoginRequest(ClientPacket packet)
    {
        LoginRequestReader.Parsed login;
        try
        {
            login = LoginRequestReader.Read(packet.LoginBytes);
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            Counters.HandlerExceptions++;
            return;
        }

        if (login.Account.Length > 50)
        {
            SendLoginRequestReject(0x09);
            Terminate(FakeAceTermination.AccountInformationInvalid);
            return;
        }

        _server.After(Script.AccountLookupDelay, () => DoLogin(login));
    }

    private void DoLogin(LoginRequestReader.Parsed login)
    {
        Script.Accounts.TryGetValue(login.Account, out var account);
        if (account is null && login.NetAuth == NetAuthType.AccountPassword
            && !string.IsNullOrEmpty(login.Password) && Script.AllowAutoAccountCreation)
        {
            account = Script.AddAccount(login.Account.ToLowerInvariant(), login.Password);
        }

        AccountSelectCallback(account, login);
    }

    private void AccountSelectCallback(FakeAccount? account, LoginRequestReader.Parsed login)
    {
        if (_seedsDiscarded)
        {
            Terminate(FakeAceTermination.BadHandshake, (AceWireWriters.CharacterError(0x04), UiQueue));
            return;
        }

        if (login.ClientVersion != "1802")
        {
            Terminate(FakeAceTermination.ClientVersionIncorrect, (AceWireWriters.AccountBoot(" because the client is not the version this server expects (fake)"), UiQueue));
            return;
        }

        _packetQueue.Enqueue(ConnectRequestPacket());
        _seedsDiscarded = true;

        if (login.NetAuth < NetAuthType.AccountPassword)
        {
            Terminate(FakeAceTermination.NotAuthorizedNoPasswordOrTicket, (AceWireWriters.CharacterError(0x09), UiQueue));
            return;
        }

        if (account is null)
        {
            Terminate(FakeAceTermination.NotAuthorizedAccountNotFound, (AceWireWriters.CharacterError(0x0A), UiQueue));
            return;
        }

        if (!Script.AccountLoginBootsInUse && _server.FindByAccount(account.Name) is not null)
        {
            Terminate(FakeAceTermination.AccountInUse, (AceWireWriters.CharacterError(0x01), UiQueue));
            return;
        }

        if (login.NetAuth == NetAuthType.AccountPassword)
        {
            if (!account.PasswordMatches(login.Password))
            {
                Terminate(FakeAceTermination.NotAuthorizedPasswordMismatch, (AceWireWriters.AccountBoot(" because the password was not correct (fake)"), UiQueue));
                return;
            }

            if (Script.AccountLoginBootsInUse && _server.FindByAccount(account.Name) is { } previous)
            {
                previous.Terminate(FakeAceTermination.AccountLoggedIn, (AceWireWriters.CharacterError(0x01), UiQueue));
                Terminate(FakeAceTermination.AccountInUse, (AceWireWriters.CharacterError(0x01), UiQueue));
                return;
            }
        }
        else if (login.NetAuth == NetAuthType.GlsTicket)
        {
            Terminate(FakeAceTermination.NotAuthorizedGlsTicket, (AceWireWriters.CharacterError(0x09), UiQueue));
            return;
        }

        if (account.BanExpires is { } expires)
        {
            var now = _server.Time.GetUtcNow();
            if (now < expires)
            {
                // ACE interpolates the reason, so a ban without one still carries an empty string.
                var reason = account.BanReason is null ? string.Empty : " - " + account.BanReason;
                Terminate(FakeAceTermination.AccountBanned, (AceWireWriters.AccountBanned((uint)(expires - now).TotalSeconds, reason), UiQueue));
                return;
            }

            account.BanExpires = null;
            account.BanReason = null;
        }

        // The database write (account.UpdateLastLogin) runs before the state flips: the handshake race.
        _server.After(Script.DbWriteDelay, () =>
        {
            _account = account;
            AccountName = account.Name;
            State = FakeAceSessionState.AuthConnectResponse;
        });
    }

    /// <summary>ACE's <c>SendLoginRequestReject</c>: a ConnectRequest, the error, and an immediate flush.</summary>
    public void SendLoginRequestReject(uint error)
    {
        _packetQueue.Enqueue(ConnectRequestPacket());
        _seedsDiscarded = true;
        EnqueueSend((AceWireWriters.CharacterError(error), UiQueue));
        Update();
    }

    private ServerPacket ConnectRequestPacket()
    {
        var packet = new ServerPacket { Flags = ClientPacketParser.ConnectRequest };
        packet.Optional.AddRange(BitConverter.GetBytes(_server.PortalYearTicks));
        packet.Optional.AddRange(BitConverter.GetBytes(ConnectionCookie));
        packet.Optional.AddRange(BitConverter.GetBytes((uint)ClientId));
        packet.Optional.AddRange(BitConverter.GetBytes(ServerSeed));
        packet.Optional.AddRange(BitConverter.GetBytes(ClientSeed));
        packet.Optional.AddRange(BitConverter.GetBytes(0u));
        return packet;
    }

    // ---- Messages (InboundMessageManager and the handlers) ----

    private void HandleClientMessage(byte[] message)
    {
        var opcode = BinaryPrimitives.ReadUInt32LittleEndian(message);
        (FakeAceSessionState State, Action<byte[]> Handle)? handler = opcode switch
        {
            0xF7E6 => (FakeAceSessionState.AuthConnected, HandleDddInterrogationResponse),
            0xF7EA => (FakeAceSessionState.AuthConnected, static _ => { }),  // answered only after a BeginDDD, which the fake never sends
            0xF656 => (FakeAceSessionState.AuthConnected, HandleCharacterCreate),
            0xF7C8 => (FakeAceSessionState.AuthConnected, HandleEnterWorldRequest),
            0xF657 => (FakeAceSessionState.AuthConnected, HandleEnterWorld),
            0xF655 => (FakeAceSessionState.AuthConnected, HandleCharacterDelete),
            0xF7D9 => (FakeAceSessionState.AuthConnected, HandleCharacterRestore),
            0xF653 => (FakeAceSessionState.WorldConnected, HandleLogOff),
            0xF7B1 => (FakeAceSessionState.WorldConnected, HandleGameAction),
            _ => null,
        };

        if (handler is not { } h)
        {
            Counters.UnhandledMessages++;
            return;
        }

        if (h.State != State || (h.State == FakeAceSessionState.WorldConnected && PlayerGuid is null))
        {
            Counters.MessagesIgnoredForState++;
            return;
        }

        try
        {
            h.Handle(message);
            HandledMessages.Add(message);
            Counters.MessagesHandled++;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        {
            Counters.HandlerExceptions++;
        }
    }

    /// <summary><c>DDDHandler.DDD_InterrogationResponse</c> with patching off.</summary>
    private void HandleDddInterrogationResponse(byte[] message)
    {
        var response = AceClientReaders.ReadDddInterrogationResponse(message);
        bool missing = false, extra = false;
        void Compare(int client, int server, bool zeroTolerated)
        {
            if (client < server && !(zeroTolerated && client == 0))
            {
                missing = true;
            }
            else if (client > server)
            {
                extra = true;
            }
        }

        foreach (var list in response.Lists)
        {
            switch (list.DatId)
            {
                case 1 when list.DatType == 0:
                    Compare(list.Iterations, Script.PortalIteration, zeroTolerated: false);
                    break;
                case 1 when list.DatType == AceWireWriters.HighResDatType:
                    if (Script.HighResIteration is { } highRes)
                    {
                        Compare(list.Iterations, highRes, zeroTolerated: true);
                    }

                    break;
                case 2:
                    Compare(list.Iterations, Script.CellIteration, zeroTolerated: true);
                    break;
                case 3:
                    Compare(list.Iterations, Script.LanguageIteration, zeroTolerated: false);
                    break;
            }
        }

        if (extra)
        {
            Terminate(FakeAceTermination.DatsNewerThanServer, (AceWireWriters.AccountBoot(" because " + Script.DatNewerMessage), UiQueue));
        }
        else if (missing)
        {
            Terminate(FakeAceTermination.DatsPatchingDisabled, (AceWireWriters.AccountBoot(" because " + Script.DatOlderMessage), UiQueue));
        }
        else
        {
            EnqueueSend((AceWireWriters.DddEnd(), DatabaseQueue));
        }
    }

    /// <summary><c>CharacterHandler.CharacterCreate</c> and <c>CharacterCreateEx</c>.</summary>
    private void HandleCharacterCreate(byte[] message)
    {
        using (var reader = AceClientReaders.Open(message, 0xF656))
        {
            if (AceClientReaders.ReadString16L(reader) != AccountName)
            {
                return;  // ACE ignores a create for another account string, silently
            }
        }

        if (!Script.WorldOpen)
        {
            SendCharacterError(0x15);
            return;
        }

        var info = AceClientReaders.ReadCharacterCreate(message);
        if (Script.BannedNames.Contains(info.Name))
        {
            EnqueueSend((AceWireWriters.CharacterCreateResponse(4), UiQueue));
            return;
        }

        var taken = Script.Accounts.Values.SelectMany(a => a.Characters)
            .Any(c => !c.IsDeleted && string.Equals(c.Name, info.Name, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            // ACE's first availability check answers NameInUse but does not stop the create.
            EnqueueSend((AceWireWriters.CharacterCreateResponse(3), UiQueue));
            if (!Script.DuplicateNameInUse)
            {
                return;
            }
        }

        if (info.Skills.Length != 55)
        {
            Terminate(FakeAceTermination.ClientVersionIncorrect, (AceWireWriters.AccountBoot(" because the client is not the version this server expects (fake)"), UiQueue));
            return;
        }

        if (taken)
        {
            EnqueueSend((AceWireWriters.CharacterCreateResponse(3), UiQueue));
            return;
        }

        var character = new FakeCharacter(_server.NewPlayerGuid(), info.Name)
        {
            Skills = info.Skills,
            Position = Script.StartPosition,
            LastLogin = _server.Time.GetUtcNow(),
        };
        _account!.Characters.Add(character);
        Characters.Add(character);
        EnqueueSend((AceWireWriters.CharacterCreateResponse(1, character.Guid, info.Name), UiQueue));
    }

    /// <summary><c>CharacterHandler.CharacterEnterWorldRequest</c>.</summary>
    private void HandleEnterWorldRequest(byte[] message)
    {
        if (Script.WorldOpen)
        {
            EnqueueSend((AceWireWriters.EnterWorldServerReady(), UiQueue));
        }
        else
        {
            SendCharacterError(0x15);
        }
    }

    /// <summary><c>CharacterHandler.CharacterEnterWorld</c>, then a scripted <c>PlayerEnterWorld</c>.</summary>
    private void HandleEnterWorld(byte[] message)
    {
        var (guid, account, _) = AceClientReaders.ReadCharacterEnterWorld(message);
        if (Script.EnterWorldErrors.TryDequeue(out var scripted))
        {
            SendCharacterError(scripted);
            return;
        }

        if (account != AccountName)
        {
            SendCharacterError(0x0F);
            return;
        }

        var character = Characters.SingleOrDefault(c => c.Guid == guid);
        if (character is null || character.IsDeleted || character.DeleteTime is not null)
        {
            SendCharacterError(0x0F);
            return;
        }

        if (_server.FindByPlayer(guid) is not null)
        {
            SendCharacterError(0x0D);
            return;
        }

        GameEventSequence = 1;
        State = FakeAceSessionState.WorldConnected;
        PlayerGuid = guid;
        Teleporting = true;
        character.LastLogin = _server.Time.GetUtcNow();
        PlayerPosition = character.Position;
        _physicsState = EnteringPhysicsState;

        // Player.SendSelf: PlayerDescription on the UI queue (a plausible fresh character, several fragments long), then
        // PlayerCreate and the player's CreateObject on the SmartBox queue. ACE also sends CharacterTitle and the friends
        // list with the description; the fake leaves them out.
        EnqueueSend((AceWireWriters.PlayerDescription(guid, GameEventSequence++, Script.PlayerDescription?.Invoke(character.Name)
            ?? AcePlayerDescription.NewCharacter(character.Name)), UiQueue));
        EnqueueSend((AceWireWriters.PlayerCreate(guid), SmartboxQueue));
        EnqueueSend((AceWireWriters.ObjectCreate(new AceCreateObject
        {
            Guid = guid,
            PhysicsState = _physicsState,
            Position = PlayerPosition,
            Setup = 0x02000001,
            MotionTable = 0x09000001,
            SoundTable = 0x20000001,
            PhysicsEffectTable = 0x34000004,
            Sequences = Sequences(),
            Name = character.Name,
            WeenieClassId = 1,
            Icon = 0x06001036,
            ItemType = 0x10,
            ObjectDescriptionFlags = 0x8,
        }), SmartboxQueue));

        for (var i = 0; i < Script.WorldObjectsOnEnter; i++)
        {
            var p = PlayerPosition;
            EnqueueSend((AceWireWriters.ObjectCreate(new AceCreateObject
            {
                Guid = 0x80000001u + (uint)i,
                PhysicsState = 0x1,
                Position = p with { X = p.X + 2 + i, Y = p.Y + 1 },
                Setup = 0x02000003,
                Sequences = new ushort[9],
                Name = "Fake Object " + i,
                WeenieClassId = 100u + (uint)i,
                Icon = 0x06001000,
                ItemType = 0x80,
            }), SmartboxQueue));
        }
    }

    /// <summary><c>CharacterHandler.CharacterLogOff</c>: the final messages follow after the logoff delay.</summary>
    private void HandleLogOff(byte[] message) => _logOffRequested ??= Now;

    /// <summary><c>CharacterHandler.CharacterDelete</c>.</summary>
    private void HandleCharacterDelete(byte[] message)
    {
        var (account, slot, _) = AceClientReaders.ReadCharacterDelete(message);
        if (!Script.WorldOpen)
        {
            SendCharacterError(0x15);
            return;
        }

        if (account != AccountName)
        {
            SendCharacterError(0x06);
            return;
        }

        var character = Characters[checked((int)slot)];  // out of range throws, as ACE's list index does
        if (character.IsDeleted || character.DeleteTime is not null)
        {
            SendCharacterError(0x06);
            return;
        }

        EnqueueSend((AceWireWriters.CharacterDeleteAck(), UiQueue));
        character.DeleteTime = _server.Time.GetUtcNow() + Script.CharacterDeleteTime;
        EnqueueSend((CharacterListBody(), UiQueue));
    }

    /// <summary><c>CharacterHandler.CharacterRestore</c>.</summary>
    private void HandleCharacterRestore(byte[] message)
    {
        var (guid, _) = AceClientReaders.ReadCharacterRestore(message);
        if (!Script.WorldOpen)
        {
            SendCharacterError(0x15);
            return;
        }

        var character = Characters.SingleOrDefault(c => c.Guid == guid);
        if (character is null)
        {
            return;
        }

        if (character.DeleteTime is not { } deleteTime || _server.Time.GetUtcNow() > deleteTime || character.IsDeleted)
        {
            SendCharacterError(0x0F);
            return;
        }

        var taken = Script.Accounts.Values.SelectMany(a => a.Characters)
            .Any(c => c != character && !c.IsDeleted && c.DeleteTime is null && string.Equals(c.Name, character.Name, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            EnqueueSend((AceWireWriters.CharacterCreateResponse(3), UiQueue));
            return;
        }

        character.DeleteTime = null;
        EnqueueSend((AceWireWriters.CharacterRestoreResponse(guid, character.Name), UiQueue));
    }

    /// <summary><c>GameActionPacket.HandleGameAction</c> and the actions the fake scripts.</summary>
    private void HandleGameAction(byte[] message)
    {
        var (_, actionType, payload) = AceClientReaders.ReadGameAction(message);
        using (payload)
        {
            switch (actionType)
            {
                case 0x00A1:  // LoginComplete: the player materialises (Player_Location.OnTeleportComplete)
                    Teleporting = false;
                    _physicsState &= ~(0x4000u | 0x10u);
                    EnqueueSend((AceWireWriters.SetState(PlayerGuid!.Value, _physicsState, _instanceSequence, ++_stateSequence), SmartboxQueue));
                    break;
                case 0xF753:  // AutonomousPosition
                    AcceptPosition(AceClientReaders.ReadPosition(payload), payload);
                    break;
                case 0xF61C:  // MoveToState: skip the RawMotionState, then the same tail as AutonomousPosition
                    SkipRawMotionState(payload);
                    AcceptPosition(AceClientReaders.ReadPosition(payload), payload);
                    break;
                case 0x0015:  // Talk
                    var text = AceClientReaders.ReadString16L(payload);
                    if (text.StartsWith("@lifestone", StringComparison.Ordinal) && Script.ToolReplies is { } replies)
                    {
                        foreach (var line in replies(text))
                        {
                            EnqueueSend((AceWireWriters.ServerMessage(line, Script.ToolReplyChatType), UiQueue));
                        }
                    }
                    else if (!text.StartsWith('@'))
                    {
                        // Player.HandleActionTalk: the speaker hears its own speech (HearSpeech, chat type Speech = 2).
                        EnqueueSend((AceWireWriters.HearSpeech(text, PlayerName, PlayerGuid!.Value, 2), UiQueue));
                    }

                    break;
                case 0xF61B:  // Jump: JumpPack is 32 bytes to ACE; nothing is sent back here
                    payload.ReadBytes(32);
                    JumpsAccepted++;
                    break;
                case 0x0036:  // Use (GameActionUseItem): the fake answers at once with UseDone
                    UsedObjects.Add(payload.ReadUInt32());
                    EnqueueSend((AceWireWriters.UseDone(PlayerGuid!.Value, GameEventSequence++), UiQueue));
                    break;
                case 0x01BF:  // QueryHealth (GameActionQueryHealth): full health
                    EnqueueSend((AceWireWriters.UpdateHealth(PlayerGuid!.Value, GameEventSequence++, payload.ReadUInt32(), 1f), UiQueue));
                    break;
                case 0x005D:  // Tell (GameActionTell): message, then target; only the player itself is online here
                    var tellText = AceClientReaders.ReadString16L(payload);
                    var target = AceClientReaders.ReadString16L(payload).Trim();
                    EnqueueSend(string.Equals(target, PlayerName, StringComparison.OrdinalIgnoreCase)
                        ? (AceWireWriters.TellEvent(PlayerGuid!.Value, GameEventSequence++, tellText, PlayerName, PlayerGuid.Value, PlayerGuid.Value, 3), UiQueue)
                        : (AceWireWriters.WeenieError(PlayerGuid!.Value, GameEventSequence++, 0x052B), UiQueue));  // CharacterNotAvailable
                    break;
                case 0x0032:  // TalkDirect (GameActionTalkDirect.cs 18-50): message, then the target's id; the only creature on the landblock here is the player itself
                    var directText = AceClientReaders.ReadString16L(payload);
                    var directTarget = payload.ReadUInt32();
                    if (directTarget != PlayerGuid!.Value)
                    {
                        EnqueueSend((AceWireWriters.WeenieError(PlayerGuid.Value, GameEventSequence++, 0x052B), UiQueue));  // CharacterNotAvailable
                        break;
                    }

                    // The confirmation line first (ChatMessageType.OutgoingTell = 4), then, the target being a player, its incoming tell.
                    EnqueueSend((AceWireWriters.ServerMessage($"You tell {PlayerName}, \"{directText}\"", 4), UiQueue));
                    EnqueueSend((AceWireWriters.TellEvent(PlayerGuid.Value, GameEventSequence++, directText, PlayerName, PlayerGuid.Value, PlayerGuid.Value, 3), UiQueue));
                    break;
                default:
                    Counters.UnhandledMessages++;
                    break;
            }
        }
    }

    private void AcceptPosition(AceClientReaders.AceReadPosition read, BinaryReader payload)
    {
        // Four u16 sequences, then the contact byte (AutonomousPosition) or the contact/long-jump byte (MoveToState).
        payload.ReadBytes(8);
        var contact = (payload.ReadByte() & 1) != 0;
        if (Teleporting)
        {
            return;  // ACE skips SetRequestedLocation until LoginComplete
        }

        PlayerPosition = new AcePosition(read.Cell, read.X, read.Y, read.Z, read.Qw, read.Qx, read.Qy, read.Qz);
        AcceptedPositions.Add(PlayerPosition);
        EnqueueSend((AceWireWriters.UpdatePosition(PlayerGuid!.Value, PlayerPosition, contact,
            new AceUpdatePositionSequences(_instanceSequence, ++_positionSequence, 0, 0)), SmartboxQueue));
    }

    /// <summary>ACE's <c>RawMotionState</c> reader: packed flags (11 bits, then the command count), the flagged words, then 8-byte commands.</summary>
    private static void SkipRawMotionState(BinaryReader payload)
    {
        var packed = payload.ReadUInt32();
        var flags = packed & 0x7FF;
        for (var bit = 0; bit < 11; bit++)
        {
            if ((flags & (1u << bit)) != 0)
            {
                payload.ReadUInt32();
            }
        }

        payload.ReadBytes((int)(packed >> 11) * 8);
    }

    // ---- Server to client ----

    /// <summary>
    /// Forgets every packet kept for retransmission, as if they had all aged out of ACE's 120 s cache: a NAK for any of
    /// them is answered with RejectRetransmit (for the session's gap-skip tests).
    /// </summary>
    public void ForgetCachedPackets() => _cachedPackets.Clear();

    /// <summary>Queues messages on their groups' bundles (<c>NetworkSession.EnqueueSend</c>).</summary>
    public void EnqueueSend(params (byte[] Body, ushort Group)[] messages)
    {
        if (_released)
        {
            return;
        }

        foreach (var message in messages)
        {
            var bundle = _bundles[message.Group];
            bundle.Encrypted = true;
            bundle.Messages.Enqueue(message);
        }
    }

    /// <summary>Starts terminating (<c>Session.Terminate</c>): the message is queued, then two seconds of flushing.</summary>
    public void Terminate(FakeAceTermination reason, (byte[] Body, ushort Group)? message = null)
    {
        if (message is { } m)
        {
            EnqueueSend(m);
        }

        TerminationReason = reason;
        _terminationEnd = Now + Ticks(Script.TerminationFlush);
        Counters.CountTermination(reason);
    }

    /// <summary><c>Session.TickOutbound</c>.</summary>
    public void TickOutbound()
    {
        if (_terminationEnd is { } end)
        {
            if (!TerminationComplete)
            {
                State = FakeAceSessionState.TerminationStarted;
                Update();
                if (Now > end)
                {
                    TerminationComplete = true;
                    _released = true;
                }
            }

            return;
        }

        if (Now >= _timeoutAt)
        {
            Terminate(FakeAceTermination.NetworkTimeout);
            return;
        }

        Update();

        if (_logOffRequested is { } requested && Now >= requested + Ticks(Script.LogOffDelay))
        {
            SendFinalLogOffMessages();
        }

        if (State == FakeAceSessionState.AuthConnected)
        {
            if (_nextPingResponse is not { } next)
            {
                _nextPingResponse = Now + Ticks(PingResponseInterval);
            }
            else if (Now > next)
            {
                EnqueueSend((AceWireWriters.PingResponse(PlayerGuid ?? 0, GameEventSequence++), UiQueue));
                _nextPingResponse = Now + Ticks(PingResponseInterval);
            }
        }
        else
        {
            _nextPingResponse = null;
        }
    }

    private void SendFinalLogOffMessages()
    {
        _logOffRequested = null;
        PlayerGuid = null;
        Teleporting = false;
        EnqueueSend((AceWireWriters.CharacterLogOff(), UiQueue));
        RefreshCharacters();
        EnqueueSend((CharacterListBody(), UiQueue), (ServerNameBody(), UiQueue));
        State = FakeAceSessionState.AuthConnected;
    }

    private void SendCharacterError(uint code) => EnqueueSend((AceWireWriters.CharacterError(code), UiQueue));

    /// <summary><c>Session.UpdateCharacters</c> and <c>CheckCharactersForDeletion</c>, most recent login first.</summary>
    private void RefreshCharacters()
    {
        var now = _server.Time.GetUtcNow();
        foreach (var character in _account!.Characters.Where(c => c.DeleteTime is { } t && now > t))
        {
            character.IsDeleted = true;
        }

        Characters.Clear();
        Characters.AddRange(_account.Characters.Where(c => !c.IsDeleted).OrderByDescending(c => c.LastLogin));
    }

    private byte[] CharacterListBody() =>
        AceWireWriters.CharacterList(
            Characters.Select(c => new AceCharacter(c.Guid, c.Name, c.DeleteTime is null ? 0u : 1u)).ToList(),
            AccountName!,
            Script.SlotCount,
            Script.UseTurbineChat);

    private byte[] ServerNameBody() =>
        AceWireWriters.ServerName(Script.WorldName, _server.Sessions.Count(s => s.PlayerGuid is not null), Script.MaximumSessions);

    private ushort[] Sequences() => [_positionSequence, 0, _stateSequence, 0, 0, 0, 0, 0, _instanceSequence];

    /// <summary><c>NetworkSession.Update</c>.</summary>
    private void Update()
    {
        if (_released)
        {
            return;
        }

        var now = Now;
        if (now - _lastPrune > Ticks(CachedPacketPruneInterval))
        {
            _lastPrune = now;
            foreach (var stale in _cachedPackets.Where(p => now - p.Value.SentAt > Ticks(CachedPacketRetention)).Select(p => p.Key).ToList())
            {
                _cachedPackets.Remove(stale);
            }
        }

        for (var group = 0; group < GroupCount; group++)
        {
            var bundle = _bundles[group];
            if (group == InvalidQueue)
            {
                if (_sendResync && !bundle.TimeSync && now > _nextResync)
                {
                    bundle.TimeSync = true;
                    bundle.Encrypted = true;
                    _nextResync = now + Ticks(TimeBetweenTimeSync);
                }

                if (!bundle.SendAck && now > _nextAck)
                {
                    bundle.SendAck = true;
                    _nextAck = now + Ticks(TimeBetweenAck);
                }
            }

            if (bundle.NeedsSending && now >= _nextSend)
            {
                _bundles[group] = new Bundle();
                SendBundle(bundle, (ushort)group);
                _nextSend = now + Ticks(MinimumTimeBetweenBundles);
            }
        }

        FlushPackets();
    }

    /// <summary><c>NetworkSession.SendBundle</c>: messages into fragments, fragments into packets.</summary>
    private void SendBundle(Bundle bundle, ushort group)
    {
        var writeOptionalHeaders = true;
        var fragments = new List<OutboundMessage>();
        while (bundle.Messages.TryDequeue(out var message))
        {
            fragments.Add(new OutboundMessage(message.Body, message.Group, _fragmentSequence++));
        }

        while (fragments.Count > 0 || writeOptionalHeaders)
        {
            var packet = new ServerPacket();
            if (fragments.Count > 0)
            {
                packet.Flags |= ClientPacketParser.BlobFragments;
            }

            if (bundle.Encrypted)
            {
                packet.Flags |= ClientPacketParser.EncryptedChecksum;
            }

            var available = MaxPacketPayload;
            var first = fragments.FirstOrDefault();
            if (first is not null)
            {
                if (first.DataRemaining >= available)
                {
                    var fragment = first.GetNextFragment();
                    packet.Fragments.Add(fragment);
                    available -= fragment.Length;
                    if (first.DataRemaining <= 0)
                    {
                        fragments.Remove(first);
                    }
                }
                else
                {
                    if (writeOptionalHeaders)
                    {
                        writeOptionalHeaders = false;
                        WriteOptionalHeaders(bundle, packet);
                        available -= packet.Optional.Count;
                    }

                    var done = new List<OutboundMessage>();
                    foreach (var message in fragments)
                    {
                        var skipped = false;
                        if (!message.TailSent && available >= message.TailSize)
                        {
                            var fragment = message.GetTailFragment();
                            packet.Fragments.Add(fragment);
                            available -= fragment.Length;
                        }
                        else if (available >= message.NextSize)
                        {
                            var fragment = message.GetNextFragment();
                            packet.Fragments.Add(fragment);
                            available -= fragment.Length;
                        }
                        else
                        {
                            skipped = true;
                        }

                        if (message.DataRemaining <= 0)
                        {
                            done.Add(message);
                        }

                        if (skipped && group == UiQueue)
                        {
                            break;  // UI-queue messages go out strictly in order
                        }
                    }

                    fragments.RemoveAll(done.Contains);
                }
            }
            else if (writeOptionalHeaders)
            {
                writeOptionalHeaders = false;
                WriteOptionalHeaders(bundle, packet);
            }

            _packetQueue.Enqueue(packet);
        }
    }

    /// <summary><c>NetworkSession.WriteOptionalHeaders</c>: ack, time sync, echo response.</summary>
    private void WriteOptionalHeaders(Bundle bundle, ServerPacket packet)
    {
        if (bundle.SendAck)
        {
            packet.Flags |= ClientPacketParser.AckSequence;
            packet.Optional.AddRange(BitConverter.GetBytes(_lastReceivedPacketSequence));
        }

        if (bundle.TimeSync)
        {
            packet.Flags |= ClientPacketParser.TimeSync;
            packet.Optional.AddRange(BitConverter.GetBytes(_server.PortalYearTicks));
        }

        if (bundle.ClientTime != -1f)
        {
            packet.Flags |= ClientPacketParser.EchoResponse;
            packet.Optional.AddRange(BitConverter.GetBytes(bundle.ClientTime));
            packet.Optional.AddRange(BitConverter.GetBytes((float)_server.PortalYearTicks - bundle.ClientTime));
        }
    }

    /// <summary><c>NetworkSession.FlushPackets</c>: sequences, caching, keys, send.</summary>
    private void FlushPackets()
    {
        while (_packetQueue.TryDequeue(out var packet))
        {
            if ((packet.Flags & ClientPacketParser.EncryptedChecksum) != 0 && _packetSequence == 0)
            {
                _packetSequence = 1;
            }

            var isNak = (packet.Flags & ClientPacketParser.RequestRetransmit) != 0;
            if (packet.Flags == ClientPacketParser.AckSequence || isNak)
            {
                packet.Sequence = _packetSequence;
            }
            else
            {
                _packetSequence = _packetSequence == uint.MaxValue ? 0 : _packetSequence + 1;
                packet.Sequence = _packetSequence;
            }

            packet.Time = (ushort)_server.PortalYearTicks;
            packet.SentAt = Now;
            if (packet.Sequence >= 2 && !isNak)
            {
                _cachedPackets.TryAdd(packet.Sequence, packet);
            }

            if ((packet.Flags & ClientPacketParser.RejectRetransmit) != 0)
            {
                Counters.S2CRejects++;
            }

            if ((packet.Flags & ClientPacketParser.EncryptedChecksum) != 0)
            {
                packet.IsaacXor = _isaacServer.Next();
            }

            SendPacketRaw(packet);
        }
    }

    /// <summary><c>NetworkSession.Retransmit</c>: the cached packet again, with Retransmission set and its key kept.</summary>
    private bool Retransmit(uint sequence)
    {
        if (!_cachedPackets.TryGetValue(sequence, out var cached))
        {
            return false;
        }

        cached.Flags |= ClientPacketParser.Retransmission;
        SendPacketRaw(cached);
        Counters.S2CRetransmits++;
        return true;
    }

    /// <summary><c>NetworkSession.SendPacketRaw</c>: the login port until the ConnectResponse named the world endpoint.</summary>
    private void SendPacketRaw(ServerPacket packet)
    {
        var datagram = packet.Build(FakeAceServer.ServerId);
        _server.SendFrom(EndPointS2C is not null, EndPointS2C ?? EndPointC2S, datagram);
    }

    private long Ticks(TimeSpan span) => (long)(span.TotalSeconds * _server.Time.TimestampFrequency);

    /// <summary>A group's pending bundle (ACE <c>NetworkBundle</c>).</summary>
    private sealed class Bundle
    {
        private bool _changed;
        private bool _timeSync;
        private bool _sendAck;
        private float _clientTime = -1f;

        public Queue<(byte[] Body, ushort Group)> Messages { get; } = new();

        public bool Encrypted { get; set; }

        public bool NeedsSending => _changed || Messages.Count > 0;

        public bool TimeSync
        {
            get => _timeSync;
            set
            {
                _timeSync = value;
                _changed = true;
            }
        }

        public bool SendAck
        {
            get => _sendAck;
            set
            {
                _sendAck = value;
                _changed = true;
            }
        }

        public float ClientTime
        {
            get => _clientTime;
            set
            {
                _clientTime = value;
                _changed = true;
            }
        }
    }

    /// <summary>A message being split into fragments (ACE <c>MessageFragment</c>).</summary>
    private sealed class OutboundMessage(byte[] body, ushort group, uint sequence)
    {
        private ushort _index;

        public int DataRemaining { get; private set; } = body.Length;

        public ushort Count { get; } = (ushort)Math.Ceiling(body.Length / (double)MaxFragmentData);

        public bool TailSent { get; private set; } = body.Length <= MaxFragmentData;

        public int NextSize => 16 + Math.Min(DataRemaining, MaxFragmentData);

        /// <summary>ACE's tail size: 16 plus the length modulo 448 (16 alone when the length is a multiple of 448, as in ACE).</summary>
        public int TailSize => 16 + (body.Length % MaxFragmentData);

        public ServerFragment GetTailFragment()
        {
            TailSent = true;
            return Create((ushort)(Count - 1));
        }

        public ServerFragment GetNextFragment() => Create(_index++);

        private ServerFragment Create(ushort index)
        {
            var position = index * MaxFragmentData;
            var length = Math.Min(body.Length - position, MaxFragmentData);
            DataRemaining -= length;
            return new ServerFragment(sequence, Count, index, group, body.AsSpan(position, length).ToArray());
        }
    }

    /// <summary>A client message being reassembled (ACE <c>MessageBuffer</c>).</summary>
    private sealed class MessageBuffer(ushort total)
    {
        private readonly List<ClientFragment> _fragments = [];

        public bool Complete => _fragments.Count == total;

        public void Add(ClientFragment fragment)
        {
            if (!Complete && _fragments.All(f => f.Index != fragment.Index))
            {
                _fragments.Add(fragment);
            }
        }

        public byte[]? TryGetMessage()
        {
            var message = _fragments.OrderBy(f => f.Index).SelectMany(f => f.Data).ToArray();
            return message.Length < 4 ? null : message;
        }
    }
}

/// <summary>A server fragment.</summary>
internal sealed record ServerFragment(uint Sequence, ushort Count, ushort Index, ushort Queue, byte[] Data)
{
    /// <summary>Its size on the wire, header included.</summary>
    public int Length => 16 + Data.Length;
}

/// <summary>A server packet before (and after) it is sequenced and sealed (ACE <c>ServerPacket</c>).</summary>
internal sealed class ServerPacket
{
    private uint? _isaacXor;

    public uint Flags { get; set; }

    public uint Sequence { get; set; }

    public ushort Time { get; set; }

    public long SentAt { get; set; }

    public List<byte> Optional { get; } = [];

    public List<ServerFragment> Fragments { get; } = [];

    /// <summary>The key, set once when an encrypted packet is first sent; a retransmission keeps it.</summary>
    public uint IsaacXor
    {
        get => _isaacXor ?? 0;
        set
        {
            if (_isaacXor is not null)
            {
                throw new InvalidOperationException("a packet's key is set once");
            }

            _isaacXor = value;
        }
    }

    /// <summary>The datagram: header, optional section, fragments; checksum per docs/protocol.md §2.4.</summary>
    public byte[] Build(ushort id)
    {
        var body = new List<byte>(Optional);
        var payloadHash = Hash32.Calculate(Optional.ToArray());
        foreach (var fragment in Fragments)
        {
            var header = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(header, fragment.Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x80000000);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), fragment.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)fragment.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), fragment.Index);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), fragment.Queue);
            payloadHash = unchecked(payloadHash + Hash32.Calculate(header) + Hash32.Calculate(fragment.Data));
            body.AddRange(header);
            body.AddRange(fragment.Data);
        }

        var datagram = new byte[ClientPacketParser.HeaderSize + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram, Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(4), Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8), ClientPacketParser.ChecksumPlaceholder);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(14), Time);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(16), (ushort)body.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(18), 1);
        var headerHash = Hash32.Calculate(datagram.AsSpan(0, ClientPacketParser.HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8), unchecked(headerHash + (payloadHash ^ IsaacXor)));
        body.CopyTo(datagram, ClientPacketParser.HeaderSize);
        return datagram;
    }
}
