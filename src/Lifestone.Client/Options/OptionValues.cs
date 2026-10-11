using System.Globalization;

namespace Lifestone.Client.Options;

/// <summary>
/// Value parsers shared by the option parsers, with one rule: a value that does not parse is reported in the caller's
/// error list (never thrown, never silently replaced by something else) and the option keeps its fallback.
/// </summary>
public static class OptionValues
{
    /// <summary>An integer (invariant culture).</summary>
    public static int Int(CommandLineArgument argument, List<string> errors, int fallback)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (int.TryParse(argument.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }

        errors.Add($"{argument.Name}: '{argument.Value}' is not an integer");
        return fallback;
    }

    /// <summary>A finite number (invariant culture).</summary>
    public static double Double(CommandLineArgument argument, List<string> errors, double fallback)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (double.TryParse(argument.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
        {
            return v;
        }

        errors.Add($"{argument.Name}: '{argument.Value}' is not a number");
        return fallback;
    }

    /// <summary>
    /// An on/off switch: no value, <c>on</c>, <c>true</c>, <c>yes</c> or <c>1</c> is on; <c>off</c>, <c>false</c>,
    /// <c>no</c> or <c>0</c> is off; anything else is an error.
    /// </summary>
    public static bool Switch(CommandLineArgument argument, List<string> errors, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(errors);
        switch (argument.Value)
        {
            case "" or "on" or "true" or "yes" or "1":
                return true;
            case "off" or "false" or "no" or "0":
                return false;
            default:
                errors.Add($"{argument.Name}: '{argument.Value}' is not on or off");
                return fallback;
        }
    }

    /// <summary>One of a fixed set of words (exact, lower case); anything else is an error listing the choices.</summary>
    public static T Choice<T>(CommandLineArgument argument, List<string> errors, T fallback, params (string Text, T Value)[] choices)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(choices);
        foreach (var (text, value) in choices)
        {
            if (string.Equals(argument.Value, text, StringComparison.Ordinal))
            {
                return value;
            }
        }

        errors.Add($"{argument.Name}: '{argument.Value}' is not one of {string.Join(", ", choices.Select(c => c.Text).Distinct())}");
        return fallback;
    }
}
