using System.Globalization;
using Godot;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.RetailChecks;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=keep</c> (M5 6 k2, D32 and D33; docs/session.md): one Godot process, two logins of Lifestone Throwaway on the tools test account. Login 1 flips one character option
/// (<see cref="KeepAcrossLogins.OptionName"/>) and moves one floating window, then logs off to the character list; login 2 enters again, reads the option from the new PlayerDescription and the
/// window's place from the layout the shell applied, puts the option back and logs off. The script is <see cref="KeepAcrossLogins"/>; this node steps it once per frame after the session host and play
/// mode, and is the <see cref="IKeepShell"/> over the retail UI's game screen: no automation click moves a window, the move is the layout keeper's own seam (<c>UiElement.MoveTo</c>; M3 D29).
/// </summary>
/// <remarks>
/// The verdict is <c>[logincheck] keep options_kept=&lt;yes|no&gt; layout_kept=&lt;yes|no&gt; option=&lt;name&gt; window=&lt;tag&gt;</c> (report-only: a new judge) and then
/// <c>[logincheck] keep done ran=&lt;true|false&gt; held=&lt;true|false&gt; problems=&lt;n&gt;</c>. The process ends 0 when the script ran to its end (<c>ran</c>); <c>held</c> is the two yes values. Nothing in the
/// lines names a character or the account. It is a kind of its own, never part of <c>--login-check=all</c>.
/// </remarks>
public sealed partial class KeepLegRunner : Node, IKeepShell
{
    /// <summary>The longest the check may take, in seconds.</summary>
    public const double WholeCheckSeconds = 300.0;

    /// <summary>How long a log off asked for at the limit may take, in seconds.</summary>
    public const double RecoverySeconds = 30.0;

    private const string LogCategory = "logincheck";
    private const double QuitFlushSeconds = 1.5;

    // The first of these the shell has is the one moved: always on the screen in the game shell (the radar, the vitals, the chat).
    private static readonly string[] Preferred = ["<RADA>", "<VITS>", "<CHAT>", "<TBAR>"];

    private SessionHost _host = null!;
    private RetailUiLayer _ui = null!;
    private KeepAcrossLogins _scenario = null!;
    private double _runSeconds;
    private double _doneAt = double.NaN;
    private double _limitAt = double.NaN;
    private bool _finished;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        if (ui is null)
        {
            Log.Error(LogCategory, "the keep check reads the retail UI's layout, and the retail UI is off (--retail-ui=off); it cannot run");
            main.GetTree().Quit(1);
            return;
        }

