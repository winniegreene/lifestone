using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Flow;

/// <summary>Where a character error sends the interface, and what it says there.</summary>
/// <param name="Code">The CharacterError code (F659).</param>
/// <param name="Mode">The mode it queues: <see cref="UiModes.Disconnected"/> or <see cref="UiModes.CharacterManagement"/>.</param>
/// <param name="Token">The error's token in the pregame string table (<c>ID_CHAR_ERROR_...</c>).</param>
public readonly record struct UiCharacterErrorRoute(uint Code, uint Mode, string Token);

/// <summary>
/// The game's UI flow (retail's <c>gmUIFlow</c>): the eight retail modes registered in retail's order, the data check
/// queued first, the default pointer set, the UI flow's input map, and the two notices that change the mode on their own:
/// a character error and a lost connection.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmUIFlow::gmUIFlow</c> (11.4186 0x0047A610, 11.6096 0x0047AA10). Retail first runs
/// <c>UIGlobals::Init</c> (11.4186 0x004EE470, 11.6096 0x004EF100) and <c>gmUIHelper::RegisterElements</c> (11.4186
/// 0x0047A460, 11.6096 0x0047A860: the game's element classes, which the screen tracks register with
/// <see cref="UiClassRegistry"/>), then registers the modes data patch, intro, character management, game play,
/// epilogue, disconnected, CharGen and credits (<see cref="UiModes.RetailOrder"/>), queues the data patch, sets the
/// pointer of UICURSOR enum 1 as the default at hot spot (0, 0) (<c>UIElementManager::SetCursor</c>, 11.4186 0x0045A800,
/// 11.6096 0x0045A910), hears the character-error and server-died notices, and registers input map 14 at priority 4000.
/// </para>
/// <para>
/// <c>gmUIFlow::RecvNotice_CharacterError</c> (11.4186 0x0047A7C0, 11.6096 0x0047ABC0) names the error by its token in
/// the pregame strings (<c>StringInfo::SetTableEnum</c> 0x10000002, which is StringTable 0x23000002) and queues the
/// disconnected screen for a session-ending error or character management for an entry error
/// (<see cref="Routes"/>); any other code changes nothing. <c>gmUIFlow::RecvNotice_ServerDied</c> (11.4186 0x0047A9A0,
/// 11.6096 0x0047ADA0) queues the disconnected screen with <c>ID_NetErr_ConnectionLost</c>, unless that screen already
/// shows or is queued.
/// </para>
/// <para>
/// What <c>gmUIFlow</c> does with input map 14's actions is not in the decompile: an action that arrives there is logged
/// loudly once and passed on (TODO(retailui-runtime): gmUIFlow's action callback, 11.6096 unknown).
/// </para>
/// </remarks>
public sealed class GmUiFlow : IInputActionSink, IDisposable
{
    /// <summary>The UI flow's input map.</summary>
    public const uint InputMap = 14;

    /// <summary>The UI flow's input map's priority.</summary>
    public const int InputMapPriority = 4000;

    /// <summary>
    /// The pregame strings' table (STRINGTABLE enum 0x10000002, <see cref="UiStringTables.UiPregameEnum"/>, which the
    /// retail map resolves to 0x23000002; a dat test pins it).
    /// </summary>
    public const uint PregameTableId = 0x23000002;

    /// <summary>The token of the lost-connection error.</summary>
    public const string ConnectionLostToken = "ID_NetErr_ConnectionLost";

    private readonly UiElementManager _manager;
    private readonly uint _tableId;

    /// <summary>
    /// The flow over <paramref name="manager"/>: each retail mode shows the screen <paramref name="screens"/> makes for it
    /// (null for none yet), the default pointer is <paramref name="defaultCursor"/>, and errors name strings of
    /// <paramref name="pregameTableId"/>.
    /// </summary>
    public GmUiFlow(UiElementManager manager, Func<uint, IUiScreen?> screens, UiCursor? defaultCursor, uint pregameTableId = PregameTableId)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(screens);
        _manager = manager;
        _tableId = pregameTableId;
        Flow = new UiFlow(manager);
        foreach (var mode in UiModes.RetailOrder)
        {
            Flow.RegisterFrameworkClass(mode, () => screens(mode));
        }

