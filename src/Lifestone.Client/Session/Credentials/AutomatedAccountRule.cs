namespace Lifestone.Client.Session.Credentials;

/// <summary>
/// The rule "an automated run uses only the four test accounts" (<c>test</c>, <c>test-melee</c>, <c>test-archer</c> and <c>test-tools</c> of secrets.json; M4 D31 and D39, listed in <see cref="TestAccounts"/>), applied to the session options before the
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
    /// gets the test account (<c>test</c>); one that names a test account goes on; one that names any other account is refused (the account name is never repeated: a mistyped option could be a password).
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

        if (TestAccounts.IsTestAccount(options.Account))
        {
            return true;
        }

        refusal = "an automated run (--login-check, or any run that ends by itself) uses only the test accounts " +
                  $"({string.Join(", ", TestAccounts.Keys)} of secrets.json): leave --account out or give --account={TestAccounts.Test}, " +
                  $"--account={TestAccounts.Melee}, --account={TestAccounts.Archer} or --account={TestAccounts.Tools}. The play account is the owner's, and so is any other";
        return false;
    }

    /// <summary>
    /// M4 P4 rfix-b (review R11): the characters an automated run may enter or create. <c>test</c> keeps its four (Smoke, Loopback, BSD Mage, Hunter);
    /// on <c>test-melee</c> and <c>test-archer</c> a run may enter only that account's one fighter (<see cref="TestAccounts.Characters"/>) and may not create any character;
    /// on <c>test-tools</c> (M4 D39, D43) a run may enter and create only Lifestone Throwaway and Lifestone Tools
    /// (the fighter-build scenario creates it by its own path, not through this check). The refusal never repeats the account or a character name.
    /// </summary>
    /// <param name="account">The account key of the run (null or blank means the test account).</param>
    /// <param name="enter">The character the run enters, or null for none named.</param>
    /// <param name="create">The character the run creates when missing, or null when it creates none.</param>
    /// <param name="refusal">Why it is refused; null when it is not.</param>
    /// <returns>True when the run may go on.</returns>
    public static bool TryCharacters(string? account, string? enter, string? create, out string? refusal)
    {
        refusal = null;
        if (string.IsNullOrWhiteSpace(account) || string.Equals(account, TestAccounts.Test, StringComparison.Ordinal))
        {
            // M4 D39: Lifestone Throwaway lives on test-tools (its name is unique server-wide), so the main test account neither enters nor creates it.
            if (string.Equals(enter, ThrowawayCharacter, StringComparison.OrdinalIgnoreCase) || string.Equals(create, ThrowawayCharacter, StringComparison.OrdinalIgnoreCase))
            {
                refusal = "Lifestone Throwaway lives on the tools test account, not on the main test account (M4 D39): give the tools account (see TestAccounts.Tools)";
                return false;
            }

            return true;
        }

        if (!TestAccounts.Characters.TryGetValue(account, out var own))
        {
            return true;
        }

        var tools = string.Equals(account, TestAccounts.Tools, StringComparison.Ordinal);
        if (tools && ((create is not null && !IsToolsCharacter(create)) || (enter is not null && !IsToolsCharacter(enter))))
        {
            refusal = "an automated run on the tools test account (M4 D39, D43) enters and creates only Lifestone Throwaway and Lifestone Tools (TestAccounts.Characters): the character named belongs to another account, or to none";
            return false;
        }

        var model = string.Equals(account, TestAccounts.Qwinnie, StringComparison.Ordinal);
        if (model && ((create is not null && !own.Contains(create, StringComparer.OrdinalIgnoreCase)) || (enter is not null && !own.Contains(enter, StringComparer.OrdinalIgnoreCase))))
        {
            refusal = "an automated run on the model's own account enters and creates only Qwinnie (TestAccounts.Characters)";
            return false;
        }

        if (!tools && !model && create is not null)
        {
            refusal = "an automated run on a fighter test account (not the main test account) creates no character here (each holds exactly its one fighter, built by the fighter-build scenario; a new test character is the owner's decision): remove --create-character, or a login check that creates its character";
            return false;
        }

        if (enter is not null && !own.Any(n => string.Equals(n, enter, StringComparison.OrdinalIgnoreCase)))
        {
            refusal = "an automated run on a fighter test account (not the main test account) enters only that account's own fighter (TestAccounts.Characters): the character named belongs to another account, or to none";
            return false;
        }

        return true;
    }

    /// <summary>
    /// M4 P4 rfix-b (review R11): the in-engine login checks that create their character when it is missing (<c>enter</c>, <c>academy-exit</c>, <c>holtburg-laps</c>, <c>all</c>, <c>throwaway</c>) run on the
    /// <c>test</c> account only; the fighter, mage and death kinds have their own runners and templates.
    /// </summary>
    /// <param name="account">The account key of the run (null or blank means the test account).</param>
    /// <param name="kind">The login check's kind.</param>
    /// <param name="refusal">Why it is refused; null when it is not.</param>
    /// <returns>True when the run may go on.</returns>
    public static bool TryLoginCheck(string? account, Lifestone.Client.Play.LoginCheckKind kind, out string? refusal)
    {
        if (kind == Lifestone.Client.Play.LoginCheckKind.Keep)
        {
            // M5 6 k2: the keep check enters Lifestone Throwaway (created when absent) twice, so it runs on the tools account and nowhere else, as the throwaway check does.
            if (!string.Equals(account, TestAccounts.Tools, StringComparison.Ordinal))
            {
                refusal = "the keep login check enters Lifestone Throwaway, which lives on the tools test account only (M4 D39): give the tools account (see TestAccounts.Tools)";
                return false;
            }

            return TryCharacters(account, ThrowawayCharacter, ThrowawayCharacter, out refusal);
        }

        if (kind == Lifestone.Client.Play.LoginCheckKind.Throwaway)
        {
            // M4 D39: Lifestone Throwaway lives on test-tools, so the throwaway login check runs there and nowhere else.
            if (!string.Equals(account, TestAccounts.Tools, StringComparison.Ordinal))
            {
                refusal = "the throwaway login check creates Lifestone Throwaway, which lives on the tools test account only (M4 D39): give the tools account (see TestAccounts.Tools)";
                return false;
            }

            return TryCharacters(account, ThrowawayCharacter, ThrowawayCharacter, out refusal);
        }

        var creates = kind is Lifestone.Client.Play.LoginCheckKind.Enter or Lifestone.Client.Play.LoginCheckKind.AcademyExit or Lifestone.Client.Play.LoginCheckKind.HoltburgLaps
            or Lifestone.Client.Play.LoginCheckKind.All;
        return TryCharacters(account, creates ? LoginCheckCharacter : null, creates ? LoginCheckCharacter : null, out refusal);
    }

    private const string LoginCheckCharacter = "Lifestone Smoke";
    private const string ThrowawayCharacter = "Lifestone Throwaway";

    private static bool IsToolsCharacter(string name) => TestAccounts.Characters[TestAccounts.Tools].Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}
