using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.World.Data;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// Marks a loopback test (or class) that runs its scenarios through <see cref="LiveRun"/>, <see cref="HuntLiveRun"/> or <see cref="LiveCharacter"/>, i.e. over the character's shared live session. Every other loopback test logs in
/// on its own, so the shared session of its account is logged off before it starts (ACE boots a second login of one account).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class LiveSharedSessionAttribute : Attribute;

/// <summary>What one scenario run over a <see cref="LiveCharacter"/> produced.</summary>
/// <param name="Result">The run's result (its own seconds, errors and counters; the same summary line as a one-shot run).</param>
/// <param name="Lines">The session's log lines written during the run.</param>
/// <param name="RecordingName">The recording file the session writes to (the file name only).</param>
internal sealed record LiveRunOutput(HeadlessRunResult Result, IReadOnlyList<string> Lines, string RecordingName);

/// <summary>
/// One login per character (M4 P4, D31): a lazily created, process-wide <see cref="HeadlessSession"/> per test CHARACTER, created on first use inside a test body (the Loopback collection and classes have no fixtures on purpose,
/// <c>LoopbackConventionTests</c>), on the character's own test account. Tests of one character then run their scenarios over the one login; only the tests whose point is a relog log in on their own.
/// </summary>
/// <remarks>
/// <para><b>One live session per account at a time.</b> Asking for another character of the account, or a test that logs in on its own (no <see cref="LiveSharedSessionAttribute"/>), logs the cached one off first (ACE boots a second
/// login of one account); a fresh login of an account waits until six seconds after the last log off of it (ACE needs the previous one to finish). Every session is logged off at process exit.</para>
/// <para><b>Known state.</b> The first run of each test puts the character in a known state before the test's scenario (<see cref="LiveKnownStateLeg"/>, which logs each change as a <c>[live-session]</c> line): peace mode; for a fighter the
/// launcher and the ammunition taken off and the template's weapon wielded (nothing for the Archer), standing in Holtburg. The Action API and the same <c>place</c> tool the tests already use; no mod verb. A character that is no fighter
/// (BSD Mage, Lifestone Throwaway, Lifestone Loopback) is only put in peace mode.</para>
/// <para><b>After a failure.</b> A test that failed after it used the session sends it through one relog before the next test: <c>[live-session] relog after failure: &lt;test&gt;</c>. The failure stays a failure; nothing is retried.</para>
/// <para><b>Switch.</b> <c>LIFESTONE_LOOPBACK_SESSION=per-test</c> makes every run log in and out as before M4 P4 (the default is <c>shared</c>); the lead proves the two give the same verdicts on one commit.</para>
/// </remarks>
internal sealed class LiveCharacter : IDisposable
{
    /// <summary>The environment variable that picks the mode: <c>shared</c> (default) or <c>per-test</c>.</summary>
    internal const string ModeVariable = "LIFESTONE_LOOPBACK_SESSION";

    private static readonly TimeSpan AccountPause = TimeSpan.FromSeconds(6);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, LiveCharacter> OpenByAccount = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> LastLogOff = new(StringComparer.Ordinal);
    private static bool _hooked;
    private static long _testSerial;
    private static string _currentTest = "(no test)";
    private static volatile bool _currentTestShared;

    private readonly DatSet _dats;
    private readonly FileStream _recording;
    private readonly RecordingLogSink _log = new();
    private HeadlessSession? _host;
    private long _resetSerial = -1;
    private long _usedSerial = -1;
    private string? _failedBy;

    private LiveCharacter(string character, string account, DatSet dats, FileStream recording, string recordingName)
    {
        Character = character;
        Account = account;
        _dats = dats;
        _recording = recording;
        RecordingName = recordingName;
    }

