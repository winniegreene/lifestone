using Lifestone.Loopback.Tests.Session;
using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.ServerMod;

/// <summary>
/// The server mod's <c>char purge</c> against the bundled ACE (owner decision D59; m2-plan §6.9, slice p1): WRITTEN AND COMPILED, RUN BY THE LEAD. It
/// creates a throwaway character on the test-tools account, proves the verb's refusals, deletes the throwaway through the Action API (ACE marks it, as the
/// retail screens do), purges it from the character <c>Lifestone Tools</c> (the purge caller of the tools account, M4 D43), and checks that the account's character list is what it was
/// at the start, on a fresh login as well. Headless: everything goes through the Action API (<see cref="ActionSource.Host"/>); no window opens.
/// </summary>
/// <remarks>
/// <para><b>Touches only the test account</b> and only the throwaway: the graduated characters are entered and left, never changed (no Add XP, no
/// quest, no teleport). Always ends logged off: a failure after the player entered the world logs off before the run ends.</para>
/// <para><b>A leftover</b> <c>Lifestone Throwaway</c> from a failed run is deleted when needed, then purged from <c>Lifestone Tools</c> first
/// (<c>leftover:</c> in the test output), and the list at the start is the list after that clean-up.</para>
/// <para><b>Where the purge is NOT exercised</b> (unit tests only, <c>CharPurgeRulesTests</c>): <c>own-character</c> (the graduated names are refused before
/// it, and both test characters are graduated), <c>character-online</c> (an account has one session).</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Tools")]
public sealed class ModCharPurgeLiveTests(ITestOutputHelper output)
{
    /// <summary>The graduated character that stays at the character screen's baseline and does the purging.</summary>
    private const string Caller = "Lifestone Tools"; // M4 D43: on test-tools, created once through the Action API when absent, never levelled or reset

    private const string Smoke = "Lifestone Smoke";

    private const string Hunter = "Holtburg Hunter";
    private const string Brawler = "Holtburg Brawler";
    private const string Archer = "Holtburg Archer";

    private const string Throwaway = "Lifestone Throwaway";

    private const string NotOnTheAccount = "Lifestone Nobody";

    [LoopbackFact]
    public void A_deleted_throwaway_is_purged_from_a_graduated_character_and_the_account_is_left_as_it_was()
    {
        using var dats = DatSet.Open();
        var leg = new PurgeLeg(HeadlessCharacter.RetailSkillClasses(dats), output.WriteLine);
        var result = Run(dats, leg, 900);
        output.WriteLine(result.SummaryLine("memory"));
        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", leg.Stage);

        // Leftover handling is logged, never silent.
        output.WriteLine($"leftover: {leg.Leftover}");

        // The refusals, each with its own code, in the rules' order.
        (string, string)[] expected =
        [
            (Throwaway, ToolErrors.NotPendingDeletion),
            (Smoke, ToolErrors.Graduated),
            (Caller, ToolErrors.Graduated), // M4 D43: Lifestone Tools is on the server mod's graduated list (needs setup-server.ps1 -ModOnly after this commit)
            (Hunter, ToolErrors.Graduated),
            (Brawler, ToolErrors.Graduated),
            (Archer, ToolErrors.Graduated),
            (NotOnTheAccount, ToolErrors.UnknownCharacter),
        ];
        Assert.Equal(expected, leg.Refusals);

        // The purge: ACE's own counts, all of it removed.
        var purged = leg.Purged ?? throw new Xunit.Sdk.XunitException("the purge gave no result");
        Assert.Equal(Throwaway, purged.Name);
        Assert.Equal(1, purged.Characters);
        Assert.Equal(1, purged.PlayerBiotas);
        Assert.True(purged.Characters + purged.PlayerBiotas + purged.Possessions > 1, "the purge removed nothing but the records");
        output.WriteLine($"purged {purged.Name}: characters {purged.Characters}, player biotas {purged.PlayerBiotas}, possessions {purged.Possessions}");

        // The list as before, in the same session after the log off (the verb drops the character from the session's own list) ...
        Assert.NotEmpty(leg.Baseline);
        Assert.DoesNotContain(leg.Baseline, c => string.Equals(c.Name, Throwaway, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(leg.Baseline, leg.EndList);

        // ... and on a fresh login, which reads the database.
        var after = new PurgeLeg.ListOnly(output.WriteLine);
        var again = Run(dats, after, 120);
        Assert.True(again.Ok, again.Failure);
        Assert.Equal(leg.Baseline, after.List);
        Assert.Contains(after.List, c => c.Name == Caller && !c.Pending);
    }

    private static HeadlessRunResult Run(DatSet dats, IScenario scenario, double seconds)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        LiveCharacter.Release(Console.WriteLine, LoopbackAccounts.For(Caller));
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = LoopbackAccounts.For(Caller), Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = seconds,
                },
                scenario,
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

        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        {
            return runner.Run(context, CancellationToken.None);
        }
    }

