using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Text;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail UI's sounds through the portal track's sound player (<see cref="ISoundSink"/>, the audio service's
/// <c>RetailSoundPlayer</c>, whose interface calls play on the UI bus): a sound entry of a media machine is
/// <c>SoundManager::PlaySoundFromCenter</c> (11.4186 0x00550950 / 0x005509E0, 11.6096 0x00551560 / 0x005515F0). The sink is looked up when
/// a sound plays, because the audio service may be installed after the retail UI in the main scene; with no audio service the sound is
/// dropped, counted and logged once (<c>TODO(ui-sound-sink)</c>). Every request is counted in <see cref="Stats"/> (M3 1.3 s2a).
/// </summary>
/// <param name="sink">Finds the sink at play time, or null while there is none.</param>
/// <param name="log">Where the one-time <c>TODO(ui-sound-sink)</c> goes when a sound is asked for and there is no sink, or null for no log.</param>
/// <param name="isMuted">True while the master level is silent (D8), or null when that is unknown; only counted, it never changes what is handed to the player.</param>
public sealed class UiSoundAdapter(Func<ISoundSink?> sink, ILogSink? log = null, Func<bool>? isMuted = null) : IUiSoundSink
{
    private bool _loggedNoSink;

    /// <summary>What became of every request (requested, played, dropped, silenced, muted).</summary>
    public RetailUiSoundStats Stats { get; } = new();

    /// <summary>How many sounds were asked for while there was no sink (each one is dropped).</summary>
    public int Dropped => Stats.Dropped;

    /// <inheritdoc/>
    public void PlayFromCenter(uint soundTableId, uint soundType)
    {
        if (sink() is not { } found)
        {
            Drop(0, soundTableId, soundType);
            return;
        }

        var before = Started(found);
        found.PlayFromCenter(soundTableId, soundType);
        Stats.RecordPlayed(Started(found) == before && before >= 0, isMuted?.Invoke() ?? false);
    }

    /// <inheritdoc/>
    public void PlayWaveFromCenter(uint waveId, float volume)
    {
        if (sink() is not { } found)
        {
            Drop(waveId, 0, 0);
            return;
        }

        var before = Started(found);
        found.PlayWaveFromCenter(waveId, volume);
        Stats.RecordPlayed(Started(found) == before && before >= 0, isMuted?.Invoke() ?? false);
    }

    /// <summary>The voices the sound player has started so far, or -1 for a sink that does not say (its sounds are then never counted as silenced).</summary>
    private static int Started(ISoundSink found) => found is RetailSoundPlayer player ? player.Played : -1;

    private void Drop(uint wave, uint table, uint type)
    {
        Stats.RecordDropped();
        if (_loggedNoSink)
        {
            return;
        }

        _loggedNoSink = true;
        var what = wave != 0 ? $"wave 0x{wave:X8}" : $"table 0x{table:X8} type 0x{type:X}";
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
