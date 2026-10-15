namespace Lifestone.Client.Session.Credentials;

/// <summary>
/// The rule "an automated run uses only the test account" (<c>accounts.test</c> of secrets.json), applied to the session options before the
/// bootstrap resolves the account. The play account is the owner's: no agent, check or test logs in to it, and the in-engine login check
/// (<c>--login-check=enter</c>) writes to its account (it creates the 'Lifestone Smoke' character). tools\play.ps1 -Offscreen enforces the same
/// rule on its side; the game's <c>SessionHost.Start</c> applies this one, so a bare <c>godot.ps1 -GameArgs:'--login-check=enter'</c> cannot reach
/// the owner's account either; and the headless host applies it twice, in <c>Program.Run</c> (before the backend or the dats are touched) and in
/// <c>HeadlessRunner.StartWith</c> (so every caller, the tests' scenarios included, gets it): a headless run is always automated.
/// </summary>
public static class AutomatedAccountRule
{
    /// <summary>
    /// The options an automated run signs in with. A run that is not automated is returned as it is. An automated run with no <c>--account</c>
    /// gets the test account; one that names any other account is refused (the account name is never repeated: a mistyped option could be a password).
    /// </summary>
    /// <param name="options">The session options of the run.</param>
    /// <param name="automated">True when the run ends by itself (<c>GameOptions.IsAutomated</c>) or runs a login check.</param>
    /// <param name="applied">The options to use; the input when refused.</param>
    /// <param name="refusal">Why the run is refused, in words that name the remedy and never a secret; null when it is not.</param>
    /// <returns>True when the run may go on with <paramref name="applied"/>.</returns>
    public static bool TryApply(SessionOptions options, bool automated, out SessionOptions applied, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(options);
        applied = options;
        refusal = null;
        if (!automated)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.Account))
        {
            applied = options with { Account = PlayAccounts.TestKey };
            return true;
        }

        if (string.Equals(options.Account, PlayAccounts.TestKey, StringComparison.Ordinal))
        {
            return true;
        }

        refusal = $"an automated run (--login-check, or any run that ends by itself) uses only the test account (accounts.{PlayAccounts.TestKey} of secrets.json): " +
                  $"leave --account out or give --account={PlayAccounts.TestKey}. The play account is the owner's, and so is any other";
        return false;
    }
}