    /// <summary>One character of the list, as the test compares lists: the name and whether it is pending deletion.</summary>
    internal readonly record struct Listed(string Name, bool Pending);

    /// <summary>
    /// The whole leg in one session, through the Action API: login, the list at the start, the leftover clean-up if any, the throwaway's creation, the refusals
    /// from <c>Lifestone Tools</c>, the throwaway's deletion, the purge from <c>Lifestone Tools</c>, the list, quit. A failure after the player entered
    /// the world logs off first (<c>bailing</c>), so the run always ends logged off.
    /// </summary>
    internal sealed class PurgeLeg : IScenario
    {
        private const double ArrivalSeconds = 90;
        private const double BailSeconds = 30;
        private const int MaxPurges = 5;

        private readonly IReadOnlyList<uint> _skillClasses;
        private readonly Action<string> _log;
        private readonly Queue<(string Name, string Code)> _refusalsToAsk = new([(Throwaway, ToolErrors.NotPendingDeletion), (Smoke, ToolErrors.Graduated), (Caller, ToolErrors.Graduated), (Hunter, ToolErrors.Graduated), (Brawler, ToolErrors.Graduated), (Archer, ToolErrors.Graduated), (NotOnTheAccount, ToolErrors.UnknownCharacter)]);
        private readonly List<(string Name, string Code)> _refusals = [];
        private ActionHandle? _pending;
        private ToolHandle<CharPurgeResult>? _purge;
        private string _worldTask = "none";
        private string _afterLogOff = "none";
        private double _since;
        private (string Name, string Code) _asked;
        private int _purgeCount;
        private CharPurgeResult? _firstPurge;

        public PurgeLeg(IReadOnlyList<uint> skillClasses, Action<string> log)
        {
            _skillClasses = skillClasses;
            _log = log;
        }

        public string Name => "char-purge-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        /// <summary>True when a throwaway from an earlier failed run was found and cleaned up first.</summary>
        public bool Leftover { get; private set; }

        /// <summary>The list at the start (after the leftover's clean-up), sorted by name.</summary>
        public IReadOnlyList<Listed> Baseline { get; private set; } = [];

        /// <summary>The list after the last log off, sorted by name.</summary>
        public IReadOnlyList<Listed> EndList { get; private set; } = [];

        /// <summary>Each refusal asked for, with the code the mod answered.</summary>
        public IReadOnlyList<(string Name, string Code)> Refusals => _refusals;

