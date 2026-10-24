namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The buff cycle (M3 2.1 s0b; plan decisions D10 and D24) and its re-buff driver (M4 4.1 lane a), read by the headless host's buff scenario and the game's buff leg; parsed by
    /// src/Lifestone.Client/Session/Mage/BuffOptions.cs (<c>BuffOptions.FromArguments</c>).
    /// </summary>
    public static OptionGroup Buff() => new("Buff cycle", "main scene and headless host",
    [
        new("--buff-cast-chance", "P", "The ACE cast chance a spell level must reach for the buff cycle to cast it, above 0 and at most 1 (default 0.9; the highest level that reaches it is cast, from the skill the casts before it left)."),
        new("--buff-rebuff-margin-s", "S", "A buff with less than S seconds left is recast by a re-buff pass, and a buff with more is skipped by a cycle (default 300)."),
        new("--buff-transfer-tries", "N", "How many Stamina to Mana casts the cycle tries in a row before it gives up on a fizzling line, at least 1 (default 8; each try is a cast in play, not a check retry)."),
        new("--buff-mana-wait-s", "S", "The longest the cycle waits for mana to regenerate, when neither Stamina to Mana nor Revitalize can be cast, before it fails (default 180)."),
        new("--buff-auto", "on|off", "Whether the re-buff driver keeps the buffs up by itself (default off; M4 4.1): on the session clock, every --buff-auto-check-s, when a buff line has less than --buff-rebuff-margin-s left it enters the magic combat mode and runs one re-buff pass (never the forced pass), then returns to peace. It waits, and logs why once per change, while the world is not entered, the player is in portal space or dead, another action holds a lane, or the player is in a fight (melee or missile mode, or damage taken in the last 10 s); a guard that holds for 120 s with a line due logs a warning naming the line. A failed pass, or one that missed lines, backs off 60 s and logs the reason. In play the Pause key turns it on and off (a key retail does not use) and says so in the chat with a [Lifestone] notice, as it does when a pass starts and ends; a character without a buff profile (only BSD Mage has one) gets the notice \"no buff profile for this character\" and it stays off. With --login-check=buff it replaces the forced re-buff pass: the leg sets the margin from the shortest line (its remaining time less 30 s, at least 1), the driver's pass comes within about 30 s, and the verdict prints rebuffed= from that pass, forced=no, driver=on, rebuff_lines= and rebuff_s=."),
        new("--buff-auto-check-s", "S", "How often the re-buff driver asks which buff lines are due, in seconds of the session clock, at least 1 (default 5; used with --buff-auto on)."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
