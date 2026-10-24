using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which stages of <see cref="DeathScenario"/> run (a host that has done one of them leaves it out; the tests pick the ones they script).</summary>
[Flags]
public enum DeathStages
{
    /// <summary>Only the logging off (the stage mask of a run with nothing to do).</summary>
    None = 0,

    /// <summary>The gear scenario's steps, then the mage's spell bars (<see cref="MageSpellBars"/>: tab I the buffs, tab II the fight).</summary>
    Gear = 1,

    /// <summary>D34: the teleport to Holtburg, the nearest lifestone and a Use on it (ACE moves the player to it and attunes it).</summary>
    Bind = 2,

    /// <summary>The teleport to the Black Spawn Den's arrival and the settle on its floor.</summary>
    Den = 4,

    /// <summary>D33: a suicide in the den when any enchantment but the vitae is active, so the mage meets the tuskers unbuffed.</summary>
    Unbuff = 8,

    /// <summary>The walk to the nearest tusker cell and the wait for the tuskers' death of the player, the respawn and the vitals.</summary>
    Death = 16,

    /// <summary>The gear scenario's steps again: what the death dropped is spawned again, the wand and the five pieces wielded.</summary>
    Regear = 32,

    /// <summary>
    /// D37, the last stage, after the measures are frozen and before the log off: the vitae is paid back with the server mod's <c>xp</c> verb (ACE <c>GrantXP</c> then <c>UpdateXpVitae</c>, <c>Player_Xp.cs</c> 53-82, 133-175), the
    /// amount computed from ACE's rule (<see cref="Lifestone.Client.Session.Contracts.EffectRules.VitaeExperienceToClear"/>), allowed to cross a level since D42 (logged as vitae_level_crossed=yes level=a->b) but never more than it clears; skipped when the registry holds no vitae.
    /// </summary>
    Vitae = 64,

    /// <summary>
    /// M4 2.2 s1 (D5): the trip back to the mage's own corpse: teleport to where it died, open it, take everything, close it, teleport back to Holtburg (<c>--death-recover-corpse</c>, <c>--death-recover-min-health</c>). It comes after the respawn's vitals
    /// and before <see cref="Regear"/> when <see cref="Fight"/> is off (M4 2.2's order), after the regear, the rebuff and the fight when it is on (M4 3.1 d1). A skipped precondition or an abort leaves M3's run (the regear spawns the gear).
    /// </summary>
    Corpse = 128,

    /// <summary>
    /// M4 3.1 d1 (D20): after the respawn and its vitals (and the regear), the buff cycle at Holtburg: the death purged every enchantment, so this is the full cycle (<see cref="FightStages.Buffs"/> of the composed inner <see cref="FightScenario"/>;
    /// <c>--death-rebuff</c>). With <see cref="Fight"/> off it runs after the regear; the order is in <see cref="DeathScenario"/>'s class doc.
    /// </summary>
    Rebuff = 256,

    /// <summary>
    /// M4 3.1 d1 (D20): after the rebuff, the fight at the Black Spawn Den (<see cref="FightStages.Place"/> and <see cref="FightStages.Mode"/> and the kill loop of the composed inner <see cref="FightScenario"/>; <c>--death-fight</c>,
    /// <c>--death-fight-kills</c>, <c>--death-fight-seconds</c>): the corpse trip then comes after it, with the tuskers dead. With it off the order is M4 2.2's (the trip straight after the respawn's vitals, the regear after it).
    /// </summary>
    Fight = 512,

    /// <summary>Everything (the headless host's, and the game's bsd-death leg).</summary>
    All = Gear | Bind | Den | Unbuff | Death | Regear | Vitae | Corpse | Rebuff | Fight,
}

/// <summary>
/// The death loop's options (M3 3.2 lane da; <c>--death-seconds</c> is registered in <c>OptionCatalog.Death.cs</c>, read by the headless host and the game's <c>bsd-death</c> leg): the seconds the wait for the tuskers'
/// blows may take. <see cref="StopAfterHits"/> is not on the command line. Immutable; <see cref="FromArguments"/> is the parser both hosts call.
/// </summary>
public sealed record DeathOptions
{
    /// <summary>The default of <c>--death-seconds</c>.</summary>
    public const double DefaultSeconds = 600;

    /// <summary>The least <c>--death-seconds</c> accepted.</summary>
    public const double MinSeconds = 1;

    /// <summary>The most <c>--death-seconds</c> accepted.</summary>
    public const double MaxSeconds = 3600;

    /// <summary>
    /// The seconds, on the session clock from the start of the wait among the tuskers, after which the scenario fails loudly (<c>--death-seconds</c>); with <see cref="StopAfterHits"/> the same limit bounds the
    /// wait for the first blow.
    /// </summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>
    /// True: the scenario does not wait for the death (DoD 9's live test, not on the command line). On the first 0x01B2 melee notice with the health share under 100% it waits <see cref="DeathScenario.StopAfterHitsSeconds"/> s, then
    /// stays in non-combat and logs off; the regear stage does not run. A mage that dies first (unbuffed it can in about 20 s) is reported, its respawn is not judged, and the run still logs off cleanly.
    /// </summary>
    public bool StopAfterHits { get; init; }

