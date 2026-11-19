using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;
using Lifestone.Game.Sessions;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--video-timer[=LEVEL]</c> (M6's hero video, off by default): a clock under the indicators window at the top left, plain text with a thin outline (no box, no background): "Time to Lvl 80: " and H:MM:SS since the character was
/// created (her <c>CreationTimestamp</c>, PropertyInt 0x62, as the character information page reads it), so a restart of the game keeps counting. It stops, and logs the moment once, when her level reaches
/// LEVEL (default <see cref="DefaultLevel"/>). Hidden until the game screen and her creation time are known; a character description without a creation time is a loud TODO and no clock. Lifestone's own
/// text for videos, not a retail element.
/// <para>M6 onscreen start (<c>--login-check=climber-start</c>, the run that creates her in the window): before her creation time is known the clock reads 0:00:00, at the top left of the character screen and character creation
/// (and under the indicators until the description arrives), so the take shows the clock from its first screens; once she is in the world it counts from her creation, as it always does.</para>
/// </summary>
public sealed partial class VideoTimer : CanvasLayer
{
    /// <summary>The clock's place on the screens before the world, in window pixels from the top left (Lifestone's own, clear of retail's character list and character creation's title).</summary>
    public static readonly Vector2 ScreensPosition = new(8f, 6f);

    private readonly bool _beforeBirth;
    private bool _bornKnown;
    private bool _screensLogged;
    /// <summary>The level the clock stops at without a value (M6 ends at 80).</summary>
    public const int DefaultLevel = 80;

    /// <summary>Above the retail UI (10), below the sync marker (127).</summary>
    public const int LayerSlot = 11;

    /// <summary>The creation time's property (PropertyInt.CreationTimestamp).</summary>
    public const uint CreationTimestampProperty = 0x62;

    private readonly int _level;
    private Label _label = null!;
    private long? _stoppedSeconds;
    private bool _missingLogged;
    private bool _startLogged;

    private VideoTimer(int level, bool beforeBirth)
    {
        _level = level;
        _beforeBirth = beforeBirth;
    }

    /// <summary>The level the run asked the clock to stop at, or null when <c>--video-timer</c> is not given (a bad value is a warning and the default).</summary>
    public static int? Requested()
    {
        foreach (var argument in CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()))
        {
            if (argument.Name != "--video-timer")
            {
                continue;
            }

            if (string.IsNullOrEmpty(argument.Value))
            {
                return DefaultLevel;
            }

            if (int.TryParse(argument.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level is >= 2 and <= 275)
            {
                return level;
            }

            Log.Warn("video", "--video-timer takes no value or a level from 2 to 275; the default is used", ("value", argument.Value), ("default", DefaultLevel));
            return DefaultLevel;
        }

        return null;
    }

    /// <summary>Adds the clock to <paramref name="main"/>.</summary>
    /// <param name="main">The main scene.</param>
    /// <param name="level">The level it stops at.</param>
    /// <param name="beforeBirth">True when the run creates the character in this window (<c>--login-check=climber-start</c>): 0:00:00 shows on the screens before she exists.</param>
    public static void Attach(Node main, int level, bool beforeBirth = false)
    {
        ArgumentNullException.ThrowIfNull(main);
        main.AddChild(new VideoTimer(level, beforeBirth) { Name = "VideoTimer", Layer = LayerSlot });
        Log.Info("video", "--video-timer: a clock since the character's creation under the indicators, stopping at the level", ("level", level), ("before_birth", beforeBirth ? "0:00:00 on the screens until her creation time is known" : "hidden until the game screen"));
    }

    /// <summary>The caption before the time (the owner's ruling of 2026-11-18: the clock names its target level): <see cref="Caption"/> of 80 reads Time to Lvl 80 and a colon, the level the run's.</summary>
    public static string Caption(int level) => string.Create(CultureInfo.InvariantCulture, $"Time to Lvl {level}: ");

    /// <summary>The clock's text for <paramref name="seconds"/>: H:MM:SS, the hours unbounded.</summary>
    public static string Format(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    public override void _Ready()
    {
        _label = new Label { Name = "Clock", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (RetailUiLayer.Current is not { } ui || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { Indicators: { } indicators }
            || SessionHost.Instance?.Session is not { } session)
        {
            if (_beforeBirth && !_bornKnown && RetailUiLayer.Current is { Screens.Flow.CurrentScreen: not null } screens)
            {
                ShowZero(screens, ScreensPosition, true); // the character screen and character creation: she does not exist yet
                return;
            }

            _label.Visible = false;
            return;
        }

        var qualities = session.State.Self.Qualities;
        if (!qualities.TryGetInt(CreationTimestampProperty, out var born) || born <= 0)
        {
            if (_beforeBirth && !_bornKnown)
            {
                var under = new Vector2((indicators.Box.X0 * ui.Scale) + 6, ((indicators.Box.Y0 + indicators.Box.Height) * ui.Scale) + 4);
                ShowZero(ui, under, indicators.Visible); // in the world before her description: still 0:00:00, where the clock will count
                return;
            }

            _label.Visible = false;
            if (!_missingLogged && qualities.Level > 0)
            {
                _missingLogged = true;
                Log.Error("video", "TODO(m6-video-timer-birth): the character's description has no CreationTimestamp, so --video-timer shows no clock");
            }

            return;
        }

        _bornKnown = true;
        var now = (long)Time.GetUnixTimeFromSystem();
        var seconds = _stoppedSeconds ?? now - born;
        if (!_startLogged)
        {
            _startLogged = true;
            Log.Info("video", "video timer: shown", ("created_unix", born), ("since", Format(seconds)), ("level", qualities.Level), ("stops_at", _level));
        }

        if (_stoppedSeconds is null && qualities.Level >= _level)
        {
            _stoppedSeconds = seconds;
            Log.Info("video", "video timer: stopped at the level", ("level", qualities.Level), ("time", Format(seconds)), ("seconds", seconds));
        }

        // Under the indicators window, at its left edge, in window pixels (the UI's pixels times its whole-number scale); the text grows with the window's height, 22 px at 1080.
        var scale = ui.Scale;
        var box = indicators.Box;
        var height = ui.WindowSize.Y;
        var size = Math.Max(14, (int)Math.Round(height / 49.0));
        _label.AddThemeFontSizeOverride("font_size", size);
        _label.Position = new Vector2((box.X0 * scale) + 6, ((box.Y0 + box.Height) * scale) + 4);
        _label.Text = Caption(_level) + Format(seconds);
        _label.Visible = indicators.Visible;
    }

    /// <summary>The clock at 0:00:00 (the climber start, before her creation time is known), at <paramref name="position"/> in window pixels, in the size the counting clock uses.</summary>
    private void ShowZero(RetailUiLayer ui, Vector2 position, bool visible)
    {
        if (!_screensLogged)
        {
            _screensLogged = true;
            Log.Info("video", "video timer: 0:00:00 before the character exists (the climber start creates her in this window); it counts from her creation once she is in the world", ("stops_at", _level));
        }

        var size = Math.Max(14, (int)Math.Round(ui.WindowSize.Y / 49.0));
        _label.AddThemeFontSizeOverride("font_size", size);
        _label.Position = position;
        _label.Text = Caption(_level) + Format(0);
        _label.Visible = visible;
    }
}
