using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Tests;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 item 0 a3a (D7): the climber's options and verdict, plain unit tests (no backend). What they prove: <c>--scenario=climber-create</c> is registered and parses with <c>--fresh-character</c> on the default account; it is refused without the flag (the standing job's Winnie is never re-created
/// without the owner's word), on any other account, with <c>--character</c> or <c>--create-character</c> (so never as BSD Mage), and the other scenarios' <c>--fresh-character</c> rules are unchanged; <c>--journey=climber</c> runs on the account test only; and the verdict line has the fields
/// <c>fresh= purged= granted= template= level=</c>, with <c>granted</c> read from the mod-verb tally (nothing is granted before the run starts, so a scenario that has not run reports 0 by construction).
/// </summary>
public sealed class FreshClimberOptionsTests
{
    [Fact]
    public void The_scenario_and_the_journey_are_registered_with_help_that_names_the_climber()
    {
        var scenario = Assert.Single(OptionCatalog.Headless().Options, o => o.Name == "--scenario");
        Assert.Contains("climber-create", scenario.Value!, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-climber-creation-credits)", scenario.Help, StringComparison.Ordinal);
        Assert.Contains("--journey=climber", scenario.Help, StringComparison.Ordinal);
        var journey = Assert.Single(OptionCatalog.Headless().Options, o => o.Name == "--journey");
        Assert.Contains("climber", journey.Value!.Split('|'));
    }

    [Fact]
    public void Climber_create_with_fresh_character_on_the_default_account_parses_clean()
    {
        var o = HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character"]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.ClimberCreate, o.Scenario);
        Assert.True(o.FreshCharacter);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--account=test"]).Errors);
    }

    [Fact]
    public void M6_14_the_creation_reads_the_buff_bots_option_for_its_skill_order()
    {
        var on = HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--account=test"]);
        Assert.Empty(on.Errors);
        Assert.True(on.Autopilot.BuffBot); // the default: the bot's order (Life Magic, Item Enchantment, Mana Conversion, Creature Enchantment)
        Assert.Equal(MageTemplate.ClimberCreationOrderWithBot, MageTemplate.ClimberOrder(on.Autopilot.BuffBot));

        var off = HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--account=test", "--autopilot-buff-bot=off"]);
        Assert.Empty(off.Errors);
        Assert.False(off.Autopilot.BuffBot);
        Assert.Equal(MageTemplate.ClimberCreationOrder, MageTemplate.ClimberOrder(off.Autopilot.BuffBot));
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--autopilot-buff-bot=maybe"]).Errors);
    }

    [Fact]
    public void Climber_create_is_refused_without_the_flag_off_the_test_account_and_with_a_character_named()
    {
        Assert.Contains(HeadlessOptions.Parse(["--scenario=climber-create"]).Errors, e => e.Contains("needs --fresh-character", StringComparison.Ordinal));
        foreach (var account in new[] { "test-melee", "test-archer", "test-tools" })
        {
            Assert.Contains(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", $"--account={account}"]).Errors, e => e.Contains("--account", StringComparison.Ordinal) && e.Contains("climber-create", StringComparison.Ordinal));
        }

        Assert.Contains(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--character=BSD Mage"]).Errors, e => e.Contains("--scenario=climber-create creates the climber only", StringComparison.Ordinal));
        Assert.Contains(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--create-character=BSD Mage"]).Errors, e => e.Contains("--scenario=climber-create creates the climber only", StringComparison.Ordinal));
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=climber-create", "--fresh-character", "--mage-level=80"]).Errors);
    }

    [Fact]
    public void The_other_scenarios_keep_their_fresh_character_rules()
    {
        Assert.Empty(HeadlessOptions.Parse(["--scenario=mage-build", "--fresh-character"]).Errors);
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=login", "--fresh-character"]).Errors);
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=journey", "--journey=mage", "--fresh-character"]).Errors);
    }

    [Fact]
    public void The_climber_journey_runs_on_the_test_account_only()
    {
        var o = HeadlessOptions.Parse(["--scenario=journey", "--journey=climber"]);
        Assert.Empty(o.Errors);
        Assert.Equal("climber", o.Journey!.Name);
        Assert.Equal(("Winnie", "test"), (o.Journey.Character, o.Session.Account));
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=journey", "--journey=climber", "--account=test-melee"]).Errors);
    }

    [Fact]
    public void The_verdict_has_the_fields_and_granted_comes_from_the_tally()
    {
        var plan = MageTemplate.PlanClimberCreation(52, id => id == MageTemplate.WarMagic ? 14 : 6, id => 28);
        var scenario = new ClimberCreateScenario(new uint[CharacterCreateIntentSkills], plan, new RecordingLogSink());
        Assert.Matches(new Regex(@"^fresh=none purged=0 granted=0 template=short template_short=not_entered level=-1$"), scenario.VerdictFields());
        Assert.Equal(0, scenario.Granted); // the tally of an unstarted run: no verb was sent
        Assert.Equal("climber-create", scenario.Name);
        Assert.DoesNotContain("granted=0", File.ReadAllText(ScenarioSource()).Replace("granted={Granted}", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal); // never a literal
    }

    private const int CharacterCreateIntentSkills = Lifestone.Client.Session.Contracts.CharacterCreateIntent.RequiredSkillCount;

    private static string ScenarioSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "Lifestone.Client", "Session", "Scenarios", "ClimberCreateScenario.cs");
    }
}
