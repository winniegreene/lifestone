using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>What the reset found of <see cref="ThrowawayReset.Throwaway"/> on the <c>test-tools</c> character list.</summary>
internal enum ThrowawayListed
{
    /// <summary>No row of the name is listed.</summary>
    None,

    /// <summary>Only rows pending deletion (marked on the character screen, still restorable).</summary>
    Pending,

    /// <summary>A live row (not pending deletion) is listed.</summary>
    Live,
}

/// <summary>What the reset does after one answer to its retail delete (<see cref="ThrowawayReset.AfterDelete"/>).</summary>
internal enum ThrowawayDeleteStep
{
    /// <summary>The delete is confirmed and the list shows the Throwaway pending: go on to the purge.</summary>
    Done,

    /// <summary>ACE still holds the character online (a recent log off): wait <see cref="ThrowawayReset.HoldWaitSeconds"/>, check the list, delete again.</summary>
    Wait,

    /// <summary>A named failure.</summary>
    Fail,
}

/// <summary>
/// M6 1 L: leaves the account <c>test-tools</c> with NO <c>Lifestone Throwaway</c>, for the Throwaway's build loopbacks (<c>ScrivenerLearnLiveTests</c>, <c>BuyTripLiveTests</c>; D39), which then build it with
/// <c>freshCharacter: false</c>. The fresh build's own purge cannot serve them: the server mod lets a character purge its own old copies only for the re-created names (<c>CharPurgeRules.Recreated</c>), so the Throwaway's
/// purge refuses <c>own-character</c>; the Throwaway's purge caller is <c>Lifestone Tools</c> (M4 D43), as in <c>ModCharPurgeLiveTests</c>' leftover path, which this reset follows.
/// </summary>
/// <remarks>
/// <para><b>The leg</b> (<see cref="ThrowawayResetLeg"/>, one login of <c>test-tools</c> through the Action API): the list is read (check); a live Throwaway is deleted through the retail delete (act), and the list must then show it
/// pending (check); <c>Lifestone Tools</c> is created once when absent (never levelled or changed) and entered; the mod's <c>char purge</c> of the Throwaway's name is asked until it answers <c>unknown-character</c>
/// (<see cref="FreshPurge"/>'s loop and limit, also when nothing is listed: an expired row is invisible in the list); after the log off the list must show no Throwaway (check); quit.</para>
/// <para><b>ACE's hold after a recent log off.</b> ACE refuses the delete with <c>CharacterError.Delete</c> (0x6) while the character is not on its offline list (@95ef426d Network/Handlers/CharacterHandler.cs:302-306,
/// <c>PlayerManager.GetOfflinePlayer</c> is null for an online player). A player that logged off, or whose session dropped, stays online until its logout animation has run and <c>FinalizeLogout</c> moves it to the
/// offline list (@95ef426d WorldObjects/Player.cs:578-656; Network/Session.cs:319-326: "they'll still exist on that landblock until the logout animation completes (~6s)"), and ACE's own normal log off ends six
/// seconds after the request (Network/Session.cs:150-152). So the rule: a 0x6 refusal while the list still shows the Throwaway live is waited out for <see cref="HoldWaitSeconds"/> (ACE's six seconds), the list is
/// read again, and the delete is sent again, at most <see cref="MaxDeleteAttempts"/> deletes (four waits, four times ACE's window); each attempt and each wait is logged; at the bound a named failure (a longer hold is
/// a player-killer delayed log off, Player.cs LogOut's <c>pk_timer</c>, or a stuck session: neither is waited out). Any other refusal fails at once.</para>
/// <para><b>The vitals refill</b> (<see cref="ThrowawayRefillLeg"/>): a non-fresh <see cref="MageBuildScenario"/> skips the fresh path's refill (<see cref="FreshRefill"/>, one level-up after the build's raises lifted the maxima),
/// so the classes run it after their build on the Throwaway itself: the same component, the same one grant, so the built Throwaway ends one level above the build's, as the fresh build left it.</para>
/// <para>The line in the test output: <c>throwaway-reset: listed=&lt;live|pending|none&gt; deleted=&lt;n&gt; purged=&lt;n&gt; waits=&lt;n&gt;</c>.</para>
/// </remarks>
internal static class ThrowawayReset
{
    /// <summary>The character reset (D39: the only character levelled freely, never Winnie).</summary>
    public const string Throwaway = "Lifestone Throwaway";