        /// <summary>The purge's result, or null.</summary>
        public CharPurgeResult? Purged { get; private set; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            switch (Stage)
            {
                case "done":
                    return ScenarioStatus.Succeeded;
                case "failed":
                    return ScenarioStatus.Failed;
                case "bailing":
                    return Bail(session, now);
                case "start":
                    _pending = session.Actions.Login(ActionSource.Host);
                    return Go("login");
                case "login":
                    return Settled(out var login)
                        ? (login.Kind == ActionOutcomeKind.Confirmed ? Inspect(session) : Fail(session, now, "login", login))
                        : ScenarioStatus.Running;
                case "creating-caller":
                    return Settled(out var callerCreated)
                        ? (callerCreated.Kind == ActionOutcomeKind.Confirmed && Find(session, Caller) is { PendingDeletion: false } ? Inspect(session) : Fail(session, now, "creating the purge caller", callerCreated))
                        : ScenarioStatus.Running;
                case "creating":
                    return Settled(out var created)
                        ? (created.Kind == ActionOutcomeKind.Confirmed ? AfterCreate(session, now) : Fail(session, now, "creating the throwaway", created))
                        : ScenarioStatus.Running;
                case "leftover-deleting":
                    return Settled(out var leftoverDeleted)
                        ? (leftoverDeleted.Kind == ActionOutcomeKind.Confirmed ? EnterCaller(session, now, "leftover") : Fail(session, now, "deleting the leftover throwaway", leftoverDeleted))
                        : ScenarioStatus.Running;
                case "entering":
                    if (Settled(out var entered))
                    {
                        if (entered.Kind != ActionOutcomeKind.Confirmed)
                        {
                            return Fail(session, now, "entering the world as " + Caller, entered);
                        }

                        _since = session.Clock.Now;
                        return Go("arriving");
                    }

                    return ScenarioStatus.Running;
                case "arriving":
                    if (session.State.Self.Travel == SelfTravel.Present)
                    {
                        return StartWorldTask(session, now);
                    }

                    return session.Clock.Now - _since > ArrivalSeconds
                        ? Fail(session, now, "arriving: the player was not unhidden within " + ArrivalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s")
                        : ScenarioStatus.Running;
                case "purging-leftover":
                    return Settled(out var leftoverPurged) ? AfterPurge(session, now, leftoverPurged, "purging-leftover", "after-leftover", "purging the leftover throwaway") : ScenarioStatus.Running;
                case "refusing":
                    return Settled(out var refused) ? AfterRefusal(session, now, refused) : ScenarioStatus.Running;
                case "purging":
                    return Settled(out var purged) ? AfterPurge(session, now, purged, "purging", "after-purge", "purging the throwaway") : ScenarioStatus.Running;
                case "logging-off":
                    return Settled(out var loggedOff)
                        ? (loggedOff.Kind == ActionOutcomeKind.Confirmed ? AfterLogOff(session, now) : Fail(session, now, "logging off", loggedOff))
                        : ScenarioStatus.Running;
                case "deleting":
                    if (Settled(out var deleted))
                    {
                        if (deleted.Kind != ActionOutcomeKind.Confirmed)
                        {
                            return Fail(session, now, "deleting the throwaway", deleted);
                        }

                        return Find(session, Throwaway) is { PendingDeletion: true }
                            ? EnterCaller(session, now, "purge")
                            : Fail(session, now, "deleting the throwaway: the list does not show it pending deletion");
                    }

                    return ScenarioStatus.Running;
                case "quitting":
                    return Settled(out var quit)
                        ? (quit.Kind == ActionOutcomeKind.Sent ? Go("done", ScenarioStatus.Succeeded) : Fail(session, now, "quitting", quit))
                        : ScenarioStatus.Running;
                default:
                    return Fail(session, now, "unknown stage " + Stage);
            }
        }

        private ScenarioStatus Inspect(IGameSessionView session)
        {
            if (Find(session, Caller) is null)
            {
                _log($"the purge caller '{Caller}' is not on the account: created once through the Action API (never levelled or reset)");
                _pending = session.Actions.CreateCharacter(HeadlessCharacter.Intent(Caller, _skillClasses), ActionSource.Host);
                return Go("creating-caller");
            }

            var leftover = Find(session, Throwaway);
            if (leftover is null)
            {
                Baseline = Snapshot(session);
                _log("start list: " + Describe(Baseline));
                return Create(session);
            }

            Leftover = true;
            _log($"leftover: '{Throwaway}' from an earlier run is on the account (pending deletion: {leftover.Value.PendingDeletion}); it is deleted if needed and purged first");
            if (leftover.Value.PendingDeletion)
            {
                return EnterCaller(session, session.Clock.Now, "leftover");
            }

            _pending = session.Actions.DeleteCharacter(leftover.Value.Guid, ActionSource.Host);
            return Go("leftover-deleting");
        }

        private ScenarioStatus Create(IGameSessionView session)
        {
            _pending = session.Actions.CreateCharacter(HeadlessCharacter.Intent(Throwaway, _skillClasses), ActionSource.Host);
            return Go("creating");
        }

        private ScenarioStatus AfterCreate(IGameSessionView session, double now)
        {
            if (Find(session, Throwaway) is not { PendingDeletion: false })
            {
                return Fail(session, now, "creating the throwaway: the list does not show it");
            }

            return EnterCaller(session, now, "refusals");
        }

        private ScenarioStatus EnterCaller(IGameSessionView session, double now, string task)
        {
            if (Find(session, Caller) is not { PendingDeletion: false } caller)
            {
                return Fail(session, now, "the account has no " + Caller + " to purge from");
            }

            _worldTask = task;
            _pending = session.Actions.EnterWorld(caller.Guid, ActionSource.Host);
            return Go("entering");
        }

        private ScenarioStatus StartWorldTask(IGameSessionView session, double now)
        {
            switch (_worldTask)
            {
                case "leftover":
                    _purgeCount = 0;
                    _firstPurge = null;
                    _purge = session.Tools.PurgeCharacter(Throwaway, ActionSource.Host);
                    _pending = _purge;
                    return Go("purging-leftover");
                case "refusals":
                    return AskNextRefusal(session, now);
                case "purge":
                    _purgeCount = 0;
                    _firstPurge = null;
                    _purge = session.Tools.PurgeCharacter(Throwaway, ActionSource.Host);
                    _pending = _purge;
                    return Go("purging");
                default:
                    return Fail(session, now, "unknown world task " + _worldTask);
            }
        }

        /// <summary>
        /// One purge answered: a purge that removed something is asked again (a stale expired row of the same name, invisible in the list, may be next) until the verb
        /// answers unknown-character, at most <see cref="MaxPurges"/> times.
        /// </summary>
        private ScenarioStatus AfterPurge(IGameSessionView session, double now, in ActionOutcome outcome, string stage, string afterLogOff, string what)
        {
            if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith(ToolErrors.UnknownCharacter + ": ", StringComparison.Ordinal) == true && _firstPurge is not null)
            {
                _log($"{what}: unknown-character after {_purgeCount} purge(s)");
                return LogOff(session, afterLogOff, _firstPurge);
            }

            if (outcome.Kind != ActionOutcomeKind.Confirmed || _purge?.Result is not { Characters: > 0 } result)
            {
                return Fail(session, now, what, outcome);
            }

            _firstPurge ??= result;
            if (++_purgeCount >= MaxPurges)
            {
                return Fail(session, now, $"{what}: purged {MaxPurges} times and the verb still does not answer unknown-character");
            }

            _purge = session.Tools.PurgeCharacter(Throwaway, ActionSource.Host);
            _pending = _purge;
            return Go(stage);
        }

