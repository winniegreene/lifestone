namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// What became of the sounds the retail UI asked for (<see cref="IUiSoundSink"/>: the media machine's sound entries, M3 1.3 s2a, closing
/// <c>TODO(ui-sound-sink)</c>): a read-only count the host fills and the checks read (<c>sounds_ui=</c> is <see cref="Played"/>).
/// <para>Every request is exactly one of <see cref="Played"/> (handed to the sound player), <see cref="Dropped"/> (there was no sound
/// player) or counted by neither while it is in flight, so <c>Requested == Played + Dropped</c> after each call. Of the played ones,
/// <see cref="Silenced"/> are those the sound player's own rules started no voice for (its interface gate, a failed probability roll, a
/// missing wave, a level below retail's floor) and <see cref="Muted"/> are those handed over while the master level is silent (M2 D8: an
/// automated run is silent unless <c>--volume</c> is given); neither changes <see cref="Played"/>, so a muted run counts the same sounds a
/// loud one does.</para>
/// <para>Main thread only; no allocation per request.</para>
/// </summary>
public sealed class RetailUiSoundStats
{
    /// <summary>Sounds the UI asked for.</summary>
    public int Requested { get; private set; }

    /// <summary>Requests handed to the sound player (whatever the master volume).</summary>
    public int Played { get; private set; }

    /// <summary>Requests dropped because there was no sound player (each is logged once as <c>TODO(ui-sound-sink)</c>).</summary>
    public int Dropped { get; private set; }

    /// <summary>Played requests for which the sound player started no voice.</summary>
    public int Silenced { get; private set; }

    /// <summary>Played requests handed over while the master level was silent (D8).</summary>
    public int Muted { get; private set; }

    /// <summary>Records a request that reached a sound player.</summary>
    /// <param name="silenced">The player started no voice for it.</param>
    /// <param name="muted">The master level was silent when it was handed over.</param>
    public void RecordPlayed(bool silenced, bool muted)
    {
        Requested++;
        Played++;
        if (silenced)
        {
            Silenced++;
        }

        if (muted)
        {
            Muted++;
        }
    }

    /// <summary>Records a request that found no sound player.</summary>
    public void RecordDropped()
    {
        Requested++;
        Dropped++;
    }

    /// <summary>The counts on one line, for a log or a check's detail.</summary>
    public override string ToString() => $"sounds_ui={Played} requested={Requested} dropped={Dropped} silenced={Silenced} muted={Muted}";
}