    /// <summary>
    /// The stages to run (<c>--death-stages=gear,bind,den,unbuff,death,corpse,regear,rebuff,fight,vitae</c>, or <c>all</c> or <c>none</c>), or null for all of them. A run without the Death stage judges nothing about the death (only a failure fails it),
    /// so <c>--death-stages=vitae</c> pays the vitae of BSD Mage alone. Read by the headless host; the game's bsd-death leg always runs all.
    /// </summary>
    public DeathStages? Stages { get; init; }

    /// <summary>The default of <c>--death-fight-kills</c>.</summary>
    public const int DefaultFightKills = 4;

    /// <summary>The default of <c>--death-fight-seconds</c>.</summary>
    public const double DefaultFightSeconds = 180;

    /// <summary>The default of <c>--death-recover-min-health</c>, in percent of the maximum health.</summary>
    public const double DefaultRecoverMinHealthPercent = 60;

    /// <summary>
    /// True (default, <c>--death-recover-corpse on</c>): the <see cref="DeathStages.Corpse"/> stage goes back for the mage's own corpse after the respawn. False (<c>off</c>) is M3's run exactly (D8: the corpse is not looted,
    /// the regear spawns the gear).
    /// </summary>
    public bool RecoverCorpse { get; init; } = true;

    /// <summary>The least health share, in percent, the mage must have for the trip to start (<c>--death-recover-min-health</c>, 0 to 100, default 60; the respawn leaves it at 75).</summary>
    public double RecoverMinHealthPercent { get; init; } = DefaultRecoverMinHealthPercent;

    /// <summary>
    /// True (default, <c>--death-decoys on</c>): the death loop's kit carries the eight decoys of <see cref="Lifestone.Client.Session.Mage.DecoyKit"/> (ordinary, light, expensive items a death drops instead of the gear the loops need; M4 3.1 d0, D20), built by
    /// <see cref="Lifestone.Client.Session.Mage.GearKit.ForDeathLoop"/>. False (<c>off</c>) is M3's kit exactly: no decoy is spawned and none is counted.
    /// </summary>
    public bool Decoys { get; init; } = true;

    /// <summary>
    /// True (default, <c>--death-rebuff on</c>): after the respawn the loop runs the buff cycle at Holtburg (<see cref="DeathStages.Rebuff"/>; the death purged every enchantment). False (<c>off</c>) leaves the mage unbuffed for the next stage.
    /// </summary>
    public bool Rebuff { get; init; } = true;

    /// <summary>
    /// True (default, <c>--death-fight on</c>): after the respawn, the regear and the rebuff the loop goes to the Black Spawn Den and kills <see cref="FightKills"/> tuskers (<see cref="DeathStages.Fight"/>) before the corpse trip. False (<c>off</c>) is M4 2.2's order
    /// exactly (the trip straight after the respawn's vitals, the regear after it).
    /// </summary>
    public bool Fight { get; init; } = true;

    /// <summary>The kills that end the death loop's fight (<c>--death-fight-kills</c>, 1 to <see cref="FightOptions.MaxKills"/>, default <see cref="DefaultFightKills"/>).</summary>
    public int FightKills { get; init; } = DefaultFightKills;

    /// <summary>The seconds after which the death loop's fight fails loudly (<c>--death-fight-seconds</c>, above 0, default <see cref="DefaultFightSeconds"/>).</summary>
    public double FightSeconds { get; init; } = DefaultFightSeconds;

    /// <summary>
    /// M4 P4 g3fix: the most seconds the fight stage waits, at Holtburg and before the teleport to the den, for the mana to regenerate to <see cref="FightManaFloorPercent"/> of the maximum (0, the default: no wait, the game's leg is unchanged).
    /// The rebuff's 27 casts leave the mage at about 23 mana, and the mage regenerates about 0.43 a second, so a 180 s fight started there waits for mana for most of its time (g3: 3 of 4 kills, phase=Mana). Not a retry: nothing has been cast.
    /// </summary>
    public double FightManaWaitSeconds { get; init; }

    /// <summary>The share of the maximum mana, in percent, that ends the wait of <see cref="FightManaWaitSeconds"/> (default 70).</summary>
    public double FightManaFloorPercent { get; init; } = DefaultFightManaFloorPercent;

    /// <summary>The default of <see cref="FightManaFloorPercent"/>.</summary>
    public const double DefaultFightManaFloorPercent = 70;