        private ScenarioStatus AskNextRefusal(IGameSessionView session, double now)
        {
            if (!_refusalsToAsk.TryDequeue(out _asked))
            {
                return LogOff(session, "after-refusals", null);
            }

            _pending = session.Tools.PurgeCharacter(_asked.Name, ActionSource.Host);
            return Go("refusing");
        }

        private ScenarioStatus AfterRefusal(IGameSessionView session, double now, in ActionOutcome outcome)
        {
            var prefix = _asked.Code + ": ";
            if (outcome.Kind != ActionOutcomeKind.ServerError || outcome.Detail is not { } detail || !detail.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Fail(session, now, $"purge of '{_asked.Name}' should be refused with {_asked.Code}", outcome);
            }

            _log($"refused as {_asked.Code}: {detail}");
            _refusals.Add(_asked);
            return AskNextRefusal(session, now);
        }

        private ScenarioStatus LogOff(IGameSessionView session, string then, CharPurgeResult? result)
        {
            if (result is not null)
            {
                Purged = result;
            }

            _afterLogOff = then;
            _pending = session.Actions.LogOff(ActionSource.Host);
            return Go("logging-off");
        }

        private ScenarioStatus AfterLogOff(IGameSessionView session, double now)
        {
            switch (_afterLogOff)
            {
                case "after-leftover":
                    // The leftover is gone: the list now is the baseline, as the rest of the run found it.
                    Baseline = Snapshot(session);
                    _log("start list (after the leftover's clean-up): " + Describe(Baseline));
                    return Create(session);
                case "after-refusals":
                    if (Find(session, Throwaway) is not { PendingDeletion: false } throwaway)
                    {
                        return Fail(session, now, "the throwaway is not on the list after the refusals");
                    }

                    _pending = session.Actions.DeleteCharacter(throwaway.Guid, ActionSource.Host);
                    return Go("deleting");
                case "after-purge":
                    EndList = Snapshot(session);
                    _log("end list: " + Describe(EndList));
                    _pending = session.Actions.Quit(ActionSource.Host);
                    return Go("quitting");
                default:
                    return Fail(session, now, "unknown log-off continuation " + _afterLogOff);
            }
        }

