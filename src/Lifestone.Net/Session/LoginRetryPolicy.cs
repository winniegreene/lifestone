using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Net.Session;

/// <summary>
/// When a login is retried automatically (owner decision D18, a Lifestone deviation): on CharacterError
/// <see cref="CharacterErrorCode.Logon"/> (the previous session is still being torn down),
/// <see cref="CharacterErrorCode.EnterGameCharacterInWorld"/> and <see cref="CharacterErrorCode.LogonServerFull"/> (the
/// world is not open yet), and on a lost ConnectRequest; up to <see cref="Attempts"/> tries, <see cref="Delay"/> apart,
/// each from a fresh socket (ACE keys sessions by endpoint, so a LoginRequest is never resent from the same one).
/// D18 grants 3 retries, so the default is 4 attempts: a lost ConnectRequest leaves ACE a session that holds the account,
/// and with ACE's default <c>account_login_boots_in_use</c> the attempt after it always gets 0x01, which leaves one
/// retry spare for a transient failure. <see cref="Retail"/> (<c>--retail-net</c>) makes one attempt only, as retail
/// does.
/// </summary>
public sealed record LoginRetryPolicy
{
    /// <summary>Lifestone's policy (D18): the first attempt and 3 retries, 3 s apart.</summary>
    public static LoginRetryPolicy Default { get; } = new();

    /// <summary>Retail's: a single attempt, and no wait for a character the server still holds.</summary>
    public static LoginRetryPolicy Retail { get; } = new() { Attempts = 1, CharacterInWorldWait = TimeSpan.Zero };

    /// <summary>
    /// How long the enter-world request keeps retrying <see cref="CharacterErrorCode.EnterGameCharacterInWorld"/> (0x0D), in total (M4 4.1 fix; the 0x0D case of D18, extended). ACE answers 0x0D while it still lists the character as
    /// online (<c>CharacterHandler.cs</c> 230-234: <c>PlayerManager.GetOnlinePlayer(guid) != null</c>), and a character whose client simply vanished stays online until ACE logs it off: the session is dropped after
    /// <c>DefaultSessionTimeout</c> seconds of silence (60 by default and in the bundled server's Config.js; <c>NetworkSession.cs</c> 332-338 sets the timeout tick on every packet, <c>Session.cs</c> 141-145 terminates the session
    /// with NetworkTimeout, <c>Session.cs</c> 319-321 then calls <c>LogOffPlayer</c>), the logout animation runs (<c>Player.cs</c> 524-627, a few seconds), and <c>FinalizeLogout</c> moves the player offline
    /// (<c>PlayerManager.SwitchPlayerFromOnlineToOffline</c>). A graceful log off ends in seconds. The worst case ACE's own queue allows is 15 minutes (<c>PlayerManager.cs</c> 92-99, 132-141: the forced log off), which no test run should wait for.
    /// The default, 75 s, is the session timeout plus 15 s for the animation and the final log off; every retry is logged with the wait so far. Zero restores the plain <see cref="Attempts"/> rule (retail's).
    /// </summary>
    public TimeSpan CharacterInWorldWait { get; init; } = TimeSpan.FromSeconds(DefaultCharacterInWorldWaitSeconds);

    /// <summary>
    /// ACE's <c>DefaultSessionTimeout</c> as the bundled server runs it: <c>server/templates/Config.template.js</c> sets 60, and the default wait below follows it (M4 4.1 fix2b, review R12; a test reads the template and fails when the two drift).
    /// A server configured with a longer timeout needs <see cref="CharacterInWorldWait"/> set to match.
    /// </summary>
    public const double BundledSessionTimeoutSeconds = 60;

    /// <summary>The seconds added to <see cref="BundledSessionTimeoutSeconds"/> for the logout animation and <c>FinalizeLogout</c>: Lifestone's estimate, not an ACE number.</summary>
    public const double LogoutAllowanceSeconds = 15;

    /// <summary>The default of <see cref="CharacterInWorldWait"/> in seconds: the bundled session timeout plus the logout allowance (the action tracker's time limit for entering the world adds it to its own 30 s and a slack, <c>ActionPolicy.EnterWorldSeconds</c>).</summary>
    public const double DefaultCharacterInWorldWaitSeconds = BundledSessionTimeoutSeconds + LogoutAllowanceSeconds;

    /// <summary>The wait between two retries of a 0x0D refusal (see <see cref="CharacterInWorldWait"/>).</summary>
    public TimeSpan CharacterInWorldDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Decides whether an enter-world refusal starts another attempt, and how long it waits first.
    /// A 0x0D refusal retries while <paramref name="waitedInWorld"/> plus the next delay stays within <see cref="CharacterInWorldWait"/> (the number of attempts does not count); any other retryable code uses <see cref="Attempts"/> and <see cref="Delay"/>.
    /// </summary>
    /// <param name="code">The CharacterError code.</param>
    /// <param name="attempt">The attempt that was refused (1 for the first).</param>
    /// <param name="waitedInWorld">The delays already spent on 0x0D refusals of this enter-world request.</param>
    public EnterWorldRetry DecideEnterWorldRetry(uint code, int attempt, TimeSpan waitedInWorld)
    {
        if (!RetryableCharacterErrors.Contains(code))
        {
            return EnterWorldRetry.None;
        }

        if (code == (uint)CharacterErrorCode.EnterGameCharacterInWorld && CharacterInWorldWait > TimeSpan.Zero)
        {
            return waitedInWorld + CharacterInWorldDelay <= CharacterInWorldWait ? new EnterWorldRetry(true, CharacterInWorldDelay, true) : EnterWorldRetry.None;
        }

        return attempt < Attempts ? new EnterWorldRetry(true, Delay, false) : EnterWorldRetry.None;
    }

    /// <summary>The total number of attempts, the first included (at least 1).</summary>
    public int Attempts { get; init; } = 4;

    /// <summary>The wait between attempts.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The CharacterError codes that start another attempt.</summary>
    public ImmutableArray<uint> RetryableCharacterErrors { get; init; } =
    [
        (uint)CharacterErrorCode.Logon,
        (uint)CharacterErrorCode.EnterGameCharacterInWorld,
        (uint)CharacterErrorCode.LogonServerFull,
    ];

    /// <summary>Whether a lost ConnectRequest (<see cref="SessionTimeouts.ConnectRequest"/> elapsed) starts another attempt.</summary>
    public bool RetryLostConnectRequest { get; init; } = true;
}

/// <summary>What <see cref="LoginRetryPolicy.DecideEnterWorldRetry"/> decided.</summary>
/// <param name="Retry">True to send the enter-world request again after <paramref name="Delay"/>.</param>
/// <param name="Delay">The wait before the next attempt.</param>
/// <param name="WaitsForServer">True when the wait is the 0x0D one (ACE still holds the character), whose time is counted against <see cref="LoginRetryPolicy.CharacterInWorldWait"/>.</param>
public readonly record struct EnterWorldRetry(bool Retry, TimeSpan Delay, bool WaitsForServer)
{
    /// <summary>No further attempt.</summary>
    public static EnterWorldRetry None { get; } = new(false, TimeSpan.Zero, false);
}
