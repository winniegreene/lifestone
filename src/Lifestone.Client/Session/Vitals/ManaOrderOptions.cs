using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Casting;

namespace Lifestone.Client.Session.Vitals;

/// <summary>The options of the mana order and the Mana Conversion tries (M5 D15, D27; <c>--cast-mana-*</c>, OptionCatalog.Vitals.cs), read by the vitals keeper and the buff cycle (and F1's fight).</summary>
public sealed record ManaOrderOptions
{
    /// <summary>The default of <c>--cast-mana-tries</c> (D15: at most 8 tries a need; <see cref="CastRules.DefaultManaTries"/>).</summary>
    public const int DefaultTries = CastRules.DefaultManaTries;

    /// <summary>The most <c>--cast-mana-tries</c> accepts (Lifestone's own bound: 40 tries is ten seconds of presses at the default spacing).</summary>
    public const int MaxTries = 40;

    /// <summary>The default of <c>--cast-mana-try-chance</c> (D15: a try lands at least a quarter of the time; <see cref="ManaConversionModel.DefaultTryChance"/>).</summary>
    public const double DefaultTryChance = ManaConversionModel.DefaultTryChance;

    /// <summary>The default of <c>--cast-mana-try-spacing-s</c> (a person's click rate; <see cref="CastRules.DefaultManaTrySpacingSeconds"/>).</summary>
    public const double DefaultTrySpacingSeconds = CastRules.DefaultManaTrySpacingSeconds;

    /// <summary>The default of <c>--cast-mana-dance-presses</c> (Lifestone's own: at item 0's worst live run, 8 refusals before a landing, 40 presses leave a fivefold margin).</summary>
    public const int DefaultDancePresses = 40;

    /// <summary>The prefix of every parse error (a bad value fails the run at once, as the fight's options do).</summary>
    public const string ErrorPrefix = "bad cast option: ";

    /// <summary>The defaults.</summary>
    public static ManaOrderOptions Default { get; } = new();

    /// <summary>Today's ceiling order (<c>--cast-mana-tries 0</c>): no tries, no dance.</summary>
    public static ManaOrderOptions Ceiling { get; } = new() { Tries = 0 };

    /// <summary>The 0x0401 refusals one need absorbs (<c>--cast-mana-tries</c>, 0 to 40; 0 restores today's ceiling order: no tries, no dance).</summary>
    public int Tries { get; init; } = DefaultTries;

    /// <summary>The landing chance the try floor asks of a try (<c>--cast-mana-try-chance</c>, above 0 and at most 1).</summary>
    public double TryChance { get; init; } = DefaultTryChance;

    /// <summary>The wait after a refusal before the next try or press (<c>--cast-mana-try-spacing-s</c>, 0.1 to 5).</summary>
    public double TrySpacingSeconds { get; init; } = DefaultTrySpacingSeconds;

    /// <summary>The presses of one dance in all (<c>--cast-mana-dance-presses</c>, 1 to 100).</summary>
    public int DancePresses { get; init; } = DefaultDancePresses;

    /// <summary>True unless <c>--cast-mana-tries 0</c>.</summary>
    public bool On => Tries > 0;

    /// <summary>Every problem found, one line each, each starting <see cref="ErrorPrefix"/>.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Reads the <c>--cast-mana-*</c> options (the others are ignored).</summary>
    public static ManaOrderOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new ManaOrderOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            Apply(argument, errors, ref o);
        }

        return o with { Errors = errors };
    }

    /// <summary>Applies <paramref name="argument"/> when it is a <c>--cast-mana-*</c> option; a bad value adds a line starting <see cref="ErrorPrefix"/> and leaves the option as it was.</summary>
    /// <returns>True when the argument is one of these options (good or bad).</returns>
    public static bool Apply(CommandLineArgument argument, List<string> errors, ref ManaOrderOptions options)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(options);
        var scratch = new List<string>(1);
        switch (argument.Name)
        {
            case "--cast-mana-tries":
                var tries = OptionValues.Int(argument, scratch, options.Tries);
                if (scratch.Count == 0 && tries is >= 0 and <= MaxTries)
                {
                    options = options with { Tries = tries };
                }
                else
                {
                    errors.Add(ErrorPrefix + (scratch.Count > 0 ? scratch[0] : $"{argument.Name}: '{argument.Value}' is not 0 to {MaxTries}"));
                }

                return true;
            case "--cast-mana-try-chance":
                var chance = OptionValues.Double(argument, scratch, options.TryChance);
                if (scratch.Count == 0 && chance is > 0 and <= 1)
                {
                    options = options with { TryChance = chance };
                }
                else
                {
                    errors.Add(ErrorPrefix + (scratch.Count > 0 ? scratch[0] : $"{argument.Name}: '{argument.Value}' is not more than 0 and at most 1"));
                }

                return true;
            case "--cast-mana-try-spacing-s":
                var spacing = OptionValues.Double(argument, scratch, options.TrySpacingSeconds);
                if (scratch.Count == 0 && spacing is >= 0.1 and <= 5)
                {
                    options = options with { TrySpacingSeconds = spacing };
                }
                else
                {
                    errors.Add(ErrorPrefix + (scratch.Count > 0 ? scratch[0] : $"{argument.Name}: '{argument.Value}' is not 0.1 to 5 seconds"));
                }

                return true;
            case "--cast-mana-dance-presses":
                var presses = OptionValues.Int(argument, scratch, options.DancePresses);
                if (scratch.Count == 0 && presses is >= 1 and <= 100)
                {
                    options = options with { DancePresses = presses };
                }
                else
                {
                    errors.Add(ErrorPrefix + (scratch.Count > 0 ? scratch[0] : $"{argument.Name}: '{argument.Value}' is not 1 to 100"));
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>The options as the run logs them.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"cast_mana_tries={Tries} cast_mana_try_chance={TryChance} cast_mana_try_spacing_s={TrySpacingSeconds} cast_mana_dance_presses={DancePresses}");
}
