using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight loop's two options (M3 3.1 lane fa; registered in <c>OptionCatalog.Fight.cs</c>): the kills that end the fight and the seconds it may take. Immutable;
/// <see cref="FromArguments"/> is the parser both hosts call (the game's <c>bsd-fight</c> leg passes the record to <see cref="FightScenario"/>).
/// </summary>
public sealed record FightOptions
{
    /// <summary>The default of <c>--fight-kills</c>.</summary>
    public const int DefaultKills = 1;

    /// <summary>The most kills accepted.</summary>
    public const int MaxKills = 1000;

    /// <summary>The default of <c>--fight-seconds</c> (session-clock seconds from the fight's start).</summary>
    public const double DefaultSeconds = 300;

    /// <summary>The kills that end the fight (<c>--fight-kills</c>).</summary>
    public int Kills { get; init; } = DefaultKills;

    /// <summary>The seconds after which the fight fails loudly (<c>--fight-seconds</c>).</summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>The default of <see cref="ArenaSkill"/>: the stand-in's fixed magic skill (<c>TODO(m3-standin-magic-skill)</c>, <c>StandInWorld.PlaceholderMagicSkill</c>).</summary>
    public const uint DefaultArenaSkill = 300;

    /// <summary>
    /// True: the arena is where the player already stands (M3 3.1 fd; not on the command line): no gear, no buff top-up, no teleport to the den, and the floor rule is off (any cell, not only <see cref="Lifestone.Client.Session.Mage.BlackSpawnDen"/>'s floor),
    /// so the offline stand-in's tusker, placed anywhere, is a target. The Flame Bolt level and the cast range then come from <see cref="ArenaSkill"/> alone (every level counts as known), not from the read model's War Magic skill and spellbook (the stand-in has no such skill, and its book is not updated by a spell learn).
    /// </summary>
    public bool ArenaHere { get; init; }

    /// <summary>With <see cref="ArenaHere"/>: the War Magic skill the Flame Bolt level and the range are worked out from (<see cref="DefaultArenaSkill"/>).</summary>
    public uint ArenaSkill { get; init; } = DefaultArenaSkill;

    /// <summary>True: the loop picks the nearest tusker with no check of the line to it (<c>--fight-ignore-line</c>; default false: a clear line is required, M3 4.2 P4).</summary>
    public bool IgnoreLine { get; init; }

    /// <summary>Every problem found while parsing, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the two options from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
    /// <param name="args">The user arguments.</param>
    public static FightOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new FightOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--fight-kills":
                    var kills = OptionValues.Int(argument, errors, o.Kills);
                    if (errors.Count == known && kills is >= 1 and <= MaxKills)
                    {
                        o = o with { Kills = kills };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxKills}");
                    }

                    break;
                case "--fight-seconds":
                    var seconds = OptionValues.Double(argument, errors, o.Seconds);
                    if (errors.Count == known && seconds > 0)
                    {
                        o = o with { Seconds = seconds };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                    }

                    break;
                case "--fight-ignore-line":
                    o = o with { IgnoreLine = OptionValues.Switch(argument, errors, o.IgnoreLine) };
                    break;
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"kills={Kills} seconds={Seconds}{(IgnoreLine ? " ignore_line=on" : string.Empty)}{(ArenaHere ? $" arena=here skill={ArenaSkill}" : string.Empty)}");
}
