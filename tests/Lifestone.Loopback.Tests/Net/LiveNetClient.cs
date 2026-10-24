using System.Diagnostics;
using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>What one live net session reported: the sink of the net tests. Every callback runs inside <see cref="IAcSession.Pump"/> on the test's thread.</summary>
internal sealed class LiveSink : IAcSessionSink
{
    /// <summary>The session's own events, in order.</summary>
    public List<SessionEvent> Events { get; } = [];

    /// <summary>The latest character list the server sent (F658), or null.</summary>
    public CharacterListMsg? List { get; private set; }

    /// <summary>The answers to character creation (F643), in order.</summary>
    public List<CharGenResponseMsg> CharGen { get; } = [];

    /// <summary>The character errors (F659) the server sent, in order.</summary>
    public List<uint> CharacterErrors { get; } = [];

    /// <summary>The player's guid from F746, or null.</summary>
    public uint? PlayerGuid { get; private set; }

    /// <summary>The player's own CreateObject, or null.</summary>
    public CreateObjectMsg? SelfCreate { get; private set; }

    /// <summary>The SetState messages (F74B) for the player, in order.</summary>
    public List<SetStateMsg> SelfStates { get; } = [];

    /// <summary>The server messages (F7E0) with their chat type, in order.</summary>
    public List<ServerMessageMsg> ServerMessages { get; } = [];

    /// <summary>The F751 PlayerTeleport messages.</summary>
    public int Teleports { get; private set; }

    /// <summary>The teleport sequence of each F751, in order.</summary>
    public List<ushort> TeleportSeqs { get; } = [];

    /// <summary>The F748 UpdatePosition messages for the player, in order.</summary>
    public List<UpdatePositionMsg> SelfPositions { get; } = [];

    /// <summary>The CreateObject messages other than the player's own.</summary>
    public int OtherObjects { get; private set; }

    /// <summary>The messages the dispatcher could not decode.</summary>
    public List<string> Malformed { get; } = [];

    /// <summary>The server's boot reason, or null.</summary>
    public string? Boot { get; private set; }

    /// <inheritdoc/>
    public void OnSessionEvent(in SessionEvent sessionEvent) => Events.Add(sessionEvent);

    /// <inheritdoc/>
    public void OnCharacterList(in InboundContext context, CharacterListMsg message) => List = message;

    /// <inheritdoc/>
    public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message) => CharGen.Add(message);

    /// <inheritdoc/>
    public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message) => CharacterErrors.Add(message.Code);

    /// <inheritdoc/>
    public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message)
    {
        PlayerGuid = message.Guid;
        SelfCreate = null;
        SelfStates.Clear();
    }

    /// <inheritdoc/>
    public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
    {
        if (PlayerGuid == message.Guid)
        {
            SelfCreate = message;
        }
        else
        {
            OtherObjects++;
        }
    }

    /// <inheritdoc/>
    public void OnSetState(in InboundContext context, in SetStateMsg message)
    {
        if (PlayerGuid == message.Guid)
        {
            SelfStates.Add(message);
        }
    }

    /// <inheritdoc/>
    public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message)
    {
        Teleports++;
        TeleportSeqs.Add(message.TeleportSeq);
    }

    /// <inheritdoc/>
    public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message)
    {
        if (PlayerGuid == message.Guid)
        {
            SelfPositions.Add(message);
        }
    }

    /// <inheritdoc/>
    public void OnServerMessage(in InboundContext context, in ServerMessageMsg message) => ServerMessages.Add(message);

    /// <inheritdoc/>
    public void OnAccountBoot(in InboundContext context, in AccountBootMsg message) => Boot = message.Reason ?? "(no reason)";

    /// <inheritdoc/>
    public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add($"0x{opcode:X8} ({length} bytes): {reason}");

    /// <summary>The player's physics state from its CreateObject, or null.</summary>
    public PhysicsState? SelfCreateState => SelfCreate?.Physics.State;

    /// <summary>True once a SetState for the player arrived whose state has Hidden clear.</summary>
    public bool SelfUnhidden => SelfStates.Any(s => (s.State & PhysicsState.Hidden) == 0);

    /// <summary>
    /// The four sequences a client echoes after its position, as far as this session has seen them: the instance, teleport and force-position
    /// sequences of the latest F748 for the player, else the latest F751 and SetState. ACE reads them and never checks them (docs/protocol.md section 6).
    /// </summary>
    public ObjSeqs Sequences
    {
        get
        {
            if (SelfPositions.Count > 0)
            {
                var last = SelfPositions[^1];
                return new ObjSeqs(last.InstanceSeq, 0, last.TeleportSeq, last.ForcePositionSeq);
            }

            return new ObjSeqs(SelfStates.Count > 0 ? SelfStates[^1].InstanceSeq : (ushort)0, 0, TeleportSeqs.Count > 0 ? TeleportSeqs[^1] : (ushort)0, 0);
        }
    }
}

