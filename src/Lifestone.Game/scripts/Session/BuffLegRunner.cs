using System.Globalization;
using System.Security.Cryptography;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Checkout;
using Lifestone.Data.Magic;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=buff</c> (M3 2.1 s3; plan section 4.5; D20, D22, D25, D27; docs/session.md section 30): the game enters BSD Mage on the test account and runs <see cref="BuffScenario"/>'s steps in its own session
/// (the gear, the magic combat mode, the cycle of the profile's 24 buff lines, the forced re-buff pass, non-combat) with the game's own cast visuals, then logs off and quits. A kind of its own, never part of
/// <c>--login-check=all</c> (hooked by one line at the top of <see cref="LoginCheckRunner.Install"/>, as <see cref="ThrowawayLegRunner"/> is). The script is the scenario's; this node walks the character list and the
/// arrival (the session host has already started the login), re-seats the wand, steps the scenario once per frame after the session host and play mode, and reads what only the engine can see: the cast-window
/// counters of the avatar set (<c>AvatarStats.CastWindows/CastScripts/CastSounds</c>, read as deltas around the scenario), the worst frame while a cast window is open and the server sounds that sounded.
/// </summary>
/// <remarks>
/// <para><b>The re-seat is the leg's fixture, not a gear step.</b> ACE sends no sound (F750) for a cast; it sends one for a wield or an unwield (<c>Player_Inventory.cs</c> 306 and 405,
/// <c>Creature_Equipment.cs</c> 360 and 436), for an enchantment's expiry (<c>EnchantmentManager.cs</c> 331) and for an item received. On a ready, buffed mage none of these is certain, so before the magic
/// mode the leg moves the wielded wand to the pack and wields it again in its Held location, both through the Action API, to have the server sound D27 judges (<c>sounds_server&gt;=1</c>). The log says so.</para>
/// <para>The verdict is <see cref="BuffLegVerdict"/>'s (<c>[logincheck] buff done ok=...</c>); the process ends with 0 only when it held. A failure at any stage logs off before it is reported (so ACE does not keep the
/// character in the world for the next run). Per frame the node reads counters and allocates nothing; strings are built at stage changes and at the end.</para>
/// </remarks>
public sealed partial class BuffLegRunner : Node
{
    /// <summary>
    /// The longest the leg may take, in seconds, from its first frame; at the limit it fails, logs off and reports. 600 s, from the live measure and not the scripted one (M3 2.1 fix A, 2.1-R2's part in this file): the live
    /// cycle took 56 s of session time for 26 casts (the scripted ACE's mana economy gave 1,325 s), so the login, the gear, the cycle and the forced pass fit with a wide margin.
    /// </summary>
    public const double WholeCheckSeconds = 600.0;

    /// <summary>How long the log off after a failure or the limit may take, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    /// <summary>
    /// The seconds after the forced pass's cast takes effect (the first script the cast plays on the player, the target effect of a creature buff) at which the three review frames are taken: the effect's particles on the body
    /// (M3 2.1 fix A, 2.1-R9; the first cast window of the cycle, 0.4 s in, showed a small spark from behind). The frames are <c>&lt;path stem&gt;-1.png</c>, <c>-2.png</c> and <c>-3.png</c>.
    /// </summary>
    public static readonly double[] ShotDelaysSeconds = [0.1, 0.4, 0.8];

    /// <summary>The longest, in seconds, the scenario is held for the review frames after the cast's effect (the last frame is 0.8 s in); past it the frames still owed are given up.</summary>
    private const double ShotHoldSeconds = 3.0;

    private const string LogCategory = "logincheck";
    private const string SoundLeg = "buff-leg";
    private const double ListSeconds = 60.0;
    private const double SettleSeconds = 3.0;
    private const double ReseatSeconds = 30.0;
    private const double QuitFlushSeconds = 1.5;
    private const int HiddenFramesBeforeShot = 2;

    private enum Phase
    {
        Loading,
        Login,
        Entering,
        Arriving,
        Settling,
        Reseat,
        Buff,
        LoggingOff,
        Quitting,
    }

