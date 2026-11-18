using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The re-buff driver in play mode (M4 4.1 lane c; DoD 14): a thin Godot shell around <see cref="RebuffHostLogic"/>, which holds every rule. This node loads the SpellTable on a worker when the switch asks for it, ticks the logic once a
/// frame (counters only, no allocation) after the session host and play mode, hands the Lifestone key's press to it (<see cref="Lifestone.Client.RetailUi.LifestonePanels.LifestoneHotkeys.ToggleAutoBuff"/>: <b>Pause</b>, which retail's
/// key map does not bind) and keeps the armour ledger in the user data (<c>user://armour</c>, a file per character guid) across a log-off. It is on at start with <c>--buff-auto on</c>, and the key turns it on and off in the game.
/// </summary>
/// <remarks>
/// <para>Installed by <see cref="PlayMode.Install"/> for a normal play session (not for a <c>--login-check</c> leg: the buff leg runs its own driver inside its scenario, and two drivers would fight over one mage).</para>
/// <para>A character with no buff profile (only BSD Mage has one: <see cref="BuffProfiles"/>) gets the chat notice "[Lifestone] no buff profile for this character" and the driver stays off. Chat notices are Lifestone's own client lines,
/// not retail's: a <b>placeholder</b> for what retail never had.</para>
/// </remarks>
public sealed partial class RebuffDriverHost : Node
{
    private const string LogCategory = "buff";

    private SessionHost _host = null!;
    private RetailUiLayer? _ui;
    private RebuffHostLogic _logic = null!;
    private GameServices _services = null!;
    private Task<IReadOnlyDictionary<uint, SpellInfo>>? _loading;
    private object? _wiredHotkeys;
    private bool _saved;
    private bool _autopilotLooked;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("RebuffDriverHost", LogCategory, GodotLogSink.Instance);

    /// <summary>Adds the host to the scene.</summary>
    public static RebuffDriverHost Install(Main main, GameServices services, SessionHost host, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        var options = BuffOptions.FromArguments(CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()));
        foreach (var error in options.Errors)
        {
            Log.Error(LogCategory, "auto-buff: " + error);
        }

        options = options with { AccountName = TryLoginName(host.Options.Account) }; // M6 1 w (closes TODO(m6-account-formula-callers) for the play host): the login name the server hashes, so the host's cycles plan a school without its focus on the account's formula
        var store = new ArmourLedgerFileStore(new UserDataFiles(ProjectSettings.GlobalizePath("user://armour")), GodotLogSink.Instance);
        var node = new RebuffDriverHost
        {
            Name = "RebuffDriverHost",
            ProcessPriority = 50, // after the session host (-900), play mode (-800) and the retail UI: it reads what they did this frame
            _host = host,
            _ui = ui,
            _services = services,
            _logic = new RebuffHostLogic(options, GodotLogSink.Instance, store),
        };
        main.AddChild(node);
        Log.Info(LogCategory, "auto-buff host installed: " + (options.Auto ? "on at start (--buff-auto on)" : "off at start") + "; the key is Pause", ("buff_options", options.ToString()));
        return node;
    }

    /// <summary>The login name of the account this play session signs in to, or null (logged, the foci formula then) when the secrets file cannot give it; hashed by the cycle, never stored in a log or printed.</summary>
    private static string? TryLoginName(string? accountKey)
    {
        try
        {
            return JourneyLegRunner.LoginName(accountKey);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogCategory, "auto-buff: the account's name could not be read, so the cycles plan on the foci formula (" + e.GetType().Name + ")");
            return null;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_guard.Tripped || _host.Session is not { } session)
        {
            return; // after a fault the driver stays stopped (the game runs on)
        }

        try
        {
            WireKey(session);
            if (!_autopilotLooked)
            {
                // M5 4 F3, 4-R4: PlayMode.Install adds this node and the autopilot's side by side; while the dial is Running or Stopping it owns the buffs (its upkeep casts them on its own margin) and the driver stands down.
                _autopilotLooked = true;
                if (GetTree().Root.FindChild("AutopilotHost", recursive: true, owned: false) is AutopilotHost autopilot)
                {
                    _logic.StandDownWhile = () => autopilot.OwnsBuffs;
                }
            }

            if (_logic.NeedsSpells)
            {
                LoadSpells();
            }

            _logic.Tick(session);
        }
        catch (Exception e) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(e))
        {
            // M5 item 0 c2: reported once; the driver is switched off and released, the game keeps running (no leg to fail here).
            _guard.Catch("_Process", e);
            if (_logic.Driver is { } driver)
            {
                driver.Enabled = false;
            }

            _logic.Stop(); // 0-R9: releases the driver and makes the Pause key a log line only
            Log.Error(LogCategory, "auto-buff: the driver is stopped for this run after the fault above (the game keeps running)");
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        // The game is closing without a log-off: keep what the ledger knows (a log-off saves it in Tick when the phase leaves the world).
        if (!_saved && _host?.Session is { } session)
        {
            _saved = true;
            _logic.Save(session);
        }

        _logic.Dispose();
    }

    /// <summary>The Lifestone key lives on the game screen's hotkeys, which exist while the game screen does: wire the toggle once to each instance.</summary>
    private void WireKey(Lifestone.Client.Session.Contracts.IGameSessionView session)
    {
        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { Hotkeys: { } hotkeys } || ReferenceEquals(hotkeys, _wiredHotkeys))
        {
            return;
        }

        _wiredHotkeys = hotkeys;
        hotkeys.ToggleAutoBuff = () => _logic.Toggle(session);
        Log.Info(LogCategory, "auto-buff: the Pause key is wired to the driver's switch");
    }

    private void LoadSpells()
    {
        if (_loading is null)
        {
            var dats = _services.Dats;
            if (dats is null)
            {
                _logic.SpellsFailed("the game has no dats");
                return;
            }

            // The SpellTable is read on a worker, not in a frame (the buff leg does the same).
            _loading = Task.Factory.StartNew<IReadOnlyDictionary<uint, SpellInfo>>(
                () => SpellTableData.Load(dats).Spells,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Log.Info(LogCategory, "auto-buff: reading the SpellTable on a worker");
            return;
        }

        if (!_loading.IsCompleted)
        {
            return;
        }

        if (_loading.IsFaulted)
        {
            _logic.SpellsFailed(_loading.Exception?.GetBaseException().Message ?? "unknown");
        }
        else
        {
            _logic.SetSpells(_loading.Result);
        }

        _loading = null;
    }
}

/// <summary>The armour ledger's files in a folder of the game's user data (<c>user://armour</c>): written to a temporary file and moved over the old one.</summary>
/// <param name="directory">The folder.</param>
internal sealed class UserDataFiles(string directory) : IArmourLedgerFiles
{
    /// <inheritdoc/>
    public string? ReadText(string name)
    {
        var path = System.IO.Path.Combine(directory, name);
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
    }

    /// <inheritdoc/>
    public void WriteText(string name, string text)
    {
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, name);
        var temporary = path + ".tmp";
        System.IO.File.WriteAllText(temporary, text);
        System.IO.File.Move(temporary, path, overwrite: true);
    }
}
