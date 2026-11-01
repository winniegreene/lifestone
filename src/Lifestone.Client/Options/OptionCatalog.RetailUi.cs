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
            new("--retail-effects-stale-info", null, "Retail's stale info box on the effects pages (default: the box and the selection clear when the last effect ends): retail's list rebuild returns before it updates the selection when nothing is in effect, so the ended spell's name and text stay (gmEffectsUI::RebuildList, 11.6096 0x004B8F70; docs/retail-ui.md section 14). --retail-ui-glitches includes it."),
            new("--retail-confirm-slot", null, "Retail's single confirmation slot (M5 6 u1): a second question from the server is dropped while one shows (gmGamePlayUI::MakeGameplayConfirmationDialog refuses; it times out at the server). Default: the dialogs queue and each answer goes to its own question. Part of --retail-ui-glitches."),
            new("--examine-icon", "on|off", "The examine window's item icon (default off, retail): retail's merged floaty layout has no icon element (ItemExamineUI::SetAppraiseInfo, 11.6096 0x004B7ED0, skips it), so the window is drawn without one. on adds the icon, composed like the pack's cell, from the standalone layout 0x2100001C (an addition to retail, M4 4.1)."),
            new("--examine-set-details", "on|off", "The examine window's set details (default off, retail): retail writes only \"Set: <name>\" for an item of an equipment set (ItemExamineUI::Appraisal_ShowSet, 11.6096 0x004AEC00). on adds, under it, the pieces of the set the player wears and the set's spells at each piece count from the SpellTable, the count in force marked (an addition to retail, M5 7 D137)."),
            new("--layout-store", "auto|disk|memory", "Where the in-game shell keeps its window layout files (M5 6 fix, 6-R2; retail's UI-<world>-<character>-<W>-<H>.txt). disk is the Godot user data; memory is a fresh store shared by the run's logins and gone with the process. Default auto: memory for --play-checks, the review frames and the login checks other than keep (so no check or frame reads a layout an earlier run saved, and the place a check moves is not carried into the next run), disk for live play and --login-check=keep (which proves the file round trip)."),
            new("--retail-stack-count", null, "Retail's stack count on an item cell (M5 7 D132): the plain number, so five or more digits start at the cell's left edge and lose the last digit (\"2500(\" for 25,000). By default a count wider than the cell drops the 3-pixel right margin and draws its digits a pixel closer, so five digits fit right-aligned in retail's font, and six or more show as thousands (\"100k\"); the tooltip keeps the full number."),
            new("--retail-disconnect", null, "Retail's exit from the disconnected screen (D43); by default its button reconnects."),
        ],
        OptionScenes.Main);
}