    /// <summary>Every problem found while parsing, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses <c>--death-seconds</c>, <c>--death-stages</c>, <c>--death-recover-corpse</c>, <c>--death-recover-min-health</c>, <c>--death-decoys</c>, <c>--death-rebuff</c>, <c>--death-fight</c>, <c>--death-fight-kills</c> and <c>--death-fight-seconds</c> from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
    /// <param name="args">The user arguments.</param>
    public static DeathOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new DeathOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            if (argument.Name == "--death-stages")
            {
                var parsed = ParseStages(argument.Value, out var problem);
                if (problem is null)
                {
                    o = o with { Stages = parsed };
                }
                else
                {
                    errors.Add($"{argument.Name}: {problem}");
                }

                continue;
            }

            if (argument.Name == "--death-recover-corpse")
            {
                o = o with { RecoverCorpse = OptionValues.Switch(argument, errors, o.RecoverCorpse) };
                continue;
            }

            if (argument.Name == "--death-decoys")
            {
                o = o with { Decoys = OptionValues.Switch(argument, errors, o.Decoys) };
                continue;
            }

            if (argument.Name == "--death-rebuff")
            {
                o = o with { Rebuff = OptionValues.Switch(argument, errors, o.Rebuff) };
                continue;
            }

            if (argument.Name == "--death-fight")
            {
                o = o with { Fight = OptionValues.Switch(argument, errors, o.Fight) };
                continue;
            }

            if (argument.Name == "--death-fight-kills")
            {
                var before = errors.Count;
                var kills = OptionValues.Int(argument, errors, o.FightKills);
                if (errors.Count == before && kills is >= 1 and <= FightOptions.MaxKills)
                {
                    o = o with { FightKills = kills };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {FightOptions.MaxKills}");
                }

                continue;
            }

            if (argument.Name == "--death-fight-seconds")
            {
                var before = errors.Count;
                var fightSeconds = OptionValues.Double(argument, errors, o.FightSeconds);
                if (errors.Count == before && fightSeconds > 0)
                {
                    o = o with { FightSeconds = fightSeconds };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                }

                continue;
            }

            if (argument.Name == "--death-recover-min-health")
            {
                var before = errors.Count;
                var share = OptionValues.Double(argument, errors, o.RecoverMinHealthPercent);
                if (errors.Count == before && share is >= 0 and <= 100)
                {
                    o = o with { RecoverMinHealthPercent = share };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to 100 percent");
                }

                continue;
            }

            if (argument.Name != "--death-seconds")
            {
                continue;
            }

            var known = errors.Count;
            var seconds = OptionValues.Double(argument, errors, o.Seconds);
            if (errors.Count == known && seconds is >= MinSeconds and <= MaxSeconds)
            {
                o = o with { Seconds = seconds };
            }
            else if (errors.Count == known)
            {
                errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds");
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>Parses a stage list: names joined by commas or plus signs, case-insensitive; <c>all</c> and <c>none</c> stand alone.</summary>
    /// <param name="text">The option's value.</param>
    /// <param name="problem">Why it does not parse, or null.</param>
    public static DeathStages ParseStages(string? text, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "a list of stages is expected (gear, bind, den, unbuff, death, corpse, regear, rebuff, fight, vitae, all, none)";
            return DeathStages.All;
        }

        var stages = DeathStages.None;
        foreach (var part in text.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "gear": stages |= DeathStages.Gear; break;
                case "bind": stages |= DeathStages.Bind; break;
                case "den": stages |= DeathStages.Den; break;
                case "unbuff": stages |= DeathStages.Unbuff; break;
                case "death": stages |= DeathStages.Death; break;
                case "corpse": stages |= DeathStages.Corpse; break;
                case "regear": stages |= DeathStages.Regear; break;
                case "rebuff": stages |= DeathStages.Rebuff; break;
                case "fight": stages |= DeathStages.Fight; break;
                case "vitae": stages |= DeathStages.Vitae; break;
                case "all": stages |= DeathStages.All; break;
                case "none": break;
                default:
                    problem = $"'{part}' is not a stage (gear, bind, den, unbuff, death, corpse, regear, rebuff, fight, vitae, all, none)";
                    return DeathStages.All;
            }
        }

        return stages;
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"seconds={Seconds}{(StopAfterHits ? " stop_after_hits=yes" : string.Empty)}{(RecoverCorpse ? string.Empty : " recover_corpse=off")}{(Decoys ? string.Empty : " decoys=off")}{(Rebuff ? string.Empty : " rebuff=off")}{(Fight ? string.Empty : " fight=off")}{(FightKills == DefaultFightKills ? string.Empty : " fight_kills=" + FightKills)}{(FightSeconds == DefaultFightSeconds ? string.Empty : " fight_seconds=" + FightSeconds)}{(RecoverMinHealthPercent == DefaultRecoverMinHealthPercent ? string.Empty : " recover_min_health=" + RecoverMinHealthPercent)}{(Stages is { } stages ? " stages=" + stages.ToString().Replace(", ", "+", StringComparison.Ordinal).ToLowerInvariant() : string.Empty)}");
}
