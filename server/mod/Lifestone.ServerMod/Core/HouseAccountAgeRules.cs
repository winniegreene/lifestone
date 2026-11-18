namespace Lifestone.ServerMod;

/// <summary>
/// The rule of <see cref="HouseAccountAge"/>, with nothing from ACE in it, so Lifestone.ServerMod.Tests runs it (HouseAccountAgeRulesTests). M6 villa, the owner's decision of 2026-11-16: a house needs no account age.
/// </summary>
/// <remarks>
/// <para><b>Retail and ACE.</b> Retail refused any dwelling but an apartment to an account younger than 15 days. ACE keeps that rule behind its server property <see cref="Property"/> (default true; @95ef426d
/// WorldObjects/Player_House.cs:88-100), and while it holds, ACE also back-dates a young account's HousePurchaseTimestamp so the house panel shows the wait (Player_House.cs:1862-1893, run at login, Player_Networking.cs:49-54).</para>
/// <para><b>Why off.</b> A single-player world with fresh test accounts: the wait guards nothing here and only parks the climber (the owner, 2026-11-16). This is an owner decision against retail, not a glitch fix.</para>
/// <para><b>The default and the flag.</b> The mod sets the property false at every start; <see cref="EnvironmentVariable"/> set to <see cref="Ace"/> sets it true and restores the 15-day rule. Both ways on purpose: ACE saves the value, so a
/// flag that only skipped the set would leave the last value standing. With the property false ACE itself clears the back-dated timestamp at the character's next login (or purchase), so the 30-day re-purchase cooldown
/// (<c>house_30day_cooldown</c>, untouched) no longer reads the account's age.</para>
/// </remarks>
internal static class HouseAccountAgeRules
{
    /// <summary>ACE's server property (a boolean in <c>config_properties_boolean</c>).</summary>
    public const string Property = "house_15day_account";

    /// <summary>The environment variable that restores the 15-day rule, read once when the world opens.</summary>
    public const string EnvironmentVariable = "LIFESTONE_ACE_HOUSE_ACCOUNT_AGE";

    /// <summary>The value that restores the 15-day rule.</summary>
    public const string Ace = "ace";

    /// <summary>The value the property takes: false (no account age, the owner's rule) unless the variable says <see cref="Ace"/> (compared ignoring case and spaces at its ends; any other value leaves the owner's rule).</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    public static bool RequireFifteenDays(string? value) => string.Equals(value?.Trim(), Ace, StringComparison.OrdinalIgnoreCase);
}