    /// <summary>True when every run logs in and out on its own (<c>LIFESTONE_LOOPBACK_SESSION=per-test</c>).</summary>
    internal static bool PerTest
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ModeVariable);
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "shared", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(value, "per-test", StringComparison.OrdinalIgnoreCase)
                ? true
                : throw new InvalidOperationException($"{ModeVariable} is 'shared' or 'per-test', not '{value}'");
        }
    }

    /// <summary>The character's name.</summary>
    internal string Character { get; }

    /// <summary>The character's account key.</summary>
    internal string Account { get; }

    /// <summary>The recording file's name.</summary>
    internal string RecordingName { get; }

    /// <summary>
    /// Runs <paramref name="scenario"/> over <paramref name="character"/>'s session: in shared mode the cached one (created, or recreated after a failure, a switch of character or a session a scenario ended), after the
    /// known-state reset of the test's first run; with <paramref name="ownLogin"/> or in per-test mode a session of its own that logs in for this run and out after it.
    /// </summary>
    /// <param name="output">The test's output.</param>
    /// <param name="scenario">What to run; it finds the player in the world and must not log in.</param>
    /// <param name="name">The run's name (the recording's name in a session of its own).</param>
    /// <param name="seconds">The limit on the run, in seconds on the session clock.</param>
    /// <param name="character">The character to run it on.</param>
    /// <param name="item">The M4 item whose <c>cache/m4/&lt;item&gt;/live</c> folder holds the recording.</param>
    /// <param name="ownLogin">True for a run that must log in and out by itself (a relog is its point).</param>
    internal static LiveRunOutput Run(ITestOutputHelper output, IScenario scenario, string name, double seconds, string character, string item, bool ownLogin = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(character);
        Hook();
        var own = ownLogin || PerTest;
        var live = Acquire(output, character, item, name, own);
        if (!own && !live._host!.IsInWorld)
        {
            // The session left the world after Acquire looked (a failed session, a scenario that logged off): never reset or run over it; log in again.
            output.WriteLine($"[live-session] relog: {character} was not in the world");
            lock (Gate)
            {
                if (OpenByAccount.TryGetValue(live.Account, out var open) && ReferenceEquals(open, live))
                {
                    OpenByAccount.Remove(live.Account);
                }
            }

            live.Dispose();
            lock (Gate)
            {
                LastLogOff[live.Account] = DateTime.UtcNow;
            }

            live = Acquire(output, character, item, name, own);
        }

        var serial = Interlocked.Read(ref _testSerial);
        try
        {
            if (!own && live._resetSerial != serial)
            {
                live.ResetKnownState(output);
                live._resetSerial = serial;
            }

            live._usedSerial = serial;
            var first = live._log.Lines.Count;
            LiveRun.KeepSession = !own;
            var result = live._host!.Run(scenario, seconds);
            return new LiveRunOutput(result, [.. live._log.Lines.Skip(first)], live.RecordingName);
        }
        finally
        {
            LiveRun.KeepSession = false;
            if (own)
            {
                live.Dispose();
                lock (Gate)
                {
                    LastLogOff[live.Account] = DateTime.UtcNow;
                }
            }
        }
    }

    /// <summary>
    /// Called at the top of a test that logs in on its own (an inline <c>HeadlessRunner.Start</c>, a Net live-phase test, a build test): logs off and releases the shared session of
    /// <paramref name="account"/> (ACE boots a second login of one account), writes a line saying so, and waits until six seconds after the last log off of that account. The next shared
    /// test of the account logs in again.
    /// </summary>
    /// <param name="output">The test's output.</param>
    /// <param name="account">The account key the test logs in on (<c>test</c> unless it names another).</param>
    internal static void Release(ITestOutputHelper output, string? account = null) => Release(line => output.WriteLine(line), account);

    /// <summary><see cref="Release(ITestOutputHelper, string?)"/> with the line given to <paramref name="write"/> (the Net live-phase tests have no test output at their login).</summary>
    /// <param name="write">Where the line goes.</param>
    /// <param name="account">The account key the test logs in on.</param>
    internal static void Release(Action<string> write, string? account = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        account ??= BackendProbe.TestAccountKey;
        LiveCharacter? open;
        lock (Gate)
        {
            OpenByAccount.Remove(account, out open);
        }

        if (open is not null)
        {
            write($"[live-session] released {open.Character} ({account}): this test logs in on its own");
            open.Dispose();
            lock (Gate)
            {
                LastLogOff[account] = DateTime.UtcNow;
            }
        }
        else
        {
            write($"[live-session] released nothing on '{account}': no shared session is logged in");
        }

        DateTime last;
        lock (Gate)
        {
            last = LastLogOff.GetValueOrDefault(account, DateTime.MinValue);
        }

        var wait = AccountPause - (DateTime.UtcNow - last);
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }
    }

    /// <summary>Logs off the cached session of every account (a test that logs in on its own is about to run, or the process is exiting). Safe to call at any time.</summary>
    internal static void ReleaseAll(string reason, ITestOutputHelper? output = null)
    {
        List<LiveCharacter> open;
        lock (Gate)
        {
            open = [.. OpenByAccount.Values];
            OpenByAccount.Clear();
        }

        foreach (var live in open)
        {
            output?.WriteLine($"[live-session] logging {live.Character} off ({live.Account}): {reason}");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            live.Dispose();
            if (output is null)
            {
                // M4 P4 rfix-b (R13): the end-of-process log off had no output and wrote nothing; one line per account on the console (the runner reads it).
                Console.WriteLine(ProcessExitLine(live.Character, watch.Elapsed.TotalSeconds));
            }

            lock (Gate)
            {
                LastLogOff[live.Account] = DateTime.UtcNow;
            }
        }
    }

    /// <summary>The line the log off at process exit writes for one account (R13).</summary>
    internal static string ProcessExitLine(string character, double seconds) =>
        $"[live-session] logged {character} off at process exit ({seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s)";

    /// <summary>Logs the session off (when still in the world), stops its thread and closes the recording and the dats.</summary>
    public void Dispose()
    {
        try
        {
            _host?.Dispose();
        }
        finally
        {
            _recording.Dispose();
            _dats.Dispose();
        }
    }

    private static void Hook()
    {
        lock (Gate)
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
        }

        LoopbackRun.TestStarting += OnTestStarting;
        LoopbackRun.TestFinished += OnTestFinished;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAll("process exit");
    }

    private static void OnTestStarting(ITestMethod method)
    {
        Interlocked.Increment(ref _testSerial);
        _currentTest = method.TestClass.Class.Name + "." + method.Method.Name;
        var type = typeof(LiveCharacter).Assembly.GetType(method.TestClass.Class.Name);
        var shared = type is not null && (type.IsDefined(typeof(LiveSharedSessionAttribute), false) || type.GetMethod(method.Method.Name)?.IsDefined(typeof(LiveSharedSessionAttribute), false) == true);
        _currentTestShared = shared;
        if (!shared)
        {
            ReleaseAll("the next test logs in on its own");
        }
    }

    private static void OnTestFinished(string displayName, bool failed)
    {
        if (!_currentTestShared)
        {
            // A test that logged in on its own has just logged off: the next shared login of any account waits out ACE's pause.
            lock (Gate)
            {
                foreach (var account in TestAccounts.Keys)
                {
                    LastLogOff[account] = DateTime.UtcNow;
                }
            }
        }

        if (!failed)
        {
            return;
        }

        var serial = Interlocked.Read(ref _testSerial);
        lock (Gate)
        {
            foreach (var live in OpenByAccount.Values.Where(l => l._usedSerial == serial))
            {
                live._failedBy = displayName;
            }
        }
    }

    private static LiveCharacter Acquire(ITestOutputHelper output, string character, string item, string name, bool own)
    {
        var account = LoopbackAccounts.For(character);
        LiveCharacter? stale = null;
        string? why = null;
        lock (Gate)
        {
            if (OpenByAccount.TryGetValue(account, out var cur))
            {
                if (!own && cur._failedBy is null && string.Equals(cur.Character, character, StringComparison.Ordinal) && cur._host!.IsInWorld)
                {
                    return cur;
                }

                why = cur._failedBy is { } failedBy ? "relog after failure: " + failedBy
                    : own ? "a run with its own login"
                    : !string.Equals(cur.Character, character, StringComparison.Ordinal) ? $"switching to {character} (one live session per account)"
                    : "relog: " + character + " was not in the world";
                stale = cur;
                OpenByAccount.Remove(account);
            }
        }

        if (stale is not null)
        {
            output.WriteLine(why!.StartsWith("relog", StringComparison.Ordinal) ? "[live-session] " + why : $"[live-session] logging {stale.Character} off: {why}");
            stale.Dispose();
            lock (Gate)
            {
                LastLogOff[account] = DateTime.UtcNow;
            }
        }

        DateTime last;
        lock (Gate)
        {
            last = LastLogOff.GetValueOrDefault(account, DateTime.MinValue);
        }

        var wait = AccountPause - (DateTime.UtcNow - last);
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait); // ACE needs the previous log off of the account to finish before it logs in again
        }

        var fresh = Start(output, character, account, item, own ? name : "session-" + character.Replace(' ', '-').ToLowerInvariant());
        if (!own)
        {
            fresh._host!.StartKeepAlive(); // the session is pumped only during a run: without this the 20 s silence watchdog ended an idle shared session (proof-shared.log, MeleeLiveTests)
            lock (Gate)
            {
                OpenByAccount[account] = fresh;
            }
        }

        return fresh;
    }

    private static LiveCharacter Start(ITestOutputHelper output, string character, string account, string item, string recordingLabel)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var dats = DatSet.Open();
        FileStream? recording = null;
        try
        {
            var folder = Path.Combine(RepositoryFiles.MainCheckout, "cache", "m4", item, "live");
            Directory.CreateDirectory(folder);
            var fileName = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + recordingLabel + RecordingFormat.FileExtension;
            recording = new FileStream(Path.Combine(folder, fileName), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            var live = new LiveCharacter(character, account, dats, recording, fileName);
            var throwaway = string.Equals(character, LiveRun.Throwaway, StringComparison.Ordinal);
            var entry = new LoginScenario(
                character: throwaway ? null : character,
                createCharacter: throwaway ? character : null,
                skillClasses: throwaway ? HeadlessCharacter.RetailSkillClasses(dats) : null) { StayInWorld = true };
            var secrets = File.ReadAllBytes(files.SecretsFile);
            try
            {
                live._host = HeadlessSession.Start(
                    new HeadlessRunOptions { Session = new SessionOptions { Account = account, Character = character, Server = BackendProbe.Probe().AceEndPoint! } },
                    entry,
                    secrets,
                    File.ReadAllBytes(files.PidsFile),
                    dats,
                    TimeProvider.System,
                    live._log,
                    recording);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secrets);
            }

            output.WriteLine($"[live-session] logged {character} in on '{account}' ({(PerTest ? "per-test" : "shared")} mode): {live._host.EntryResult.SummaryLine(fileName)}");
            if (!live._host.EntryResult.Ok)
            {
                var failure = live._host.EntryResult.Failure;
                live.Dispose();
                throw new InvalidOperationException($"[live-session] entering {character} failed: {failure}");
            }

            return live;
        }
        catch
        {
            if (recording is null)
            {
                dats.Dispose();
            }

            throw;
        }
    }

    private void ResetKnownState(ITestOutputHelper output)
    {
        var template = FighterTemplate.ForName(Character);
        var leg = new LiveKnownStateLeg(Character, template, line => output.WriteLine(line), _dats);
        var result = _host!.Run(leg, 180);
        output.WriteLine($"[live-session] known state for {Character}: {(result.Ok ? "ok" : "FAILED")} in {result.Seconds:0.0} s");
        if (!result.Ok)
        {
            _failedBy = _currentTest;
            throw new InvalidOperationException($"[live-session] the known-state reset of {Character} failed: {result.Failure}");
        }
    }
}

