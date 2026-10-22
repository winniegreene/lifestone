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

    /// <summary>Runs <paramref name="scenario"/> against the live backend and prints the run's summary line to <paramref name="output"/>; the recording goes to <c>cache/m4/&lt;item&gt;/live</c> of the main checkout (M4 3.1 s4: the item names the folder, default 2.1's).</summary>
    internal static HeadlessRunResult Run(ITestOutputHelper output, DatSet dats, IScenario scenario, string name, double seconds, List<string>? logLines = null, string item = "2-1")
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        var folder = Path.Combine(RepositoryFiles.MainCheckout, "cache", "m4", item, "live");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + name + RecordingFormat.FileExtension);
        HeadlessRunResult result;
        using (var recording = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            var secrets = File.ReadAllBytes(files.SecretsFile);
            HeadlessRunner runner;
            try
            {
                runner = HeadlessRunner.Start(
                    new HeadlessRunOptions
                    {
                        Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                        QuitAfterSeconds = seconds,
                    },
                    scenario,
                    secrets,
                    File.ReadAllBytes(files.PidsFile),
                    dats,
                    TimeProvider.System,
                    log,
                    recording);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secrets);
            }

            var context = new GameThreadSynchronizationContext();
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }
        }

        logLines?.AddRange(log.Lines);
        output.WriteLine(result.SummaryLine(Path.GetFileName(path)));
        foreach (var line in log.Lines.Where(l => l.Contains("[hunt]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        return result;
    }

    /// <summary>Between two sessions of one test: ACE needs the previous log off to finish before the account logs in again.</summary>
    internal static void Pause() => Thread.Sleep(TimeSpan.FromSeconds(6));
}

/// <summary>The script machinery every live leg shares: the step loop, the follower that walks the client's body when ACE moves it, the waits, the checks and the throwaway's entry.</summary>
internal abstract class LiveLeg(string name, IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : IScenario
{
    private const double ArrivalSeconds = 90;
    private const double BailSeconds = 30;
    private readonly List<string> _problems = [];
    private IEnumerator<ActionHandle?>? _script;
    private ActionHandle? _wait;
    private double _since;

    public string Name => name;

    public string Stage { get; protected set; } = "start";

    public string? Failure { get; protected set; }

    public double? WhereMaxMetres => null;

    public long Unmatched => 0;

    /// <summary>What the leg's checks found wrong, one line each (empty when every check held).</summary>
    public IReadOnlyList<string> Problems => _problems;

    protected DatSet Dats => dats;

    protected IGameSessionView S { get; private set; } = null!;

    protected double Now { get; private set; }

    /// <summary>The follower, or null; the leg that gives it to the hunt sets <see cref="FollowIsTheHunts"/> while the hunt ticks it.</summary>
    protected IHuntFollow? Follow { get; set; }

    protected bool FollowIsTheHunts { get; set; }

    protected void Log(string text) => log($"[live {name} t={S?.Clock.Now ?? 0:0.0}] {text}");

    /// <summary>Records a failed check (logged, the leg goes on) and returns whether it held.</summary>
    protected bool Check(bool ok, string what)
    {
        if (!ok)
        {
            _problems.Add(what);
            Log("CHECK FAILED: " + what);
        }

        return ok;
    }

    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        S = session;
        Now = now;
        switch (Stage)
        {
            case "done":
                return ScenarioStatus.Succeeded;
            case "failed":
                return ScenarioStatus.Failed;
            case "bailing":
                if (_wait is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || session.Clock.Now - _since > BailSeconds)
                {
                    Stage = "failed";
                    return ScenarioStatus.Failed;
                }

                return ScenarioStatus.Running;
        }

        if (Follow is { } follow && !FollowIsTheHunts)
        {
            follow.Prepare(session, now);
            follow.Tick(session, now);
        }

        if (_wait is { IsCompleted: false })
        {
            return ScenarioStatus.Running;
        }

        try
        {
            _script ??= Script().GetEnumerator();
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return ScenarioStatus.Running;
            }

            Stage = "done";
            Follow?.Dispose();
            return Failure is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            Failure ??= Stage + ": " + e.Message;
            _problems.Add("the leg failed: " + Failure);
            Log("FAILED: " + Failure);
            if (session.State.Phase == SessionPhase.InWorld)
            {
                _since = session.Clock.Now;
                _wait = session.Actions.LogOff(ActionSource.Host);
                Stage = "bailing";
                return ScenarioStatus.Running;
            }

            Stage = "failed";
            return ScenarioStatus.Failed;
        }
    }

    protected abstract IEnumerable<ActionHandle?> Script();

    // ---------------------------------------------------------------------------------------- helpers a script uses

    protected IEnumerable<ActionHandle?> Pause(double seconds)
    {
        var until = S.Clock.Now + seconds;
        while (S.Clock.Now < until)
        {
            yield return null;
        }
    }

    protected IEnumerable<ActionHandle?> Until(Func<bool> done, double max)
    {
        var until = S.Clock.Now + max;
        while (!done() && S.Clock.Now < until)
        {
            yield return null;
        }
    }

    protected static string Describe(ActionHandle handle) =>
        handle.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : $" (code 0x{o.Code:X})") + (o.Detail is null ? string.Empty : ": " + o.Detail) : "not settled";

    /// <summary>The handle settled as Confirmed; a failed check names <paramref name="what"/>. A 0x001D (busy) is also its own check: the Action API's lane rule says no move is sent into a busy chain.</summary>
    protected bool Confirmed(ActionHandle handle, string what)
    {
        Log(what + ": " + Describe(handle));
        var notBusy = Check(handle.Outcome is not { Code: 0x001D }, what + ": the server refused the move as busy (0x001D): the lane rule was broken");
        return Check(handle.Outcome is { Kind: ActionOutcomeKind.Confirmed }, what + ": " + Describe(handle)) && notBusy;
    }

    protected static TeleportDestination Destination(string label, uint cell, Vector3 at) =>
        new(label, [], new WorldLocation(label, new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "live");

    /// <summary>The nearest object of that exact name within <paramref name="metres"/> of the player, or 0.</summary>
    protected uint Nearest(string objectName, double metres)
    {
        if (S.State.Self.Position is not { } here || !S.State.Objects.NearestNamed(objectName, in here, S.State.Self.Guid, out var guid))
        {
            return 0;
        }

        var found = S.State.Objects.TryGet(guid, out var o) && o.Position is { } p && p.CellId >> 16 == here.CellId >> 16 ? Vector3.Distance(p.Origin, here.Origin) : double.MaxValue;
        Log($"nearest '{objectName}' {guid:X8} is {found:0.0} m away");
        return found <= metres ? guid : 0;
    }

    /// <summary>Waits for the item's world object and its place in the inventory model (ACE sends the 0022 before the CreateObject).</summary>
    protected IEnumerable<ActionHandle?> WaitHeld(uint guid, string what)
    {
        foreach (var h in Until(() => S.State.Self.Inventory.Contains(guid) && S.State.Objects.TryGet(guid, out _), 10))
        {
            yield return h;
        }

        Log($"{what} {guid:X8}: in the model {S.State.Self.Inventory.Contains(guid)}, world object {S.State.Objects.TryGet(guid, out _)}");
    }

    /// <summary>
    /// A <c>place loc</c> (or poi), the end of the own teleport and a 3 s settle; a refusal is a failed check. The end of the teleport is what ACE waits for before it takes another place, a move or a give
    /// (2.1-R2: its teleporting flag clears when it handles the post-teleport LoginComplete, which portal space sends 5.6 s after the place): the travel reaches TeleportArrived, then Present, and the LoginComplete is sent.
    /// </summary>
    protected IEnumerable<ActionHandle?> PlaceAndSettle(TeleportDestination destination, string what)
    {
        var placeLogTotal = S.ActionLog.Total;
        var place = S.Tools.Place(destination, ActionSource.Host);
        foreach (var h in Until(() => place.IsCompleted, 30))
        {
            yield return h;
        }

        Log($"place {what}: {Describe(place)}");
        Check(place.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"place {what}: {Describe(place)}");
        foreach (var h in Until(() => S.State.Self.Travel is SelfTravel.TeleportArrived or SelfTravel.Arriving or SelfTravel.Materialising, 30))
        {
            yield return h;
        }

        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(placeLogTotal), 30))
        {
            yield return h;
        }

        Log($"place {what}: the teleport ended (travel {S.State.Self.Travel}, the post-teleport LoginComplete confirmed)");
        foreach (var h in Pause(3))
        {
            yield return h;
        }
    }

    /// <summary>True when the action log holds a Confirmed LoginComplete added after the log's <paramref name="total"/> entries.</summary>
    protected bool LoginCompleteConfirmedSince(long total)
    {
        var log = S.ActionLog;
        var fresh = (int)Math.Min(log.Count, log.Total - total);
        for (var i = log.Count - fresh; i < log.Count; i++)
        {
            if (log[i].Kind == ActionKind.LoginComplete && log[i].Outcome.Kind == ActionOutcomeKind.Confirmed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The stack size the model knows for a held or world item, or -1.</summary>
    protected int StackOf(uint guid)
    {
        if (S.State.Self.Inventory.TryGetStack(guid, out var size, out _))
        {
            return (int)size;
        }

        return S.State.Objects.TryGet(guid, out var o) ? (int)(o.Weenie.StackSize ?? 1u) : -1;
    }

    /// <summary>The guids of the held items (pack, side packs, worn) of that weenie class.</summary>
    protected HashSet<uint> HeldOf(uint wcid)
    {
        var set = new HashSet<uint>();
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && S.State.Self.Inventory.Contains(o.Guid))
            {
                set.Add(o.Guid);
            }
        }

        return set;
    }

    /// <summary>The guids of the world objects of that weenie class the player does not hold and that no container holds.</summary>
    protected HashSet<uint> GroundOf(uint wcid)
    {
        var set = new HashSet<uint>();
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && !S.State.Self.Inventory.Contains(o.Guid) && o.ContainerId is null or 0)
            {
                set.Add(o.Guid);
            }
        }

        return set;
    }

    protected IEnumerable<ActionHandle?> LogOffAndQuit()
    {
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

    protected static void Require(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException(what);
        }
    }
}
