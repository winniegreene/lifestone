using System.Text;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Taps;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// A session under test on the fake server (m2-plan §3.3 N5): a <see cref="SessionHarness"/> in virtual time, an
/// <see cref="AcSession"/> on its in-memory sockets, and recorders for everything the session puts out: the sink
/// (<see cref="RecordingSink"/>), the raw tap (<see cref="RecordingTap"/>) and the log (<see cref="CapturingNetLog"/>).
/// The session is pumped after every server tick with <see cref="Budget"/>, unless <see cref="Pumping"/> is off (a
/// stalled game thread).
/// </summary>
internal sealed class SessionRig : IDisposable
{
    public const string Account = "tester";
    public const string Password = "test-password-1";
    public const uint HeroGuid = 0x50000001;

    /// <summary>A rig on <paramref name="script"/> (default: <see cref="DefaultScript"/>), with <paramref name="configure"/> applied to the options.</summary>
    public SessionRig(FakeAceScript? script = null, Func<AcSessionOptions, AcSessionOptions>? configure = null, LossModel? clientToServer = null, LossModel? serverToClient = null)
    {
        Harness = new SessionHarness(script ?? DefaultScript(), clientToServer, serverToClient);
        var options = new AcSessionOptions
        {
            Server = Harness.ServerEndPoint,
            Iterations = new DddIterations(2072, 982, 994, null),
            Time = Harness.Time,
            SocketFactory = server =>
            {
                SocketsOpened++;
                return Harness.SocketFactory(server);
            },
            Log = Log,
        };
        Options = configure is null ? options : configure(options);
        Session = AcSession.Create(Options, Sink, Tap);
        Harness.AddParticipant(() =>
        {
            if (Pumping)
            {
                PumpResults.Add(Session.Pump(Budget));
            }
        });
    }

    public SessionHarness Harness { get; }

    public AcSessionOptions Options { get; }

    public IAcSession Session { get; }

    internal SessionMachine Machine => (SessionMachine)Session;

    public RecordingSink Sink { get; } = new();

    public RecordingTap Tap { get; } = new();

    public CapturingNetLog Log { get; } = new();

    /// <summary>The budget each pump gets (unbounded by default).</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.MaxValue;

    /// <summary>False stops pumping (a stalled game thread); the server keeps running.</summary>
    public bool Pumping { get; set; } = true;

    public List<PumpResult> PumpResults { get; } = [];

    /// <summary>Sockets the session opened.</summary>
    public int SocketsOpened { get; private set; }

    public FakeAceScript Script => Harness.Script;

    public FakeAceServer Server => Harness.Server;

    /// <summary>The fake's newest session.</summary>
    public FakeAceSession ServerSession => Server.History[^1];

    /// <summary>The default world: the account <see cref="Account"/> with one character, "Hero".</summary>
    public static FakeAceScript DefaultScript(bool withCharacter = true)
    {
        var script = new FakeAceScript();
        var account = script.AddAccount(Account, Password);
        if (withCharacter)
        {
            account.Characters.Add(new FakeCharacter(HeroGuid, "Hero") { Position = script.StartPosition });
        }

        return script;
    }

    public void Login(string account = Account, string password = Password)
    {
        using var credentials = new AcCredentials(account, password);
        Session.Login(credentials);
    }

    public bool RunUntil(Func<bool> condition, TimeSpan timeout) => Harness.RunUntil(condition, timeout);

    public void RunFor(TimeSpan duration) => Harness.RunFor(duration);

    /// <summary>Logs in and runs to character select.</summary>
    public void ToCharacterSelect(string account = Account, string password = Password)
    {
        Login(account, password);
        Assert.True(RunUntil(() => Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(15)),
            $"stuck in {Session.Phase} ({Session.Failure})");
    }

    /// <summary>Logs in, enters the world as <paramref name="guid"/> and runs until PlayerCreate.</summary>
    public void ToWorld(uint guid = HeroGuid)
    {
        ToCharacterSelect();
        Session.EnterWorld(guid);
        Assert.True(RunUntil(() => Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(5)), $"stuck in {Session.Phase} ({Session.Failure})");
    }

    /// <summary>The client messages the fake's newest session handled, by opcode.</summary>
    public List<uint> HandledOpcodes() => ServerSession.HandledMessages.Select(m => BitConverter.ToUInt32(m)).ToList();

    public void Dispose() => Session.Dispose();
}

/// <summary>Records everything a session reports, in order.</summary>
internal sealed class RecordingSink : IAcSessionSink
{
    /// <summary>Every session event.</summary>
    public List<SessionEvent> Events { get; } = [];

    /// <summary>Every message callback: its fragment sequence and opcode (the event type for a game event), in order.</summary>
    public List<(uint Sequence, uint Opcode)> Dispatched { get; } = [];

