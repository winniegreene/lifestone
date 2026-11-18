using Lifestone.Client.Tests;

namespace Lifestone.ServerMod.Tests;

/// <summary>
/// M6 7 finish, <c>m6-corpse-coin-server</c>: retail's coin on a corpse is the default and a flag restores ACE's destroy. ACE's assemblies are not in this project, so the rule runs here and the seam is pinned on the source; the live proof is the lead's (the ACE log's
/// <c>Lifestone corpse coin:</c> line, a read-only SELECT on <c>config_properties_boolean</c>, a death whose corpse holds coin).
/// </summary>
public sealed class CorpseCoinRulesTests
{
    private static string Seam => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "CorpseCoin.cs"));

    private static string Watch => File.ReadAllText(RepositoryFiles.PathOf("server", "mod", "Lifestone.ServerMod", "StartupWatch.cs"));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("retail", false)]
    [InlineData("acee", false)]
    [InlineData("ace", true)]
    [InlineData(" ACE ", true)]
    public void Retail_keeps_the_coin_unless_the_flag_says_ace(string? value, bool destroy) => Assert.Equal(destroy, CorpseCoinRules.DestroyCoin(value));

    [Fact]
    public void The_seam_sets_ACEs_own_property_reads_it_back_and_opens_no_database()
    {
        Assert.Equal("corpse_destroy_pyreals", CorpseCoinRules.Property);
        Assert.Contains("PropertyManager.ModifyBool(CorpseCoinRules.Property, want)", Seam, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseManager", Seam, StringComparison.Ordinal);
        Assert.DoesNotContain("MySql", Seam, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_coin_rule_withholds_the_ready_line()
    {
        var apply = Watch.IndexOf("CorpseCoin.Apply()", StringComparison.Ordinal);
        var ready = Watch.IndexOf("{ModLog.Ready}", StringComparison.Ordinal);
        Assert.InRange(apply, 0, ready);
        Assert.Contains("if (!CorpseCoin.Apply())", Watch, StringComparison.Ordinal);
    }
}