/// <summary>
/// The known-state reset (M4 P4) as a leg: peace mode; for a fighter, the launcher and the ammunition taken off, the template's weapon wielded when the template rests with one (the Mace; the Archer rests with nothing
/// wielded), and standing in Holtburg (<c>place</c> tool, only when the character is in another landblock). Every change is a logged line; a character that is already in the state costs a few frames and no message.
/// </summary>
internal sealed class LiveKnownStateLeg(string character, FighterTemplate? template, Action<string> log, DatSet dats) : LiveLeg("known-state", [], log, dats)
{
    private readonly List<uint> _scratch = [];

    protected override IEnumerable<ActionHandle?> Script()
    {
        Stage = "reset";
        var changes = new List<string>();
        var mode = S.State.Self.Qualities.CombatMode;
        if (mode != (int)CombatMode.NonCombat)
        {
            var peace = S.Actions.SetCombatMode(CombatMode.NonCombat);
            yield return peace;
            Require(peace.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local }, "peace mode: " + Describe(peace));
            changes.Add($"combat mode {mode} to peace");
        }

        if (template is not null)
        {
            if (FighterItems.WieldedAmmunition(S.State) is var ammunition && ammunition != 0)
            {
                var off = S.Actions.Unwield(ammunition, ActionSource.Host);
                yield return off;
                Require(off.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "taking the wielded ammunition off: " + Describe(off));
                changes.Add("ammunition taken off");
            }

            if (FighterItems.WieldedLauncher(S.State, _scratch) is { } launcher)
            {
                var off = S.Actions.Unwield(launcher.Guid, ActionSource.Host);
                yield return off;
                Require(off.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "taking the wielded launcher off: " + Describe(off));
                changes.Add("launcher taken off");
            }

            if (template.WieldedWeaponWcid != 0 && template.Kit.ItemOf(template.WieldedWeaponWcid) is { } weapon)
            {
                Require(FighterItems.Find(S.State, _scratch, weapon.Item.Wcid, out var guid, out var equipped), $"the {weapon.Item.Name} is not held (build {character} first)");
                if (!equipped)
                {
                    var wield = S.Actions.Wield(guid, (EquipMask)WieldSlotChoice.Choose(S.State.Self.Inventory, (uint)weapon.Item.ValidLocations), ActionSource.Host);
                    yield return wield;
                    Require(wield.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"wielding the {weapon.Item.Name}: " + Describe(wield));
                    changes.Add($"{weapon.Item.Name} wielded");
                }
            }

            var holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
            if (S.State.Self.Position is not { } at || at.CellId >> 16 != holtburg.Location.Position.CellId >> 16)
            {
                foreach (var h in PlaceAndSettle(holtburg, "Holtburg"))
                {
                    yield return h;
                }

                changes.Add("placed in Holtburg");
            }
        }

        Log(changes.Count == 0 ? $"[live-session] reset {character}: already in the known state" : $"[live-session] reset {character}: " + string.Join("; ", changes));
    }
}
