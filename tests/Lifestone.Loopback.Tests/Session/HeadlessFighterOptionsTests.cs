using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 P4's <c>--fighter</c> option and the account rule of the fighter scenarios (plan D31): plain unit tests of <see cref="HeadlessOptions.Parse"/> (no backend). What they prove: the template follows
/// <c>--account</c> (test = hunter, test-melee = melee, test-archer = archer); an explicit <c>--fighter</c> that does not match the account is a loud error; the build and hunt scenarios accept the three template
/// accounts while every other scenario stays on <c>test</c>; the hunt's default ground and weapon follow the template and a weapon the template's kit has none of is an error.
/// </summary>
public sealed class HeadlessFighterOptionsTests
{
    [Theory]
    [InlineData("fighter-build", null, "hunter")]
    [InlineData("fighter-build", "test", "hunter")]
    [InlineData("fighter-build", "test-melee", "melee")]
    [InlineData("fighter-build", "test-archer", "archer")]
    [InlineData("hunt", null, "hunter")]
    [InlineData("hunt", "test-melee", "melee")]
    [InlineData("hunt", "test-archer", "archer")]
    public void The_fighter_follows_the_account_of_the_build_and_the_hunt(string scenario, string? account, string expected)
    {
        string[] args = account is null ? ["--scenario=" + scenario] : ["--scenario=" + scenario, "--account=" + account];
        var o = HeadlessOptions.Parse(args);
        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.Fighter!.Key);
        string[] explicitArgs = [.. args, "--fighter=" + expected];
        var withFighter = HeadlessOptions.Parse(explicitArgs);
        Assert.Empty(withFighter.Errors);
        Assert.Same(o.Fighter, withFighter.Fighter);
    }

    [Theory]
    [InlineData("melee", "test")]
    [InlineData("archer", "test")]
    [InlineData("hunter", "test-melee")]
    [InlineData("archer", "test-melee")]
    [InlineData("melee", "test-archer")]
    public void A_fighter_that_does_not_live_on_the_account_is_a_loud_error_naming_no_account_value(string fighter, string account)
    {
        var errors = HeadlessOptions.Parse(["--scenario=fighter-build", "--account=" + account, "--fighter=" + fighter]).Errors;
        var error = Assert.Single(errors);
        Assert.StartsWith("--fighter:", error, StringComparison.Ordinal);
        Assert.DoesNotContain(account + "'", error.Replace(FighterTemplate.ForKey(fighter)!.AccountKey, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Single(HeadlessOptions.Parse(["--scenario=hunt", "--account=" + account, "--fighter=" + fighter]).Errors);
    }

    [Fact]
    public void An_unknown_fighter_or_a_fighter_on_another_scenario_or_an_account_with_no_template_is_an_error()
    {
        Assert.Contains("is not one of hunter, melee, archer", Assert.Single(HeadlessOptions.Parse(["--scenario=fighter-build", "--fighter=mage"]).Errors), StringComparison.Ordinal);
        Assert.StartsWith("--fighter: only", Assert.Single(HeadlessOptions.Parse(["--scenario=fight", "--fighter=melee", "--account=test-melee"]).Errors, e => e.StartsWith("--fighter", StringComparison.Ordinal)), StringComparison.Ordinal);
        var noTemplate = Assert.Single(HeadlessOptions.Parse(["--scenario=hunt", "--account=somebody"]).Errors);
        Assert.Contains("fighter template", noTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody", noTemplate, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mage-build")]
    [InlineData("gear")]
    [InlineData("fight")]
    [InlineData("death-and-regear")]
    public void Every_other_permanent_character_scenario_still_runs_on_the_default_test_account_only(string scenario)
    {
        foreach (var account in new[] { "test-melee", "test-archer" })
        {
            var error = Assert.Single(HeadlessOptions.Parse(["--scenario=" + scenario, "--account=" + account]).Errors);
            Assert.Equal($"--account: --scenario={scenario} runs on the default test account 'test' only; the value is not shown", error);
        }
    }

    [Theory]
    [InlineData("test-melee", "melee")]
    [InlineData("test-archer", "archer")]
    public void The_buff_scenario_runs_on_the_account_of_a_fighter_with_a_caster_as_that_fighter_M4_P5a_C4(string account, string expected)
    {
        var o = HeadlessOptions.Parse(["--scenario=buff", "--account=" + account]);
        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.Fighter!.Key);
        var explicitFighter = HeadlessOptions.Parse(["--scenario=buff", "--account=" + account, "--fighter=" + expected]);
        Assert.Empty(explicitFighter.Errors);
        Assert.Same(o.Fighter, explicitFighter.Fighter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test")]
    public void The_buff_scenario_on_the_default_account_is_the_mages_with_no_fighter_M4_P5a_C4(string? account)
    {
        string[] args = account is null ? ["--scenario=buff"] : ["--scenario=buff", "--account=" + account];
        var o = HeadlessOptions.Parse(args);
        Assert.Empty(o.Errors);
        Assert.Null(o.Fighter); // the Hunter lives on the default account and has no caster: the mage's scenario, unchanged
    }

    [Fact]
    public void The_buff_scenario_refuses_the_Hunter_a_mismatched_fighter_and_an_account_with_no_caster_naming_no_account_value_M4_P5a_C4()
    {
        Assert.Contains("no caster", Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--fighter=hunter"]).Errors), StringComparison.Ordinal);
        Assert.StartsWith("--fighter:", Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--account=test", "--fighter=melee"]).Errors), StringComparison.Ordinal);
        Assert.StartsWith("--fighter:", Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--account=test-melee", "--fighter=archer"]).Errors), StringComparison.Ordinal);
        var unknown = Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--account=somebody"]).Errors);
        Assert.Equal("--account: --scenario=buff runs on the default test account 'test' or the account of a fighter with a caster (test-melee, test-archer) only; the value is not shown", unknown);
        Assert.DoesNotContain("somebody", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hunt_goes_where_the_template_goes_unless_the_command_line_says_otherwise()
    {
        var hunter = HeadlessOptions.Parse(["--scenario=hunt"]);
        Assert.Equal((HuntWeapon.Melee, HuntGroundChoice.Belt), (hunter.Hunt.Weapon, hunter.Hunt.Ground));
        var melee = HeadlessOptions.Parse(["--scenario=hunt", "--account=test-melee"]);
        Assert.Empty(melee.Errors);
        Assert.Equal((HuntWeapon.Melee, HuntGroundChoice.Belt), (melee.Hunt.Weapon, melee.Hunt.Ground));
        var archer = HeadlessOptions.Parse(["--scenario=hunt", "--account=test-archer"]);
        Assert.Empty(archer.Errors);
        Assert.Equal((HuntWeapon.Missile, HuntGroundChoice.Forest), (archer.Hunt.Weapon, archer.Hunt.Ground));
        var overridden = HeadlessOptions.Parse(["--scenario=hunt", "--account=test-archer", "--hunt-ground=belt"]);
        Assert.Equal((HuntWeapon.Missile, HuntGroundChoice.Belt), (overridden.Hunt.Weapon, overridden.Hunt.Ground));
        Assert.Contains("no bow", Assert.Single(HeadlessOptions.Parse(["--scenario=hunt", "--account=test-melee", "--hunt-weapon=missile"]).Errors), StringComparison.Ordinal);
        Assert.Contains("no melee weapon", Assert.Single(HeadlessOptions.Parse(["--scenario=hunt", "--account=test-archer", "--hunt-weapon=melee"]).Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void The_fighter_option_is_registered_in_the_catalog()
    {
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Headless().Options, o => o.Name == "--fighter");
    }

    [Fact]
    public void The_fighter_recreate_option_is_registered_and_defaults_off_M4_P5a_C5_D40()
    {
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Headless().Options, o => o.Name == "--fighter-recreate");
        Assert.False(HeadlessOptions.Parse(["--scenario=fighter-build", "--account=test-melee"]).FighterRecreate);
    }

    [Theory]
    [InlineData("test-melee")]
    [InlineData("test-archer")]
    public void The_fighter_recreate_option_is_taken_by_the_fighter_build_on_the_brawler_and_the_archer_M4_P5a_C5_D40(string account)
    {
        var o = HeadlessOptions.Parse(["--scenario=fighter-build", "--account=" + account, "--fighter-recreate"]);
        Assert.Empty(o.Errors);
        Assert.True(o.FighterRecreate);
        Assert.False(HeadlessOptions.Parse(["--scenario=fighter-build", "--account=" + account, "--fighter-recreate=off"]).FighterRecreate);
    }

    [Theory]
    [InlineData("--scenario=fighter-build")]
    [InlineData("--scenario=fighter-build", "--account=test")]
    [InlineData("--scenario=hunt", "--account=test-melee")]
    [InlineData("--scenario=login", "--account=test-archer")]
    [InlineData("--scenario=mage-build")]
    public void The_fighter_recreate_option_is_refused_for_the_hunter_and_every_other_scenario_M4_P5a_C5_D40(params string[] arguments)
    {
        var o = HeadlessOptions.Parse([.. arguments, "--fighter-recreate"]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--fighter-recreate: only --scenario=fighter-build on the Brawler (test-melee) or the Archer (test-archer) takes it", error, StringComparison.Ordinal);
        Assert.False(o.FighterRecreate);
    }

    // ------------------------------------------------------------------------------------------------ M4 fresh B1: --fresh-character (D44, D45)

    [Fact]
    public void The_fresh_character_option_is_registered_and_defaults_off_M4_fresh_B1()
    {
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Headless().Options, o => o.Name == "--fresh-character");
        Assert.False(HeadlessOptions.Parse(["--scenario=fighter-build", "--account=test-melee"]).FreshCharacter);
        Assert.False(HeadlessOptions.Parse(["--scenario=mage-build"]).FreshCharacter);
    }

    [Theory]
    [InlineData("--scenario=fighter-build", "--account=test-melee")]
    [InlineData("--scenario=fighter-build", "--account=test-archer")]
    [InlineData("--scenario=mage-build")]
    [InlineData("--scenario=mage-build", "--account=test")]
    public void The_fresh_character_option_is_taken_by_the_brawler_the_archer_and_the_mage_builds_M4_fresh_B1(params string[] arguments)
    {
        var o = HeadlessOptions.Parse([.. arguments, "--fresh-character"]);
        Assert.Empty(o.Errors);
        Assert.True(o.FreshCharacter);
        Assert.False(o.FighterRecreate);
        Assert.False(HeadlessOptions.Parse([.. arguments, "--fresh-character=off"]).FreshCharacter);
        Assert.True(HeadlessOptions.Parse([.. arguments, "--fresh-character=on"]).FreshCharacter);
    }

    [Theory]
    [InlineData("--scenario=fighter-build")]
    [InlineData("--scenario=fighter-build", "--account=test")]
    [InlineData("--scenario=hunt", "--account=test-melee")]
    [InlineData("--scenario=login", "--account=test-archer")]
    [InlineData("--scenario=login")]
    [InlineData("--scenario=gear")]
    [InlineData("--scenario=buff", "--account=test-melee")]
    public void The_fresh_character_option_is_refused_for_the_hunter_and_every_other_scenario_M4_fresh_B1(params string[] arguments)
    {
        var o = HeadlessOptions.Parse([.. arguments, "--fresh-character"]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--fresh-character: only --scenario=fighter-build on the Brawler (test-melee) or the Archer (test-archer) and --scenario=mage-build on the default account takes it", error, StringComparison.Ordinal);
        Assert.False(o.FreshCharacter);
    }

    [Theory]
    [InlineData("--scenario=fighter-build", "--account=test-melee")]
    [InlineData("--scenario=fighter-build", "--account=test-archer")]
    public void The_fresh_character_option_excludes_fighter_recreate_M4_fresh_B1(params string[] arguments)
    {
        var o = HeadlessOptions.Parse([.. arguments, "--fresh-character", "--fighter-recreate"]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--fresh-character: it excludes --fighter-recreate", error, StringComparison.Ordinal);
        Assert.False(o.FreshCharacter);
        Assert.Empty(HeadlessOptions.Parse([.. arguments, "--fresh-character", "--fighter-recreate=off"]).Errors);
    }

    [Theory]
    [InlineData("test-melee")]
    [InlineData("test-archer")]
    [InlineData("somebody")]
    public void The_fresh_character_option_on_the_mage_build_of_another_account_is_an_account_error_naming_no_value_M4_fresh_B1(string account)
    {
        var o = HeadlessOptions.Parse(["--scenario=mage-build", "--account=" + account, "--fresh-character"]);
        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--account: --scenario=mage-build runs on the default test account 'test' only", error, StringComparison.Ordinal);
        Assert.DoesNotContain(account, error, StringComparison.Ordinal);
    }
}
