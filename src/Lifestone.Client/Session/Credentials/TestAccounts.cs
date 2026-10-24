namespace Lifestone.Client.Session.Credentials;

/// <summary>
/// The three test accounts that automation may log in to (M4 D31) and the characters each one holds: the one list in code.
/// <c>tools\lib\Play.psm1</c> keeps the same keys in <c>$AutomatedAccounts</c> (a test pins that the two agree), and
/// <see cref="AutomatedAccountRule"/> refuses any other account for an automated run. Keys name entries of
/// <c>server\runtime\secrets.json</c> (names only, never a password).
/// </summary>
public static class TestAccounts
{
    /// <summary>The magic lane's account and the baseline characters' (<c>accounts.test</c>).</summary>
    public const string Test = PlayAccounts.TestKey;

    /// <summary>The melee lane's account (M4 D31): Holtburg Brawler.</summary>
    public const string Melee = "test-melee";

    /// <summary>The archer lane's account (M4 D31): Holtburg Archer.</summary>
    public const string Archer = "test-archer";

    /// <summary>The three account keys, in lane order.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Test, Melee, Archer];

    /// <summary>Per account key, its characters.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Characters { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [Test] = ["Lifestone Smoke", "Lifestone Loopback", "Lifestone Throwaway", "BSD Mage", "Holtburg Hunter"],
        [Melee] = ["Holtburg Brawler"],
        [Archer] = ["Holtburg Archer"],
    };

    /// <summary>True when <paramref name="account"/> is one of the three keys (ordinal, case-sensitive).</summary>
    public static bool IsTestAccount(string? account) => account is not null && Characters.ContainsKey(account);

    /// <summary>The account key that holds <paramref name="characterName"/> (ignoring case), or null when no test account does.</summary>
    public static string? AccountOf(string characterName)
    {
        ArgumentNullException.ThrowIfNull(characterName);
        foreach (var (key, names) in Characters)
        {
            if (names.Any(n => string.Equals(n, characterName, StringComparison.OrdinalIgnoreCase)))
            {
                return key;
            }
        }

        return null;
    }
}
