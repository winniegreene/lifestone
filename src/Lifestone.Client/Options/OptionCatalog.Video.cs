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
            new("--clean-hud", null, "Hide the diagnostic text over the world, for videos: the position line, the F3 overlay, the objects rows, the interiors line, and the player's key hints. Placeholder notices stay."),
            new("--video-sync-marker", "[TYPE]", "For videos: once, 2 s after the world is shown with the player in it, one full-screen white frame (held 0.12 s so a 60 fps capture sees it) and, in its first frame, one UI sound (UI_IconSuccessfulDrop 0x70, or the type given, decimal or 0x hex) through the game's own sound player; logs 'video_sync_marker t=<unix_ms> frame=<n>' so tools can measure the sound-to-picture offset of a take (the hero tools (measure.ps1 -Take)). Off by default; nothing changes without it."),
            new("--video-camera", "D,H", "For videos: the chase camera's offset behind and above the pivot, D metres back (1 to 12) and H metres up (-1 to 6), so the player and a target in front of him are both in frame (retail's is 2.5, 0.75). Off by default; the camera keys still move it."),
            new("--video-hide-floaties", null, "For videos: the spellcasting window does not show in the Magic mode and a worse vitae does not open the vitae page by itself (the toolbar and the vitals stay). Off by default."),
            new("--video-chat", null, "For videos: once the window layout is read, the main chat window is 0.37 of the window's height, 400 at 1080 (its bottom edge kept) and as wide as it can be short of the player, who is drawn at the middle of the window (its right edge at half the window's width less 0.19 of its height; at least 200 pixels wide). Off by default."),
            new("--video-timer", "[LEVEL]", "For videos: a clock under the indicators at the top left (no box, no background), 'Time to Lvl LEVEL: ' and H:MM:SS since the character was created (its CreationTimestamp, so a restart keeps counting), which stops when the character reaches LEVEL (default 80) and logs that moment. Off by default."),
            new("--video-cantrip-look", "[held]", "For videos, with the autopilot running: each piece she loots with a great cantrip (Major or better) that raises a basic attribute or a skill she holds and raises with her experience (War, Life, Creature and Item Magic, Mana Conversion) is looked at as a person would: the paper doll and packs page opens, the examine window opens on the piece and both stay up 3 seconds, then the panels go back to the autopilot. With =held, for checking the look on screen, the pieces she holds now are looked at too, without the autopilot. Off by default."),
            new("--video-pace", "S", "For videos of --login-check=throwaway: hold each character-creation page, and each typed name before Finish, for S seconds (0 to 10; default 0, no hold) so a screen capture shows them. The leg and its verdict are otherwise unchanged; an invalid value is logged as an error and holds nothing."),
        ],
        OptionScenes.Main);
}