    private enum Reseating
    {
        Find,
        ToPack,
        Wielding,
    }

    private SessionHost _host = null!;
    private PlayMode _mode = null!;
    private RetailUiLayer? _ui;
    private Task<(SpellTableData Table, GearKit Kit, double BuildMilliseconds)>? _loading;
    private BuffScenario? _scenario;
    private ActionHandle? _pending;
    private readonly SoundLegTally _sounds = new();
    private Phase _phase = Phase.Loading;
    private Reseating _reseating = Reseating.Find;
    private uint _wand;
    private double _runSeconds;
    private double _phaseStart;
    private string? _failure;
    private string _failedAt = "start";
    private bool _failed;
    private bool _recoveryStarted;
    private double _recoveryStart = double.NaN;
    private double _quitAt = double.NaN;
    private bool _finished;
    private bool _entered;
    private bool _scenarioEnded;

    // The cast counters, read as deltas around the scenario.
    private AvatarSet? _avatars;
    private long _scriptsBefore;
    private long _soundsBefore;
    private long _windowsBefore;
    private long _scriptsAfter;
    private long _soundsAfter;
    private long _windowsAfter;
    private double _castFrameMaxMs = double.NaN;
    private bool _skipNextFrame;

    // The review frames (--play-screenshot=PATH without --play-screenshot-after): three, from the forced pass's cast effect.
    private string? _shotPath;
    private bool _shotBaseSet;
    private long _shotScriptsBase;
    private double _shotEffectAt = double.NaN;
    private int _shotHiddenFrames = -1;
    private int _shotsTaken;
    private bool _shotDone;
    private string _shotNote = "not taken";
    private readonly List<(Node Node, bool WasVisible)> _shotHidden = [];

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the buff leg needs the dats (Main checks for them first)");
        var key = host.Options.Account;
        var runner = new BuffLegRunner
        {
            Name = "BuffLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _ui = ui,
            _shotPath = services.Options.Play.BuffShotPath,
        };

