using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Headless;

/// <summary>What a headless run does (<c>--scenario</c>).</summary>
internal enum HeadlessScenario
{
    /// <summary>Log in, enter the world, check the server mod's status, log off and quit (ST15).</summary>
    Login,

    /// <summary>Laps around the Holtburg lifestone with zero server corrections (ST16).</summary>
    HoltburgLaps,

    /// <summary>From the Training Academy's spawn to its exit portal with zero server corrections (ST16).</summary>
    AcademyExit,

    /// <summary>Find or create the test account's level-126 combat mage and bring it to the template (M3 1.1, <c>MageBuildScenario</c>); idempotent.</summary>
    MageBuild,

    /// <summary>Spawn BSD Mage's gear kit, wield its wand and wear its leather set (M3 1.4 s1, <c>GearScenario</c>); idempotent.</summary>
    Gear,

    /// <summary>Gear, then the buff cycle of the profile's 24 lines and a forced re-buff pass (M3 2.1 s1, <c>BuffScenario</c>); idempotent.</summary>
    Buff,
}

/// <summary>
/// The headless host's command line: its own group (<c>OptionCatalog.Headless</c>), the Session group
/// (<see cref="SessionOptions.FromArguments"/>) and the five Core options it shares with the
/// game. Arguments of no group, or of the game's scenes only, are problems (<see cref="OptionRegistry.Problems"/>), named
/// without their values.
/// </summary>
internal sealed record HeadlessOptions
{
    /// <summary>The default number of Holtburg laps.</summary>
    internal const int DefaultLaps = 3;

    /// <summary>The most laps accepted.</summary>
    internal const int MaxLaps = 1000;

    /// <summary><c>--help</c> was given.</summary>
    internal bool Help { get; init; }

    /// <summary>The scenario (<c>--scenario</c>, default login).</summary>
    internal HeadlessScenario Scenario { get; init; } = HeadlessScenario.Login;

    /// <summary>Holtburg laps (<c>--laps</c>).</summary>
    internal int Laps { get; init; } = DefaultLaps;

    /// <summary>The character to use, created first when missing (<c>--create-character</c>), or null.</summary>
    internal string? CreateCharacter { get; init; }

    /// <summary>Where to write the run summary JSON (<c>--summary</c>), or null.</summary>
    internal string? SummaryPath { get; init; }

    /// <summary>The dat folder (<c>--dat-dir</c>), or null for the default.</summary>
    internal string? DatDirectory { get; init; }

    /// <summary>The lowest severity logged (<c>--log-level</c>, default info).</summary>
    internal LogSeverity LogLevel { get; init; } = LogSeverity.Info;

    /// <summary>Seconds after which the run fails whatever happens (<c>--quit-after</c>), or null.</summary>
    internal double? QuitAfter { get; init; }

    /// <summary>Decoding worker threads (<c>--workers</c>), or null for the default.</summary>
    internal int? Workers { get; init; }

    /// <summary>The buff cycle's options (<c>--buff-*</c>), used by <c>--scenario=buff</c>.</summary>
    internal BuffOptions Buff { get; init; } = new();

    /// <summary>The Session group's values.</summary>
    internal SessionOptions Session { get; init; } = new();

    /// <summary>Every problem found, one line each, values never repeated.</summary>
    internal IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the headless command line.</summary>
    /// <param name="args">The process arguments.</param>
    internal static HeadlessOptions Parse(IEnumerable<string> args)
    {
        var arguments = CommandLineArgument.ParseAll(args);
        var errors = new List<string>(OptionCatalog.Registry.Problems(arguments, OptionScenes.Headless));
        var session = SessionOptions.FromArguments(arguments);
        errors.AddRange(session.Errors);
        var buff = BuffOptions.FromArguments(arguments);
        errors.AddRange(buff.Errors);
        var o = new HeadlessOptions();
        foreach (var argument in arguments)
        {
            switch (argument.Name)
            {
                case "--help":
                    o = o with { Help = true };
                    break;
                case "--scenario":
                    o = o with
                    {
                        Scenario = OptionValues.Choice(
                            argument,
                            errors,
                            o.Scenario,
                            ("login", HeadlessScenario.Login),
                            ("holtburg-laps", HeadlessScenario.HoltburgLaps),
                            ("academy-exit", HeadlessScenario.AcademyExit),
                            ("mage-build", HeadlessScenario.MageBuild),
                            ("gear", HeadlessScenario.Gear),
                            ("buff", HeadlessScenario.Buff)),
                    };
                    break;
                case "--laps":
                    var known = errors.Count;
                    var laps = OptionValues.Int(argument, errors, o.Laps);
                    if (errors.Count == known && laps is >= 1 and <= MaxLaps)
                    {
                        o = o with { Laps = laps };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxLaps}");
                    }

                    break;
                case "--create-character":
                    var name = argument.Value.Trim();
                    if (name.Length is 0 or > SessionOptions.MaxCharacterNameLength || !name.All(c => c is >= ' ' and <= '~'))
                    {
                        errors.Add($"{argument.Name}: needs a character name (1 to {SessionOptions.MaxCharacterNameLength} printable ASCII characters); the value is not shown");
                    }
                    else
                    {
                        o = o with { CreateCharacter = name };
                    }

                    break;
                case "--summary":
                    o = o with { SummaryPath = Required(argument, errors, "a file path") ?? o.SummaryPath };
                    break;
                case "--dat-dir":
                    o = o with { DatDirectory = Required(argument, errors, "a folder") ?? o.DatDirectory };
                    break;
                case "--log-level":
                    o = o with
                    {
                        LogLevel = OptionValues.Choice(
                            argument,
                            errors,
                            o.LogLevel,
                            ("debug", LogSeverity.Debug),
                            ("info", LogSeverity.Info),
                            ("warning", LogSeverity.Warning),
                            ("error", LogSeverity.Error)),
                    };
                    break;
                case "--quit-after":
                    var knownQuit = errors.Count;
                    var seconds = OptionValues.Double(argument, errors, 0);
                    if (errors.Count == knownQuit && seconds > 0)
                    {
                        o = o with { QuitAfter = seconds };
                    }
                    else if (errors.Count == knownQuit)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                    }

                    break;
                case "--workers":
                    var knownWorkers = errors.Count;
                    var workers = OptionValues.Int(argument, errors, 0);
                    if (errors.Count == knownWorkers && workers >= 1)
                    {
                        o = o with { Workers = workers };
                    }
                    else if (errors.Count == knownWorkers)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not at least 1");
                    }

                    break;
            }
        }

        if (o.Scenario is HeadlessScenario.MageBuild or HeadlessScenario.Gear or HeadlessScenario.Buff && session.Account is { } account && !string.Equals(account, Program.DefaultAccount, StringComparison.OrdinalIgnoreCase))
        {
            // M3 1.1-R5: the build creates and levels a permanent character, so it runs on the default test account only (automation uses accounts.test only).
            errors.Add($"--account: --scenario={(o.Scenario switch { HeadlessScenario.Gear => "gear", HeadlessScenario.Buff => "buff", _ => "mage-build" })} runs on the default test account '{Program.DefaultAccount}' only; the value is not shown");
        }

        return o with { Session = session, Buff = buff, Errors = errors };
    }

    private static string? Required(CommandLineArgument argument, List<string> errors, string what)
    {
        if (string.IsNullOrWhiteSpace(argument.Value))
        {
            errors.Add($"{argument.Name}: needs {what} (--name=VALUE)");
            return null;
        }

        return argument.Value;
    }
}
