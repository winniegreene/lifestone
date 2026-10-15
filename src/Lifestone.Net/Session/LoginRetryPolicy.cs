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

    /// <summary>Retail's: a single attempt.</summary>
    public static LoginRetryPolicy Retail { get; } = new() { Attempts = 1 };

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
