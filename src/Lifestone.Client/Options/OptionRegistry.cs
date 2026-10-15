using System.Text;

namespace Lifestone.Client.Options;

/// <summary>
/// Every command-line option Lifestone knows, registered by feature with its help text. The single answer to "is this
/// argument known?", "is it read in this scene?" and "does this run end by itself?", and the source of <c>--help</c>.
/// </summary>
/// <remarks>
/// <para>Each feature registers its options as one <see cref="OptionGroup"/> (see <see cref="OptionCatalog"/>); a name
/// may be registered once only, so two features cannot give the same option different meanings by accident. Parsing
/// the values stays with each feature (its own record of settings), using <see cref="CommandLineArgument"/> and
/// <see cref="OptionValues"/>; an argument no group registers is unknown everywhere.</para>
/// <para><b>Scenes.</b> Each group (and option) says which scenes read it (<see cref="OptionScenes"/>). In a run of
/// another scene the option is a problem like an unknown one (<see cref="Problems"/>: reported, ignored) and does not
/// make the run automated (<see cref="EndsRun"/>): the terrain preview's <c>--golden</c> given to the main scene must
/// not put it into automated mode with nothing that ends the run.</para>
/// <para>Engine-agnostic and immutable after construction; thread-safe for reads.</para>
/// </remarks>
public sealed class OptionRegistry
{
    /// <summary>How <see cref="FormatHelp(string)"/> marks the options that make a run automated.</summary>
    public const string EndsRunMarker = "[ends run]";

    /// <summary>What <see cref="Problems"/> shows in place of an argument's value.</summary>
    public const string ValueHidden = "(value hidden)";

    private readonly Dictionary<string, (OptionGroup Group, OptionSpec Spec)> _byName = new(StringComparer.Ordinal);

    /// <summary>Builds a registry from feature groups.</summary>
    /// <exception cref="ArgumentException">An option name is malformed or registered twice, or an option is read in no scene.</exception>
    public OptionRegistry(IEnumerable<OptionGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Groups = groups.ToArray();
        foreach (var group in Groups)
        {
            foreach (var spec in group.Options)
            {
                if (!IsWellFormedName(spec.Name))
                {
                    throw new ArgumentException($"option name '{spec.Name}' ({group.Feature}) must be '--' followed by lower-case letters, digits and dashes", nameof(groups));
                }

                if ((group.Scenes | spec.AlsoIn) == OptionScenes.None)
                {
                    throw new ArgumentException($"option '{spec.Name}' ({group.Feature}) is read in no scene", nameof(groups));
                }

                if (!_byName.TryAdd(spec.Name, (group, spec)))
                {
                    throw new ArgumentException($"option '{spec.Name}' is registered by both '{_byName[spec.Name].Group.Feature}' and '{group.Feature}'", nameof(groups));
                }
            }
        }
    }

    /// <summary>The registered groups, in help order.</summary>
    public IReadOnlyList<OptionGroup> Groups { get; }

    /// <summary>Every registered option name.</summary>
    public IEnumerable<string> Names => _byName.Keys;

    /// <summary>True when some feature registered <paramref name="name"/>.</summary>
    public bool IsKnown(string name) => _byName.ContainsKey(name);

    /// <summary>The option registered as <paramref name="name"/>, or null.</summary>
    public OptionSpec? Find(string name) => _byName.TryGetValue(name, out var entry) ? entry.Spec : null;

    /// <summary>The feature that registered <paramref name="name"/>, or null.</summary>
    public string? FeatureOf(string name) => _byName.TryGetValue(name, out var entry) ? entry.Group.Feature : null;

    /// <summary>The scenes that read <paramref name="name"/> (its group's and its own), or <see cref="OptionScenes.None"/> when unknown.</summary>
    public OptionScenes ScenesOf(string name) => _byName.TryGetValue(name, out var entry) ? entry.Group.Scenes | entry.Spec.AlsoIn : OptionScenes.None;

    /// <summary>True when <paramref name="name"/> is registered and read in a run of <paramref name="scene"/>.</summary>
    public bool AppliesTo(string name, OptionScenes scene) => (ScenesOf(name) & scene) != OptionScenes.None;

