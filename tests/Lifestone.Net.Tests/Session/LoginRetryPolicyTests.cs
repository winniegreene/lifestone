using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The enter-world retry policy (M4 4.1 fix; <see cref="LoginRetryPolicy.DecideEnterWorldRetry"/>): a 0x0D refusal (ACE still holds the character online) waits 5 s a time up to a total of 75 s, however many attempts that takes;
/// any other retryable code keeps D18's attempts and delay; retail waits for nothing.
/// </summary>
public sealed class LoginRetryPolicyTests
{
    private const uint InWorld = (uint)CharacterErrorCode.EnterGameCharacterInWorld;

    [Fact]
    public void A_held_character_is_retried_every_five_seconds_until_seventy_five_seconds_are_spent()
    {
        var policy = LoginRetryPolicy.Default;
        var waited = TimeSpan.Zero;
        var retries = 0;
        while (policy.DecideEnterWorldRetry(InWorld, retries + 1, waited) is { Retry: true } d)
        {
            Assert.True(d.WaitsForServer);
            Assert.Equal(TimeSpan.FromSeconds(5), d.Delay);
            waited += d.Delay;
            retries++;
            Assert.True(retries < 100);
        }

        Assert.Equal(15, retries);
        Assert.Equal(TimeSpan.FromSeconds(75), waited);
        Assert.Equal(EnterWorldRetry.None, policy.DecideEnterWorldRetry(InWorld, 16, waited));
    }

    [Fact]
    public void The_wait_covers_ACEs_session_timeout_and_the_logout_that_follows_it()
    {
        // ACE drops a silent client after DefaultSessionTimeout (60 s) and then runs the logout animation: the wait must outlast the first, with room for the second.
        Assert.True(LoginRetryPolicy.Default.CharacterInWorldWait >= TimeSpan.FromSeconds(60 + 10));
    }

    [Theory]
    [InlineData(0x01u)]
    [InlineData(0x15u)]
    public void Other_retryable_codes_keep_the_attempts_and_the_three_second_delay(uint code)
    {
        var policy = LoginRetryPolicy.Default;
        Assert.Equal(new EnterWorldRetry(true, TimeSpan.FromSeconds(3), false), policy.DecideEnterWorldRetry(code, 1, TimeSpan.Zero));
        Assert.Equal(new EnterWorldRetry(true, TimeSpan.FromSeconds(3), false), policy.DecideEnterWorldRetry(code, 3, TimeSpan.Zero));
        Assert.Equal(EnterWorldRetry.None, policy.DecideEnterWorldRetry(code, 4, TimeSpan.Zero));
    }

    [Fact]
    public void A_code_that_is_not_retryable_never_retries()
    {
        Assert.Equal(EnterWorldRetry.None, LoginRetryPolicy.Default.DecideEnterWorldRetry((uint)CharacterErrorCode.EnterGameCharacterLocked, 1, TimeSpan.Zero));
    }

    [Fact]
    public void Retail_and_a_zero_wait_fall_back_to_the_plain_attempts_rule()
    {
        Assert.Equal(EnterWorldRetry.None, LoginRetryPolicy.Retail.DecideEnterWorldRetry(InWorld, 1, TimeSpan.Zero));
        var plain = LoginRetryPolicy.Default with { CharacterInWorldWait = TimeSpan.Zero };
        Assert.Equal(new EnterWorldRetry(true, TimeSpan.FromSeconds(3), false), plain.DecideEnterWorldRetry(InWorld, 1, TimeSpan.Zero));
        Assert.Equal(EnterWorldRetry.None, plain.DecideEnterWorldRetry(InWorld, 4, TimeSpan.Zero));
    }
}