    public List<CharacterListMsg> CharacterLists { get; } = [];

    public List<CharGenResponseMsg> CharGenResponses { get; } = [];

    public List<uint> CharacterErrors { get; } = [];

    public List<AccountBootMsg> Boots { get; } = [];

    public List<(uint Opcode, string Reason)> Malformed { get; } = [];

    /// <summary>Called on every message callback with its opcode (a test's cost model, or a reaction).</summary>
    public Action<uint>? OnAnyMessage { get; set; }

    /// <summary>Called on every session event (a test's reaction).</summary>
    public Action<SessionEvent>? OnAnyEvent { get; set; }

    public IEnumerable<SessionPhase> Phases => Events.Where(e => e.Kind == SessionEventKind.PhaseChanged).Select(e => e.Phase);

    public IEnumerable<SessionEvent> OfKind(SessionEventKind kind) => Events.Where(e => e.Kind == kind);

    public IEnumerable<uint> Opcodes => Dispatched.Select(d => d.Opcode);

    public void OnSessionEvent(in SessionEvent sessionEvent)
    {
        Events.Add(sessionEvent);
        OnAnyEvent?.Invoke(sessionEvent);
    }

    public void OnCharacterList(in InboundContext context, CharacterListMsg message)
    {
        CharacterLists.Add(message);
        Record(context, MessageOpcodes.CharacterList);
    }

    public void OnServerName(in InboundContext context, in ServerNameMsg message) => Record(context, MessageOpcodes.ServerName);

    public void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message) => Record(context, MessageOpcodes.DddInterrogation);

    public void OnDddBegin(in InboundContext context, in DddBeginMsg message) => Record(context, MessageOpcodes.DddBegin);

    public void OnDddEnd(in InboundContext context) => Record(context, MessageOpcodes.DddEnd);

    public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message)
    {
        CharGenResponses.Add(message);
        Record(context, MessageOpcodes.CharacterCreateResponse);
    }

    public void OnCharacterDeleteAck(in InboundContext context) => Record(context, MessageOpcodes.CharacterDelete);

    public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message)
    {
        CharacterErrors.Add(message.Code);
        Record(context, MessageOpcodes.CharacterError);
    }

    public void OnAccountBoot(in InboundContext context, in AccountBootMsg message)
    {
        Boots.Add(message);
        Record(context, MessageOpcodes.AccountBoot);
    }

    public void OnAccountBanned(in InboundContext context, in AccountBannedMsg message) => Record(context, MessageOpcodes.AccountBanned);

    public void OnEnterWorldServerReady(in InboundContext context) => Record(context, MessageOpcodes.CharacterEnterWorldServerReady);

    public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message) => Record(context, MessageOpcodes.PlayerCreate);

    public void OnCharacterLogOff(in InboundContext context) => Record(context, MessageOpcodes.CharacterLogOff);

    public void OnServerMessage(in InboundContext context, in ServerMessageMsg message) => Record(context, MessageOpcodes.ServerMessage);

    public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) => Record(context, MessageOpcodes.GameEvent);

    // World, chat and the decoded game events (N7): each is kept and recorded under its opcode (F7B0 for an event).

    public List<(CreateObjectMsg Message, bool IsUpdate)> Creates { get; } = [];

    public List<UpdatePositionMsg> Positions { get; } = [];

    public List<UpdateMotionMsg> Motions { get; } = [];

    public List<SetStateMsg> States { get; } = [];

    public List<PlayerTeleportMsg> Teleports { get; } = [];

    public List<SpeechMsg> Speech { get; } = [];

    public List<EmoteMsg> Emotes { get; } = [];

    /// <summary>Every decoded game event's header and record, in order.</summary>
    public List<(GameEventHeader Header, object Event)> DecodedEvents { get; } = [];

    public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
    {
        Creates.Add((message, isUpdate));
        Record(context, isUpdate ? MessageOpcodes.UpdateObject : MessageOpcodes.ObjectCreate);
    }

    public void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message) => Record(context, MessageOpcodes.ObjectDelete);

    public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message)
    {
        Positions.Add(message);
        Record(context, MessageOpcodes.UpdatePosition);
    }

    public void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message)
    {
        Motions.Add(message);
        Record(context, MessageOpcodes.UpdateMotion);
    }

    public void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message) => Record(context, MessageOpcodes.VectorUpdate);

    public void OnSetState(in InboundContext context, in SetStateMsg message)
    {
        States.Add(message);
        Record(context, MessageOpcodes.SetState);
    }

    public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message)
    {
        Teleports.Add(message);
        Record(context, MessageOpcodes.PlayerTeleport);
    }

    public void OnParentEvent(in InboundContext context, in ParentEventMsg message) => Record(context, MessageOpcodes.ParentEvent);

    public void OnPickupEvent(in InboundContext context, in PickupEventMsg message) => Record(context, MessageOpcodes.PickupEvent);

    public void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message) => Record(context, MessageOpcodes.ObjDescEvent);

    public void OnSound(in InboundContext context, in SoundMsg message) => Record(context, MessageOpcodes.Sound);

    public void OnPlayEffect(in InboundContext context, in PlayEffectMsg message) => Record(context, MessageOpcodes.PlayEffect);

    public void OnSpeech(in InboundContext context, in SpeechMsg message)
    {
        Speech.Add(message);
        Record(context, message.Range is null ? MessageOpcodes.HearSpeech : MessageOpcodes.HearRangedSpeech);
    }

    public void OnEmote(in InboundContext context, in EmoteMsg message)
    {
        Emotes.Add(message);
        Record(context, message.IsSoulEmote ? MessageOpcodes.SoulEmote : MessageOpcodes.EmoteText);
    }

    public void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message) => RecordEvent(context, header, message);

    public void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message) => RecordEvent(context, header, message);

    public void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message) => RecordEvent(context, header, message);

    public void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message) => RecordEvent(context, header, message);

    public void OnUseDone(in InboundContext context, in GameEventHeader header, in UseDoneEvent message) => RecordEvent(context, header, message);

    public void OnUpdateHealth(in InboundContext context, in GameEventHeader header, in UpdateHealthEvent message) => RecordEvent(context, header, message);

    /// <summary>Every PlayerDescription (N10), in order.</summary>
    public List<PlayerDescriptionMsg> Descriptions { get; } = [];

    /// <summary>Every PrivateUpdate (N10), in order.</summary>
    public List<PrivateUpdateMsg> PrivateUpdates { get; } = [];

    public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
    {
        Descriptions.Add(message);
        RecordEvent(context, header, message);
    }

    public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message)
    {
        PrivateUpdates.Add(message);
        Record(context, (uint)message.Kind);
    }

    public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) => Record(context, opcode);

    public void OnMalformed(in InboundContext context, uint opcode, int length, string reason)
    {
        Malformed.Add((opcode, reason));
        Record(context, opcode);
    }

    private void RecordEvent(in InboundContext context, in GameEventHeader header, object message)
    {
        DecodedEvents.Add((header, message));
        Record(context, MessageOpcodes.GameEvent);
    }

    private void Record(in InboundContext context, uint opcode)
    {
        Dispatched.Add((context.FragmentSequence, opcode));
        OnAnyMessage?.Invoke(opcode);
    }
}