    /// <summary>
    /// One message per argument a run of <paramref name="scene"/> cannot use, in order: arguments no feature registered
    /// (truly unknown) and options only other scenes read.
    /// </summary>
    /// <remarks>
    /// The messages never repeat a value: an argument is named by its option name, with <c>=(value hidden)</c> when it had
    /// a value, so a password typed into a mistyped option (<c>--pasword=...</c>) reaches no log or console. An argument
    /// that is not shaped like an option at all (a stray word, perhaps a secret given without its option) is named only by
    /// its position.
    /// </remarks>
    public IReadOnlyList<string> Problems(IEnumerable<CommandLineArgument> args, OptionScenes scene)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        var position = 0;
        foreach (var argument in args)
        {
            position++;
            if (!_byName.TryGetValue(argument.Name, out var entry))
            {
                problems.Add(LooksLikeAnOption(argument.Name)
                    ? $"unknown argument {Named(argument)} (--help lists every option)"
                    : $"unknown argument #{position}, which is not an --option (hidden; --help lists every option)");
            }
            else if (!AppliesTo(argument.Name, scene))
            {
                problems.Add($"{Named(argument)} is an option of the {entry.Group.Feature} ({entry.Group.Scope}), not read in this run's {SceneName(scene)}; ignored");
            }
        }

        return problems;
    }

    /// <summary>
    /// True when one of <paramref name="args"/> makes a run of <paramref name="scene"/> end by itself
    /// (<see cref="OptionSpec.EndsRun"/>); options of other scenes do not count.
    /// </summary>
    public bool EndsRun(IEnumerable<CommandLineArgument> args, OptionScenes scene) =>
        args.Any(a => Find(a.Name)?.EndsRun == true && AppliesTo(a.Name, scene));

    /// <summary>A scene as the messages name it.</summary>
    public static string SceneName(OptionScenes scene) => scene switch
    {
        OptionScenes.Main => "main scene",
        OptionScenes.SpellViewer => "spell viewer",
        OptionScenes.TerrainPreview => "terrain preview",
        OptionScenes.Headless => "headless host",
        _ => "scene",
    };

    /// <summary>
    /// The <c>--help</c> text: every group with its scope and every option with its help line. A group with no options
    /// yet (registered ahead of its owner's options) is left out.
    /// </summary>
    public string FormatHelp(string? preamble = null) => FormatHelpWhere(preamble, static (_, _) => true);

    /// <summary>
    /// The help text of one run kind: only the options a run of <paramref name="scene"/> reads (<see cref="AppliesTo"/>),
    /// under their groups; groups with none are left out. The headless host prints its <c>--help</c> this way.
    /// </summary>
    public string FormatHelp(string? preamble, OptionScenes scene) =>
        FormatHelpWhere(preamble, (group, spec) => ((group.Scenes | spec.AlsoIn) & scene) != OptionScenes.None);

    private string FormatHelpWhere(string? preamble, Func<OptionGroup, OptionSpec, bool> include)
    {
        var shown = Groups
            .Select(g => (Group: g, Options: g.Options.Where(o => include(g, o)).ToArray()))
            .Where(g => g.Options.Length > 0)
            .ToArray();
        var width = Math.Min(40, shown.SelectMany(g => g.Options).Select(o => o.Usage.Length).DefaultIfEmpty(0).Max() + 2);
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(preamble))
        {
            sb.AppendLine(preamble).AppendLine();
        }

        foreach (var (group, options) in shown)
        {
            sb.Append(group.Feature).Append(" (").Append(group.Scope).AppendLine(")");
            foreach (var spec in options)
            {
                var usage = spec.Usage;
                sb.Append("  ").Append(usage);
                if (usage.Length + 2 > width)
                {
                    sb.AppendLine().Append(' ', width + 2);
                }
                else
                {
                    sb.Append(' ', width - usage.Length);
                }

                sb.Append(spec.Help);
                sb.AppendLine(spec.Role is OptionRole.EndsRun or OptionRole.CheckSuite ? " " + EndsRunMarker : string.Empty);
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + System.Environment.NewLine;
    }

    private static bool IsWellFormedName(string name) =>
        name.Length > 2
        && name.StartsWith("--", StringComparison.Ordinal)
        && name[2] != '-'
        && name.Skip(2).All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>
    /// True when an unknown argument's name may be shown: <c>--</c> and up to 60 ASCII letters, digits, dashes or
    /// underscores (a mistyped or mis-cased option). Anything else may be a value given without its option.
    /// </summary>
    private static bool LooksLikeAnOption(string name) =>
        name.Length is > 2 and <= 62
        && name.StartsWith("--", StringComparison.Ordinal)
        && name.Skip(2).All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_');

    /// <summary>An argument as <see cref="Problems"/> names it: <c>'--name'</c>, or <c>'--name=(value hidden)'</c> when it had a value.</summary>
    private static string Named(CommandLineArgument argument) =>
        argument.HasValue ? $"'{argument.Name}={ValueHidden}'" : $"'{argument.Name}'";
}
