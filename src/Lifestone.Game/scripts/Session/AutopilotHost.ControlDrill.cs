using Godot;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The control drill's host half (M5 7 D145, <c>--autopilot-control-drill=DIR</c>): the drill itself is <see cref="ControlDrill"/> (engine-free, every rule and verdict); this gives it Godot's hands: Move Forward's key through Godot's input
/// (<c>Input.ParseInputEvent</c>, the path a person's key takes: the retail router, the movement bindings and the key lock), lines typed into the main chat's entry and sent (<see cref="TypeInChat"/>), the player's body, the main chat's text
/// and a frame of the window into DIR as the soak frames are saved.
/// </summary>
public sealed partial class AutopilotHost
{
    /// <summary>The drill (null without <c>--autopilot-control-drill</c>).</summary>
    public ControlDrill? Drill => _drill;

    /// <summary>Makes the drill for <paramref name="directory"/> (the output-path rule; a refused folder is logged and no drill runs).</summary>
    private void SetupDrill(string directory)
    {
        var resolved = ScreenshotWriter.Resolve(directory, LogCategory);
        if (resolved is null)
        {
            Log.Error(LogCategory, ControlDrill.Prefix + " --autopilot-control-drill: the folder cannot be used (see the line above); no drill runs");
            return;
        }

        _drill = new ControlDrill(new GodotDrillHands(this, resolved), GodotLogSink.Instance);
        Log.Info(LogCategory, ControlDrill.Prefix + " on; frames go to " + resolved);
    }

    /// <summary>The Godot hands of the drill.</summary>
    private sealed class GodotDrillHands(AutopilotHost host, string directory) : IControlDrillHands
    {
        private Key _held;

        public CellFrame? Position => PlayerController.Current is { Placing: false } controller && controller.Position.CellId != 0 ? controller.Position : null;

        public string ChatText => host._ui?.Screens?.Flow.CurrentScreen is GamePlayScreen { Chat.Main: { } main } ? main.LogText : string.Empty;

        public bool Forward(bool down)
        {
            if (down)
            {
                if (AutopilotTestHandOff.MovementKeyOf(host._ui?.Manager?.Input.Bindings) is not { } key)
                {
                    return false;
                }

                _held = key.Key;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = _held, Keycode = _held, Pressed = true });
                Log.Info(LogCategory, ControlDrill.Prefix + " pressed " + key.Name + " through Godot's input");
                return true;
            }

            if (_held == Key.None)
            {
                return false;
            }

            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = _held, Keycode = _held, Pressed = false });
            Log.Info(LogCategory, ControlDrill.Prefix + " released " + OS.GetKeycodeString(_held) + " through Godot's input");
            _held = Key.None;
            return true;
        }

        public bool Type(string line) => TypeInChat(host._ui, line);

        public void Frame(string name)
        {
            var path = System.IO.Path.Combine(directory, name + ".png");
            var readback = ScreenshotWriter.SaveInBackgroundQuietly(host.GetViewport(), path, LogCategory, ok => Log.Info(LogCategory, ControlDrill.Prefix + (ok ? " frame saved " : " frame NOT saved ") + name + ".png"));
            if (readback < 0)
            {
                Log.Warn(LogCategory, ControlDrill.Prefix + " frame skipped (the window could not be read back): " + name + ".png");
            }
        }
    }
}

/// <summary>The chat's /stop and /auto as two delegates (M5 7 D145): the play host's and the login-check leg's own.</summary>
internal sealed class DialControls(Func<string> stop, Func<string> auto) : IAutopilotChatControls
{
    /// <inheritdoc/>
    public string Stop() => stop();

    /// <inheritdoc/>
    public string Auto() => auto();
}
