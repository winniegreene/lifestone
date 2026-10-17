namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The death loop (M3 3.2 lane da), read by the headless host's death-and-regear scenario and the game's bsd-death leg; parsed by
    /// src/Lifestone.Client/Session/Scenarios/DeathOptions.cs (<c>DeathOptions.FromArguments</c>).
    /// </summary>
    public static OptionGroup Death() => new("Death loop", "main scene and headless host",
    [
        new("--death-seconds", "S", "The most session-clock seconds the death loop waits among the Black Spawn Den's tuskers for them to kill the unbuffed mage (1 to 3600, default 600), counted from the start of the wait (after the gear, the bind at Holtburg's lifestone and the suicide that unbuffs); past it the scenario fails loudly with the hits, evades and the lowest health share. A bad value fails the headless run and the game's bsd-death leg."),
        new("--death-stages", "LIST", "Which stages of the death-and-regear scenario run (headless host only; the game's bsd-death leg always runs all): a list of gear, bind, den, unbuff, death, regear, vitae, joined by commas or plus signs, or all (the default) or none. vitae is the last stage (D37): after the verdict's measures are frozen and before the log off, BSD Mage's vitae is paid back with the server mod's xp verb (the experience ACE's rule asks, refused if it would cross the next level) and the line reports vitae_paid= and vitae_after=. A run without the death stage judges nothing about the death, so --scenario=death-and-regear --death-stages=vitae pays the vitae alone. A bad value is an error."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