/// <summary>Records every message the raw tap sees.</summary>
internal sealed class RecordingTap : IRawMessageTap
{
    public List<(MessageDirection Direction, long Timestamp, uint Sequence, MessageQueue Queue, byte[] Body)> Messages { get; } = [];

    public IEnumerable<byte[]> Outbound => Messages.Where(m => m.Direction == MessageDirection.Outbound).Select(m => m.Body);

    public IEnumerable<uint> OutboundOpcodes => Outbound.Select(b => BitConverter.ToUInt32(b));

    public void OnMessage(MessageDirection direction, long timestamp, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body) =>
        Messages.Add((direction, timestamp, fragmentSequence, queue, body.ToArray()));
}

/// <summary>Captures every log line.</summary>
internal sealed class CapturingNetLog : INetLog
{
    public List<(NetLogLevel Level, string Message)> Lines { get; } = [];

    public void Log(NetLogLevel level, string message) => Lines.Add((level, message));

    public IEnumerable<string> At(NetLogLevel level) => Lines.Where(l => l.Level == level).Select(l => l.Message);

    public override string ToString() => string.Join('\n', Lines.Select(l => $"{l.Level}: {l.Message}"));
}

/// <summary>Byte-level searches for a secret in captured output.</summary>
internal static class SecretScan
{
    /// <summary>The forms a password could take on the way out: ASCII/CP1252, UTF-16LE, and String32L-framed.</summary>
    public static IEnumerable<byte[]> Forms(string secret)
    {
        var ascii = Encoding.ASCII.GetBytes(secret);
        yield return ascii;
        yield return Encoding.Unicode.GetBytes(secret);
        var framed = new byte[5 + ascii.Length];
        BitConverter.TryWriteBytes(framed, ascii.Length + 1);
        framed[4] = (byte)ascii.Length;
        ascii.CopyTo(framed, 5);
        yield return framed;
    }

    /// <summary>True when <paramref name="haystack"/> contains any form of <paramref name="secret"/>.</summary>
    public static bool Contains(ReadOnlySpan<byte> haystack, string secret)
    {
        foreach (var form in Forms(secret))
        {
            if (haystack.IndexOf(form) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
