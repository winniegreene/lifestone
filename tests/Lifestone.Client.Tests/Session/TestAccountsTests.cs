using System.Text.RegularExpressions;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Session;

/// <summary>M4 D31: the four test accounts and their characters are one list in code, and tools\lib\Play.psm1 keeps the same set.</summary>
public sealed class TestAccountsTests
{
    private const string PlayModule = "tools/lib/Play.psm1";

    [Fact]
    public void The_list_is_the_four_accounts_with_one_fighter_each_for_melee_and_archer_and_the_throwaway_on_tools()
    {
        Assert.Equal(["test", "test-melee", "test-archer", "test-tools"], TestAccounts.Keys);
        Assert.Equal(["Lifestone Smoke", "Lifestone Loopback", "BSD Mage", "Holtburg Hunter"], TestAccounts.Characters["test"]);
        Assert.Equal(["Holtburg Brawler"], TestAccounts.Characters["test-melee"]);
        Assert.Equal(["Holtburg Archer"], TestAccounts.Characters["test-archer"]);
        Assert.Equal(["Lifestone Throwaway", "Lifestone Tools"], TestAccounts.Characters["test-tools"]);
        Assert.Equal(TestAccounts.Keys.Order(StringComparer.Ordinal), TestAccounts.Characters.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(TestAccounts.Characters.Values.Sum(c => c.Count), TestAccounts.Characters.Values.SelectMany(c => c).Distinct(StringComparer.OrdinalIgnoreCase).Count()); // no character on two accounts
    }

    [Theory]
    [InlineData("Holtburg Brawler", "test-melee")]
    [InlineData("holtburg archer", "test-archer")]
    [InlineData("BSD Mage", "test")]
    [InlineData("Lifestone Throwaway", "test-tools")]
    public void A_character_belongs_to_its_account(string character, string account) => Assert.Equal(account, TestAccounts.AccountOf(character));

    [Fact]
    public void P5b_G0_Lifestone_Throwaway_is_on_test_tools_and_no_other_account()
    {
        Assert.Equal("test-tools", TestAccounts.Tools);
        Assert.Equal(["test-tools"], TestAccounts.Characters.Where(a => a.Value.Contains("Lifestone Throwaway", StringComparer.OrdinalIgnoreCase)).Select(a => a.Key).ToArray());
        Assert.Equal("test-tools", TestAccounts.AccountOf("LIFESTONE THROWAWAY"));
        Assert.Equal(["Lifestone Throwaway", "Lifestone Tools"], TestAccounts.Characters[TestAccounts.Tools]); // the Throwaway and its purge caller (D43)
        Assert.Equal("test-tools", TestAccounts.AccountOf("Lifestone Tools"));
        Assert.DoesNotContain(TestAccounts.Characters[TestAccounts.Test], n => n.Contains("Throwaway", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unlisted_characters_and_the_play_account_are_not_test_accounts()
    {
        Assert.Null(TestAccounts.AccountOf("Somebody Else"));
        Assert.False(TestAccounts.IsTestAccount("play"));
        Assert.False(TestAccounts.IsTestAccount("TEST"));
        Assert.False(TestAccounts.IsTestAccount(null));
    }

    [Fact]
    public void Play_psm1_automated_set_equals_the_list_in_code()
    {
        var text = CheckoutFiles.ReadText(PlayModule);
        var match = Regex.Match(text, @"\$script:AutomatedAccounts\s*=\s*@\(([^)]*)\)");
        Assert.True(match.Success, "Play.psm1 must define $script:AutomatedAccounts = @('..', ..)");
        var keys = Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(TestAccounts.Keys, keys);
    }
}
