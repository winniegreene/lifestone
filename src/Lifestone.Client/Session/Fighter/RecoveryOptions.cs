using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The recovery rule's options (<c>--heal</c>, <c>--heal-below</c>, <c>--heal-until</c>, <c>--heal-chance</c>, <c>--heal-floor-chance</c>; M4 3.1 s3 and the fix slice), parsed from the command line like <see cref="LootOptions"/>, and carried by
/// <see cref="Scenarios.HuntOptions.Recovery"/> so the headless host and the hunt leg read them with the hunt's own options. A value that does not parse is reported in <see cref="Errors"/> and the option keeps its default.
/// </summary>
public sealed record RecoveryOptions
{
    /// <summary>The default share (percent of the maximum health) under which a recovery starts: between kills and before the first fight (<c>--heal-below</c>).</summary>
    public const int DefaultBelowPercent = 75;

    /// <summary>The default share (percent) a started recovery heals to (<c>--heal-until</c>).</summary>
    public const int DefaultUntilPercent = 90;

    /// <summary>The default least estimated chance at which the kit is used (<c>--heal-chance</c>; capture.md C1: 5 of 6 uses failed at a chance of about 0.17).</summary>
    public const double DefaultChance = 0.7;

    /// <summary>The default floor of the last-resort kit (<c>--heal-floor-chance</c>; <see cref="RecoveryRules.KitFloorChance"/>).</summary>
    public const double DefaultFloorChance = RecoveryRules.KitFloorChance;

    /// <summary>The default options.</summary>
    public static RecoveryOptions Default { get; } = new();

    /// <summary>True (default) when the hunt recovers between its kills and before its first fight (<c>--heal</c>); off runs no recovery at all (the retreat at 30% still protects).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The share of the maximum health, in percent, under which a recovery starts (<c>--heal-below</c>, 1 to 100).</summary>
    public int BelowPercent { get; init; } = DefaultBelowPercent;

    /// <summary>The share, in percent, a started recovery heals to (<c>--heal-until</c>, 1 to 100, not under <see cref="BelowPercent"/>).</summary>
    public int UntilPercent { get; init; } = DefaultUntilPercent;

    /// <summary>The least estimated chance at which the kit is used (<c>--heal-chance</c>, 0 to 1): under it a potion is used, or the kit as the last resort (<see cref="FloorChance"/>), or the recovery waits.</summary>
    public double Chance { get; init; } = DefaultChance;

    /// <summary>
    /// The least estimated chance at which the kit is used as the last resort (<c>--heal-floor-chance</c>, 0 to 1; M4 3.1 fix, review R1): with no potion to drink and the kit's chance under <see cref="Chance"/>, a kit at or over this is used
    /// before the regeneration wait. Not above <see cref="Chance"/> in effect (a kit at <see cref="Chance"/> is used first-hand).
    /// </summary>
    public double FloorChance { get; init; } = DefaultFloorChance;

    /// <summary>Every problem found, one line each, values never repeated beyond the option's own text.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary><see cref="BelowPercent"/> as a share (0 to 1).</summary>
    public double Below => BelowPercent / 100.0;

    /// <summary><see cref="UntilPercent"/> as a share (0 to 1).</summary>
    public double Until => UntilPercent / 100.0;

    /// <summary>Reads the recovery options out of the parsed arguments (the others are ignored).</summary>
    public static RecoveryOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new RecoveryOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            Apply(argument, errors, ref o);
        }

        Validate(o, errors);
        return o with { Errors = errors };
    }

    /// <summary>
    /// Applies <paramref name="argument"/> to <paramref name="options"/> when it is a recovery option; a bad value adds a line to <paramref name="errors"/> and leaves the option as it was.
    /// </summary>
    /// <returns>True when the argument is one of the recovery options (good or bad).</returns>
    public static bool Apply(CommandLineArgument argument, List<string> errors, ref RecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var known = errors.Count;
        switch (argument.Name)
        {
            case "--heal":
                options = options with { Enabled = OptionValues.Switch(argument, errors, options.Enabled) };
                return true;
            case "--heal-below":
                var below = OptionValues.Int(argument, errors, options.BelowPercent);
                if (errors.Count == known && below is >= 1 and <= 100)
                {
                    options = options with { BelowPercent = below };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to 100 percent");
                }

                return true;
            case "--heal-until":
                var until = OptionValues.Int(argument, errors, options.UntilPercent);
                if (errors.Count == known && until is >= 1 and <= 100)
                {
                    options = options with { UntilPercent = until };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to 100 percent");
                }

                return true;
            case "--heal-chance":
                var chance = OptionValues.Double(argument, errors, options.Chance);
                if (errors.Count == known && chance is >= 0 and <= 1)
                {
                    options = options with { Chance = chance };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a chance from 0 to 1");
                }

                return true;
            case "--heal-floor-chance":
                var floor = OptionValues.Double(argument, errors, options.FloorChance);
                if (errors.Count == known && floor is >= 0 and <= 1)
                {
                    options = options with { FloorChance = floor };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a chance from 0 to 1");
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Adds the cross-option problem, if any: a recovery that heals to a share under the one it starts at.</summary>
    /// <param name="options">The options as parsed.</param>
    /// <param name="errors">Where the problem goes.</param>
    public static void Validate(RecoveryOptions options, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(errors);
        if (options.UntilPercent < options.BelowPercent)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"--heal-until: {options.UntilPercent} is under --heal-below {options.BelowPercent} (a recovery cannot stop under the share it starts at)"));
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"heal={(Enabled ? "on" : "off")} heal_below={BelowPercent}% heal_until={UntilPercent}% heal_chance={Chance:0.##} heal_floor_chance={FloorChance:0.##}");
}
