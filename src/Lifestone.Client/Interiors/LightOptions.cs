using Lifestone.Client.Options;

namespace Lifestone.Client.Interiors;

/// <summary>The Lights option group's values (registered with their help in <c>OptionCatalog.Lights</c>).</summary>
/// <remarks><c>--viewer-light=on|off</c> (default on): sets <see cref="DynamicLightList.ViewerLightEnabled"/>.</remarks>
public sealed record LightOptions
{
    /// <summary>The viewer light takes part in the dynamic list (<c>--viewer-light</c>, default on).</summary>
    public bool ViewerLight { get; init; } = true;

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the group's options; other groups' arguments are skipped.</summary>
    public static LightOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new LightOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            if (argument.Name == "--viewer-light")
            {
                options = options with { ViewerLight = OptionValues.Switch(argument, errors, options.ViewerLight) };
            }
        }

        return options with { Errors = errors };
    }
}
