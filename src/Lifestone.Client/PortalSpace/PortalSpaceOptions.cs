using Lifestone.Client.Options;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The Portal space option group's values (docs/portal-space.md §12; registered with their help in
/// <c>OptionCatalog.PortalSpace</c>), as the game's portal view parses them (<see cref="Parse(IEnumerable{string})"/>) and
/// the headless host may read its one option (<c>--retail-logoff-tunnel</c>).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--portal-transition=on|off</term><description>Play portal space when the teleport panel jumps (default on). Check suites,
/// golden viewpoints and tours never play it (they install no portal view).</description></item>
/// <item><term>--portal-space</term><description>Hold the tunnel in state 3 until quit, for review.</description></item>
/// <item><term>--portal-space-frames[=DIR]</term><description>Play a scripted sequence at a fixed step and seed, capture four frames to DIR
/// (default <see cref="DefaultFramesDirectory"/>) and quit.</description></item>
/// <item><term>--retail-logoff-tunnel</term><description>Retail's logoff tunnel (D28): the tunnel ends and the world shows again before the
/// character list arrives. Default: hold the tunnel until the list arrives.</description></item>
/// </list>
/// </remarks>
public sealed record PortalSpaceOptions
{
    /// <summary>The default folder for <c>--portal-space-frames</c>, relative to the repository root (gitignored).</summary>
    public const string DefaultFramesDirectory = "docs/screenshots/m2/portal-space";

    /// <summary>The offline teleport panel plays portal space (<c>--portal-transition</c>, default on).</summary>
    public bool Transition { get; init; } = true;

    /// <summary>Hold the tunnel in state 3 until quit (<c>--portal-space</c>).</summary>
    public bool Hold { get; init; }

    /// <summary>The folder the scripted sequence captures into, or null when <c>--portal-space-frames</c> was not given.</summary>
    public string? FramesDirectory { get; init; }

    /// <summary>Retail's logoff tunnel instead of holding it until the character list arrives (<c>--retail-logoff-tunnel</c>, D28).</summary>
    public bool RetailLogoffTunnel { get; init; }

    /// <summary>The policy the state machine is built with for <see cref="RetailLogoffTunnel"/>.</summary>
    public LogOffTunnelPolicy LogOffPolicy => RetailLogoffTunnel ? LogOffTunnelPolicy.Retail : LogOffTunnelPolicy.HoldUntilCharacterList;

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Parses the group's options with the shared tokenizer and value parsers (<see cref="CommandLineArgument"/>,
    /// <see cref="OptionValues"/>): a value that does not parse is added to <see cref="Errors"/> and the option keeps its
    /// default. Other groups' arguments are skipped.
    /// </summary>
    public static PortalSpaceOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new PortalSpaceOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--portal-transition":
                    options = options with { Transition = OptionValues.Switch(argument, errors, options.Transition) };
                    break;
                case "--portal-space":
                    options = options with { Hold = true };
                    break;
                case "--portal-space-frames":
                    options = options with { FramesDirectory = argument.Value.Length == 0 ? DefaultFramesDirectory : argument.Value };
                    break;
                case "--retail-logoff-tunnel":
                    options = options with { RetailLogoffTunnel = true };
                    break;
            }
        }

        return options with { Errors = errors };
    }
}
