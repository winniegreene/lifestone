namespace Lifestone.ServerMod;

/// <summary>
/// The rule of <see cref="CorpseCoin"/>, with nothing from ACE in it, so Lifestone.ServerMod.Tests runs it (CorpseCoinRulesTests). M6 7 finish, closes <c>m6-corpse-coin-server</c>: whether a dead player's coin lies on the corpse.
/// </summary>
/// <remarks>
/// <para><b>Retail and ACE.</b> Retail left half of a dead player's pyreals on the corpse. ACE's server property <see cref="Property"/> (default true) destroys them instead (@95ef426d WorldObjects/Player_Death.cs:569-578).</para>
/// <para><b>The default and the flag.</b> Retail is the default, so the mod sets the property false at every start; <see cref="EnvironmentVariable"/> set to <see cref="Ace"/> sets it true and restores ACE's destroy. The mod sets it both ways on purpose: ACE saves the value, so a
/// flag that only skipped the set would leave retail's value standing.</para>
/// </remarks>
internal static class CorpseCoinRules
{
    /// <summary>ACE's server property (a boolean in <c>config_properties_boolean</c>).</summary>
    public const string Property = "corpse_destroy_pyreals";

    /// <summary>The environment variable that restores ACE's destroy, read once when the world opens.</summary>
    public const string EnvironmentVariable = "LIFESTONE_ACE_CORPSE_COIN";

    /// <summary>The value that restores ACE's destroy.</summary>
    public const string Ace = "ace";

    /// <summary>The value the property takes: false (retail, the coin stays on the corpse) unless the variable says <see cref="Ace"/> (compared ignoring case and spaces at its ends; any other value leaves retail's rule).</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    public static bool DestroyCoin(string? value) => string.Equals(value?.Trim(), Ace, StringComparison.OrdinalIgnoreCase);
}
