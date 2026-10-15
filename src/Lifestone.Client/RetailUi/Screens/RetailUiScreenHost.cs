using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The screen host (m2-plan G2 U3): <c>gmUIFlow</c> with the login screens and the session behind them. It builds the flow with the screen
/// factory (<see cref="LoginScreens"/>), hears the session (<see cref="GmUiFlow.Attach"/> for character errors, and the loss of the
/// connection, which the contract reports in network terms and this turns into <see cref="GmUiFlow.RecvNoticeServerDied"/>), and owns the
/// router the game's text notices go to (<see cref="Notices"/>). The session is a live one, or the offline stand-in
/// (<c>GameSession.Offline</c> with <c>StandInSessions</c>): the screens read <see cref="IGameSessionView"/> and act only through
/// <see cref="IActionApi"/>, so they cannot tell.
/// </summary>
/// <remarks>
/// The host calls <see cref="Update"/> once a frame after the element runtime's update (<c>UIFlow::Update</c>). The mode order is the
/// data check, then character management (the intro is skipped, D38); the epilogue and, with D43, the disconnected screen's button end
/// the application or reconnect through <see cref="IUiLoginHost"/>.
/// </remarks>
public sealed class RetailUiScreenHost : IDisposable
{
    private readonly UiLoginContext _context;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;
    private bool _lost;

    /// <summary>
    /// The host over <paramref name="manager"/> and <paramref name="session"/>: the flow queued at the data check, or at
    /// <paramref name="startMode"/> when one is given (<c>--retail-ui-start</c>; the disconnected screen then starts with the lost-connection reason).
    /// </summary>
    /// <param name="manager">The element runtime.</param>
    /// <param name="session">The session the screens read and act on.</param>
    /// <param name="host">The application (exit, reconnect).</param>
    /// <param name="defaultCursor">The default pointer (UICURSOR enum 1), or null for the host's own.</param>
    /// <param name="retailDisconnect">True for retail's disconnected button (<c>--retail-disconnect</c>, D43).</param>
    /// <param name="startMode">The first mode, or null for the data check.</param>
    /// <param name="charGen">What character creation takes from the application, or null: the mode then shows its stand-in (placeholder).</param>
    /// <param name="liveGame">True when a live session can enter the world (play mode): kept for the application; the game screen is <see cref="GamePlayScreen"/> either way.</param>
    /// <param name="stageShell">True to build the in-game shell over frames (<see cref="UiLoginContext.StageShell"/>); the live host sets it.</param>
    public RetailUiScreenHost(UiElementManager manager, IGameSessionView session, IUiLoginHost host, UiCursor? defaultCursor, bool retailDisconnect = false, uint? startMode = null, CharGenSetup? charGen = null, bool liveGame = false, bool stageShell = false)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        UiLoginContext? context = null;
        Gm = new GmUiFlow(manager, mode => LoginScreens.Create(context!, mode), defaultCursor);
        Notices = new UiNoticeRouter(manager.Services.Log);
        _context = context = new UiLoginContext(manager, session, Gm.Flow, host, retailDisconnect, charGen, liveGame, Notices, stageShell);
        _subscriptions.Add(Gm.Attach(session.Events));
        if (startMode is { } start)
        {
            if (start == UiModes.Disconnected)
            {
                Gm.Flow.QueueUIModeWithError(start, new UiStringRef(StringHash.Tokenize(GmUiFlow.ConnectionLostToken), GmUiFlow.PregameTableId));
            }
            else
            {
                Gm.Flow.QueueUIMode(start);
            }
        }
    }

    /// <summary>The game's UI flow (<c>gmUIFlow</c>).</summary>
    public GmUiFlow Gm { get; }

    /// <summary>The modes (<c>UIFlow</c>).</summary>
    public UiFlow Flow => Gm.Flow;

    /// <summary>Where the game's text notices go (spew box type 26, chat for the rest; placeholders until G4 registers the windows).</summary>
    public UiNoticeRouter Notices { get; }

    /// <summary>What the screens share.</summary>
    public UiLoginContext Context => _context;

    /// <summary><c>UIFlow::Update</c>: the current screen's frame. The host calls it once a frame, after the element runtime's update.</summary>
    public void Update()
    {
        CheckConnection();
        Gm.Flow.Update();
    }

    /// <summary>Stops hearing the session and disposes the flow and its screen.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        Gm.Dispose();
    }

    /// <summary>
    /// Reports a session that ended in failure to the flow as a lost connection (<c>gmUIFlow::RecvNotice_ServerDied</c>), once per failure: it is
    /// read from the state each frame, so a session that failed before the host started is reported too. A reconnect arms it again.
    /// </summary>
    private void CheckConnection()
    {
        var state = _context.Session.State;
        if (!state.ConnectionLost)
        {
            _lost = false;
            return;
        }

        if (_lost)
        {
            return;
        }

        _lost = true;
        _context.Manager.Services.Log.Warn(UiElementManager.Category, $"the session ended in failure ({state.FailureMessage ?? "no reason given"}): the disconnected screen reports the lost connection");
        Gm.RecvNoticeServerDied();
    }
}
