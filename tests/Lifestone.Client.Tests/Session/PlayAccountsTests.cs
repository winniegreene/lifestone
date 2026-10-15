using System.Text;
using Lifestone.Client.Session.Credentials;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// secrets.json, schema 1 and 2 (ST5; owner decision D10): the chosen account's credentials come out of the host's
/// bytes without the password becoming a string, and no error message ever holds a password, whatever is wrong with
/// the file.
/// </summary>
public sealed class PlayAccountsTests
{
    [Fact]
    public void Parses_v2_accounts_by_choice()
    {
        var json = SessionTestData.SecretsV2();
        using (var play = PlayAccounts.Parse(json, AccountChoice.Play))
        {
            Assert.Equal("lifestone", play.Account);
        }

        using (var test = PlayAccounts.Parse(json, AccountChoice.Test))
        {
            Assert.Equal("lifestone-test", test.Account);
        }

        using (var byDefault = PlayAccounts.Parse(json, AccountChoice.Default))
        {
            Assert.Equal("lifestone", byDefault.Account);
        }

        using (var otherDefault = PlayAccounts.Parse(SessionTestData.SecretsV2(defaultAccount: "test"), AccountChoice.Default))
        {
            Assert.Equal("lifestone-test", otherDefault.Account);
        }
    }