/// <summary>
/// One live session of the net layer (<see cref="AcSession"/>) against the bundled ACE on 127.0.0.1, driven from the test's own thread the way a host drives it:
/// pump, look, send. It logs in as the test account (<c>accounts.test</c>; the password is read from secrets.json as bytes, held by the session and zeroed
/// by it; this class keeps only the byte forms the fixture guard searches for), taps both directions of the message stream, and records what the server sent.
/// </summary>
internal sealed class LiveNetClient : IDisposable
{
    /// <summary>The character the live net tests use on the test account, created when missing.</summary>
    public const string Character = "Lifestone Smoke";

    /// <summary>
    /// The character the live phase-B tests use (movement, teleports, the lossy soak), created on the test account when missing. It is the loopback runs'
    /// character, not <see cref="Character"/>, which the game's login check expects at the Academy start.
    /// </summary>
    public const string LoopbackCharacter = "Lifestone Loopback";

    private readonly IReadOnlyList<uint> _skillClasses;
    private readonly string _character;
    private IReadOnlyList<byte[]> _needles = [];

    private LiveNetClient(IAcSession session, LiveSink sink, NetCapture capture, IReadOnlyList<uint> skillClasses, string character)
    {
        Session = session;
        Sink = sink;
        Capture = capture;
        _skillClasses = skillClasses;
        _character = character;
    }

    /// <summary>The name of the character this client logs in with.</summary>
    public string CharacterName => _character;

    /// <summary>The network session.</summary>
    public IAcSession Session { get; }

    /// <summary>What the server reported.</summary>
    public LiveSink Sink { get; }

    /// <summary>The tap's capture.</summary>
    public NetCapture Capture { get; }

    /// <summary>The byte forms of the password that no message body may hold (see <see cref="NetFixtureStore.PasswordNeedles"/>).</summary>
    public IReadOnlyList<byte[]> PasswordNeedles => _needles;

    /// <summary>
    /// Creates a session for the backend (not yet logged in). <paramref name="retail"/> is one login attempt, so a 0x01 shows instead of being retried.
    /// <paramref name="character"/> is the character to use (default <see cref="Character"/>); <paramref name="lossy"/> puts the seeded lossy socket between the
    /// session and ACE.
    /// </summary>
    public static LiveNetClient Create(DatSet dats, bool retail, string character = Character, LossyLink? lossy = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentException.ThrowIfNullOrEmpty(character);
        var sink = new LiveSink();
        var capture = new NetCapture();
        var session = AcSession.Create(
            new AcSessionOptions
            {
                Server = BackendProbe.Probe().AceEndPoint ?? throw new InvalidOperationException("the backend probe has no endpoint"),
                Iterations = SessionBootstrap.LanguageIterations(dats),
                Retry = retail ? LoginRetryPolicy.Retail : LoginRetryPolicy.Default,
                SocketFactory = lossy?.SocketFactory,
            },
            sink,
            capture);
        return new LiveNetClient(session, sink, capture, HeadlessCharacter.RetailSkillClasses(dats), character);
    }

    /// <summary>
    /// ACE keeps a session that was quit or booted findable for 2 s (<c>SessionTerminationDetails.TerminationEndTicks</c>, ACE @95ef426d), and while it does it
    /// answers a login of the same account with CharacterError 0x01 (Logon) and terminates both sessions. The wait that lets a test log in without meeting it.
    /// </summary>
    public static readonly TimeSpan AceSessionLinger = TimeSpan.FromSeconds(2);

