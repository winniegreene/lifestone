using Lifestone.Client.Options;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>The vitals keeper's options (M4 P5a lane C2): the defaults, every <c>--vitals-*</c> option, the recovery rule's <c>--heal-*</c> carried through, and the bad values reported.</summary>
public sealed class VitalsOptionsTests
{
    private static VitalsOptions Parse(params string[] args) => VitalsOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_defaults_are_the_design_s()
    {
        var o = Parse();
        Assert.Empty(o.Errors);
        Assert.Equal((30, 80, 15, 600.0, RestPosture.Stand, false, 40, 0.9), (o.StaminaBelowPercent, o.StaminaUntilPercent, o.AttackStaminaPercent, o.RestCapSeconds, o.Posture, o.InFightPotion, o.InFightPotionBelowPercent, o.CastChance));
        Assert.Equal((75, 90), (o.Recovery.BelowPercent, o.Recovery.UntilPercent));
    }

    [Fact]
    public void Every_option_is_read()
    {
        var o = Parse(
            "--vitals-stamina-below=25", "--vitals-stamina-until=70", "--vitals-attack-stamina=10", "--vitals-rest-cap-s=120", "--vitals-rest-posture=lie", "--vitals-infight-potion=on",
            "--vitals-infight-potion-below=35", "--vitals-cast-chance=0.8", "--heal-below=60", "--heal-until=85");
        Assert.Empty(o.Errors);
        Assert.Equal((25, 70, 10, 120.0, RestPosture.Lie, true, 35, 0.8), (o.StaminaBelowPercent, o.StaminaUntilPercent, o.AttackStaminaPercent, o.RestCapSeconds, o.Posture, o.InFightPotion, o.InFightPotionBelowPercent, o.CastChance));
        Assert.Equal((60, 85), (o.Recovery.BelowPercent, o.Recovery.UntilPercent));
        Assert.Equal(RestPosture.Sit, Parse("--vitals-rest-posture=sit").Posture);
    }

    [Fact]
    public void Bad_values_are_reported_and_keep_the_default()
    {
        var o = Parse("--vitals-stamina-below=120", "--vitals-rest-cap-s=0", "--vitals-rest-posture=kneel", "--vitals-cast-chance=2", "--vitals-infight-potion=maybe", "--heal-below=0");
        Assert.Equal(6, o.Errors.Count);
        Assert.Equal(VitalsOptions.Default with { Errors = o.Errors }, o);
        Assert.Contains(o.Errors, e => e.StartsWith("--vitals-rest-posture: 'kneel' is not one of stand, sit, lie", StringComparison.Ordinal));
    }

    [Fact]
    public void An_until_under_the_trigger_is_raised_to_it_and_reported()
    {
        var o = Parse("--vitals-stamina-below=50", "--vitals-stamina-until=40");
        Assert.Equal(50, o.StaminaUntilPercent);
        Assert.Single(o.Errors);
    }

    [Fact]
    public void Every_vitals_option_is_registered_for_the_main_scene_and_the_headless_host()
    {
        string[] names = ["--vitals-stamina-below", "--vitals-stamina-until", "--vitals-attack-stamina", "--vitals-rest-cap-s", "--vitals-rest-posture", "--vitals-infight-potion", "--vitals-infight-potion-below", "--vitals-cast-chance"];
        var group = Assert.Single(OptionCatalog.Registry.Groups, g => g.Feature == "Vitals keeper");
        Assert.Equal(names, group.Options.Select(o => o.Name));
        Assert.All(names, n => Assert.Equal(OptionScenes.Main | OptionScenes.Headless, OptionCatalog.Registry.ScenesOf(n)));
    }
}
