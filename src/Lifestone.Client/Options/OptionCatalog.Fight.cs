namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The fight loop (M3 3.1 lane fa), read by the headless host's fight scenario and the game's bsd-fight leg; parsed by
    /// src/Lifestone.Client/Session/Scenarios/FightOptions.cs (<c>FightOptions.FromArguments</c>).
    /// </summary>
    public static OptionGroup Fight() => new("Fight loop", "main scene and headless host",
    [
        new("--fight-kills", "N", "How many kills end the fight loop (1 to 1000, default 1): the Black Spawn Den fight of the mage stops at the Nth 0x01AD kill notice. A bad value fails the headless run and the game's bsd-fight leg; the game leg's buff top-up ignores the --buff-* options (it uses their defaults)."),
        new("--fight-seconds", "S", "The most session-clock seconds the fight loop may take from its first cast stage (default 300); past it the fight fails loudly with the counts."),
        new("--fight-ignore-line", null, "The fight loop casts at the nearest tusker whether or not a bolt can reach it (the pre-4.2 pick: from the arrival the three bolts hit the den's walls). Default: the loop picks the nearest tusker with a clear line from the caster (a sphere sweep through the den's collision) and walks on when none has one. This is the loop's own judgement, not a retail glitch fix: retail's client does not check a line before a war cast (ClientMagicSystem::CastSpell 11.6096 0x00568DE0), the player does."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