    /// <summary>The Throwaway's purge caller on <c>test-tools</c> (M4 D43): created once when absent, never levelled or reset; it only enters and leaves to purge.</summary>
    public const string Caller = "Lifestone Tools";

    /// <summary>ACE's <c>CharacterError.Delete</c> (@95ef426d Network/Enum/CharacterError.cs:37), the code of every refusal of CharacterHandler.CharacterDelete.</summary>
    public const uint CharacterErrorDelete = 0x6;

    /// <summary>The wait after a hold refusal, in seconds on the session clock: ACE's normal log off ends six seconds after the request (@95ef426d Network/Session.cs:150-152).</summary>
    public const double HoldWaitSeconds = 6;

    /// <summary>The most retail deletes the reset sends (so at most four waits, four times ACE's six-second window) before a named failure.</summary>
    public const int MaxDeleteAttempts = 5;

    /// <summary>The log category of the reset's and the refill's lines.</summary>
    public const string Category = "throwaway-reset";

    /// <summary>What the list holds of the Throwaway: a live row wins over pending ones.</summary>
    /// <param name="entries">The character list.</param>
    public static ThrowawayListed Classify(IEnumerable<CharacterSlot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var listed = ThrowawayListed.None;
        foreach (var c in entries)
        {
            if (!string.Equals(c.Name, Throwaway, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!c.PendingDeletion)
            {
                return ThrowawayListed.Live;
            }

            listed = ThrowawayListed.Pending;
        }

        return listed;
    }

    /// <summary>
    /// The decision after one answer to the retail delete (see the class remarks): confirmed with the list showing the Throwaway pending is done; a <see cref="CharacterErrorDelete"/> refusal while the list still shows it live
    /// is ACE's hold after a recent log off, waited out while <paramref name="attempts"/> is under <see cref="MaxDeleteAttempts"/>; everything else fails, named in <paramref name="why"/>.
    /// </summary>
    /// <param name="outcome">The delete's outcome.</param>
    /// <param name="listedNow">The list as it stands after the answer.</param>
    /// <param name="attempts">The deletes sent so far, this one included.</param>
    /// <param name="why">The reason, for the log line or the failure.</param>
    public static ThrowawayDeleteStep AfterDelete(in ActionOutcome outcome, ThrowawayListed listedNow, int attempts, out string why)
    {
        var said = outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
        if (outcome.Kind == ActionOutcomeKind.Confirmed)
        {
            if (listedNow == ThrowawayListed.Pending)
            {
                why = "the delete is confirmed and the list shows the Throwaway pending deletion";
                return ThrowawayDeleteStep.Done;
            }

            why = "the delete is confirmed but the list shows the Throwaway " + listedNow.ToString().ToLowerInvariant() + ", not pending deletion";
            return ThrowawayDeleteStep.Fail;
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == CharacterErrorDelete && listedNow == ThrowawayListed.Live)
        {
            if (attempts < MaxDeleteAttempts)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"ACE refused the delete ({said}) with the Throwaway still listed live: ACE still holds it online after a recent log off (CharacterHandler.cs:302-306); waiting {HoldWaitSeconds:0} s (attempt {attempts} of {MaxDeleteAttempts})");
                return ThrowawayDeleteStep.Wait;
            }

            why = string.Create(CultureInfo.InvariantCulture, $"ACE refused the delete {attempts} times ({said}), {(attempts - 1) * HoldWaitSeconds:0} s of waits: the Throwaway is still online, longer than ACE's log off (a player-killer delayed log off or a stuck session); nothing more is sent");
            return ThrowawayDeleteStep.Fail;
        }