    private static long _lastQuit = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 60;

    /// <summary>Quits (the Disconnect) and remembers when, for <see cref="WaitOutAceLinger"/>.</summary>
    public void Quit()
    {
        Session.Quit();
        Interlocked.Exchange(ref _lastQuit, Stopwatch.GetTimestamp());
    }

    /// <summary>Sleeps until ACE has forgotten the last session any client of this process quit (a margin of half a second after <see cref="AceSessionLinger"/>).</summary>
    public static void WaitOutAceLinger()
    {
        var since = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastQuit));
        var wait = AceSessionLinger + TimeSpan.FromMilliseconds(500) - since;
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }
    }

    /// <summary>Logs in as the test account. The secrets' bytes are zeroed here; the session zeroes its own copy of the password.</summary>
    public void Login()
    {
        Lifestone.Loopback.Tests.Session.LiveCharacter.Release(Console.WriteLine, BackendProbe.TestAccountKey); // a shared session of the account is logged off first (ACE boots a second login)
        var secrets = File.ReadAllBytes(BackendProbe.MainCheckoutFiles.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default));
            _needles = NetFixtureStore.PasswordNeedles(credentials);
            Session.Login(credentials);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    /// <summary>Pumps until <paramref name="done"/> holds, the session fails or <paramref name="timeout"/> passes. Returns whether it holds.</summary>
    public bool PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(done);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Session.Pump(TimeSpan.FromMilliseconds(8));
            if (done())
            {
                return true;
            }

            if (Session.Phase == SessionPhase.Failed || watch.Elapsed > timeout)
            {
                return false;
            }

            Session.WaitForInboundAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>The character list entry named <see cref="CharacterName"/>, or null.</summary>
    public CharacterListEntry? FindCharacter() =>
        Session.CharacterList is { } list
            ? list.Characters.Cast<CharacterListEntry?>().FirstOrDefault(c => string.Equals(c!.Value.Name, _character, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// Creates the character when the account has none by that name (Aluvian, male, 55 in each attribute, the 55 skill classes retail starts with): sends F656
    /// and waits for the server's answer.
    /// </summary>
    /// <returns>True when a character was created.</returns>
    public bool CreateCharacterIfMissing()
    {
        if (FindCharacter() is not null)
        {
            return false;
        }

        var intent = HeadlessCharacter.Intent(_character, _skillClasses);
        var slot = (uint)(Session.CharacterList?.Characters.Length ?? 0);
        Session.CreateCharacter(intent.ToRequest(slot));
        var answered = Sink.CharGen.Count;
        if (!PumpUntil(() => Sink.CharGen.Count > answered, TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("the server did not answer the character creation");
        }

        var answer = Sink.CharGen[^1];
        if (answer.Result != CharGenResult.Ok)
        {
            throw new InvalidOperationException($"the server refused the character: {answer.Result}");
        }

        return true;
    }

    /// <summary>Quits (the Disconnect) and drops the session.</summary>
    public void Dispose()
    {
        try
        {
            var connected = Session.Phase is not (SessionPhase.Closed or SessionPhase.Failed or SessionPhase.Idle);
            Session.Dispose();
            if (connected)
            {
                Interlocked.Exchange(ref _lastQuit, Stopwatch.GetTimestamp());
            }
        }
        finally
        {
            foreach (var needle in _needles)
            {
                CryptographicOperations.ZeroMemory(needle);
            }

            _needles = [];
        }
    }

    /// <summary>The session's events as one line each, for a failure message (no password or message body is in them).</summary>
    public string Describe() =>
        (Session.Failure is { } failure ? $"[failure {failure.Kind} code 0x{failure.Code:X}: {failure.Message}] " : string.Empty) + string.Join(
            "; ",
            Sink.Events.Select(e => e.Kind == SessionEventKind.PhaseChanged ? $"{e.PreviousPhase}->{e.Phase}" : e.Kind + (e.Code != 0 ? $"(0x{e.Code:X2})" : string.Empty) + (e.Text is null ? string.Empty : ":" + e.Text)));
}
