using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// What the BSD Mage's live tests share (M4 P4 sess2): they run their scenario over the mage's shared session (<see cref="LiveCharacter"/>, one login per test process; the scenario finds the mage in the world and, with
/// <c>RequireCharacter</c>, fails at once and sends nothing if it is another character). Not a test itself.
/// </summary>
internal static class LiveMage
{
    /// <summary>The M4 item whose <c>cache/m4/&lt;item&gt;/live</c> folder holds the shared session's recording.</summary>
    internal const string Item = "p4";

    /// <summary>The account name the gear kit is built for (ACE customises the taper slots from it; read from the secrets file at run time, never printed).</summary>
    internal static string KitAccount()
    {
        var secrets = File.ReadAllBytes(BackendProbe.MainCheckoutFiles.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(BackendProbe.TestAccountKey, AccountChoice.Default));
            return credentials.Account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    /// <summary>Why the player in the world is not <paramref name="expected"/> (its name, or that the name is not known yet), or null when it is: a leg of a shared session checks it before it sends anything.</summary>
    internal static string? WrongCharacter(IGameSessionView session, string expected)
    {
        ArgumentNullException.ThrowIfNull(session);
        var name = session.State.Self.Name;
        return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) ? null : $"the character in the world is '{name ?? "(name not known)"}', not {expected}: nothing is sent for it";
    }

    /// <summary>True when the session's entry (<see cref="LiveCharacter"/>) already put a character in the world, arrived with its qualities known: a leg then skips its own login and entering.</summary>
    internal static bool AlreadyInWorld(IGameSessionView s) =>
        s.State.Phase == SessionPhase.InWorld && s.State.Self.Guid != 0 && s.State.Self.Travel == SelfTravel.Present && s.State.Self.Qualities.IsKnown;

    /// <summary>
    /// Runs <paramref name="scenario"/> on BSD Mage over the shared session (or, with <paramref name="ownLogin"/> or <c>LIFESTONE_LOOPBACK_SESSION=per-test</c>, over a login of its own) and prints the run's summary line as
    /// <c>[label] ...</c>. The scenario must not log the mage off at its end (<c>endSession: false</c>).
    /// </summary>
    internal static LiveRunOutput Run(ITestOutputHelper output, IScenario scenario, string label, double seconds, bool ownLogin = false)
    {
        var live = LiveCharacter.Run(output, scenario, label, seconds, MageTemplate.CharacterName, Item, ownLogin);
        output.WriteLine($"[{label}] {live.Result.SummaryLine(live.RecordingName)}");
        return live;
    }
}
