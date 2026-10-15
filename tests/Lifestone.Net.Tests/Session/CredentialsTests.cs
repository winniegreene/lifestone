using System.Reflection;
using Lifestone.Net.Session;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Password custody (m2-plan §4.6, <see cref="AcCredentials"/>): the password is copied into a private array, never
/// printed, never part of an exception message, and zeroed on Dispose. The account name follows ACE's limits.
/// </summary>
public sealed class CredentialsTests
{
    /// <summary>A sentinel that must never surface in any text the class produces.</summary>
    private const string Sentinel = "Sentinel-Pw-7Q2xZ";

    [Fact]
    public void ToString_names_the_account_and_redacts_the_password()
    {
        using var credentials = new AcCredentials("lifestone-test", Sentinel);
        Assert.Equal("lifestone-test", credentials.Account);
        Assert.Equal("account=lifestone-test, password=(redacted)", credentials.ToString());
        Assert.DoesNotContain(Sentinel, credentials.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_password_is_copied_not_referenced()
    {
        var source = Sentinel.ToCharArray();
        using var credentials = new AcCredentials("lifestone-test", source);
        Array.Clear(source);
        Assert.Equal(Sentinel, new string(credentials.Password));
    }

    [Fact]
    public void Dispose_zeroes_the_password_and_closes_access_to_it()
    {
        var credentials = new AcCredentials("lifestone-test", Sentinel);
        var buffer = PasswordBuffer(credentials);
        Assert.Equal(Sentinel, new string(buffer));

        credentials.Dispose();
        Assert.True(credentials.IsDisposed);
        Assert.All(buffer, c => Assert.Equal('\0', c));
        Assert.Throws<ObjectDisposedException>(() => credentials.Password.Length);

        credentials.Dispose(); // a second Dispose is harmless
        Assert.Equal("account=lifestone-test, password=(redacted)", credentials.ToString());
    }

    [Fact]
    public void The_password_is_held_in_exactly_one_private_char_array()
    {
        var fields = typeof(AcCredentials).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.Single(fields, f => f.FieldType == typeof(char[]));
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(string) && !f.Name.Contains("Account", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lifestöne")]
    [InlineData("life\tstone")]
    [InlineData("lifestone\u0000")]
    [InlineData("аccount")] // a Cyrillic 'a'
    public void Accounts_that_are_empty_or_not_printable_ascii_are_refused(string account)
    {
        var thrown = Assert.Throws<ArgumentException>(() => new AcCredentials(account, Sentinel));
        Assert.Equal("account", thrown.ParamName);
        Assert.DoesNotContain(Sentinel, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accounts_of_up_to_50_characters_are_accepted_and_longer_ones_refused()
    {
        using var longest = new AcCredentials(new string('a', AcCredentials.MaxAccountLength), Sentinel);
        Assert.Equal(50, longest.Account.Length);

        var thrown = Assert.Throws<ArgumentException>(() => new AcCredentials(new string('a', 51), Sentinel));
        Assert.DoesNotContain(Sentinel, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_account_or_an_empty_password_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new AcCredentials(null!, Sentinel));
        var thrown = Assert.Throws<ArgumentException>(() => new AcCredentials("lifestone-test", ReadOnlySpan<char>.Empty));
        Assert.Equal("password", thrown.ParamName);
    }

    private static char[] PasswordBuffer(AcCredentials credentials) =>
        (char[])typeof(AcCredentials).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(char[])).GetValue(credentials)!;
}
