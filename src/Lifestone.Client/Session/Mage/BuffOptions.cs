using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The buff cycle's six options (M3 2.1 s0b; plan decisions D10 and D24; M4 4.1 lane a; registered in <c>OptionCatalog.Buff.cs</c>): the cast chance a level must reach, the re-buff margin, the transfer tries of the mana loop and the
/// bound of its regeneration wait, and the re-buff driver's switch and check interval (<see cref="RebuffDriver"/>). Immutable; <see cref="FromArguments"/> is the parser both hosts call.
/// </summary>
public sealed record BuffOptions
{
    /// <summary>The default of <c>--buff-cast-chance</c> (D10).</summary>
    public const double DefaultCastChance = 0.9;

    /// <summary>The default of <c>--buff-rebuff-margin-s</c>: a line with less than this left is recast by a re-buff pass.</summary>
    public const double DefaultRebuffMarginSeconds = 300;

    /// <summary>The default of <c>--buff-transfer-tries</c>: the casts of Stamina to Mana tried until one fires (D24; gameplay, not a check retry).</summary>
    public const int DefaultTransferTries = 8;

    /// <summary>The default of <c>--buff-mana-wait-s</c>: the longest a named regeneration wait lasts before the cycle fails.</summary>
    public const double DefaultManaWaitSeconds = 180;

    /// <summary>The default of <c>--buff-auto-check-s</c>: how often the re-buff driver asks which lines are due.</summary>
    public const double DefaultAutoCheckSeconds = 5;

    /// <summary>The least <c>--buff-auto-check-s</c> accepts.</summary>
    public const double MinAutoCheckSeconds = 1;

    /// <summary>The ACE cast chance (<see cref="Scenarios.GearCasts.Chance"/>) a level must reach to be cast, in (0, 1] (<c>--buff-cast-chance</c>).</summary>
    public double CastChance { get; init; } = DefaultCastChance;

    /// <summary>The seconds a line may have left before a re-buff pass recasts it (<c>--buff-rebuff-margin-s</c>).</summary>
    public double RebuffMarginSeconds { get; init; } = DefaultRebuffMarginSeconds;

    /// <summary>The tries of Stamina to Mana (and of Revitalize) in a row that may fizzle before the cycle fails (<c>--buff-transfer-tries</c>).</summary>
    public int TransferTries { get; init; } = DefaultTransferTries;

    /// <summary>The bound of the regeneration wait, in seconds on the session clock (<c>--buff-mana-wait-s</c>).</summary>
    public double ManaWaitSeconds { get; init; } = DefaultManaWaitSeconds;

    /// <summary>True when the re-buff driver starts passes by itself (<c>--buff-auto on</c>; default off, M4 4.1).</summary>
    public bool Auto { get; init; }

    /// <summary>The seconds between the re-buff driver's checks, at least <see cref="MinAutoCheckSeconds"/> (<c>--buff-auto-check-s</c>).</summary>
    public double AutoCheckSeconds { get; init; } = DefaultAutoCheckSeconds;

    /// <summary>Every problem found while parsing, one line each, values never repeated beyond what the option parsers print.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the six options from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
    /// <param name="args">The user arguments.</param>
    public static BuffOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new BuffOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--buff-cast-chance":
                    var chance = OptionValues.Double(argument, errors, o.CastChance);
                    if (errors.Count == known && chance is > 0 and <= 1)
                    {
                        o = o with { CastChance = chance };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not more than 0 and at most 1");
                    }

                    break;
                case "--buff-rebuff-margin-s":
                    var margin = OptionValues.Double(argument, errors, o.RebuffMarginSeconds);
                    if (errors.Count == known && margin >= 0)
                    {
                        o = o with { RebuffMarginSeconds = margin };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is negative");
                    }

                    break;
                case "--buff-transfer-tries":
                    var tries = OptionValues.Int(argument, errors, o.TransferTries);
                    if (errors.Count == known && tries >= 1)
                    {
                        o = o with { TransferTries = tries };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not at least 1");
                    }

                    break;
                case "--buff-mana-wait-s":
                    var wait = OptionValues.Double(argument, errors, o.ManaWaitSeconds);
                    if (errors.Count == known && wait > 0)
                    {
                        o = o with { ManaWaitSeconds = wait };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                    }

                    break;
                case "--buff-auto":
                    o = o with { Auto = OptionValues.Switch(argument, errors, o.Auto) };
                    break;
                case "--buff-auto-check-s":
                    var check = OptionValues.Double(argument, errors, o.AutoCheckSeconds);
                    if (errors.Count == known && check >= MinAutoCheckSeconds)
                    {
                        o = o with { AutoCheckSeconds = check };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is under {MinAutoCheckSeconds:0} second");
                    }

                    break;
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"cast_chance={CastChance} rebuff_margin_s={RebuffMarginSeconds} transfer_tries={TransferTries} mana_wait_s={ManaWaitSeconds} auto={(Auto ? "on" : "off")} auto_check_s={AutoCheckSeconds}");
}
