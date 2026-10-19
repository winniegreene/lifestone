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
    /// amount computed from ACE's rule (<see cref="Lifestone.Client.Session.Contracts.EffectRules.VitaeExperienceToClear"/>), refused loudly if it would cross the next level; skipped when the registry holds no vitae.
    /// </summary>
    Vitae = 64,

    /// <summary>Everything (the headless host's, and the game's bsd-death leg).</summary>
    All = Gear | Bind | Den | Unbuff | Death | Regear | Vitae,
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
    /// The stages to run (<c>--death-stages=gear,bind,den,unbuff,death,regear,vitae</c>, or <c>all</c> or <c>none</c>), or null for all of them. A run without the Death stage judges nothing about the death (only a failure fails it),
    /// so <c>--death-stages=vitae</c> pays the vitae of BSD Mage alone. Read by the headless host; the game's bsd-death leg always runs all.
    /// </summary>
    public DeathStages? Stages { get; init; }

    /// <summary>Every problem found while parsing, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses <c>--death-seconds</c> from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
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
            problem = "a list of stages is expected (gear, bind, den, unbuff, death, regear, vitae, all, none)";
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
                case "regear": stages |= DeathStages.Regear; break;
                case "vitae": stages |= DeathStages.Vitae; break;
                case "all": stages |= DeathStages.All; break;
                case "none": break;
                default:
                    problem = $"'{part}' is not a stage (gear, bind, den, unbuff, death, regear, vitae, all, none)";
                    return DeathStages.All;
            }
        }

        return stages;
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"seconds={Seconds}{(StopAfterHits ? " stop_after_hits=yes" : string.Empty)}{(Stages is { } stages ? " stages=" + stages.ToString().Replace(", ", "+", StringComparison.Ordinal).ToLowerInvariant() : string.Empty)}");
}
