using System.Collections.Immutable;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// One stand-in character (<see cref="StandInSessions"/>): a Lifestone-authored name, never one from the dats or a server.
/// </summary>
/// <param name="Name">The name: 1 to 32 printable ASCII characters.</param>
/// <param name="PendingDeletion">True to list it as pending deletion (restorable, not enterable).</param>
public readonly record struct StandInCharacter(string Name, bool PendingDeletion = false);

/// <summary>
/// Stand-in data for an offline session (<see cref="GameSession.Offline"/>; step ST7b, checkpoint C-state-offline): the
/// network session's events and the server's login messages, made up here and fed through the same reducer a live
/// session uses, so the retail screens run without a backend (data check, character list, disconnected screen). Names are
/// Lifestone-authored only; nothing comes from the dats, a server or a recording.
/// </summary>
/// <remarks>
/// <para>Each method takes the handler <c>GameSession.Offline</c> passes to its stand-in callback (the session's reducer):
/// <code>GameSession.Offline(h => StandInSessions.CharacterSelect(h), TimeProvider.System, log)</code> The handler must be a
/// network-session sink (<see cref="IAcSessionSink"/>), since the phases come as session events. The immediate methods
/// work with any sink (a test's); <see cref="DataCheckThenCharacterSelect"/> needs an offline session's reducer, whose
/// <c>Tick</c> runs the later steps.</para>
/// <para>Every server action on an offline session is refused (<see cref="Contracts.ActionOutcomeKind.Refused"/>);
/// selecting a character works, since it is local.</para>
/// </remarks>
public static partial class StandInSessions
{
    /// <summary>The stand-in world's name (F7E1).</summary>
    public const string WorldName = "Lifestone Stand-in";

    /// <summary>The first stand-in character's object id (ACE's first player id); the others follow it.</summary>
    public const uint FirstCharacterGuid = 0x50000001;

    /// <summary>The slot count the stand-in list reports (ACE's <c>max_chars_per_account</c>).</summary>
    public const uint SlotCount = 11;

    /// <summary>The longest stand-in name.</summary>
    public const int MaxNameLength = 32;

    private const string StandInAccount = "stand-in";

    /// <summary>The characters used when none are given: two ready ones and one pending deletion.</summary>
    public static IReadOnlyList<StandInCharacter> DefaultCharacters { get; } =
        [new("Stand-in Alpha"), new("Stand-in Beta"), new("Stand-in Gamma", PendingDeletion: true)];

    /// <summary>How long <see cref="DataCheckThenCharacterSelect"/>'s data check lasts when the caller does not say.</summary>
    public static TimeSpan DefaultDataCheckDuration { get; } = TimeSpan.FromSeconds(2);

    /// <summary>How long <see cref="AnswerLocally"/>'s answers take when the caller does not say: long enough to see the "please wait" dialog.</summary>
    public static TimeSpan DefaultAnswerDelay { get; } = TimeSpan.FromSeconds(StandInResponder.DefaultDelaySeconds);

    /// <summary>A stand-in failure for <see cref="Disconnected"/>: the server went silent at character select.</summary>
    public static SessionFailure ServerStopped { get; } =
        new(SessionFailureKind.Silence, SessionPhase.CharacterSelect, "Stand-in: the server stopped answering.");

    /// <summary>A stand-in failure for <see cref="Disconnected"/>: no server answered the login.</summary>
    public static SessionFailure NoServer { get; } =
        new(SessionFailureKind.ConnectTimeout, SessionPhase.Connecting, "Stand-in: no server answered the login.");

    /// <summary>
    /// A completed login, at once: connecting, the handshake, the data check (F7E5, F7EA), the world's name (F7E1) and the
    /// character list (F658), ending at character select.
    /// </summary>
    /// <param name="handler">The session's reducer (any <see cref="IAcSessionSink"/>).</param>
    /// <param name="characters">The characters, in list order; none means <see cref="DefaultCharacters"/>.</param>
    public static void CharacterSelect(IInboundMessageHandler handler, params StandInCharacter[] characters)
    {
        var sink = SinkOf(handler);
        var list = ListOf(characters);
        BeginDataCheck(sink);
        FinishLogin(sink, list);
    }

    /// <summary>A login stopped in the data check (F7E5 answered, no F7EA): the data-patch screen stays up.</summary>
    /// <param name="handler">The session's reducer (any <see cref="IAcSessionSink"/>).</param>
    public static void DataCheckInProgress(IInboundMessageHandler handler) => BeginDataCheck(SinkOf(handler));

    /// <summary>
    /// The data check now, then, <paramref name="dataCheck"/> later on the session clock, the rest of
    /// <see cref="CharacterSelect"/>: the data-patch screen animates, then the character list comes up. Needs an offline
    /// session's reducer (the later steps run in its <c>Tick</c>).
    /// </summary>
    /// <param name="handler">The offline session's reducer.</param>
    /// <param name="dataCheck">How long the data check lasts (zero or more).</param>
    /// <param name="characters">The characters; none means <see cref="DefaultCharacters"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="handler"/> is not an offline session's reducer.</exception>
    public static void DataCheckThenCharacterSelect(IInboundMessageHandler handler, TimeSpan dataCheck, params StandInCharacter[] characters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dataCheck, TimeSpan.Zero);
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException(
                "timed stand-in data needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var list = ListOf(characters);
        BeginDataCheck(reducer);
        schedule.At(reducer.Clock.Now + dataCheck.TotalSeconds, sink => FinishLogin(sink, list));
    }

