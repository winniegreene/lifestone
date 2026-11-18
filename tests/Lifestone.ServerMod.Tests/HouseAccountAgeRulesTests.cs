using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// M6 villa, the owner's decision of 2026-11-16: a house needs no account age, and a flag restores retail's 15 days. ACE's assemblies are not in this project, so the rule runs here and the seam is pinned on the source; the live proof is the
/// lead's (the ACE log's <c>Lifestone house account age:</c> line, a read-only SELECT on <c>config_properties_boolean</c>, a villa bought by an account younger than 15 days).
/// </summary>
public sealed class HouseAccountAgeRulesTests
{
    private static string Seam => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "HouseAccountAge.cs"));

    private static string Watch => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "StartupWatch.cs"));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("retail", false)]
    [InlineData("acee", false)]
    [InlineData("ace", true)]
    [InlineData(" ACE ", true)]
    public void No_account_age_unless_the_flag_says_ace(string? value, bool require) => Assert.Equal(require, HouseAccountAgeRules.RequireFifteenDays(value));

    [Fact]
    public void The_seam_sets_ACEs_own_property_reads_it_back_and_opens_no_database()
    {
        Assert.Equal("house_15day_account", HouseAccountAgeRules.Property);
        Assert.Contains("PropertyManager.ModifyBool(HouseAccountAgeRules.Property, want)", Seam, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseManager", Seam, StringComparison.Ordinal);
        Assert.DoesNotContain("MySql", Seam, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_account_age_rule_withholds_the_ready_line()
    {
        var apply = Watch.IndexOf("HouseAccountAge.Apply()", StringComparison.Ordinal);
        var ready = Watch.IndexOf("{ModLog.Ready}", StringComparison.Ordinal);
        Assert.InRange(apply, 0, ready);
        Assert.Contains("if (!HouseAccountAge.Apply())", Watch, StringComparison.Ordinal);
    }
}
