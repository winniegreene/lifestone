namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// Sound output in every scene (M2 wave-2 track G1, the audio service; parsed by
    /// <see cref="Lifestone.Client.Audio.AudioOptions"/>). The headless host plays no sound, so the group is not read there.
    /// </summary>
    public static OptionGroup Audio() => new(
        "Audio",
        "every scene",
        [
            new("--mute", null, "Silence all sound."),
            new("--volume", "0..1", "Master volume, linear (default 1; automated off-screen runs are silent unless it is given)."),
        ],
        OptionScenes.All);
}