    /// <summary>
    /// Lets the offline session answer a character <b>delete</b>, <b>restore</b> and <b>create</b> locally, through the same reducer
    /// a server's messages go through (a synthetic F655 acknowledgement and F658, F643), <paramref name="delay"/> later on the
    /// session clock; every other server action stays refused. Opt-in: <c>--retail-ui=standin</c> calls it so every screen can be
    /// clicked through; an offline session that does not call it, and every live session, behave as before. Calling it again keeps
    /// the first responder (a reconnect runs the stand-in callback again).
    /// </summary>
    /// <param name="handler">The offline session's reducer (the handler <c>GameSession.Offline</c> passes).</param>
    /// <param name="delay">How long an answer takes (default <see cref="DefaultAnswerDelay"/>); zero answers on the next tick.</param>
    /// <exception cref="ArgumentException"><paramref name="handler"/> is not an offline session's reducer.</exception>
    public static void AnswerLocally(IInboundMessageHandler handler, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler is not GameStateReducer { StandIn: not null } reducer)
        {
            throw new ArgumentException("answering locally needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var wait = delay ?? DefaultAnswerDelay;
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero, nameof(delay));
        reducer.LocalResponder ??= new StandInResponder(reducer, wait.TotalSeconds);
    }

    /// <summary>
    /// A login that failed with <paramref name="failure"/>: the phases up to the failure's phase (when it is one of the
    /// login's), then the failure, for the disconnected screen.
    /// </summary>
    /// <param name="handler">The session's reducer (any <see cref="IAcSessionSink"/>).</param>
    /// <param name="failure">The failure (its message is shown; Lifestone-authored text only).</param>
    public static void Disconnected(IInboundMessageHandler handler, SessionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var sink = SinkOf(handler);
        var phase = SessionPhase.Idle;
        foreach (var next in (ReadOnlySpan<SessionPhase>)[SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.CharacterSelect])
        {
            if (next > failure.Phase)
            {
                break;
            }

            sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), phase, next));
            phase = next;
        }

        sink.OnSessionEvent(SessionEvent.Failed(Now(sink), failure));
    }

    private static void BeginDataCheck(IAcSessionSink sink)
    {
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.Idle, SessionPhase.Connecting));
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.Connecting, SessionPhase.Handshaking));
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.Handshaking, SessionPhase.DataCheck));
        sink.OnDddInterrogation(Context(sink), new DddInterrogationMsg(1, 1, 1, [1]));
        sink.OnSessionEvent(SessionEvent.DataCheckStarted(Now(sink), SessionPhase.DataCheck));
    }

    private static void FinishLogin(IAcSessionSink sink, CharacterListMsg list)
    {
        sink.OnDddEnd(Context(sink));
        sink.OnSessionEvent(SessionEvent.DataCheckComplete(Now(sink), SessionPhase.DataCheck));
        sink.OnServerName(Context(sink), new ServerNameMsg(0, -1, WorldName));
        sink.OnCharacterList(Context(sink), list);
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.DataCheck, SessionPhase.CharacterSelect));
    }

    private static CharacterListMsg ListOf(StandInCharacter[]? characters, IReadOnlySet<uint>? purged = null)
    {
        IReadOnlyList<StandInCharacter> chosen = characters is { Length: > 0 } ? characters : DefaultCharacters;
        var entries = ImmutableArray.CreateBuilder<CharacterListEntry>(chosen.Count);
        for (var i = 0; i < chosen.Count; i++)
        {
            var c = chosen[i];
            if (string.IsNullOrEmpty(c.Name) || c.Name.Length > MaxNameLength || c.Name.Any(ch => ch is < ' ' or > '~'))
            {
                throw new ArgumentException($"stand-in character {i} needs a name of 1 to {MaxNameLength} printable ASCII characters", nameof(characters));
            }

            if (purged is not null && purged.Contains(FirstCharacterGuid + (uint)i))
            {
                continue;
            }

            // ACE reports a pending deletion as at least 1 (owner decision D52: no countdown).
            entries.Add(new CharacterListEntry(FirstCharacterGuid + (uint)i, c.Name, c.PendingDeletion ? 1u : 0u));
        }

        return new CharacterListMsg(0, entries.ToImmutable(), 0, SlotCount, StandInAccount, 0, 1);
    }

    private static IAcSessionSink SinkOf(IInboundMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler as IAcSessionSink
            ?? throw new ArgumentException("stand-in data needs a network-session sink (the session's reducer): the phases come as session events", nameof(handler));
    }

    /// <summary>The handler's clock timestamp when it is a session's reducer, else 0.</summary>
    private static long Now(IAcSessionSink sink) => sink is GameStateReducer reducer ? reducer.Clock.Time.GetTimestamp() : 0;

    private static InboundContext Context(IAcSessionSink sink) => new(0, Lifestone.Net.Types.MessageQueue.Login, Now(sink));
}