        // The SpellTable (the kit's components and the casts' costs) and the kit are built on a worker thread: the arrival's frames are never spent on them (the login takes seconds anyway). The login name the kit's
        // formulas are customised for (hashed, never stored, logged or printed) is read inside that worker and stays in its locals: the worker returns the kit, so the name is in no field and no result (M3 2.1 fix A, 2.1-R8).
        runner._loading = Task.Factory.StartNew(
            () =>
            {
                var table = SpellTableData.Load(dats);
                var built = System.Diagnostics.Stopwatch.GetTimestamp();
                var kit = GearKit.Build(table.Spells, LoginName(key));
                return (table, kit, System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'buff' installed: the test account enters BSD Mage, the wand is re-seated, the buff scenario runs in the game's session (gear, magic mode, cycle, forced re-buff pass), then the game logs off and quits",
            ("scenario", BuffScenario.ScenarioName),
            ("review_frames", runner._shotPath is not null));
    }

    /// <summary>The login name of the account the run signs in to (the kit hashes it; it is never stored, logged or printed).</summary>
    private static string LoginName(string? accountKey)
    {
        if (!ServerRuntimePaths.TryFind(System.Environment.GetEnvironmentVariable(ServerRuntimePaths.EnvironmentVariable), CheckoutLocator.DefaultSearchRoots(), out var runtime, out var problem))
        {
            throw new InvalidOperationException(problem ?? "the server runtime folder was not found");
        }

        var secrets = File.ReadAllBytes(runtime.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(accountKey, AccountChoice.Default));
            return credentials.Account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        if (_host.Session is not { } session)
        {
            Finish("the session host has no session");
            return;
        }

        StepSounds();
        if (!double.IsNaN(_quitAt))
        {
            if (_runSeconds - _quitAt >= QuitFlushSeconds)
            {
                Finish(null);
            }

            return;
        }

        if (_failed)
        {
            StepRecovery(session);
            return;
        }

        if (_runSeconds > WholeCheckSeconds)
        {
            Fail(session, $"the leg took more than {WholeCheckSeconds:0} s");
            return;
        }

        if (_phase == Phase.Buff)
        {
            SampleCastFrame(delta);
        }

        switch (_phase)
        {
            case Phase.Loading:
                Loading();
                break;
            case Phase.Login:
                Login(session);
                break;
            case Phase.Entering:
                Entering(session);
                break;
            case Phase.Arriving:
                Arriving(session);
                break;
            case Phase.Settling:
                Settling(session);
                break;
            case Phase.Reseat:
                Reseat(session);
                break;
            case Phase.Buff:
                StepScenario(session);
                break;
            case Phase.LoggingOff:
                LoggingOff(session);
                break;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => RestoreDeveloperHud();

    // ------------------------------------------------------------------------------------------------ the way in

    private void Loading()
    {
        if (_loading is not { IsCompleted: true })
        {
            return;
        }

        if (_loading.IsFaulted)
        {
            Fail(null, "the spell table or the account could not be read: " + _loading.Exception?.GetBaseException().Message);
            return;
        }

        Go(Phase.Login);
    }

    private void Login(IGameSessionView session)
    {
        var state = session.State;
        if (BuffLegPhases.Failed(state))
        {
            Fail(session, "login: the connection failed (" + (state.FailureMessage ?? "no reason given") + ")");
            return;
        }

        if (!BuffLegPhases.AtCharacterSelect(state))
        {
            if (_runSeconds - _phaseStart > ListSeconds)
            {
                Fail(session, string.Create(CultureInfo.InvariantCulture, $"login: the character list did not come within {ListSeconds:0} s (phase {state.Phase}, data check {state.DataCheck})"));
            }

            return;
        }

        if (state.DataCheck != DataCheckState.Complete)
        {
            Fail(session, "login: the character list came without a completed data check (" + state.DataCheck + ")");
            return;
        }

        foreach (var c in state.Characters.Entries)
        {
            if (!string.Equals(c.Name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (c.PendingDeletion)
            {
                Fail(session, "choosing: " + MageTemplate.CharacterName + " is pending deletion (restore it first)");
                return;
            }

            session.Actions.SelectCharacter(c.Guid, ActionSource.Host);
            _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
            Log.Info(LogCategory, "buff: entering the existing character " + MageTemplate.CharacterName);
            Go(Phase.Entering);
            return;
        }

        Fail(session, "choosing: " + MageTemplate.CharacterName + " is not in the character list (run --scenario=mage-build first; the buff leg creates nobody)");
    }

    private void Entering(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } outcome })
        {
            return;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            Fail(session, "entering the world: " + Describe(outcome));
            return;
        }

        _entered = true;
        Go(Phase.Arriving);
    }

    private void Arriving(IGameSessionView session)
    {
        var state = session.State;
        if (state.Self.Travel == SelfTravel.Present)
        {
            Go(Phase.Settling);
            return;
        }

        if (BuffLegPhases.Failed(state) || BuffLegPhases.AtCharacterSelect(state))
        {
            Fail(session, "arriving: the session left the world before the player was unhidden (phase " + state.Phase + ")");
            return;
        }

        if (_runSeconds - _phaseStart > LoginScenario.DefaultArrivalSeconds)
        {
            Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {LoginScenario.DefaultArrivalSeconds:0} s (travel {state.Self.Travel})"));
        }
    }

    private void Settling(IGameSessionView session)
    {
        var state = session.State;
        if (!BuffLegPhases.InWorld(state) || state.Self.Travel != SelfTravel.Present || state.Self.Hidden)
        {
            Fail(session, "settling: the player did not stay in the world (phase " + state.Phase + ", travel " + state.Self.Travel + ")");
            return;
        }

        if (_runSeconds - _phaseStart >= SettleSeconds)
        {
            _reseating = Reseating.Find;
            Go(Phase.Reseat);
        }
    }

    // ------------------------------------------------------------------------------------------------ the wand re-seat (the leg's fixture for D27)

    private void Reseat(IGameSessionView session)
    {
        switch (_reseating)
        {
            case Reseating.Find:
                FindWand(session, out var wielded);
                if (_wand == 0)
                {
                    Log.Info(LogCategory, "buff: no wand is held yet; the gear step creates and wields it (its wield sends the server sound)");
                    BeginScenario(session);
                    return;
                }

                if (wielded)
                {
                    Log.Info(LogCategory, "buff: re-seating the wielded wand (to the pack, then wielded in its Held location again): ACE sends a server sound on a wield and none on a cast or a login, so this is the leg's fixture for sounds_server>=1 (D27), not a gear step");
                    _pending = session.Actions.MoveToPack(_wand, 0, 0, ActionSource.Host);
                    _reseating = Reseating.ToPack;
                }
                else
                {
                    Log.Info(LogCategory, "buff: the wand is in the pack; wielding it in its Held location (the wield sends the server sound D27 judges)");
                    _pending = session.Actions.Wield(_wand, EquipMask.Held, ActionSource.Host);
                    _reseating = Reseating.Wielding;
                }

                _phaseStart = _runSeconds;
                break;

            case Reseating.ToPack:
                if (_pending is { Outcome: { } packed })
                {
                    _pending = null;
                    if (packed.Kind != ActionOutcomeKind.Confirmed)
                    {
                        Fail(session, "reseat: the wand was not moved to the pack: " + Describe(packed));
                        return;
                    }

                    _pending = session.Actions.Wield(_wand, EquipMask.Held, ActionSource.Host);
                    _reseating = Reseating.Wielding;
                    _phaseStart = _runSeconds;
                }
                else if (_runSeconds - _phaseStart > ReseatSeconds)
                {
                    Fail(session, "reseat: the wand's move to the pack was not answered within " + ReseatSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
                }

                break;

            case Reseating.Wielding:
                if (_pending is { Outcome: { } wielded2 })
                {
                    _pending = null;
                    if (wielded2.Kind != ActionOutcomeKind.Confirmed)
                    {
                        Fail(session, "reseat: the wand was not wielded again: " + Describe(wielded2));
                        return;
                    }

                    Log.Info(LogCategory, "buff: the wand is wielded again; the buff scenario next");
                    BeginScenario(session);
                }
                else if (_runSeconds - _phaseStart > ReseatSeconds)
                {
                    Fail(session, "reseat: the wand's wield was not answered within " + ReseatSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
                }

                break;
        }
    }

    /// <summary>Finds the wand: the item in the Held slot (<paramref name="wielded"/>), else a wand weenie in the pack or a side pack; <see cref="_wand"/> is 0 when there is none.</summary>
    private void FindWand(IGameSessionView session, out bool wielded)
    {
        var state = session.State;
        var inventory = state.Self.Inventory;
        wielded = false;
        _wand = 0;
        if (inventory.EquippedAt((uint)EquipMask.Held) is { } held)
        {
            _wand = held;
            wielded = true;
            return;
        }

        var pack = inventory.Pack;
        for (var i = 0; i < pack.Count && _wand == 0; i++)
        {
            _wand = IsWand(state, pack[i].Guid) ? pack[i].Guid : 0;
        }

        var side = inventory.SidePacks;
        for (var i = 0; i < side.Count && _wand == 0; i++)
        {
            var inside = inventory.ItemsIn(side[i].Guid);
            for (var j = 0; j < inside.Count && _wand == 0; j++)
            {
                _wand = IsWand(state, inside[j].Guid) ? inside[j].Guid : 0;
            }
        }
    }

    private static bool IsWand(GameState state, uint guid) => state.Objects.TryGet(guid, out var o) && o.Weenie.WeenieClassId == GearKit.Wand.Wcid;

    // ------------------------------------------------------------------------------------------------ the buff scenario

    private void BeginScenario(IGameSessionView session)
    {
        var (table, kit, buildMilliseconds) = _loading!.Result;
        Log.Info(LogCategory, "buff: the gear kit was built on the loading worker, off the game thread", ("build_ms", buildMilliseconds));
        _scenario = new BuffScenario(kit, table.Spells, new BuffOptions(), GodotLogSink.Instance, LoginScenario.DefaultArrivalSeconds, endSession: false);
        _avatars = _mode.Avatars;
        if (_avatars is { } avatars)
        {
            var stats = avatars.Stats;
            (_scriptsBefore, _soundsBefore, _windowsBefore) = (stats.CastScripts, stats.CastSounds, stats.CastWindows);
        }
        else
        {
            Log.Warn(LogCategory, "buff: the avatar set is not running, so the cast window counters read 0");
        }

        Go(Phase.Buff);
    }

    private void StepScenario(IGameSessionView session)
    {
        StepShot();
        if (ShotsOwed)
        {
            return; // the scenario is held (at most ShotHoldSeconds) while the three review frames are taken, so the mage is still in its cast and the HUD stays hidden for them
        }

        switch (_scenario!.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                ReadCastCounters();
                Fail(session, _scenario.Failure ?? "the scenario failed");
                break;
            case ScenarioStatus.Succeeded:
                _scenarioEnded = true;
                ReadCastCounters();
                _pending = session.Actions.LogOff(ActionSource.Host);
                Go(Phase.LoggingOff);
                break;
        }
    }

    private void ReadCastCounters()
    {
        if (_avatars is { } avatars)
        {
            var stats = avatars.Stats;
            (_scriptsAfter, _soundsAfter, _windowsAfter) = (stats.CastScripts, stats.CastSounds, stats.CastWindows);
        }
        else
        {
            (_scriptsAfter, _soundsAfter, _windowsAfter) = (_scriptsBefore, _soundsBefore, _windowsBefore);
        }
    }

    /// <summary>The slowest frame while a cast window is open (the engine's delta is the previous frame's whole time); the frame after the mid-cast capture is left out (the read-back stalls it).</summary>
    private void SampleCastFrame(double delta)
    {
        if (_skipNextFrame)
        {
            _skipNextFrame = false;
            return;
        }

        if (_avatars is { CastWindowOpen: true })
        {
            var ms = delta * 1000.0;
            if (double.IsNaN(_castFrameMaxMs) || ms > _castFrameMaxMs)
            {
                _castFrameMaxMs = ms;
            }
        }
    }

    /// <summary>The sound counters of the leg: every frame from the Enter to the scenario's end belongs to one leg (frames before the Enter belong to none).</summary>
    private void StepSounds()
    {
        var inLeg = _entered && !_scenarioEnded && !_failed;
        _sounds.Step(inLeg ? SoundLeg : null, AudioService.ReadSources(_mode.HookSounds, _ui?.SoundStats, AmbientSoundNode.Current?.Player));
    }

    // ------------------------------------------------------------------------------------------------ the mid-cast frame

    /// <summary>True from the forced pass's cast effect until the third frame is taken (or given up): the scenario waits.</summary>
    private bool ShotsOwed => !_shotDone && !double.IsNaN(_shotEffectAt);

    /// <summary>
    /// <c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c> (M3 2.1 fix A, 2.1-R9): the frames are of the <b>first cast of the cycle or the forced pass that plays a script on the player</b> (a buff on the player, with its target
    /// effect), not the first cast window. The effect time is the first script a cast plays on the player during the cycle or the forced pass (<see cref="AvatarStats.CastScripts"/> rises); at that frame the developer HUD is
    /// hidden, and at the effect time plus each of <see cref="ShotDelaysSeconds"/> (after at least <see cref="HiddenFramesBeforeShot"/> hidden frames) the viewport is saved (800x600 in a check run) to
    /// <c>&lt;stem&gt;-1.png</c>, <c>-2.png</c> and <c>-3.png</c> and each path is logged with its delay; after the third the HUD is shown again. The retail UI and the placeholder notices stay; the frames are
    /// derived from the dats and never committed. A forced pass that casts no self buff (no script on the player) says so in the log and takes no frame.
    /// </summary>
    private void StepShot()
    {
        if (_shotPath is null || _shotDone || _avatars is null || _scenario is null)
        {
            return;
        }

        if (double.IsNaN(_shotEffectAt))
        {
            // The frames are of the first cast of the cycle or the forced pass that plays a script on the player (a self buff's target effect). The gate's first run showed
            // that the forced pass alone can play none there (its line was a wand Aura, whose effect plays on the wand), so both stages are watched (M3 2.1 lead fix).
            if (!string.Equals(_scenario.Stage, "cycle", StringComparison.Ordinal) && !string.Equals(_scenario.Stage, "rebuff", StringComparison.Ordinal))
            {
                if (_shotBaseSet)
                {
                    // The cycle and the forced pass are over and no cast effect was seen on the player.
                    _shotDone = true;
                    _shotNote = "no cast of the cycle or the forced pass played a script on the player, so no frame was taken";
                    Log.Warn(LogCategory, "buff: " + _shotNote);
                }

                return;
            }

            if (!_shotBaseSet)
            {
                _shotBaseSet = true;
                _shotScriptsBase = _avatars.Stats.CastScripts;
                return;
            }

            if (_avatars.Stats.CastScripts > _shotScriptsBase)
            {
                _shotEffectAt = _runSeconds;
                _shotHiddenFrames = 0;
                HideDeveloperHud();
            }

            return;
        }

        _shotHiddenFrames++;
        var delay = ShotDelaysSeconds[_shotsTaken];
        if (_runSeconds < _shotEffectAt + delay || _shotHiddenFrames < HiddenFramesBeforeShot)
        {
            if (_runSeconds - _shotEffectAt > ShotHoldSeconds)
            {
                GiveUpShots("the frames owed were not taken within " + ShotHoldSeconds.ToString("0", CultureInfo.InvariantCulture) + " s of the cast effect");
            }

            return;
        }

        var path = ShotPath(_shotPath, _shotsTaken + 1);
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "buff-cast"), ("after_effect_s", delay), ("cast_window_open", _avatars.CastWindowOpen), ("developer_hud", "hidden"));
        _skipNextFrame = true;
        Log.Info(LogCategory, (saved ? "buff: review frame " : "buff: the review frame could not be saved ") + (_shotsTaken + 1).ToString(CultureInfo.InvariantCulture) + " of " + ShotDelaysSeconds.Length.ToString(CultureInfo.InvariantCulture) + " at the cast effect + " + delay.ToString("0.0", CultureInfo.InvariantCulture) + " s: " + path);
        if (!saved)
        {
            GiveUpShots("a frame could not be saved");
            return;
        }

        if (++_shotsTaken == ShotDelaysSeconds.Length)
        {
            _shotDone = true;
            _shotNote = "taken: " + ShotDelaysSeconds.Length.ToString(CultureInfo.InvariantCulture) + " frames of the forced pass's cast";
            RestoreDeveloperHud();
        }
    }

    private void GiveUpShots(string why)
    {
        _shotDone = true;
        _shotNote = _shotsTaken.ToString(CultureInfo.InvariantCulture) + " of " + ShotDelaysSeconds.Length.ToString(CultureInfo.InvariantCulture) + " frames taken: " + why;
        Log.Warn(LogCategory, "buff: " + _shotNote);
        RestoreDeveloperHud();
    }

    /// <summary>The path of frame <paramref name="number"/>: the asked path's stem, <c>-N</c> and the <c>.png</c> extension.</summary>
    internal static string ShotPath(string path, int number)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileNameWithoutExtension(path) + "-" + number.ToString(CultureInfo.InvariantCulture) + ".png";
        return string.IsNullOrEmpty(directory) ? name : System.IO.Path.Combine(directory, name);
    }

    private void HideDeveloperHud()
    {
        _shotHidden.Clear();
        foreach (var name in PlayMode.PlayShotDeveloperHudNames)
        {
            switch (GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    _shotHidden.Add((layer, layer.Visible));
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    _shotHidden.Add((item, item.Visible));
                    item.Visible = false;
                    break;
            }
        }
    }

    private void RestoreDeveloperHud()
    {
        foreach (var (node, wasVisible) in _shotHidden)
        {
            if (!IsInstanceValid(node))
            {
                continue;
            }

            switch (node)
            {
                case CanvasLayer layer:
                    layer.Visible = wasVisible;
                    break;
                case CanvasItem item:
                    item.Visible = wasVisible;
                    break;
            }
        }

        _shotHidden.Clear();
    }

    // ------------------------------------------------------------------------------------------------ the way out

    private void LoggingOff(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } outcome })
        {
            return;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            Fail(null, "logging off: " + Describe(outcome));
            return;
        }

        _ = session.Actions.Quit(ActionSource.Host);
        _quitAt = _runSeconds;
        _phase = Phase.Quitting;
    }

    /// <summary>Records the first failure and logs off before it is reported, so ACE does not keep the character in the world for the next run.</summary>
    private void Fail(IGameSessionView? session, string why)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        _failure = why;
        _failedAt = _phase.ToString();
        ReadCastCounters();
        Log.Error(LogCategory, "the buff leg failed", ("phase", _failedAt), ("why", why));
        if (session is not null && _entered && BuffLegPhases.InWorld(session.State) && _phase != Phase.LoggingOff)
        {
            Log.Info(LogCategory, "buff: logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _recoveryStarted = true;
            _recoveryStart = _runSeconds;
            return;
        }

        Finish(why);
    }

    private void StepRecovery(IGameSessionView session)
    {
        if (_recoveryStarted && _pending is { Outcome: { } left })
        {
            _pending = null;
            Log.Info(LogCategory, "buff: the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            _ = session.Actions.Quit(ActionSource.Host);
            _quitAt = _runSeconds;
            return;
        }

        if (!_recoveryStarted || _runSeconds - _recoveryStart > RecoverySeconds)
        {
            Finish(_failure);
        }
    }

    private void Go(Phase next)
    {
        _phase = next;
        _phaseStart = _runSeconds;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    private void Finish(string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        RestoreDeveloperHud();
        var counts = _sounds.Of(SoundLeg);
        var scenario = _scenario;
        var result = new BuffLegScenarioResult(
            scenario is not null && _scenarioEnded && !_failed,
            _failure ?? why ?? scenario?.Failure,
            scenario?.Gear.VerdictFields() ?? "gear=failed worn=0",
            scenario?.CycleResult,
            scenario?.ForcedResult);
        var measures = new BuffLegMeasures(
            Math.Max(0, _scriptsAfter - _scriptsBefore),
            Math.Max(0, _soundsAfter - _soundsBefore),
            Math.Max(0, _windowsAfter - _windowsBefore),
            double.IsNaN(_castFrameMaxMs) ? null : _castFrameMaxMs,
            counts.Server);
        var outcome = BuffLegVerdict.Compose(result, measures);
        if (!outcome.Ok)
        {
            Log.Error(LogCategory, "the buff leg failed", ("phase", _failedAt), ("why", string.Join("; ", outcome.Problems)));
        }

        Log.Info(LogCategory, outcome.Line);
        Log.Info(
            LogCategory,
            "buff detail",
            ("seconds", _runSeconds),
            ("sounds_server_handed", SoundSourceCounts.Value(counts.ServerHanded)),
            ("sounds_hooks", SoundSourceCounts.Value(counts.Hooks)),
            ("sounds_hooks_handed", SoundSourceCounts.Value(counts.HooksHanded)),
            ("sounds_ui", SoundSourceCounts.Value(counts.Ui)),
            ("review_frames", _shotPath is null ? "not asked for" : _shotNote),
            ("scenario_verdict", scenario?.VerdictFields() ?? "n/a"));
        Log.FlushToGodot();
        GetTree().Quit(outcome.Ok ? 0 : 1);
    }
}
