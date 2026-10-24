using Lifestone.Client.Options;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>--hunt-ground belt|forest</c> (M4 3.1 s0): the default is the belt, so every earlier command line hunts as it did; a bad value is an error line (never a silent default); the row the choice names is what
/// the headless host and the game's hunt leg pass to the scenario; and the option is registered for the main scene and the headless host.
/// </summary>
public sealed class HuntOptionsTests
{
    private static HuntOptions Parse(params string[] args) => HuntOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_default_ground_is_the_belt()
    {
        var o = Parse();

        Assert.Equal(HuntGroundChoice.Belt, o.Ground);
        Assert.Same(HuntGround.LowB, o.GroundRow);
        Assert.Empty(o.Errors);
        Assert.Contains("ground=belt", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Hunt_ground_forest_chooses_the_forest_row_and_the_other_hunt_options_stay_as_given()
    {
        var o = Parse("--hunt-ground=forest", "--hunt-kills=3", "--hunt-weapon=missile");

        Assert.Empty(o.Errors);
        Assert.Equal(HuntGroundChoice.Forest, o.Ground);
        Assert.Same(HuntGround.LowNorthForestMix, o.GroundRow);
        Assert.Equal((3, HuntWeapon.Missile), (o.Kills, o.Weapon));
        Assert.Contains("ground=forest", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Hunt_ground_belt_chooses_the_belt_row_and_the_last_one_given_wins()
    {
        Assert.Same(HuntGround.LowB, Parse("--hunt-ground=belt").GroundRow);
        Assert.Equal(HuntGroundChoice.Belt, Parse("--hunt-ground=forest", "--hunt-ground=belt").Ground);
        Assert.Equal(HuntGroundChoice.Forest, Parse("--hunt-ground=belt", "--hunt-ground=forest").Ground);
    }

    [Theory]
    [InlineData("--hunt-ground=meadow")]
    [InlineData("--hunt-ground=Forest")]
    [InlineData("--hunt-ground=")]
    [InlineData("--hunt-ground")]
    public void A_bad_ground_is_an_error_and_the_belt_stays(string argument)
    {
        var o = Parse(argument);

        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--hunt-ground:", error, StringComparison.Ordinal);
        Assert.Contains("belt, forest", error, StringComparison.Ordinal);
        Assert.Equal(HuntGroundChoice.Belt, o.Ground);
    }

    [Fact]
    public void Hunt_ground_is_registered_for_the_main_scene_and_the_headless_host()
    {
        Assert.True(OptionCatalog.Registry.IsKnown("--hunt-ground"));
        var arguments = CommandLineArgument.ParseAll(["--hunt-ground=forest"]);

        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Main));
        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Headless));
    }

    [Fact]
    public void M4_3_2_s4a_sell_is_on_buy_kits_is_2_and_appraise_loot_is_on_by_default_and_the_line_names_them()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.Equal((true, 2, true), (o.Sell, o.BuyKits, o.AppraiseLoot));
        Assert.Equal(HuntOptions.DefaultBuyKits, o.BuyKits);
        Assert.EndsWith("sell=on buy_kits=2 appraise_loot=on", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_3_2_s4a_the_values_are_read_and_the_last_one_given_wins()
    {
        var o = Parse("--sell=off", "--buy-kits=0", "--appraise-loot=off");
        Assert.Empty(o.Errors);
        Assert.Equal((false, 0, false), (o.Sell, o.BuyKits, o.AppraiseLoot));
        Assert.Equal(100, Parse("--buy-kits=100").BuyKits);
        Assert.True(Parse("--sell=off", "--sell=on").Sell);
        Assert.Equal(7, Parse("--buy-kits=3", "--buy-kits=7").BuyKits);
        var mixed = Parse("--hunt-kills=3", "--sell=off", "--heal=off", "--buy-kits=4");
        Assert.Empty(mixed.Errors);
        Assert.Equal((3, false, 4, false), (mixed.Kills, mixed.Sell, mixed.BuyKits, mixed.Recovery.Enabled));
    }

    [Theory]
    [InlineData("--buy-kits=-1")]
    [InlineData("--buy-kits=101")]
    [InlineData("--buy-kits=two")]
    [InlineData("--buy-kits=")]
    [InlineData("--sell=maybe")]
    [InlineData("--appraise-loot=maybe")]
    public void M4_3_2_s4a_a_bad_value_is_an_error_line_and_the_default_stays(string argument)
    {
        var o = Parse(argument);

        var error = Assert.Single(o.Errors);
        Assert.StartsWith(argument.Split('=')[0] + ":", error, StringComparison.Ordinal);
        Assert.Equal((true, 2, true), (o.Sell, o.BuyKits, o.AppraiseLoot));
    }

    [Fact]
    public void M4_3_2_s4a_the_options_are_registered_for_the_main_scene_and_the_headless_host_with_a_help_text()
    {
        foreach (var name in new[] { "--sell", "--buy-kits", "--appraise-loot" })
        {
            Assert.True(OptionCatalog.Registry.IsKnown(name), name);
            Assert.False(string.IsNullOrWhiteSpace(OptionCatalog.Registry.Find(name)!.Help), name);
        }

        var arguments = CommandLineArgument.ParseAll(["--sell=off", "--buy-kits=3", "--appraise-loot=on"]);

        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Main));
        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Headless));
    }

    [Fact]
    public void M4_3_2_s4a_the_tally_keeps_the_sale_the_purchase_and_the_appraisals_outside_its_verdict_fields_and_resets_them()
    {
        var tally = new HuntTally();
        tally.AddSale(3, 1021);
        tally.AddSale(1, 5);
        tally.AddBought(2);
        tally.SetAppraised(4);

        Assert.Equal((4, 1026, 2, 4), (tally.Sold, tally.Pyreals, tally.Bought, tally.Appraised));
        Assert.DoesNotContain("sold=", tally.Fields(), StringComparison.Ordinal); // the leg prints them, as looted
        tally.Reset();
        Assert.Equal((0, 0, 0, 0), (tally.Sold, tally.Pyreals, tally.Bought, tally.Appraised));
    }
}
