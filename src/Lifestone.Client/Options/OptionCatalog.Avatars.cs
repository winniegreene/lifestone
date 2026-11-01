namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// World objects and the player in the main scene (M2 wave-2 track G5; parsed by
    /// <see cref="Lifestone.Client.Avatars.AvatarsOptions"/>). G5 adds its options here as its stages land. Planned (M2
    /// option-name table) and not registered: <c>--avatar-stress=N</c> (the owner dropped it) and <c>--remote-ground-clamp</c>.
    /// The table's <c>--camera=chase|first</c> cannot be used as named: the spell viewer already registers <c>--camera</c> (an orbit
    /// camera), and a name is registered once, so the player's camera is <c>--player-camera</c>.
    /// </summary>
    public static OptionGroup Avatars() => new(
        "Avatars and player",
        "main scene",
        [
            new(
                "--offline-player",
                null,
                "Explore mode with your own character: a stand-in Aluvian (placeholder look) with retail's collision, motion, lighting and chase camera, spawned at the start location. Retail's keys move it (W/Up run, X/Down walk back, A/D turn, Z/C strafe, Space jumps, Left Shift walks, Q autorun, number pad and middle mouse button: camera). Without it explore mode is M1's free camera and keys."),
            new(
                "--player-camera",
                "chase|first",
                "With --offline-player: the camera the player starts with, the chase camera behind the character (default) or first person from its head. The camera keys switch (number pad point: first person, 0: default)."),
            new(
                "--physics-overlay",
                null,
                "With --offline-player: an overlay of the player's physics (cell, velocity, contact, the movement messages the dry-run sink would send, the camera sweep)."),
            new(
                "--motion-stepping",
                "retail",
                "With --offline-player: draw the character at the 30 Hz physics step positions, as retail did (stepping), instead of positions interpolated between steps (D26, the default)."),
            new(
                "--retail-run-rate-cap",
                null,
                "With --offline-player: restore retail's cap on a negative run rate (docs/avatars.md; the default fixes it)."),
            new(
                "--offline-player-frames",
                "DIR",
                "Walk the offline player through four review frames (the chase camera behind the character in Holtburg, first person, inside a building, mid-jump), save them to DIR (default docs/screenshots/m2/player) and quit. Implies --offline-player.",
                OptionRole.EndsRun,
                ValueOptional: true),
            new(
                "--server-object-transitions",
                "on|off",
                "Server objects (creatures, NPCs, doors) run retail's collision transitions through the client's server-object physics, so creatures stop at each other, the player, doors and lifestones (default on). off moves them without collision, as before M5 item 5: a diagnostics switch for measuring the frame cost, not a glitch flag."),
        ],
        OptionScenes.Main);
}