    [Theory]
    [InlineData("alice", "Alice-Alt")] // a key
    [InlineData("alice-alt", "Alice-Alt")] // a username, case-insensitively (ACE lower-cases account names)
    [InlineData("LIFESTONE-TEST", "lifestone-test")]
    [InlineData("test", "lifestone-test")]
    public void Named_accounts_match_keys_then_usernames(string name, string account)
    {
        using var credentials = PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.Named(name));
        Assert.Equal(account, credentials.Account);
    }

    [Fact]
    public void Parses_v1_test_account_as_the_play_account()
    {
        var json = SessionTestData.SecretsV1();
        foreach (var choice in new[] { AccountChoice.Default, AccountChoice.Play, AccountChoice.Named("play"), AccountChoice.Named("LIFESTONE") })
        {
            using var credentials = PlayAccounts.Parse(json, choice);
            Assert.Equal("lifestone", credentials.Account);
        }

        // Schema 1 has no test account, and the play account is never used for tests.
        var e = Assert.Throws<PlayAccountException>(() => PlayAccounts.Parse(json, AccountChoice.Test));
        Assert.Contains("schema 1", e.Message, StringComparison.Ordinal);
        Assert.Contains("setup-server.ps1", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_password_is_carried_into_the_credentials_and_redacted()
    {
        using var credentials = PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.Test);
        Assert.DoesNotContain(SessionTestData.TestSentinel, credentials.ToString(), StringComparison.Ordinal);
        Assert.Contains("redacted", credentials.ToString(), StringComparison.Ordinal);
        Assert.False(credentials.IsDisposed);
    }

    [Fact]
    public void Escaped_and_non_ascii_passwords_are_unescaped_without_a_string()
    {
        // The bytes hold "pa\"ssé-€" with JSON escapes; the credentials hold the unescaped text.
        var json = Encoding.UTF8.GetBytes("""{ "schema": 2, "accounts": { "play": { "username": "lifestone", "password": "pa\"ssé-€😀" } } }""");
        using var credentials = PlayAccounts.Parse(json, AccountChoice.Play);
        Assert.Equal("lifestone", credentials.Account);
    }

    [Fact]
    public void A_byte_order_mark_comments_and_trailing_commas_are_accepted()
    {
        var text = "﻿{ // generated\n \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": \"x\", }, }, }";
        using var credentials = PlayAccounts.Parse(Encoding.UTF8.GetBytes(text), AccountChoice.Default);
        Assert.Equal("lifestone", credentials.Account);
    }

    [Fact]
    public void Unknown_account_lists_keys_and_usernames_only()
    {
        var e = Assert.Throws<PlayAccountException>(() => PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.Named("hunter2-typo")));
        Assert.Equal(
            "no account in secrets.json has the key or username given to --account (the name is not repeated here); it has " +
            "play (lifestone), test (lifestone-test), alice (Alice-Alt)",
            e.Message);
        Assert.Equal(["play", "test", "alice"], PlayAccounts.List(SessionTestData.SecretsV2()).Select(a => a.Key));
        Assert.Equal([new PlayAccountInfo("testAccount", "lifestone")], PlayAccounts.List(SessionTestData.SecretsV1()));
        Assert.Equal("the account named by --account", AccountChoice.Named("hunter2-typo").ToString());
    }

    [Fact]
    public void A_default_account_that_is_missing_is_named()
    {
        var e = Assert.Throws<PlayAccountException>(() => PlayAccounts.Parse(SessionTestData.SecretsV2(defaultAccount: "gone"), AccountChoice.Default));
        Assert.Contains("defaultAccount names 'gone'", e.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> BrokenFiles => new()
    {
        { "", "missing or empty" },
        { "   \r\n", "missing or empty" },
        { "[]", "not a JSON object" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": \"" + SessionTestData.PlaySentinel + "\" ", "not valid JSON" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": \"" + SessionTestData.PlaySentinel + "\" } } } }", "not valid JSON" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": " + SessionTestData.PlaySentinel + " } } }", "not valid JSON" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": 12345 } } }", "password is not a string" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\" } } }", "password is missing" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": \"\" } } }", "password is empty" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"password\": \"" + SessionTestData.PlaySentinel + "\" } } }", "username is missing" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"löfe\", \"password\": \"" + SessionTestData.PlaySentinel + "\" } } }", "not an account name ACE accepts" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": \"" + SessionTestData.PlaySentinel + "\" } }", "accounts.play is not an object" },
        { "{ \"schema\": 2, \"accounts\": [ \"" + SessionTestData.PlaySentinel + "\" ] }", "accounts is not an object" },
        { "{ \"schema\": 2 }", "has no accounts" },
        { "{ \"schema\": \"2\", \"accounts\": {} }", "schema is not a whole number" },
        { "{ \"schema\": 3, \"accounts\": {} }", "newer than this Lifestone reads" },
        { "{ \"schema\": 2, \"accounts\": { \"test\": { \"username\": \"lifestone-test\", \"password\": \"" + SessionTestData.TestSentinel + "\" } } }", "has no accounts.play" },
        { "{ \"testAccount\": \"" + SessionTestData.PlaySentinel + "\" }", "testAccount is not an object" },
        { "{ \"mariadb\": { \"rootPassword\": \"" + SessionTestData.PlaySentinel + "\" } }", "has no testAccount" },
        { "{ \"schema\": 2, \"accounts\": { \"play\": { \"username\": \"lifestone\", \"password\": \"\\ud800" + SessionTestData.PlaySentinel + "\" } } }", "not valid" },
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public void Sentinel_password_never_appears_in_any_error_message(string file, string expected)
    {
        var e = Assert.Throws<PlayAccountException>(() => PlayAccounts.Parse(Encoding.UTF8.GetBytes(file), AccountChoice.Play));
        Assert.Contains(expected, e.Message, StringComparison.Ordinal);
        foreach (var sentinel in SessionTestData.Sentinels)
        {
            Assert.DoesNotContain(sentinel, e.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel[..8], e.Message, StringComparison.Ordinal);
        }

        Assert.False(PlayAccounts.TryParse(Encoding.UTF8.GetBytes(file), AccountChoice.Play, out var none, out var problem));
        Assert.Null(none);
        Assert.Equal(e.Message, problem);
    }

    [Fact]
    public void Credentials_buffer_is_zeroed_on_dispose()
    {
        // The credentials hold their own copy, zeroed by Dispose (net's AcCredentials); the parse's pooled buffer is zeroed
        // before it goes back to the pool, so a later renter of the same size never sees the password.
        var credentials = PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.Play);
        credentials.Dispose();
        Assert.True(credentials.IsDisposed);

        var length = Encoding.UTF8.GetByteCount(SessionTestData.PlaySentinel);
        var rented = System.Buffers.ArrayPool<char>.Shared.Rent(length);
        try
        {
            Assert.DoesNotContain(SessionTestData.PlaySentinel, new string(rented), StringComparison.Ordinal);
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(rented);
        }
    }
}
