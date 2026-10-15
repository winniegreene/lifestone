using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Text;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail UI's sounds through the portal track's sound player (<see cref="ISoundSink"/>, the audio service's
/// <c>RetailSoundPlayer</c>): a sound entry of a media machine is <c>SoundManager::PlaySoundFromCenter</c> (11.4186 0x00550950 /
/// 0x005509E0, 11.6096 0x00551560 / 0x005515F0). The sink is looked up when a sound plays, because the audio service is installed
/// after the retail UI in the main scene; with no audio service the UI is silent.
/// </summary>
/// <param name="sink">Finds the sink at play time, or null while there is none.</param>
/// <param name="log">Where the one-time <c>TODO(ui-sound-sink)</c> goes when a sound is asked for and there is no sink, or null for no log.</param>
public sealed class UiSoundAdapter(Func<ISoundSink?> sink, ILogSink? log = null) : IUiSoundSink
{
    private bool _loggedNoSink;

    /// <summary>How many sounds were asked for while there was no sink (each one is dropped).</summary>
    public int Dropped { get; private set; }

    /// <inheritdoc/>
    public void PlayFromCenter(uint soundTableId, uint soundType)
    {
        if (Find() is { } found)
        {
            found.PlayFromCenter(soundTableId, soundType);
        }
        else
        {
            Drop($"table 0x{soundTableId:X8} type 0x{soundType:X}");
        }
    }

    /// <inheritdoc/>
    public void PlayWaveFromCenter(uint waveId, float volume)
    {
        if (Find() is { } found)
        {
            found.PlayWaveFromCenter(waveId, volume);
        }
        else
        {
            Drop($"wave 0x{waveId:X8}");
        }
    }

    private ISoundSink? Find() => sink();

    private void Drop(string what)
    {
        Dropped++;
        if (_loggedNoSink)
        {
            return;
        }

        _loggedNoSink = true;
        log?.Warn("retailui", $"TODO(ui-sound-sink): a UI sound was asked for ({what}) and there is no sound player to play it, so it is dropped (the audio service is not installed, or this run has none); logged once");
    }
}

/// <summary>The system clipboard for text fields (copy, cut, paste; D39), through Godot's <see cref="DisplayServer"/>.</summary>
public sealed class DisplayServerClipboard : IUiClipboard
{
    /// <inheritdoc/>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        DisplayServer.ClipboardSet(text);
    }

    /// <inheritdoc/>
    public string? GetText() => DisplayServer.ClipboardHas() ? DisplayServer.ClipboardGet() : null;
}

/// <summary>
/// Where the mouse goes when the UI does not take it (<see cref="IWorldPointerSink"/>): a relay to the world's player controller
/// (G5), which sets <see cref="Target"/>, and the source of <see cref="RetailUiLayer.KeyboardCaptured"/> (a focused text field has
/// the keyboard, so movement keys must not reach the world). Until a target is set the world hears nothing.
/// </summary>
public sealed class WorldPointerRelay : IWorldPointerSink
{
    private bool _textEntry;
    private IWorldPointerSink? _target;

    /// <summary>The world's sink, or null; a sink set while a text field has the keyboard is told so at once.</summary>
    public IWorldPointerSink? Target
    {
        get => _target;
        set
        {
            _target = value;
            if (value is not null)
            {
                value.TextEntryActive = _textEntry;
            }
        }
    }

    /// <summary>Raised when a text field takes or gives up the keyboard.</summary>
    public event Action<bool>? TextEntryChanged;

    /// <summary>True while a text field has the keyboard.</summary>
    public bool TextEntryActive
    {
        get => _textEntry;
        set
        {
            if (value == _textEntry)
            {
                return;
            }

            _textEntry = value;
            if (_target is { } target)
            {
                target.TextEntryActive = value;
            }

            TextEntryChanged?.Invoke(value);
        }
    }

    /// <inheritdoc/>
    public void OnWorldPointer(int x, int y, UiMouseButton button, bool down, bool doubleClick) => _target?.OnWorldPointer(x, y, button, down, doubleClick);

    /// <inheritdoc/>
    public void OnMouseLook(float dx, float dy) => _target?.OnMouseLook(dx, dy);
}
