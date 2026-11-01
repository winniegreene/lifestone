using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The death loop's retired decoy option (M5 3 d3, D51): <c>--death-decoys</c> (M4 3.1 d0, D20: the kit carried eight decoys a death dropped instead of the gear) is refused by name, whatever its value, with its reason in
/// <see cref="DeathOptions.Errors"/> (which fails the headless run and the game's leg as every bad option does); the death loop carries only the mage's own kit, and its siblings parse as before. The option stays in
/// <see cref="OptionCatalog.Death"/> as a retired entry, as <c>--smoke-statics</c> does.
/// </summary>
public sealed class DeathDecoyOptionsTests
{
    private const string Retired = "the decoys are retired: M5 D51";

    private static DeathOptions Parse(params string[] args) => DeathOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Theory]
    [InlineData("--death-decoys=on")]
    [InlineData("--death-decoys=off")]
    [InlineData("--death-decoys")]
    public void Death_decoys_is_refused_by_name_whatever_its_value(string argument)
    {
        var o = Parse(argument);

        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--death-decoys: ", error, StringComparison.Ordinal);
        Assert.Contains(Retired, error, StringComparison.Ordinal);
        Assert.Equal("seconds=600", o.ToString()); // nothing of it reaches the options or their log line
    }

    [Fact]
    public void Without_the_option_there_is_no_error_and_the_log_line_is_the_default()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.Equal("seconds=600", o.ToString());
    }

    [Fact]
    public void The_refusal_leaves_the_other_options_reading_as_before()
    {
        var o = Parse("--death-seconds=120", "--death-decoys=on", "--death-recover-corpse=off", "--death-recover-min-health=75");

        var error = Assert.Single(o.Errors);
        Assert.Contains(Retired, error, StringComparison.Ordinal);
        Assert.Equal(120.0, o.Seconds);
        Assert.False(o.RecoverCorpse);
        Assert.Equal(75.0, o.RecoverMinHealthPercent);
        Assert.Equal("seconds=120 recover_corpse=off recover_min_health=75", o.ToString());
    }

    [Fact]
    public void The_option_stays_registered_as_a_retired_entry_for_the_main_scene_and_the_headless_host()
    {
        var group = OptionCatalog.Death();

        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, group.Scenes);
        var decoys = Assert.Single(group.Options, o => o.Name == "--death-decoys");
        Assert.StartsWith("Retired", decoys.Help, StringComparison.Ordinal);
        Assert.Contains(Retired, decoys.Help, StringComparison.Ordinal);
        // The siblings are still there, in the same order.
        Assert.Equal(["--death-seconds", "--death-stages", "--death-recover-corpse", "--death-recover-min-health", "--death-recover-abort-health", "--death-trip-aggro-m", "--death-recover-abort-seconds", "--death-decoys", "--death-rebuff", "--death-fight", "--death-fight-kills", "--death-fight-seconds"], group.Options.Select(o => o.Name));
    }
}
