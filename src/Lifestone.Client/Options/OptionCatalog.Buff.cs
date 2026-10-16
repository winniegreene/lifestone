namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The buff cycle (M3 2.1 s0b; plan decisions D10 and D24), read by the headless host's buff scenario and the game's buff leg; parsed by
    /// src/Lifestone.Client/Session/Mage/BuffOptions.cs (<c>BuffOptions.FromArguments</c>).
    /// </summary>
    public static OptionGroup Buff() => new("Buff cycle", "main scene and headless host",
    [
        new("--buff-cast-chance", "P", "The ACE cast chance a spell level must reach for the buff cycle to cast it, above 0 and at most 1 (default 0.9; the highest level that reaches it is cast, from the skill the casts before it left)."),
        new("--buff-rebuff-margin-s", "S", "A buff with less than S seconds left is recast by a re-buff pass, and a buff with more is skipped by a cycle (default 300)."),
        new("--buff-transfer-tries", "N", "How many Stamina to Mana casts the cycle tries in a row before it gives up on a fizzling line, at least 1 (default 8; each try is a cast in play, not a check retry)."),
        new("--buff-mana-wait-s", "S", "The longest the cycle waits for mana to regenerate, when neither Stamina to Mana nor Revitalize can be cast, before it fails (default 180)."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
