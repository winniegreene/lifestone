using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The second climber (the local model's own): one predicate for every gate, her own journey and account, and Winnie unchanged.</summary>
public sealed partial class ClimberIdentityTests
{
    private static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Theory]
    [InlineData("Winnie", true)]
    [InlineData("Qwinnie", true)]
    [InlineData("qwinnie", true)]
    [InlineData("BSD Mage", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_predicate_names_the_climbers(string? name, bool expected) => Assert.Equal(expected, MageTemplate.IsClimber(name));

    [Fact]
    public void Her_journey_is_her_own_and_cheat_free_and_winnies_is_unchanged()
    {
        var plan = JourneyPlans.ClimberFor("Qwinnie");
        Assert.Equal(("qwinnie", "Qwinnie", "qwinnie", true), (plan.Name, plan.Character, plan.Account, plan.CheatFree));
        Assert.Same(plan, JourneyPlans.ForName("qwinnie"));
        Assert.Equal(JourneyPlans.Climber.Stations, plan.Stations);

        var winnie = JourneyPlans.ClimberFor("Winnie");
        Assert.Same(JourneyPlans.Climber, winnie);
        Assert.Equal(("climber", "Winnie", "test"), (winnie.Name, winnie.Character, winnie.Account));
    }

    [Fact]
    public void The_level_up_job_plays_the_climber_named_and_refuses_any_other()
    {
        Assert.Equal("Winnie", Parse("--autopilot-job=level-up").Climber);

        var options = Parse("--autopilot-job=level-up", "--autopilot-climber=qwinnie");
        Assert.Empty(options.Errors);
        Assert.Equal("Qwinnie", AutopilotHostLogic.RequiredCharacter(options));
        Assert.Null(AutopilotHostLogic.StartRefusal(options, "Qwinnie"));
        Assert.Contains("plays Qwinnie only", AutopilotHostLogic.StartRefusal(options, "Winnie"), StringComparison.Ordinal);

        var plan = AutopilotHostLogic.PlanFor(options);
        Assert.Equal(("Qwinnie", "qwinnie"), (plan.Character, plan.Account));
        var dungeon = AutopilotHostLogic.PlanFor(options, GroundCatalog.TryGet("tusker-cave"));
        Assert.Equal(("Qwinnie", StationWhere.Den), (dungeon.Character, dungeon.Ground));

        Assert.StartsWith("--autopilot-climber:", Assert.Single(Parse("--autopilot-climber=BSD Mage").Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Her_account_enters_and_creates_only_her()
    {
        Assert.Equal("qwinnie", TestAccounts.AccountOf("Qwinnie"));
        Assert.True(AutomatedAccountRule.TryCharacters(TestAccounts.Qwinnie, "Qwinnie", "Qwinnie", out _));
        Assert.False(AutomatedAccountRule.TryCharacters(TestAccounts.Qwinnie, "Winnie", null, out var enter));
        Assert.Contains("only Qwinnie", enter, StringComparison.Ordinal);
        Assert.False(AutomatedAccountRule.TryCharacters(TestAccounts.Qwinnie, null, "Someone Else", out _));
    }

    [GeneratedRegex(@"(Equals\([^;]*\bClimberName\b|[=!]=\s*[\w.]*\bClimberName\b|\bClimberName\b\s*[=!]=|\bis\s+(not\s+)?[\w.]*\bClimberName\b|\bClimberName\.(Equals|StartsWith|Contains)\b|Contains\([\w.]*\bClimberName\b)")]
    private static partial Regex NameComparison();

    [Fact]
    public void No_gate_compares_a_name_to_the_climber_but_the_one_predicate()
    {
        var root = RepositoryFiles.PathOf("src");
        var found = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(p => File.ReadLines(p).Select(l => (p, l)))
            .Where(x => NameComparison().IsMatch(x.l) && !x.l.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.p)}: {x.l.Trim()}")
            .ToList();
        Assert.True(found.Count == 0, "use MageTemplate.IsClimber:\n" + string.Join("\n", found));
    }

    [Fact]
    public void Her_twin_is_named_as_a_literal_only_where_the_lists_of_names_live()
    {
        string[] allowed = ["AutopilotOptions.cs", "TestAccounts.cs", "MageTemplate.cs", "StandInCharPurge.cs"];
        var found = Directory.EnumerateFiles(RepositoryFiles.PathOf("src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !allowed.Contains(Path.GetFileName(p), StringComparer.Ordinal) && File.ReadAllText(p).Contains("\"Winnie\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(found.Count == 0, "a comparison with this literal would silently skip the other climber: " + string.Join(", ", found));
    }

    [Theory]
    [InlineData("Winnie")]
    [InlineData("Qwinnie")]
    public void The_rules_read_by_name_treat_both_climbers_alike(string name)
    {
        Assert.True(Lifestone.Client.Session.Mage.CharacterPolicy.For(name).ManaBySum); // M6 16 Q9: ManaOrder.PicksByNet became the policy's ManaBySum
        Assert.Same(BuffProfiles.Climber, BuffProfiles.For(name));
        Assert.All(BuffProfiles.For(name)!.Lines, l => Assert.True(l.ByPlay));
    }
}
