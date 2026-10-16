using System.Net;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Action API over a reducer (<see cref="ReducerHarness"/>: a manual clock, every event recorded) and a
/// <see cref="FakeAcSession"/>, with the server's side played by the test: the login messages, entering the world,
/// unhiding the player. <see cref="Tick"/> advances the clock and runs the Action API's update, as the session's tick
/// does after the network session's messages.
/// </summary>
internal sealed class ActionHarness
{
    /// <summary>The first character of <see cref="AtCharacterSelect"/>'s default list (the player in <see cref="InWorld"/>).</summary>
    public const uint Amy = ReducerHarness.PlayerGuid;

    /// <summary>The second character, pending deletion.</summary>
    public const uint Bo = 0x50000002;

    /// <summary>The third character.</summary>
    public const uint Cy = 0x50000003;

    /// <summary>A sentinel password: the tests check it never reaches a log line.</summary>
    public const string Password = "Sentinel-ACTION-pw-55d0";

    public ActionHarness(EndPoint? server = null, bool offline = false)
    {
        R = new ReducerHarness();
        Server = server ?? new IPEndPoint(IPAddress.Loopback, 9000);
        Link = offline ? null : new ActionLink(Server, () => Net, s =>
        {
            using var credentials = new AcCredentials("lifestone-test", Password);
            s.Login(credentials);
        });
        ActionLog = new ActionLog();
        Api = new ActionApi(R.Reducer, R.Bus, ActionLog, R.Log, Link, thread: null);
    }

    public ReducerHarness R { get; }

    public FakeAcSession Net { get; } = new();

    public EndPoint Server { get; }

    public ActionLink? Link { get; }

    public ActionLog ActionLog { get; }

    public ActionApi Api { get; }

    public GameState State => R.State;

    public RecordingLogSink Log => R.Log;

    /// <summary>Advances the clock by <paramref name="seconds"/>, then runs the Action API's update at that frame time.</summary>
    public void Tick(double seconds = 0)
    {
        if (seconds > 0)
        {
            R.Time.Advance(TimeSpan.FromSeconds(seconds));
        }

        Api.Update(R.Clock.Now);
    }

    /// <summary>The login messages up to character select with <paramref name="characters"/> (default: Amy, Bo pending deletion, Cy).</summary>
    public void AtCharacterSelect(params (string Name, uint DeleteTimeout, uint Guid)[] characters)
    {
        if (characters.Length == 0)
        {
            characters = [("Amy", 0u, Amy), ("Bo", 1u, Bo), ("Cy", 0u, Cy)];
        }

        Phase(SessionPhase.Idle, SessionPhase.Connecting);
        Phase(SessionPhase.Connecting, SessionPhase.Handshaking);
        Phase(SessionPhase.Handshaking, SessionPhase.DataCheck);
        R.Reducer.OnSessionEvent(SessionEvent.DataCheckStarted(R.Time.GetTimestamp(), SessionPhase.DataCheck));
        R.Reducer.OnDddEnd(R.Context);
        CharacterList(characters);
        Phase(SessionPhase.DataCheck, SessionPhase.CharacterSelect);
    }

    /// <summary>F658 with <paramref name="characters"/>.</summary>
    public void CharacterList(params (string Name, uint DeleteTimeout, uint Guid)[] characters) =>
        R.Reducer.OnCharacterList(
            R.Context,
            new CharacterListMsg(0, [.. characters.Select(c => new CharacterListEntry(c.Guid, c.Name, c.DeleteTimeout))], 0, 11, "account-string", 0, 1));

    /// <summary>A phase change of the network session.</summary>
    public void Phase(SessionPhase from, SessionPhase to) => R.Phase(from, to);

    /// <summary>Character select, then the world: F746 and the player's hidden CreateObject (the player is <see cref="SelfTravel.Arriving"/>).</summary>
    public void InWorld()
    {
        if (State.Phase != SessionPhase.CharacterSelect)
        {
            AtCharacterSelect();
        }

        Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        Phase(SessionPhase.EnteringWorld, SessionPhase.InWorld);
        R.EnterWorld();
    }

    /// <summary>The player's PlayerDescription (an empty one unless <paramref name="description"/> is given): LoginComplete waits for it (M3 1.2 s5).</summary>
    public void Describe(PlayerDescriptionMsg? description = null) =>
        R.Reducer.OnPlayerDescription(R.Context, Header, description ?? new PlayerDescriptionMsg());

    /// <summary>ACE's SetState that clears Hidden on the player (the player becomes <see cref="SelfTravel.Present"/>).</summary>
    public void Unhide(ushort stateStamp = 2) =>
        R.Reducer.OnSetState(R.Context, new SetStateMsg(Amy, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, stateStamp));

    /// <summary>In the world and present: <see cref="InWorld"/>, LoginComplete sent (notice only), unhidden.</summary>
    public void Present()
    {
        InWorld();
        R.Reducer.ApplyLocal(new Lifestone.Client.Session.Recording.RecordingNotice(Lifestone.Client.Session.State.SessionNotices.LoginCompleteSent, 0, null));
        Unhide();
    }

    /// <summary>An F7E0 ServerMessage.</summary>
    public void ServerMessage(string text, int chatType = 0) => R.Reducer.OnServerMessage(R.Context, new ServerMessageMsg(text, chatType));

    /// <summary>A game-event header for the player.</summary>
    public GameEventHeader Header => new(Amy, 1, 0);
}
