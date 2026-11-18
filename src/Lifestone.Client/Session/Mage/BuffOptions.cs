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

    /// <summary>M5 7 F24, lowered by F25 (D93) from 120 s: the default of <c>--buff-hard-margin-s</c>, the hard floor: a line with less than this left (or already lapsed) is recast by the fight between its casts, whatever is in reach (the emergency case; the lookahead at a lull keeps the lines above it).</summary>
    public const double DefaultHardMarginSeconds = 30;

    /// <summary>M5 7 F25 (D93): the default of <c>--buff-lookahead-margin-s</c>, Lifestone's own: the seconds added to the fight's mean length to give the lookahead's horizon at a lull.</summary>
    public const double DefaultLookaheadMarginSeconds = 60;

    /// <summary>
    /// M5 7 F25 (D93): the default of <c>--buff-fight-seed-s</c>, the seed of the fight's mean length (from its first hit taken to the lull): soak round 3's den run, 39 engagements read from its hit lines (a 10 s quiet gap ends one,
    /// the lull at the last hit plus the 10 s quiet), a mean of 90 s (24 s to 272 s). The hit gaps do not see a creature standing in reach without hitting, so the measured lulls come later; the fight's own mean replaces the seed.
    /// </summary>
    public const double DefaultFightSeedSeconds = 90;

    /// <summary>
    /// D126 (a): one buff cycle in seconds, the start pass's margin on the autopilot (<c>--buff-start-margin-s</c>): the level-80 profile's lines are all VI, 2,700 s each (M5 7 F12; <c>ArenaProfile</c>'s durations), so a start's or a
    /// restart's pass recasts every line not cast within the pass itself, and no line left from before falls due on its own in the first fight (soak round 12's section 9 row <c>m5-start-pass-margin</c>).
    /// </summary>
    public const double OneBuffCycleSeconds = 2700;

    /// <summary>The default of <c>--buff-auto-check-s</c>: how often the re-buff driver asks which lines are due.</summary>
    public const double DefaultAutoCheckSeconds = 5;

    /// <summary>The least <c>--buff-auto-check-s</c> accepts.</summary>
    public const double MinAutoCheckSeconds = 1;

    /// <summary>The ACE cast chance (<see cref="Scenarios.GearCasts.Chance"/>) a level must reach to be cast, in (0, 1] (<c>--buff-cast-chance</c>).</summary>
    public double CastChance { get; init; } = DefaultCastChance;

    /// <summary>The seconds a line may have left before a re-buff pass recasts it (<c>--buff-rebuff-margin-s</c>).</summary>
    public double RebuffMarginSeconds { get; init; } = DefaultRebuffMarginSeconds;

    /// <summary>
    /// D126 (a): the margin of a start's pass (<see cref="BuffCycle.Start"/>, not a re-buff pass) in seconds (<c>--buff-start-margin-s</c>): an active line with more than this left is skipped. Null: the re-buff margin
    /// (<see cref="RebuffMarginSeconds"/>), as before. The autopilot's buff station sets <see cref="OneBuffCycleSeconds"/> unless the command line names one.
    /// </summary>
    public double? StartMarginSeconds { get; init; }

    /// <summary>M5 7 F24: the seconds a line may have left before the fight recasts it between its casts, with a hostile in reach or not (<c>--buff-hard-margin-s</c>; the soft margin <see cref="RebuffMarginSeconds"/> recasts only in a lull).</summary>
    public double HardMarginSeconds { get; init; } = DefaultHardMarginSeconds;

    /// <summary>M5 7 F25: the seconds added to the fight's mean length for the lookahead's horizon at a lull (<c>--buff-lookahead-margin-s</c>): a line with less left than the mean plus this is recast at the lull.</summary>
    public double LookaheadMarginSeconds { get; init; } = DefaultLookaheadMarginSeconds;

    /// <summary>
    /// M6 item 1 h1 (<c>m6-account-formula-callers</c>, the fight's side): the character's account name, lower case as the server stores it, or null. The level floor reads it for a school whose focus the pack does not hold (a school trained from
    /// the level-up credits has no starter focus: its casts burn the account's full formula, <see cref="AccountFormula"/>); null: the foci formula for every school (today's rule). Set by the scenario that knows the login
    /// (<c>FightScenario</c>'s account argument, <c>AutopilotScenario</c>'s journey context), never parsed from the command line, never printed (<see cref="ToString"/> leaves it out).
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// M6 7 AT: the fight's reach in metres for a pass in the ground (the autopilot's ground pass sets it from the aggro reach; 0, the default: off). An attackable creature within it that keeps her from lying down for the pass's wait cuts the pass (named, once for that creature),
    /// so the fight shoots it first and she rests after; one beyond it but inside the quiet distance keeps the wait (<see cref="Vitals.RestBlockerRule"/>).
    /// </summary>
    public double ShootFirstMetres { get; init; }

    /// <summary>
    /// M6 14 (<c>m6-banes</c>; not a command-line option): the DamageType bits the creatures of the field deal, for a cycle that runs in the field (a fight's refresh and top-up, the autopilot's pass in the ground:
    /// <see cref="ArmourBanes.FieldMask"/> of the ground); null (the default): a pass at home, which casts every armour line the book holds. With a value, one of the six other banes is wanted only when the mask holds
    /// its damage type (<see cref="BuffCycle.FieldDamageTypes"/>); Impenetrability, Bludgeon Bane and every other line are wanted as before.
    /// </summary>
    public uint? FieldDamageTypes { get; init; }

    /// <summary>M6 7 AT: where a pass notes the components it found the pack short of (<see cref="Mage.ComponentNeeds"/>), so the logistics counters call the trip that buys them; null (the default): the pass notes nothing. The autopilot's own passes get the session's.</summary>
    public ComponentNeeds? ComponentNeeds { get; init; }

    /// <summary>M5 7 F25: the seed of the fight's mean length, in seconds (<c>--buff-fight-seed-s</c>), until the fight has measured its own.</summary>
    public double FightSeedSeconds { get; init; } = DefaultFightSeedSeconds;

    /// <summary>The tries of Stamina to Mana (and of Revitalize) in a row that may fizzle before the cycle fails (<c>--buff-transfer-tries</c>).</summary>
    public int TransferTries { get; init; } = DefaultTransferTries;

    /// <summary>The bound of the regeneration wait, in seconds on the session clock (<c>--buff-mana-wait-s</c>).</summary>
    public double ManaWaitSeconds { get; init; } = DefaultManaWaitSeconds;

    /// <summary>True when the re-buff driver starts passes by itself (<c>--buff-auto on</c>; default off, M4 4.1).</summary>
    public bool Auto { get; init; }

    /// <summary>
    /// M5 7 F23 (not a command-line option): true when the re-buff driver runs inside the autopilot's own buff or upkeep station (the journey's buff station with <c>JourneyStations.UnattendedArgument</c>). Its <c>in a fight</c> guard
    /// then never holds the pass: the guard waits only when a person runs the driver (soak round 2: the upkeep stood still for 50 s, "the pass waits: in a fight", with a Tusker Guard on the mage).
    /// </summary>
    public bool DriverUnattended { get; init; }

    /// <summary>
    /// M5 7 P7c (not a command-line option; D139 row a): true (the default) when a start's pass keeps the reserve, the mana of the next Stamina to Mana press, past every line and dances while a press is sure
    /// (<c>BuffCycle.PassDance</c>). False restores the pass of before (the dance only before the first line, under its cost); tests whose timing is tuned to that economy set it.
    /// </summary>
    public bool StartReserve { get; init; } = true;

    /// <summary>The seconds between the re-buff driver's checks, at least <see cref="MinAutoCheckSeconds"/> (<c>--buff-auto-check-s</c>).</summary>
    public double AutoCheckSeconds { get; init; } = DefaultAutoCheckSeconds;

    /// <summary>The mana order's options (M5 1 K5, D15 and D27: the <c>--cast-mana-*</c> options, shared with the vitals keeper); <see cref="Vitals.ManaOrderOptions.Ceiling"/> is today's ceiling order.</summary>
    public Vitals.ManaOrderOptions Mana { get; init; } = Vitals.ManaOrderOptions.Default;

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
                case "--buff-start-margin-s":
                    var start = OptionValues.Double(argument, errors, o.StartMarginSeconds ?? o.RebuffMarginSeconds);
                    if (errors.Count == known && start >= 0)
                    {
                        o = o with { StartMarginSeconds = start };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is negative");
                    }

                    break;
                case "--buff-hard-margin-s":
                    var hard = OptionValues.Double(argument, errors, o.HardMarginSeconds);
                    if (errors.Count == known && hard >= 0)
                    {
                        o = o with { HardMarginSeconds = hard };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is negative");
                    }

                    break;
                case "--buff-lookahead-margin-s":
                    var ahead = OptionValues.Double(argument, errors, o.LookaheadMarginSeconds);
                    if (errors.Count == known && ahead >= 0)
                    {
                        o = o with { LookaheadMarginSeconds = ahead };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is negative");
                    }

                    break;
                case "--buff-fight-seed-s":
                    var seed = OptionValues.Double(argument, errors, o.FightSeedSeconds);
                    if (errors.Count == known && seed > 0)
                    {
                        o = o with { FightSeedSeconds = seed };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not more than 0");
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
                default:
                    var mana = o.Mana;
                    if (Vitals.ManaOrderOptions.Apply(argument, errors, ref mana))
                    {
                        o = o with { Mana = mana };
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
