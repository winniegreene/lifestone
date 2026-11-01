namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The mana order and the Mana Conversion tries (M5 item 1 lane K5; D15, D27), read by the vitals keeper and the buff cycle (and the fight, F1); parsed by
    /// src/Lifestone.Client/Session/Vitals/ManaOrderOptions.cs (<c>ManaOrderOptions.Apply</c>), called from <c>VitalsOptions.FromArguments</c> and <c>BuffOptions.FromArguments</c>.
    /// </summary>
    public static OptionGroup Casting() => new("Mana order", "main scene and headless host",
    [
        new("--cast-mana-tries", "N", "The 0x0401 refusals one cast absorbs when its mana is at or over the try floor and under its ceiling, each a fresh Mana Conversion roll (ACE charges a refused cast nothing), 0 to 40 (default 8); under the floor Stamina to Mana Self is pressed until it lands (the dance). 0 restores the ceiling order: no tries, no dance. A bad value fails the run (bad cast option)."),
        new("--cast-mana-try-chance", "P", "The landing chance, above 0 and at most 1, a try must have at the mana held: the try floor is the least mana that gives it, never under 5 (default 0.25)."),
        new("--cast-mana-try-spacing-s", "S", "The wait after a refused try or press before the next, in seconds, 0.1 to 5 (default 0.25, a person's click rate)."),
        new("--cast-mana-dance-presses", "N", "The presses of one stamina-to-mana dance in all, 1 to 100 (default 40); the vitals keeper also counts each press against its 40 actions a pass."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
