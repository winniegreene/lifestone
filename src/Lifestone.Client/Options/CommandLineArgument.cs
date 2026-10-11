namespace Lifestone.Client.Options;

/// <summary>
/// One user argument split into its name and value: <c>--name=value</c>, or <c>--name</c> alone (an empty value). The
/// one tokenizer every Lifestone option parser shares, so all of them agree on what an argument is.
/// </summary>
/// <param name="Raw">The argument as given, trimmed.</param>
/// <param name="Name">Everything before the first <c>=</c> (the whole argument when there is none).</param>
/// <param name="Value">Everything after the first <c>=</c>, without surrounding double quotes; empty when there is none.</param>
/// <param name="HasValue">True when the argument contained <c>=</c> (so <c>--x=</c> has an empty value but has one).</param>
public readonly record struct CommandLineArgument(string Raw, string Name, string Value, bool HasValue)
{
    /// <summary>Splits one argument. Never throws for any input.</summary>
    public static CommandLineArgument Parse(string? raw)
    {
        var arg = (raw ?? string.Empty).Trim();
        var eq = arg.IndexOf('=', StringComparison.Ordinal);
        return eq < 0
            ? new CommandLineArgument(arg, arg, string.Empty, false)
            : new CommandLineArgument(arg, arg[..eq], arg[(eq + 1)..].Trim('"'), true);
    }

    /// <summary>Splits every argument, skipping empty ones.</summary>
    public static IReadOnlyList<CommandLineArgument> ParseAll(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var result = new List<CommandLineArgument>();
        foreach (var raw in args)
        {
            var argument = Parse(raw);
            if (argument.Raw.Length > 0)
            {
                result.Add(argument);
            }
        }

        return result;
    }
}