        why = "the delete was refused (" + said + ") with the Throwaway listed " + listedNow.ToString().ToLowerInvariant() + ": not ACE's hold after a log off";
        return ThrowawayDeleteStep.Fail;
    }

    /// <summary>Runs the reset live (<see cref="ThrowawayResetLeg"/>), prints its lines and the <c>throwaway-reset:</c> line, and fails the test when it failed.</summary>
    /// <param name="output">The test's output.</param>
    /// <param name="dats">The dats.</param>
    public static void Run(ITestOutputHelper output, DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(output);
        var log = new RecordingLogSink();
        var leg = new ThrowawayResetLeg(HeadlessCharacter.RetailSkillClasses(dats), log);
        var result = RunLeg(output, "throwaway-reset", leg, dats, log, 300);
        output.WriteLine(leg.Line);
        Assert.True(result.Ok, result.Failure ?? leg.Failure);
    }

    /// <summary>Runs the refill live on the built Throwaway (<see cref="ThrowawayRefillLeg"/>) and prints its lines and its <c>refill=</c> field; fails the test when it failed.</summary>
    /// <param name="output">The test's output.</param>
    /// <param name="dats">The dats.</param>
    /// <param name="table">The XpTable (the level-up's experience).</param>
    public static void Refill(ITestOutputHelper output, DatSet dats, XpTableData table)
    {
        ArgumentNullException.ThrowIfNull(output);
        var log = new RecordingLogSink();
        var leg = new ThrowawayRefillLeg(table, log);
        var result = RunLeg(output, "refill", leg, dats, log, 180);
        output.WriteLine("[refill] refill=" + leg.Field + " (the fresh build's level-up refill, run after the non-fresh build)");
        Assert.True(result.Ok, result.Failure ?? leg.Failure);
    }

    private static HeadlessRunResult RunLeg(ITestOutputHelper output, string phase, IScenario leg, DatSet dats, RecordingLogSink log, double seconds)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var account = LoopbackAccounts.For(Throwaway);
        LiveCharacter.Release(output, account);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = account, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = seconds,
                },
                leg,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        try
        {
            using (context.Install())
            using (runner)
            {
                result = runner.Run(context, CancellationToken.None);
            }

            output.WriteLine(result.SummaryLine("memory"));
        }
        finally
        {
            var lines = log.Lines;
            output.WriteLine($"===== {phase}: {lines.Count} log lines =====");
            foreach (var line in lines)
            {
                output.WriteLine(line);
            }

            output.WriteLine($"===== end of {phase} =====");
        }

        return result;
    }
}

/// <summary>The common stage machinery of the two legs: the pending action, the failure that logs off first, the stage.</summary>
internal abstract class ThrowawayLegBase(ILogSink log) : IScenario
{
    /// <summary>How long the arrival in the world may take, in seconds on the session clock.</summary>
    protected const double ArrivalSeconds = 90;

    private const double BailSeconds = 30;

    private double _bailSince;

    public abstract string Name { get; }

    public string Stage { get; private set; } = "start";

    public string? Failure { get; private set; }

    public double? WhereMaxMetres => null;

    public long Unmatched => 0;

    /// <summary>The action in flight, or null.</summary>
    protected ActionHandle? Pending { get; set; }

