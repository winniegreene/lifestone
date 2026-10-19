using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Sessions;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--video-sync-marker</c>, for videos (off by default): once, <see cref="DelaySeconds"/> after the world is shown with the player in it, one full-screen white frame (held <see cref="HoldSeconds"/> so a 60 fps
/// capture sees it) and, in the frame it appears, one UI sound (<c>UI_IconSuccessfulDrop</c> 0x70 of the UI sound table, or the type given) through the game's own sound player. It logs
/// <c>video_sync_marker t=&lt;unix_ms&gt; frame=&lt;n&gt;</c>. The hero video tools measure the white frame's capture time against the click's onset in the recording: the sound-to-picture offset of the take.
/// </summary>
public sealed partial class VideoSyncMarker : Node
{
    /// <summary><c>UI_IconSuccessfulDrop</c> (DatReaderWriter's <c>Sound</c> 0x70): a short thunk that rises 14 dB over the offline ambient bed in a recording. <c>UI_ButtonPress</c> (0x72) rises 3 dB there, too quiet to time.</summary>
    public const uint DefaultSoundType = 0x70;

    /// <summary>Seconds after the world is shown with the player in it.</summary>
    public const double DelaySeconds = 2.0;

    /// <summary>Seconds the white frame stays.</summary>
    public const double HoldSeconds = 0.12;

    private readonly GameServices _services;
    private double _inWorldFor;
    private double _holdLeft;
    private bool _done;
    private ColorRect? _white;

    private VideoSyncMarker(GameServices services) => _services = services;

    /// <summary>True when the run asks for it.</summary>
    public static bool Requested() => CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Any(a => a.Name == "--video-sync-marker");

    /// <summary>The UI sound type the click plays: <c>--video-sync-marker=TYPE</c> (decimal or 0x hex), else <see cref="DefaultSoundType"/>.</summary>
    private static uint SoundType()
    {
        foreach (var argument in CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()))
        {
            if (argument.Name == "--video-sync-marker" && !string.IsNullOrEmpty(argument.Value))
            {
                var text = argument.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? argument.Value[2..] : argument.Value;
                var style = argument.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer;
                if (uint.TryParse(text, style, System.Globalization.CultureInfo.InvariantCulture, out var type))
                {
                    return type;
                }

                Log.Warn("video", "--video-sync-marker takes no value or a UI sound type (decimal or 0x hex); the default is used", ("value", argument.Value));
            }
        }

        return DefaultSoundType;
    }

    /// <summary>Adds the node to <paramref name="main"/>.</summary>
    public static void Attach(Node main, GameServices services)
    {
        ArgumentNullException.ThrowIfNull(main);
        main.AddChild(new VideoSyncMarker(services) { Name = "VideoSyncMarker" });
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        if (_white is not null)
        {
            _holdLeft -= delta;
            if (_holdLeft <= 0)
            {
                _white.GetParent().QueueFree();
                _white = null;
                _done = true;
            }

            return;
        }

        var inWorld = _services.WorldVisibility.Visible
            && (SessionHost.Instance is not { } host || host.Session is { } session && session.State.Self.Travel != SelfTravel.NotInWorld)
            && AudioService.Current is not null;
        _inWorldFor = inWorld ? _inWorldFor + delta : 0.0;
        if (_inWorldFor < DelaySeconds)
        {
            return;
        }

        var layer = new CanvasLayer { Layer = 127, Name = "VideoSyncMarkerLayer" };
        _white = new ColorRect { Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore };
        _white.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_white);
        AddChild(layer);
        _holdLeft = HoldSeconds;
        var uiTable = _services.Dats is { } dats ? PortalSpaceAssets.Resolve(dats.EnumMaps).UiSoundTableId : 0u;
        AudioService.Current!.Sound.PlayFromCenter(uiTable, SoundType());
        Log.Info("video", "video_sync_marker", ("t", (long)(Time.GetUnixTimeFromSystem() * 1000.0)), ("frame", (long)Engine.GetProcessFrames()), ("sound_type", $"0x{SoundType():X}"), ("ui_table", $"0x{uiTable:X8}"));
    }
}
