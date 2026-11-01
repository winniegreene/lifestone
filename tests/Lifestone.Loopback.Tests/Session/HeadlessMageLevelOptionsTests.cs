using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 7 F3 (D87): the build's level as a registered option. Plain unit tests of <see cref="HeadlessOptions.Parse"/> (no backend). What they prove: <c>--mage-level</c> is registered in the headless group with help that names
/// its range and the 80 of the soak; it defaults to the template's 126; <c>--scenario=mage-build</c> takes any level from <see cref="MageTemplate.MinLevel"/> to 126; a lower or higher level, a value that is no number
/// and every other scenario are refused by name.
/// </summary>
public sealed class HeadlessMageLevelOptionsTests
{
    [Fact]
    public void The_option_is_registered_with_help_that_names_its_range_and_the_soaks_80()
    {
        var spec = Assert.Single(OptionCatalog.Headless().Options, o => o.Name == "--mage-level");
        Assert.Contains("--mage-level=80", spec.Help, StringComparison.Ordinal);
        Assert.Contains($"{MageTemplate.MinLevel} to {MageTemplate.TargetLevel}", spec.Help, StringComparison.Ordinal);
        Assert.Contains("TODO(m5-mage-level-credits)", spec.Help, StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_defaults_to_the_templates_126()
    {
        var o = HeadlessOptions.Parse(["--scenario=mage-build"]);
        Assert.Empty(o.Errors);
        Assert.Equal(MageTemplate.TargetLevel, o.MageLevel);
        Assert.Equal(126, HeadlessOptions.Parse(["--scenario=mage-build", "--fresh-character"]).MageLevel);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(50)]
    [InlineData(89)]
    [InlineData(90)]
    [InlineData(126)]
    public void The_mage_build_takes_any_level_from_the_lowest_to_126(int level)
    {
        var o = HeadlessOptions.Parse(["--scenario=mage-build", "--fresh-character", $"--mage-level={level}"]);
        Assert.Empty(o.Errors);
        Assert.Equal(level, o.MageLevel);
        Assert.True(o.FreshCharacter);
    }

    [Theory]
    [InlineData("49")]
    [InlineData("1")]
    [InlineData("127")]
    [InlineData("0")]
    [InlineData("-5")]
    public void A_level_outside_the_range_is_refused_by_name_and_the_default_stays(string value)
    {
        var o = HeadlessOptions.Parse(["--scenario=mage-build", $"--mage-level={value}"]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith($"--mage-level: '{value}' is not a level {MageTemplate.MinLevel} to {MageTemplate.TargetLevel}", error, StringComparison.Ordinal);
        Assert.Equal(MageTemplate.TargetLevel, o.MageLevel);
    }

    [Fact]
    public void A_value_that_is_no_number_is_refused()
    {
        var o = HeadlessOptions.Parse(["--scenario=mage-build", "--mage-level=eighty"]);
        Assert.NotEmpty(o.Errors);
        Assert.All(o.Errors, e => Assert.StartsWith("--mage-level", e, StringComparison.Ordinal));
        Assert.Equal(MageTemplate.TargetLevel, o.MageLevel);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(126)]
    public void The_mage_journey_takes_it_too(int level)
    {
        var o = HeadlessOptions.Parse(["--scenario=journey", "--journey=mage", $"--mage-level={level}"]);
        Assert.DoesNotContain(o.Errors, e => e.StartsWith("--mage-level", StringComparison.Ordinal));
        Assert.Equal(level, o.MageLevel);
    }

    [Fact]
    public void Another_journey_refuses_it()
    {
        var o = HeadlessOptions.Parse(["--scenario=journey", "--journey=melee", "--mage-level=80"]);
        Assert.Contains(o.Errors, e => e.StartsWith("--mage-level: --scenario=journey takes it with --journey=mage only", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--scenario=login")]
    [InlineData("--scenario=gear")]
    [InlineData("--scenario=fighter-build", "--account=test-melee")]
    [InlineData("--scenario=autopilot")]
    public void Every_other_scenario_refuses_it(params string[] arguments)
    {
        var o = HeadlessOptions.Parse([.. arguments, "--mage-level=80"]);
        Assert.Contains("--mage-level: only --scenario=mage-build and --scenario=journey (M5 7 F14) take it", o.Errors);
        Assert.Equal(MageTemplate.TargetLevel, o.MageLevel);
    }
}
