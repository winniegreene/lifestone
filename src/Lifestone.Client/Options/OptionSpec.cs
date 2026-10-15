namespace Lifestone.Client.Options;

/// <summary>What an option does to the run as a whole (beyond its own setting).</summary>
public enum OptionRole
{
    /// <summary>A setting; the run stays interactive.</summary>
    Setting,

    /// <summary>The run ends by itself (a screenshot, a measurement, a harness): an automated run.</summary>
    EndsRun,

    /// <summary>
    /// An engine-behaviour check suite (implies <see cref="EndsRun"/>): it owns the render globals, so nothing live (the
    /// sky clock, streaming layers) may write them while it runs. One suite per process.
    /// </summary>
    CheckSuite,

    /// <summary><c>--help</c>: print every option and quit.</summary>
    Help,
}

/// <summary>
/// The scenes an option is read in: the game's (a run shows one of them; <see cref="OptionCatalog.SceneOf"/>) and the
/// headless host, which is not a Godot scene but reads the same registry.
/// </summary>
[Flags]
public enum OptionScenes
{
    /// <summary>No scene.</summary>
    None = 0,

    /// <summary>scenes/Main.tscn: the streamed world.</summary>
    Main = 1,

    /// <summary>scenes/SpellViewer.tscn (<c>--scene=spellviewer</c> from the main scene).</summary>
    SpellViewer = 2,

    /// <summary>scenes/TerrainPreview.tscn (tools\terrain-golden.ps1).</summary>
    TerrainPreview = 4,

    /// <summary>
    /// The headless session host (<c>dotnet Lifestone.Headless.dll</c>, src/Lifestone.Headless). Not part of
    /// <see cref="All"/>: an option the headless host also reads says so with <see cref="OptionSpec.AlsoIn"/>, so the
    /// game's rendering options (<c>--vsync</c> and the like) are reported there instead of silently ignored.
    /// </summary>
    Headless = 8,

    /// <summary>Every Godot scene (also: a run whose scene is not one of the above, where no option is out of place).</summary>
    All = Main | SpellViewer | TerrainPreview,
}

/// <summary>One command-line option: its name, its value syntax and its one-line help.</summary>
/// <param name="Name">The option name with its dashes, e.g. <c>--load-radius</c>.</param>
/// <param name="Value">The value syntax shown in the help (e.g. <c>N</c>, <c>on|off</c>), or null for a flag.</param>
/// <param name="Help">One line of help, including the default.</param>
/// <param name="Role">What the option does to the run.</param>
/// <param name="ValueOptional">True when the option may also be given without a value (<c>--golden</c> and <c>--golden=DIR</c>).</param>
/// <param name="AlsoIn">Scenes beyond its group's that also read the option (<c>--seamless-normals</c> in the terrain preview).</param>
public sealed record OptionSpec(string Name, string? Value, string Help, OptionRole Role = OptionRole.Setting, bool ValueOptional = false, OptionScenes AlsoIn = OptionScenes.None)
{
    /// <summary>The option as the help shows it: <c>--name=VALUE</c>, <c>--name[=VALUE]</c> or <c>--name</c>.</summary>
    public string Usage => Value is null ? Name : ValueOptional ? $"{Name}[={Value}]" : $"{Name}={Value}";

    /// <summary>True when giving the option makes the run end by itself.</summary>
    public bool EndsRun => Role is OptionRole.EndsRun or OptionRole.CheckSuite or OptionRole.Help;
}

/// <summary>The options of one feature (a track or a scene), registered together.</summary>
/// <param name="Feature">Short feature name shown as the help section title.</param>
/// <param name="Scope">Where the options apply, in words (every scene, the main scene, a scene switch).</param>
/// <param name="Options">The options, in help order; may be empty (a group registered before its owner adds options), and <c>--help</c> leaves such a group out.</param>
/// <param name="Scenes">The scenes that read the group's options; in any other scene they are reported and ignored.</param>
public sealed record OptionGroup(string Feature, string Scope, IReadOnlyList<OptionSpec> Options, OptionScenes Scenes = OptionScenes.All);
