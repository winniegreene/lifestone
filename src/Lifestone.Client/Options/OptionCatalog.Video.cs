namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// Screen-capture videos of the main scene (lead): the game's own sound to a file (parsed by
    /// <see cref="Lifestone.Client.Audio.AudioOptions"/>), a HUD without diagnostic text (Lifestone.Game
    /// Diagnostics/CleanHud.cs) and a paced character creation in the throwaway leg (Session/ThrowawayScreens.cs). The main scene only, so another scene reports them as ignored.
    /// </summary>
    public static OptionGroup Video() => new(
        "Video capture",
        "main scene",
        [
            new("--record-audio", "FILE", "Record the game's own sound to FILE, a relative .wav path under cache/ (retail sounds are derived data, never committed), for short clips: the recording is held in memory and written when the game quits normally (--quit-after, a check's own quit or closing the window); a killed process writes nothing. The Master bus is recorded before its level, so a silent automated run records too and nothing reaches the speakers. The log's 'audio recording started' line gives the start's wall-clock time (unix_ms) for lining the sound up with a screen capture; that holds with a real audio driver only (-Offscreen's Dummy driver drifts from the wall clock). A run without the audio service (a check suite, --retail-ui-only, a failed start) logs an error and records nothing."),
            new("--clean-hud", null, "Hide the diagnostic text over the world, for videos: the position line, the F3 overlay, the objects rows, the interiors line and the player's key hints. Placeholder notices stay."),
            new("--video-pace", "S", "For videos of --login-check=throwaway: hold each character-creation page, and each typed name before Finish, for S seconds (0 to 10; default 0, no hold) so a screen capture shows them. The leg and its verdict are otherwise unchanged; an invalid value is logged as an error and holds nothing."),
        ],
        OptionScenes.Main);
}