    /// <summary>The log.</summary>
    protected ILogSink Log => log;

    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Stage switch
        {
            "done" => ScenarioStatus.Succeeded,
            "failed" => ScenarioStatus.Failed,
            "bailing" => Bail(session),
            _ => StepStage(session),
        };
    }

    /// <summary>One step of the leg's own stages.</summary>
    protected abstract ScenarioStatus StepStage(IGameSessionView session);

    /// <summary>Takes the settled outcome of <see cref="Pending"/>, if any.</summary>
    protected bool Settled(out ActionOutcome outcome)
    {
        if (Pending is { Outcome: { } done })
        {
            Pending = null;
            outcome = done;
            return true;
        }

        outcome = default;
        return false;
    }

    protected ScenarioStatus Go(string stage, ScenarioStatus status = ScenarioStatus.Running)
    {
        Stage = stage;
        return status;
    }

    protected static CharacterSlot? Find(IGameSessionView session, string name)
    {
        foreach (var c in session.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid))
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The log off (the leg's last world step); <c>logging-off</c> then <c>quitting</c> follow in <see cref="AfterLogOff"/>.</summary>
    protected ScenarioStatus LogOff(IGameSessionView session)
    {
        Pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    /// <summary>The common ending: the log off settled, the leg's check, the quit, done.</summary>
    protected ScenarioStatus Ending(IGameSessionView session, Func<string?> checkAfterLogOff)
    {
        switch (Stage)
        {
            case "logging-off":
                if (!Settled(out var loggedOff))
                {
                    return ScenarioStatus.Running;
                }

                if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                {
                    return Fail(session, "logging off", loggedOff);
                }

                if (checkAfterLogOff() is { } problem)
                {
                    return Fail(session, problem);
                }

                Pending = session.Actions.Quit(ActionSource.Host);
                return Go("quitting");
            case "quitting":
                if (!Settled(out var quit))
                {
                    return ScenarioStatus.Running;
                }

                return quit.Kind == ActionOutcomeKind.Sent ? Go("done", ScenarioStatus.Succeeded) : Fail(session, "quitting", quit);
            default:
                return Fail(session, "unknown stage " + Stage);
        }
    }

    protected ScenarioStatus Fail(IGameSessionView session, string step, in ActionOutcome outcome) =>
        Fail(session, step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    /// <summary>Fails the leg, named; a player in the world is logged off first, so the run always ends logged off.</summary>
    protected ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure ??= why;
        log.Warn(ThrowawayReset.Category, "FAILED: " + why);
        if (session.State.Phase == Lifestone.Net.Session.SessionPhase.InWorld)
        {
            _bailSince = session.Clock.Now;
            Pending = session.Actions.LogOff(ActionSource.Host);
            return Go("bailing");
        }

        return Go("failed", ScenarioStatus.Failed);
    }

    private ScenarioStatus Bail(IGameSessionView session)
    {
        if (Settled(out _) || session.State.Phase != Lifestone.Net.Session.SessionPhase.InWorld || session.Clock.Now - _bailSince > BailSeconds)
        {
            return Go("failed", ScenarioStatus.Failed);
        }

        return ScenarioStatus.Running;
    }
}

/// <summary>
/// The reset's one login of <c>test-tools</c> (see <see cref="ThrowawayReset"/>): <c>login</c>, <c>creating-caller</c> (only when <c>Lifestone Tools</c> is absent), <c>deleting</c> and <c>holding</c> (only for a live Throwaway),
/// <c>entering</c>, <c>arriving</c>, <c>purging</c>, <c>logging-off</c>, <c>quitting</c>. Each decision is one logged line.
/// </summary>
internal sealed class ThrowawayResetLeg(IReadOnlyList<uint> skillClasses, ILogSink log) : ThrowawayLegBase(log)
{
    private readonly FreshPurge _purge = new(ThrowawayReset.Throwaway, log, ThrowawayReset.Category);
    private bool _callerCreateTried;
    private double _since;

    public override string Name => "throwaway-reset";

    /// <summary>What the list held of the Throwaway at the login (null until read).</summary>
    public ThrowawayListed? Listed { get; private set; }

    /// <summary>The confirmed retail deletes (0 or 1).</summary>
    public int Deleted { get; private set; }

    /// <summary>The retail deletes sent.</summary>
    public int Attempts { get; private set; }

    /// <summary>The waits for ACE's hold after a recent log off.</summary>
    public int Waits { get; private set; }

    /// <summary>The old copies the mod purged.</summary>
    public int Purged => _purge.Purged;

    /// <summary>The test output's line.</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"throwaway-reset: listed={(Listed?.ToString().ToLowerInvariant() ?? "unread")} deleted={Deleted} purged={Purged} waits={Waits}");

    protected override ScenarioStatus StepStage(IGameSessionView session)
    {
        switch (Stage)
        {
            case "start":
                Pending = session.Actions.Login(ActionSource.Host);
                return Go("login");
            case "login":
                return Settled(out var login) ? (login.Kind == ActionOutcomeKind.Confirmed ? Inspect(session) : Fail(session, "login", login)) : ScenarioStatus.Running;
            case "creating-caller":
                return Settled(out var created)
                    ? (created.Kind == ActionOutcomeKind.Confirmed && Find(session, ThrowawayReset.Caller) is { PendingDeletion: false } ? Inspect(session) : Fail(session, "creating the purge caller " + ThrowawayReset.Caller, created))
                    : ScenarioStatus.Running;
            case "deleting":
                return Settled(out var deleted) ? AfterDelete(session, deleted) : ScenarioStatus.Running;
            case "holding":
                return session.Clock.Now - _since >= ThrowawayReset.HoldWaitSeconds ? AfterHold(session) : ScenarioStatus.Running;
            case "entering":
                if (!Settled(out var entered))
                {
                    return ScenarioStatus.Running;
                }

                if (entered.Kind != ActionOutcomeKind.Confirmed)
                {
                    return Fail(session, "entering the world as " + ThrowawayReset.Caller, entered);
                }

                _since = session.Clock.Now;
                return Go("arriving");
            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present && session.State.Self.Name is { } name)
                {
                    if (!string.Equals(name, ThrowawayReset.Caller, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(session, "the entered character is " + name + ", not " + ThrowawayReset.Caller + ": no purge is sent");
                    }

                    Log.Info(ThrowawayReset.Category, "in the world as " + ThrowawayReset.Caller + "; purging '" + ThrowawayReset.Throwaway + "' with the mod until it answers unknown-character");
                    return Go("purging");
                }

                return session.Clock.Now - _since > ArrivalSeconds
                    ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: {ThrowawayReset.Caller} was not unhidden within {ArrivalSeconds:0} s"))
                    : ScenarioStatus.Running;
            case "purging":
                switch (_purge.Poll(session, out var failure))
                {
                    case FreshPurgeProgress.Asking:
                        return ScenarioStatus.Running;
                    case FreshPurgeProgress.Failed:
                        return Fail(session, failure!);
                    default:
                        Log.Info(ThrowawayReset.Category, string.Create(CultureInfo.InvariantCulture, $"purged {Purged} cop(ies) of '{ThrowawayReset.Throwaway}'; logging {ThrowawayReset.Caller} off"));
                        return LogOff(session);
                }

            default:
                return Ending(session, () =>
                {
                    var after = ThrowawayReset.Classify(session.State.Characters.Entries);
                    Log.Info(ThrowawayReset.Category, "the list after the log off: Throwaway " + after.ToString().ToLowerInvariant());
                    return after == ThrowawayListed.None ? null : "after the purge the list still shows '" + ThrowawayReset.Throwaway + "' (" + after.ToString().ToLowerInvariant() + ")";
                });
        }
    }

    private ScenarioStatus Inspect(IGameSessionView session)
    {
        if (Find(session, ThrowawayReset.Caller) is not { PendingDeletion: false })
        {
            if (_callerCreateTried)
            {
                return Fail(session, "the purge caller " + ThrowawayReset.Caller + " is not listed live after its creation");
            }

            _callerCreateTried = true;
            Log.Info(ThrowawayReset.Category, "the purge caller '" + ThrowawayReset.Caller + "' is not on the account: created once through the Action API (never levelled or reset, M4 D43)");
            Pending = session.Actions.CreateCharacter(HeadlessCharacter.Intent(ThrowawayReset.Caller, skillClasses), ActionSource.Host);
            return Go("creating-caller");
        }

        var listed = ThrowawayReset.Classify(session.State.Characters.Entries);
        Listed ??= listed;
        Log.Info(ThrowawayReset.Category, "the list at the login: Throwaway " + listed.ToString().ToLowerInvariant() + " (" + string.Join(", ", session.State.Characters.Entries.Select(c => c.Name + (c.PendingDeletion ? " (pending deletion)" : string.Empty))) + ")");
        return listed == ThrowawayListed.Live ? SendDelete(session) : EnterCaller(session);
    }

    private ScenarioStatus SendDelete(IGameSessionView session)
    {
        if (Find(session, ThrowawayReset.Throwaway) is not { PendingDeletion: false } live)
        {
            return Fail(session, "deleting: no live '" + ThrowawayReset.Throwaway + "' is listed to delete");
        }

        Attempts++;
        Log.Info(ThrowawayReset.Category, string.Create(CultureInfo.InvariantCulture, $"delete attempt {Attempts} of {ThrowawayReset.MaxDeleteAttempts}: the live '{ThrowawayReset.Throwaway}' (0x{live.Guid:X8}) through the retail character delete (it is not entered)"));
        Pending = session.Actions.DeleteCharacter(live.Guid, ActionSource.Host);
        return Go("deleting");
    }

    private ScenarioStatus AfterDelete(IGameSessionView session, in ActionOutcome outcome)
    {
        var now = ThrowawayReset.Classify(session.State.Characters.Entries);
        switch (ThrowawayReset.AfterDelete(outcome, now, Attempts, out var why))
        {
            case ThrowawayDeleteStep.Done:
                Deleted++;
                Log.Info(ThrowawayReset.Category, why);
                return EnterCaller(session);
            case ThrowawayDeleteStep.Wait:
                Waits++;
                Log.Warn(ThrowawayReset.Category, string.Create(CultureInfo.InvariantCulture, $"wait {Waits}: {why}"));
                _since = session.Clock.Now;
                return Go("holding");
            default:
                return Fail(session, "deleting the Throwaway: " + why);
        }
    }

    private ScenarioStatus AfterHold(IGameSessionView session)
    {
        // Check before the act: the list may have changed while the leg waited.
        var now = ThrowawayReset.Classify(session.State.Characters.Entries);
        Log.Info(ThrowawayReset.Category, "after the wait the list shows the Throwaway " + now.ToString().ToLowerInvariant());
        return now == ThrowawayListed.Live ? SendDelete(session) : EnterCaller(session);
    }

    private ScenarioStatus EnterCaller(IGameSessionView session)
    {
        if (ThrowawayReset.Classify(session.State.Characters.Entries) == ThrowawayListed.Live)
        {
            return Fail(session, "the purge would leave a live '" + ThrowawayReset.Throwaway + "': it must be pending deletion or absent first");
        }

        if (Find(session, ThrowawayReset.Caller) is not { PendingDeletion: false } caller)
        {
            return Fail(session, "the account has no live " + ThrowawayReset.Caller + " to purge from");
        }

        Pending = session.Actions.EnterWorld(caller.Guid, ActionSource.Host);
        return Go("entering");
    }
}

/// <summary>
/// The refill after a non-fresh build (see <see cref="ThrowawayReset"/>): <c>login</c>, <c>entering</c> the live Throwaway, <c>arriving</c>, <c>qualities</c> (the player description, bounded), <c>refilling</c>
/// (<see cref="FreshRefill"/>: one grant of exactly the experience to the next level when a vital is below its maximum, checked in the read model), <c>logging-off</c>, <c>quitting</c>.
/// </summary>
internal sealed class ThrowawayRefillLeg(XpTableData table, ILogSink log) : ThrowawayLegBase(log)
{
    private const double QualitiesSeconds = 30;

    private readonly FreshRefill _refill = new(table, log, ThrowawayReset.Category);
    private double _since;

    public override string Name => "throwaway-refill";

    /// <summary>The refill's verdict field (<c>none</c>, <c>level-up:&lt;a&gt;-&gt;&lt;b&gt;</c> or <c>failed</c>).</summary>
    public string Field => _refill.Field;

    protected override ScenarioStatus StepStage(IGameSessionView session)
    {
        switch (Stage)
        {
            case "start":
                Pending = session.Actions.Login(ActionSource.Host);
                return Go("login");
            case "login":
                if (!Settled(out var login))
                {
                    return ScenarioStatus.Running;
                }

                if (login.Kind != ActionOutcomeKind.Confirmed)
                {
                    return Fail(session, "login", login);
                }

                if (Find(session, ThrowawayReset.Throwaway) is not { PendingDeletion: false } throwaway)
                {
                    return Fail(session, "refill: no live '" + ThrowawayReset.Throwaway + "' is listed (the build should have created it)");
                }

                Pending = session.Actions.EnterWorld(throwaway.Guid, ActionSource.Host);
                return Go("entering");
            case "entering":
                if (!Settled(out var entered))
                {
                    return ScenarioStatus.Running;
                }

                if (entered.Kind != ActionOutcomeKind.Confirmed)
                {
                    return Fail(session, "entering the world as " + ThrowawayReset.Throwaway, entered);
                }

                _since = session.Clock.Now;
                return Go("arriving");
            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present && session.State.Self.Name is { } name)
                {
                    if (!string.Equals(name, ThrowawayReset.Throwaway, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(session, "the entered character is " + name + ", not " + ThrowawayReset.Throwaway + ": nothing is granted");
                    }

                    _since = session.Clock.Now;
                    return Go("qualities");
                }

                return session.Clock.Now - _since > ArrivalSeconds
                    ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: {ThrowawayReset.Throwaway} was not unhidden within {ArrivalSeconds:0} s"))
                    : ScenarioStatus.Running;
            case "qualities":
                if (session.State.Self.Qualities.IsKnown)
                {
                    return Go("refilling");
                }

                return session.Clock.Now - _since > QualitiesSeconds
                    ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"refill: the player description did not arrive within {QualitiesSeconds:0} s"))
                    : ScenarioStatus.Running;
            case "refilling":
                switch (_refill.Poll(session, out var failure))
                {
                    case FreshRefillProgress.Asking:
                        return ScenarioStatus.Running;
                    case FreshRefillProgress.Failed:
                        return Fail(session, failure!);
                    default:
                        return LogOff(session);
                }

            default:
                return Ending(session, () => null);
        }
    }
}