        var dats = services.Dats ?? throw new InvalidOperationException("the keep check creates Lifestone Throwaway when the list lacks it, which needs the dats");
        var runner = new KeepLegRunner
        {
            Name = "KeepLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI
            _host = host,
            _ui = ui,
        };
        runner._scenario = new KeepAcrossLogins(
            runner,
            message => Log.Info(LogCategory, "keep: " + message),
            "Lifestone Throwaway",
            () => HeadlessCharacter.Intent("Lifestone Throwaway", HeadlessCharacter.RetailSkillClasses(dats)))
        {
            HostLogsIn = true, // the session host logs in when it starts
        };
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'keep' installed: the tools account's Lifestone Throwaway flips one option and moves one window, logs off, enters again and reads both back, then the game quits",
            ("scenario", KeepAcrossLogins.ScenarioName),
            ("option", KeepAcrossLogins.OptionName));
    }

    // ---- IKeepShell over the game screen ----

    private GamePlayScreen? Screen => _ui.Screens?.Flow.CurrentScreen as GamePlayScreen;

    /// <inheritdoc/>
    public bool ShellReady => Screen is { IsBuilt: true, LayoutLoaded: true, Root: not null };

    /// <inheritdoc/>
    public KeepMove? MoveOneWindow()
    {
        if (Screen is not { Root: { } root })
        {
            return null;
        }

        foreach (var tag in Preferred)
        {
            if (ScreenLayoutFile.Windows.FirstOrDefault(w => w.Tag == tag) is not { Tag: not null } window || root.GetChildRecursive(window.ElementId) is not { } element)
            {
                continue;
            }

            var box = element.Box;
            var before = new KeepWindow(tag, box.X0, box.Y0, box.Width, box.Height);
            // A fixed place (6-R6), inside the root; the alternate when the window is already at the first (an earlier run left it there).
            var (x, y) = box.X0 == KeepAcrossLogins.MovePlaceX && box.Y0 == KeepAcrossLogins.MovePlaceY
                ? (KeepAcrossLogins.MovePlaceAlternate, KeepAcrossLogins.MovePlaceAlternate)
                : (KeepAcrossLogins.MovePlaceX, KeepAcrossLogins.MovePlaceY);
            x = Math.Clamp(x, 0, Math.Max(0, root.Box.Width - box.Width));
            y = Math.Clamp(y, 0, Math.Max(0, root.Box.Height - box.Height));
            element.MoveTo(x, y);
            var moved = element.Box;
            var after = new KeepWindow(tag, moved.X0, moved.Y0, moved.Width, moved.Height);
            if (!after.SamePlace(before))
            {
                return new KeepMove(before, after);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void PutBack(KeepWindow original)
    {
        if (Screen is { Root: { } root } && ScreenLayoutFile.Windows.FirstOrDefault(w => w.Tag == original.Tag) is { Tag: not null } window && root.GetChildRecursive(window.ElementId) is { } element)
        {
            element.MoveTo(original.X, original.Y);
        }
    }

    /// <inheritdoc/>
    public KeepLayoutRead LayoutRead => Screen is { } screen
        ? new KeepLayoutRead(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(screen), screen.LastLayoutLoad.Found, screen.LastLayoutLoad.WindowsApplied, screen.LayoutReads)
        : default;

    /// <inheritdoc/>
    public KeepWindow? Read(string tag)
    {
        if (Screen is not { Root: { } root } || ScreenLayoutFile.Windows.FirstOrDefault(w => w.Tag == tag) is not { Tag: not null } window || root.GetChildRecursive(window.ElementId) is not { } element)
        {
            return null;
        }

        var box = element.Box;
        return new KeepWindow(tag, box.X0, box.Y0, box.Width, box.Height);
    }

    // ---- the node ----

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            StepFrame(delta);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error(LogCategory, "the keep check faulted", ("stage", _scenario.Stage), ("error", e.GetType().Name + ": " + e.Message));
            Finish("the check faulted: " + e.GetType().Name);
        }
    }

    private void StepFrame(double delta)
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

        if (!double.IsNaN(_doneAt))
        {
            if (_runSeconds - _doneAt >= QuitFlushSeconds)
            {
                Finish(null);
            }

            return;
        }

        if (double.IsNaN(_limitAt) && _runSeconds > WholeCheckSeconds)
        {
            _limitAt = _runSeconds;
            Log.Error(LogCategory, "the keep check took too long; logging off", ("stage", _scenario.Stage));
            if (KeepAcrossLogins.InWorld(session))
            {
                session.Actions.LogOff(ActionSource.Host);
            }
        }

        if (!double.IsNaN(_limitAt))
        {
            if (!KeepAcrossLogins.InWorld(session) || _runSeconds - _limitAt > RecoverySeconds)
            {
                Finish($"the check took more than {WholeCheckSeconds:0} s");
            }

            return;
        }

        switch (_scenario.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                Finish(_scenario.Failure ?? "the script failed");
                break;
            case ScenarioStatus.Succeeded:
                _doneAt = _runSeconds;
                break;
        }
    }

    private void Finish(string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var inv = CultureInfo.InvariantCulture;
        var ran = why is null && _scenario.SecondLoginRead && _scenario.Failure is null;
        if (!ran)
        {
            Log.Error(LogCategory, "the keep check did not run to its end", ("stage", _scenario.Stage), ("why", why ?? _scenario.Failure ?? "login 2 was not read"));
        }

        Log.Info(LogCategory, _scenario.Verdict);
        Log.Info(LogCategory, string.Create(inv, $"keep done ran={(ran ? "true" : "false")} held={(_scenario.Ok ? "true" : "false")} {_scenario.VerdictFields()}"));
        foreach (var problem in _scenario.Problems)
        {
            Log.Info(LogCategory, "keep problem: " + problem);
        }

        Log.Info(LogCategory, "keep detail", ("seconds", _runSeconds), ("stage", _scenario.Stage));
        Log.FlushToGodot();
        GetTree().Quit(ran ? 0 : 1);
    }
}
