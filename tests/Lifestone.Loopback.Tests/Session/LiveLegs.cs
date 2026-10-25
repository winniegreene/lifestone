using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// What <c>ContainerLiveTests</c> and <c>ItemMovesLiveTests</c> share (M4 2.1 s4b; moved out of the capture test that s4b removed): one headless session run against the live backend (recorded to
/// <c>cache/m4/2-1/live/&lt;UTC&gt;-&lt;name&gt;.lsrec</c> of the main checkout, derived data under the gitignored <c>cache/</c>) and the script leg that drives it. A leg is a step-by-step
/// <see cref="IScenario"/> whose script yields the handle to wait for (or null to wait a frame). Every action goes through <see cref="IActionApi"/> (the session's <c>Actions</c> and <c>Tools</c>), never a raw send.
/// A leg records what it saw as <see cref="Problems"/> (a failed check is logged and the leg goes on); the test prints its verdict line first and then asserts that there are none. Not a test itself.
/// </summary>
internal static class LiveRun
{
    /// <summary>The throwaway character's name (created when absent, as InventoryLiveTests does).</summary>
    internal const string Throwaway = "Lifestone Throwaway";

    /// <summary>Runs <paramref name="scenario"/> against the live backend and prints the run's summary line to <paramref name="output"/>; the recording goes to <c>cache/m4/&lt;item&gt;/live</c> of the main checkout (M4 3.1 s4: the item names the folder, default 2.1's; M4 D31: <paramref name="character"/> logs in on its own test account, default the characters of <c>test</c>).</summary>
    internal static HeadlessRunResult Run(ITestOutputHelper output, IScenario scenario, string name, double seconds, List<string>? logLines = null, string item = "2-1", string? character = null, bool ownLogin = false)
    {
        var live = LiveCharacter.Run(output, scenario, name, seconds, character ?? Throwaway, item, ownLogin);
        logLines?.AddRange(live.Lines);
        output.WriteLine(live.Result.SummaryLine(live.RecordingName));
        foreach (var line in live.Lines.Where(l => l.Contains("[hunt]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        return live.Result;
    }

    /// <summary>Between two sessions of one test: ACE needs the previous log off to finish before the account logs in again. <see cref="LiveCharacter"/> pauses by itself before it logs an account in again (M4 P4); this stays for a test that logs in on its own.</summary>
    internal static void Pause() => Thread.Sleep(TimeSpan.FromSeconds(6));

    /// <summary>True while a scenario runs over a shared session (<see cref="LiveCharacter"/>): a leg must not log the character off at its end, which would end the session.</summary>
    internal static volatile bool KeepSession;
}

/// <summary>The loopback tests' script leg (M4 P5b: the machinery moved to the client's <see cref="ScriptLeg"/>): it adds the dats, the throwaway's entry and the log off at the end, and on a failure in the world it logs off before it ends.</summary>
internal abstract class LiveLeg(string name, IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : ScriptLeg(name, log)
{
    private const double ArrivalSeconds = 90;
    private double _since;

    protected DatSet Dats => dats;

    /// <inheritdoc />
    protected override bool LogOffOnFailure => true;

    protected IEnumerable<ActionHandle?> LogOffAndQuit()
    {
        if (LiveRun.KeepSession)
        {
            Log("the shared session stays logged in (no log off at the end of the leg)");
            yield break;
        }

        Stage = "logging-off";
        var off = S.Actions.LogOff(ActionSource.Host);
        yield return off;
        Log("log off: " + Describe(off));
        Stage = "quitting";
        var quit = S.Actions.Quit(ActionSource.Host);
        yield return quit;
        Log("quit: " + Describe(quit));
    }

    private static CharacterSlot? Find(IGameSessionView session, string characterName) =>
        session.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, characterName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Logs in as Lifestone Throwaway (created when absent, as InventoryLiveTests does), enters the world and waits for the arrival (the player present with its qualities, so LoginComplete has completed); never another character.</summary>
    protected IEnumerable<ActionHandle?> EnterThrowaway()
    {
        if (AlreadyInWorld)
        {
            Stage = "running";
            Follow ??= new HuntFollow(dats, new RecordingLogSink());
            Log("already in the world: the shared session's entry logged the character in");
            yield break;
        }

        Stage = "login";
        var login = S.Actions.Login(ActionSource.Host);
        yield return login;
        Require(login.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "login: " + Describe(login));
        Follow ??= new HuntFollow(dats, new RecordingLogSink());

        Stage = "throwaway";
        var slot = Find(S, LiveRun.Throwaway);
        Require(slot is not { PendingDeletion: true }, $"'{LiveRun.Throwaway}' is pending deletion from an earlier run: purge it first (ModCharPurgeLiveTests does)");
        if (slot is null)
        {
            var create = S.Actions.CreateCharacter(HeadlessCharacter.Intent(LiveRun.Throwaway, classes), ActionSource.Host);
            yield return create;
            Require(create.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "creating the throwaway: " + Describe(create));
            slot = Find(S, LiveRun.Throwaway);
            Require(slot is { PendingDeletion: false }, "creating the throwaway: the list does not show it");
        }

        Stage = "entering";
        var enter = S.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
        yield return enter;
        Require(enter.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "entering the world as " + LiveRun.Throwaway + ": " + Describe(enter));
        Stage = "arriving";
        _since = S.Clock.Now;
        while (S.State.Self.Travel != SelfTravel.Present || !S.State.Self.Qualities.IsKnown)
        {
            Require(S.Clock.Now - _since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
            yield return null;
        }

        Stage = "running";
        Log("arrived");
    }
}