        /// <summary>Fails the run; if the player is in the world, logs off first.</summary>
        private ScenarioStatus Fail(IGameSessionView session, double now, string step, in ActionOutcome outcome) =>
            Fail(session, now, step + ": " + outcome.Kind + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

        private ScenarioStatus Fail(IGameSessionView session, double now, string why)
        {
            Failure ??= why;
            _log("FAILED: " + why);
            if (session.State.Phase == Lifestone.Net.Session.SessionPhase.InWorld)
            {
                _since = session.Clock.Now;
                _pending = session.Actions.LogOff(ActionSource.Host);
                return Go("bailing");
            }

            Stage = "failed";
            return ScenarioStatus.Failed;
        }

        private ScenarioStatus Bail(IGameSessionView session, double now)
        {
            if (Settled(out _) || session.State.Phase != Lifestone.Net.Session.SessionPhase.InWorld || session.Clock.Now - _since > BailSeconds)
            {
                Stage = "failed";
                return ScenarioStatus.Failed;
            }

            return ScenarioStatus.Running;
        }

        private static CharacterSlot? Find(IGameSessionView session, string name)
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

        private static List<Listed> Snapshot(IGameSessionView session) =>
            [.. session.State.Characters.Entries.Select(c => new Listed(c.Name, c.PendingDeletion)).OrderBy(c => c.Name, StringComparer.Ordinal)];

        private static string Describe(IEnumerable<Listed> list) =>
            string.Join(", ", list.Select(c => c.Name + (c.Pending ? " (pending deletion)" : string.Empty)));

        private bool Settled(out ActionOutcome outcome)
        {
            if (_pending is { Outcome: { } done })
            {
                _pending = null;
                outcome = done;
                return true;
            }

            outcome = default;
            return false;
        }

        private ScenarioStatus Go(string stage, ScenarioStatus status = ScenarioStatus.Running)
        {
            Stage = stage;
            return status;
        }

        /// <summary>A second run: log in, read the character list the server sends, quit. A fresh login reads the database.</summary>
        internal sealed class ListOnly(Action<string> log) : IScenario
        {
            private ActionHandle? _pending;

            public string Name => "char-list";

            public string Stage { get; private set; } = "start";

            public string? Failure { get; private set; }

            public double? WhereMaxMetres => null;

            public long Unmatched => 0;

            public IReadOnlyList<Listed> List { get; private set; } = [];

            public ScenarioStatus Step(IGameSessionView session, double now)
            {
                ArgumentNullException.ThrowIfNull(session);
                switch (Stage)
                {
                    case "start":
                        _pending = session.Actions.Login(ActionSource.Host);
                        Stage = "login";
                        return ScenarioStatus.Running;
                    case "login" when _pending?.Outcome is { } login:
                        if (login.Kind != ActionOutcomeKind.Confirmed)
                        {
                            return Failed("login: " + login.Kind);
                        }

                        List = Snapshot(session);
                        log("fresh login list: " + Describe(List));
                        _pending = session.Actions.Quit(ActionSource.Host);
                        Stage = "quitting";
                        return ScenarioStatus.Running;
                    case "quitting" when _pending?.Outcome is { } quit:
                        if (quit.Kind != ActionOutcomeKind.Sent)
                        {
                            return Failed("quitting: " + quit.Kind);
                        }

                        Stage = "done";
                        return ScenarioStatus.Succeeded;
                    case "done":
                        return ScenarioStatus.Succeeded;
                    default:
                        return Failure is null ? ScenarioStatus.Running : ScenarioStatus.Failed;
                }
            }

            private ScenarioStatus Failed(string why)
            {
                Failure = why;
                return ScenarioStatus.Failed;
            }
        }
    }
}
