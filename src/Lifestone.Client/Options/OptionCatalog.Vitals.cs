namespace Lifestone.Client.Options;

public static partial class OptionCatalog
{
    /// <summary>
    /// The vitals keeper (M4 P5a lane C2), read by the journeys' hosts (P5b) and the hunt; parsed by src/Lifestone.Client/Session/Vitals/VitalsOptions.cs (<c>VitalsOptions.FromArguments</c>). Its health thresholds are the
    /// recovery rule's (<c>--heal-below</c>, <c>--heal-until</c>, <c>--heal-chance</c>, <c>--heal-floor-chance</c>; OptionCatalog.Hunt.cs).
    /// </summary>
    public static OptionGroup Vitals() => new("Vitals keeper", "main scene and headless host",
    [
        new("--vitals-stamina-below", "P", "The stamina share, in percent (0 to 100), under which the vitals keeper restores stamina before a run or an attack: Revitalize Self where the character knows it and the cast chance allows, else a rest in peace (default 30)."),
        new("--vitals-stamina-until", "P", "The stamina share, in percent (1 to 100, not under --vitals-stamina-below), a started stamina restore goes on to (default 80)."),
        new("--vitals-attack-stamina", "P", "Under this stamina share, in percent (0 to 100), the vitals keeper refuses an attack when the hunt asks (default 15)."),
        new("--vitals-rest-cap-s", "S", "The longest one rest of the vitals keeper lasts, in seconds of the session clock, above 0 (default 600); over it the keeper stops with Blocked why=rest-cap:<vital>, never a silent retry."),
        new("--vitals-rest-posture", "stand|sit|lie", "The posture of the vitals keeper's rests, in peace only (default stand): sit or lie sends the emote key (MotionCommand Sitting 0x41000013 or Sleeping 0x41000014) and stands up with the stop key before any other action; ACE multiplies health and stamina regeneration by 2.5 sitting and 3.0 lying down (mana is unaffected)."),
        new("--vitals-infight-potion", "on|off", "Whether the vitals keeper may drink a Potion of Healing during a fight when the health is under --vitals-infight-potion-below and the target is out of reach (default off until the journeys wire it)."),
        new("--vitals-infight-potion-below", "P", "The health share, in percent (1 to 100), under which the in-fight potion is drunk (default 40)."),
        new("--vitals-cast-chance", "P", "The ACE cast chance, above 0 and at most 1, a level of Heal Self, Revitalize Self or Stamina to Mana Self must reach for the vitals keeper to cast it; the highest known level that reaches it is cast (default 0.9)."),
    ], OptionScenes.Main | OptionScenes.Headless);
}