        Flow.QueueUIMode(UiModes.DataPatch);
        if (defaultCursor is null)
        {
            manager.LogOnce("flow:cursor", LogSeverity.Warning, "TODO(retailui-runtime): no default pointer (UICURSOR enum 1) was given; the host's own cursor shows (placeholder)");
        }
        else
        {
            manager.Cursor.SetCursor(defaultCursor.ImageId, defaultCursor.HotspotX, defaultCursor.HotspotY, isDefault: true);
        }

        manager.Input.RegisterInputMap(InputMap, this, InputMapPriority);
    }

    /// <summary>The character-error routes, by code, as <c>gmUIFlow::RecvNotice_CharacterError</c> has them.</summary>
    public static IReadOnlyDictionary<uint, UiCharacterErrorRoute> Routes { get; } = BuildRoutes();

    /// <summary>The modes.</summary>
    public UiFlow Flow { get; }

    /// <summary>The error of a route as a string reference in the pregame strings.</summary>
    public UiStringRef ErrorText(in UiCharacterErrorRoute route) => new(StringHash.Tokenize(route.Token), _tableId);

    /// <summary>
    /// <c>gmUIFlow::RecvNotice_CharacterError</c>: a known code queues its mode with its error; false (and a log line) for a
    /// code retail ignores.
    /// </summary>
    public bool RecvNoticeCharacterError(uint code)
    {
        if (!Routes.TryGetValue(code, out var route))
        {
            _manager.Services.Log.Info(UiElementManager.Category, $"character error {code} changes no UI mode (as retail)");
            return false;
        }

        Flow.QueueUIModeWithError(route.Mode, ErrorText(route));
        return true;
    }

    /// <summary>
    /// <c>gmUIFlow::RecvNotice_ServerDied</c>: the disconnected screen with the lost-connection error, unless it already
    /// shows or is queued. False when it changed nothing.
    /// </summary>
    public bool RecvNoticeServerDied()
    {
        if (Flow.CurrentMode == UiModes.Disconnected || Flow.NextMode == UiModes.Disconnected)
        {
            return false;
        }

        Flow.QueueUIModeWithError(UiModes.Disconnected, new UiStringRef(StringHash.Compute(ConnectionLostToken), _tableId));
        return true;
    }

    /// <summary>
    /// Hears the session's character errors (<see cref="CharacterErrorReceived"/>) on <paramref name="events"/>; dispose the
    /// result to stop. A lost connection is the host's to report (<see cref="RecvNoticeServerDied"/>), because the
    /// contract reports it in network terms.
    /// </summary>
    public IDisposable Attach(IEventBus events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Subscribe<CharacterErrorReceived>(OnCharacterError);
    }

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        _manager.LogOnce($"flow:action:{inputMapId:X}:{actionId:X}", LogSeverity.Warning, $"TODO(retailui-runtime): gmUIFlow's handling of input map {inputMapId} action 0x{actionId:X8} is not known (11.6096 unknown); passed on");
        return false;
    }

    /// <summary>Stops the flow (disposes its screen) and unregisters the input map.</summary>
    public void Dispose()
    {
        _manager.Input.UnregisterInputMap(InputMap, this);
        Flow.Dispose();
    }

    private void OnCharacterError(in CharacterErrorReceived e) => RecvNoticeCharacterError(e.Code);

    private static Dictionary<uint, UiCharacterErrorRoute> BuildRoutes()
    {
        var routes = new Dictionary<uint, UiCharacterErrorRoute>();
        void Add(uint mode, string token, params uint[] codes)
        {
            foreach (var code in codes)
            {
                routes.Add(code, new UiCharacterErrorRoute(code, mode, token));
            }
        }

        // Session-ending errors: the disconnected screen.
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_LOGON", 1);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_LOGON", 3);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_SERVER_CRASH", 4, 8);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_INVALID", 9);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_DOESNT_EXIST", 10);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_ENTER_GAME_PLAYER_ACCOUNT_MISSING", 14);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_LOGON_SERVER_FULL", 21);
        Add(UiModes.Disconnected, "ID_CHAR_ERROR_SUBSCRIPTION_EXPIRED", 24);

        // Errors the player can retry: character management.
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_LOGOFF", 5);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_DELETE", 6);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_GENERIC", 11);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_STRESS_ACCOUNT", 12);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD", 13);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_NOT_OWNED", 15);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD_SERVER", 16);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER", 17);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CORRUPT_CHARACTER", 18);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_START_SERVER_DOWN", 19);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_COULDNT_PLACE_CHARACTER", 20);
        Add(UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_LOCKED", 23);
        return routes;
    }
}
