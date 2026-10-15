namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The retail user interface in the main scene (M2 wave-2 track G2, the renderer and the login screens; parsed by
    /// <see cref="Lifestone.Client.RetailUi.RetailUiOptions"/>). Owner decision D35 (2026-10-12) keeps character management
    /// retail-exact, with no 3D preview, so there is no preview option. The check suite <c>--retail-ui-checks</c> belongs to the
    /// Checks group (lead).
    /// </summary>
    public static OptionGroup RetailUi() => new(
        "Retail UI",
        "main scene",
        [
            new("--retail-ui", "off|standin", "Draw the retail user interface over the world (default off: M1's explore mode, unchanged). standin runs the screens on offline stand-ins, so each can be clicked through. --retail-ui-start, --retail-ui-frames and --retail-ui-only imply standin."),
            new("--ui-scale", "N", "The retail UI's integer scale, 1 to 16 (default: the largest that leaves at least 800x600 UI pixels, so 1 at 1080p and 2 at 1440p). 1 restores retail's pixel-for-pixel UI."),
            new("--retail-ui-start", "SCREEN", "The first retail screen: datapatch (default), intro, charmanage, game, epilogue, disconnected, chargen or credits."),
            new(
                "--retail-ui-frames",
                "DIR",
                "Play the retail login screens offline on stand-in data, capture their review frames at the window's size (default docs/screenshots/m2/ui/<W>x<H>), fail if the window is not the size --resolution asked for, and quit. No world is built. The frames are derived from the dats and never committed.",
                OptionRole.EndsRun,
                ValueOptional: true),
            new("--retail-ui-only", null, "Run the retail UI alone: no world and no sky behind it, for UI work (--retail-ui-frames implies it)."),
            new("--retail-ui-glitches", null, "Restore the retail UI glitches Lifestone fixes by default (dialog queueing, the A8 hit test, held actions on a focus loss, character creation's heritage tabs and the summary's heritage names; docs/retail-ui.md sections 14, 19 and 20)."),
            new("--retail-disconnect", null, "Retail's exit from the disconnected screen (D43); by default its button reconnects."),
        ],
        OptionScenes.Main);
}
