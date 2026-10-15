using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>One character of the account as the leg compares lists: the name and whether it is pending deletion (guids differ between a character and its re-creation, so they are not compared).</summary>
/// <param name="Name">The character's name.</param>
/// <param name="Pending">True when the character is pending deletion.</param>
public readonly record struct ListedCharacter(string Name, bool Pending);

/// <summary>
/// The scenario behind <c>--login-check=throwaway</c> (m2-plan section 6.9; owner decisions D59, D61, D65; DoD 3 and 10): a character is created through the retail
/// screens, used (Where am I, Add XP, a Places teleport through portal space), logged off, deleted, restored, deleted again and purged from the account, and the account
/// ends as it began. It acts through two doors: the Action API and <see cref="PlayerTools"/> where the screens are not the subject (entering <see cref="Smoke"/>, the
/// purge, the log off), and <see cref="IThrowawayScreens"/> where they are (character creation, the tools panel, the character list's Delete and Restore).
/// It is not part of <c>--login-check=all</c>, and it never blocks.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>), on the test account:</para>
/// <list type="number">
/// <item><c>login</c>, <c>inspect</c>: the character list is reached and remembered (<see cref="Baseline"/>). A <see cref="Throwaway"/> from a failed run
/// (<see cref="Leftover"/>), or a character under <see cref="InvalidName"/> (ACE accepted a name it should have refused), is deleted through the screen if it is not pending
/// (<c>leftover-*</c>), <see cref="Smoke"/> enters, purges each of them and logs off, and the leg goes on; the baseline is the list without them.</item>
/// <item><c>chargen-open</c>, <c>name-taken</c>, <c>name-invalid</c>, <c>name-throwaway</c>: character creation to the summary page by clicks, then three Finishes:
/// <see cref="Smoke"/> (ACE: name taken), <see cref="InvalidName"/> (ACE: name refused, a creature's name in its world database) and <see cref="Throwaway"/>. Each refusal is
/// a retail dialog that is read and dismissed (<see cref="NameErrors"/>). The third creates the character, and the client enters the world at once (D65).</item>
/// <item><c>arriving</c>, <c>settling</c>: the server unhides the player, who must be the throwaway.</item>
/// <item><c>tools-open</c>, <c>tools-where</c>, <c>tools-xp</c>, <c>tools-place</c>, <c>place-leaving</c>, <c>place-arriving</c>: the tools panel by its hotkey; Where am I
/// (<see cref="WhereMetres"/>, within <see cref="WhereToleranceMetres"/>); Add XP, refused here and by the driver unless the character in the world is the throwaway (D61);
/// a Places teleport to Holtburg through portal space.</item>
/// <item><c>logging-off</c>, then <c>char-*</c>: back at the list the throwaway is selected and deleted (the typed word), restored, and deleted again
/// (<see cref="Deletes"/>, <see cref="Restores"/>).</item>
/// <item><c>smoke-*</c>: <see cref="Smoke"/> enters (it only enters and leaves; a pending character cannot enter the world), <see cref="PlayerTools.PurgeCharacter"/> removes
/// the throwaway (<see cref="Purged"/>), and <see cref="Smoke"/> logs off. A name is purged until the verb answers <c>unknown-character</c> (at most <see cref="MaxPurges"/>
/// times), so a stale expired row of the same name that the list never shows is cleared too.</item>
/// <item><c>compare</c>, <c>quitting</c>: the list now equals the baseline (<see cref="ListSame"/>); the game quits.</item>
/// </list>
/// <para><b>Failure.</b> Any failure names its stage (<see cref="Failure"/>). From the moment the leg may have changed the account, the scenario then recovers before it reports
/// the failure: it logs off if it is in the world, and deletes (the pending state the next run's leftover path purges) the throwaway, or a character made under
/// <see cref="InvalidName"/>, if it is on the list and ready. It never touches any other character. Character names appear only in the log, never in the verdict
/// (<see cref="VerdictFields"/>).</para>
/// </remarks>
public sealed class ThrowawayLegScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token.</summary>
    public const string ScenarioName = "login-throwaway";

    /// <summary>The character the leg creates, uses, deletes and purges (a test character on the test account).</summary>
    public const string Throwaway = "Lifestone Throwaway";

    /// <summary>The graduated test character the leg enters to purge from, and the name it tries to create to see ACE's name-taken answer. It only enters and leaves.</summary>
    public const string Smoke = LoginCheckScenario.DefaultCharacter;

    /// <summary>
    /// A name the client lets through to ACE and ACE refuses: the name of a creature in ACE's world database (<c>creature_name_check</c>, on by default; the world
    /// database's weenie 7 is a Drudge Skulker). The client's only name rules are the length limit and the trim, so it sends this.
    /// </summary>
    public const string InvalidName = "Drudge Skulker";

    /// <summary>The most purges one name gets in a row: the visible pending row first, then any stale expired rows of the same name, until the verb answers unknown-character.</summary>
    public const int MaxPurges = 5;

    /// <summary>The experience Add XP gives (D61).</summary>
    public const long XpAmount = 2500;

    /// <summary>The Places search text of the teleport (Holtburg's point of interest).</summary>
    public const string PlaceQuery = "holtburg";

    /// <summary>Holtburg's landblock (the high word of its cells): the Places teleport must arrive here.</summary>
    public const uint HoltburgLandblock = 0xA9B4;

    /// <summary>How near the server's view and the client's body must be at Where am I, in metres (DoD 10).</summary>
    public const double WhereToleranceMetres = 0.5;

    /// <summary>The limit on reaching the character list, in seconds on the session clock.</summary>
    public const double ListSeconds = 60;

    /// <summary>The limit on one screen operation, in seconds.</summary>
    public const double OperationSeconds = 30;

    /// <summary>The limit on one server answer (an action, a tool), in seconds.</summary>
    public const double AnswerSeconds = 20;

    /// <summary>The limit from the create's answer to the unhide of the player, in seconds (the create, the list, Enter and portal space).</summary>
    public const double ArrivalSeconds = 120;

    /// <summary>The limit on a teleport's arrival, in seconds.</summary>
    public const double TeleportSeconds = 90;

    /// <summary>The limit on a log off, in seconds (the server answers about 6 s after the request).</summary>
    public const double LogOffSeconds = 30;

    /// <summary>How long a recovery waits at the list after a create before it trusts the list (the client's Enter for a created character may still be on its way), in seconds.</summary>
    public const double CreateSettleSeconds = 3;

    /// <summary>How long the player stays present before the tools are used, in seconds.</summary>
    public const double SettleSeconds = 3;

    private readonly IThrowawayScreens _screens;
    private readonly Func<ActionHandle?>? _requestLogOff;
    private readonly Action<string> _log;
    private readonly Func<IGameSessionView, string, ToolHandle<CharPurgeResult>>? _purge;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<uint> _initialGuids = [];
    private readonly Queue<string> _charSteps = [];
    private readonly Queue<uint> _recoverTargets = [];
    private ScreenOp? _op;
    private ActionHandle? _pending;
    private ToolHandle<CharPurgeResult>? _purgeHandle;
    private bool _subscribed;
    private double _stageStart = double.NaN;
    private string _afterLogOff = string.Empty;
    private string _smokeTask = string.Empty;
    private int _teleports;
    private int _teleportsAtPlace;
    private bool _mayHaveChangedAccount;
    private bool _recovering;
    private uint _enteringGuid;
    private double _recoverLogOffAt = double.NaN;
    private bool _recoverScanned;
    private bool _createPossible;
    private readonly List<string> _leftoverNames = [];
    private readonly Queue<string> _leftoverDeletes = [];
    private readonly Queue<string> _purgeNames = [];
    private string _leftoverCurrent = string.Empty;
    private string _purgeName = string.Empty;
    private int _purgeCount;

    /// <summary>A throwaway leg.</summary>
    /// <param name="screens">The screen-action seam (the game's driver, or a test's fake).</param>
    /// <param name="requestLogOff">How the host asks for a log off (retail's key, Shift+Escape), or null to call <see cref="IActionApi.LogOff"/>; it returns the handle of the log off it asked for, or null when it asked for none.</param>
    /// <param name="log">Where the leg writes what it found (character names allowed), or null.</param>
    /// <param name="purge">How the purge is asked for, or null for <see cref="PlayerTools.PurgeCharacter"/> on the session (tests supply their own: <see cref="PlayerTools"/> is bound to a real session).</param>
    public ThrowawayLegScenario(IThrowawayScreens screens, Func<ActionHandle?>? requestLogOff = null, Action<string>? log = null, Func<IGameSessionView, string, ToolHandle<CharPurgeResult>>? purge = null)
    {
        ArgumentNullException.ThrowIfNull(screens);
        _screens = screens;
        _requestLogOff = requestLogOff;
        _purge = purge;
        _log = log ?? (_ => { });
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The list at the start (without a leftover throwaway, which the leg purges first); the list at the end must equal it.</summary>
    public IReadOnlyList<ListedCharacter> Baseline { get; private set; } = [];

    /// <summary>The list the end was compared with, or empty before the comparison.</summary>
    public IReadOnlyList<ListedCharacter> EndList { get; private set; } = [];

    /// <summary>True when a throwaway from an earlier failed run was on the account at the start.</summary>
    public bool Leftover { get; private set; }

    /// <summary>How many refused names were shown in a retail dialog and dismissed (the leg wants 2).</summary>
    public int NameErrors { get; private set; }

    /// <summary>The dialog text of each refused name, in order (log only).</summary>
    public IReadOnlyList<string> NameErrorTexts => _nameErrorTexts;

    private readonly List<string> _nameErrorTexts = [];

    /// <summary>The metres between the server's position and the client body's at Where am I, or null before.</summary>
    public double? WhereMetres { get; private set; }

    /// <summary>True when Add XP was confirmed with <see cref="XpAmount"/> added.</summary>
    public bool XpAdded { get; private set; }

    /// <summary>True when the Places teleport took the player through portal space to Holtburg.</summary>
    public bool PlaceTeleported { get; private set; }

    /// <summary>How many times the throwaway was deleted on the character screen (the leg wants 2).</summary>
    public int Deletes { get; private set; }

    /// <summary>How many times the throwaway was restored (the leg wants 1).</summary>
    public int Restores { get; private set; }

    /// <summary>The final purge's reply, or null before it.</summary>
    public CharPurgeResult? Purged { get; private set; }

    /// <summary>How many purges beyond the first of a name were confirmed (stale rows of the same name that the list never showed), over the whole leg.</summary>
    public int StalePurges { get; private set; }

    /// <summary>The leftover's first purge reply, or null.</summary>
    public CharPurgeResult? LeftoverPurged { get; private set; }

    /// <summary>True when the list at the end equals the baseline, false when it does not, null before the comparison.</summary>
    public bool? ListSame { get; private set; }

    /// <summary>The session clock's time when the create was answered (the client goes into the world at once), or NaN.</summary>
    public double CreatedAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the session began entering the world after the create, or NaN.</summary>
    public double EnterAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the server unhid the throwaway, or NaN.</summary>
    public double PresentAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the settle ended (the player could use the tools), or NaN.</summary>
    public double SettledAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the Places teleport was asked for, or NaN.</summary>
    public double PlaceAskedAt { get; private set; } = double.NaN;

    /// <summary>The session clock's time when the player was unhidden again at the teleport's destination, or NaN.</summary>
    public double PlaceArrivedAt { get; private set; } = double.NaN;

    /// <summary>True when the leg began recovering from a failure.</summary>
    public bool Recovered => _recovering;

    /// <inheritdoc/>
    public double? WhereMaxMetres => WhereMetres;

    /// <inheritdoc/>
    public long Unmatched => 0;

    /// <summary>True when the leg ran to the end and nothing failed.</summary>
    public bool Ok => Failure is null && Stage == "done" && Verified;

    /// <summary>True when every field of the verdict has its expected value.</summary>
    public bool Verified =>
        Purged is not null && NameErrors == 2 && XpAdded && WhereMetres is <= WhereToleranceMetres && PlaceTeleported && Deletes == 2 && Restores == 1 && ListSame == true;

    /// <summary>
    /// The leg's part of the verdict line, with no names in it: <c>throwaway=purged name_errors=2 xp=added where_m=&lt;m&gt; place=teleported deletes=2 restores=1
    /// list=same leftover=&lt;yes|no&gt;</c> (a field the leg did not reach is <c>n/a</c>, a throwaway that was not purged is <c>failed</c>).
    /// </summary>
    public string VerdictFields() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"throwaway={(Purged is not null ? "purged" : "failed")} name_errors={NameErrors} xp={(XpAdded ? "added" : "n/a")} where_m={(WhereMetres is { } m ? m.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")} place={(PlaceTeleported ? "teleported" : "n/a")} deletes={Deletes} restores={Restores} list={(ListSame is { } same ? (same ? "same" : "different") : "n/a")} leftover={(Leftover ? "yes" : "no")}");

    /// <summary>Compares two lists as sets of (name, pending), ignoring order and case; <paramref name="difference"/> says what differs (names allowed: it is for the log).</summary>
    /// <param name="expected">The list at the start.</param>
    /// <param name="actual">The list at the end.</param>
    /// <param name="difference">What differs, or an empty string.</param>
    public static bool SameList(IReadOnlyList<ListedCharacter> expected, IReadOnlyList<ListedCharacter> actual, out string difference)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var missing = expected.Where(e => !actual.Any(a => Equal(a, e))).Select(Describe).ToList();
        var extra = actual.Where(a => !expected.Any(e => Equal(a, e))).Select(Describe).ToList();
        difference = missing.Count == 0 && extra.Count == 0 && expected.Count == actual.Count
            ? string.Empty
            : $"expected {expected.Count} ({string.Join(", ", expected.Select(Describe))}), got {actual.Count} ({string.Join(", ", actual.Select(Describe))}); missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}]";
        return difference.Length == 0;

        static bool Equal(ListedCharacter a, ListedCharacter b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && a.Pending == b.Pending;
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        Subscribe(session);
        var clock = session.Clock.Now;
        if (double.IsNaN(_stageStart))
        {
            _stageStart = clock;
        }

        if (Stage == "failed")
        {
            return ScenarioStatus.Failed;
        }

        if (_recovering)
        {
            return Recover(session, clock);
        }

        _op?.Advance(clock);
        var status = Run(session, clock);
        if (status == ScenarioStatus.Running && double.IsNaN(EnterAt) && !double.IsNaN(CreatedAt) && session.State.Phase is SessionPhase.EnteringWorld or SessionPhase.InWorld)
        {
            EnterAt = clock;
        }

        return status;
    }

    /// <summary>Stops the leg from outside (the host's time limit): the failure is recorded and the recovery runs as for any failure; keep stepping until it reports <see cref="ScenarioStatus.Failed"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="why">Why the leg is stopped.</param>
    public void Abort(IGameSessionView session, string why)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Stage is "failed" or "done" || _recovering)
        {
            Failure ??= why;
            return;
        }

        _stageStart = double.NaN;
        Fail(session, session.Clock.Now, why);
    }

    /// <summary>Drops the subscriptions on the session's bus.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Subscribe(IGameSessionView session)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _subscriptions.Add(session.Events.Subscribe<PlayerTeleportAnnounced>((in _) => _teleports++));
    }

    private ScenarioStatus Run(IGameSessionView session, double clock)
    {
        var state = session.State;
        switch (Stage)
        {
            case "start":
                return Go("login", clock);
            case "login":
                return Login(state, clock);
            case "inspect":
                return Inspect(session, clock);

            // The leftover throwaway of a failed run: deleted if needed, then purged from Smoke.
            case "leftover-select":
                return AfterOp(session, clock, "selecting the leftover", () => StartOp(_screens.DeleteSelected(), "leftover-delete", clock));
            case "leftover-delete":
                return AfterOp(session, clock, "deleting the leftover", () => FindSlot(state, _leftoverCurrent) is { PendingDeletion: true } ? NextLeftover(session, clock) : Fail(session, clock, "deleting the leftover: the list does not show it pending deletion"));

            // Character creation.
            case "chargen-open":
                return AfterOp(session, clock, "opening character creation", () => StartOp(_screens.Finish(Smoke), "name-taken", clock));
            case "name-taken":
                return AfterFinish(session, clock, "the taken name", "dismiss-1", false);
            case "dismiss-1":
                return AfterOp(session, clock, "dismissing the name-taken dialog", () => StartOp(_screens.Finish(InvalidName), "name-invalid", clock));
            case "name-invalid":
                return AfterFinish(session, clock, "the invalid name", "dismiss-2", true);
            case "dismiss-2":
                return AfterOp(session, clock, "dismissing the invalid-name dialog", () => StartOp(_screens.Finish(Throwaway), "name-throwaway", clock));
            case "name-throwaway":
                return CreateAnswered(session, clock);
            case "arriving":
                return Arriving(session, clock);
            case "settling":
                return Settling(session, clock);

            // The tools panel.
            case "tools-open":
                return AfterOp(session, clock, "opening the tools panel", () => StartOp(_screens.PressWhere(), "tools-where", clock));
            case "tools-where":
                return AfterWhere(session, clock);
            case "tools-xp":
                return AfterXp(session, clock);
            case "tools-place":
                return AfterPlace(session, clock);
            case "place-close":
                return AfterOp(session, clock, "closing the tools panel", () => Go("place-leaving", clock));
            case "place-leaving":
                return PlaceLeaving(session, clock);
            case "place-arriving":
                return PlaceArriving(session, clock);

            // The log off, the character screen, and the purge from Smoke.
            case "logging-off":
                return LoggingOff(session, clock);
            case "char-select":
                return AfterOp(session, clock, "selecting the throwaway", () => CharStepRun(session, clock));
            case "char-delete":
                return AfterOp(session, clock, "deleting the throwaway", () => AfterDelete(session, clock));
            case "char-restore":
                return AfterOp(session, clock, "restoring the throwaway", () => AfterRestore(session, clock));
            case "entering":
                return Entering(session, clock);
            case "smoke-arriving":
                return SmokeArriving(session, clock);
            case "purging":
                return Purging(session, clock);
            case "quitting":
                if (Poll(session, clock, "quitting", AnswerSeconds) is { } quitting)
                {
                    return quitting;
                }

                return _pending is { Outcome.Kind: ActionOutcomeKind.Sent } ? Go("done", clock, ScenarioStatus.Succeeded) : Fail(session, clock, "quitting: " + Describe(_pending?.Outcome));
            case "done":
                return ScenarioStatus.Succeeded;
            default:
                return Fail(session, clock, $"TODO(m2-throwaway): unknown stage '{Stage}'");
        }
    }

    // ------------------------------------------------------------------------------------------------ login and the leftover

    private ScenarioStatus Login(GameState state, double clock)
    {
        if (state.Phase == SessionPhase.Idle && clock - _stageStart > LoginCheckScenario.HostLoginSeconds)
        {
            return Fail(null, clock, $"login: the host never started the login ({LoginCheckScenario.HostLoginSeconds:0} s on the session clock)");
        }

        if (state.Phase == SessionPhase.Failed)
        {
            return Fail(null, clock, $"login: the connection failed ({state.FailureMessage ?? "no reason given"})");
        }

        if (state.Phase == SessionPhase.CharacterSelect && state.Characters.Entries.Count > 0)
        {
            return state.DataCheck == DataCheckState.Complete
                ? Go("inspect", clock)
                : Fail(null, clock, $"login: the character list came without a completed data check ({state.DataCheck})");
        }

        return clock - _stageStart > ListSeconds
            ? Fail(null, clock, $"login: the character list did not come within {ListSeconds:0} s (phase {state.Phase}, data check {state.DataCheck})")
            : ScenarioStatus.Running;
    }

    private ScenarioStatus Inspect(IGameSessionView session, double clock)
    {
        var state = session.State;
        var all = Snapshot(state);
        foreach (var slot in state.Characters.Entries)
        {
            _initialGuids.Add(slot.Guid);
        }

        _log("start list: " + string.Join(", ", all.Select(Describe)));
        if (FindSlot(state, Smoke) is not { PendingDeletion: false })
        {
            return Fail(null, clock, $"inspect: the account has no ready '{Smoke}' to enter and purge from");
        }

        // The leg's own characters from an earlier failed run: the throwaway, and a character under the invalid name (only if ACE accepted it).
        foreach (var name in (ReadOnlySpan<string>)[Throwaway, InvalidName])
        {
            if (FindSlot(state, name) is { } stray)
            {
                _leftoverNames.Add(name);
                if (!stray.PendingDeletion)
                {
                    _leftoverDeletes.Enqueue(name);
                }

                _log($"leftover: '{name}' from an earlier run is on the account (pending deletion: {stray.PendingDeletion}); it is deleted if needed, purged from '{Smoke}' and logged");
            }
        }

        if (_leftoverNames.Count == 0)
        {
            Baseline = all;
            return StartOp(_screens.OpenCharGenSummary(), "chargen-open", clock);
        }

        Leftover = true;
        _mayHaveChangedAccount = true;
        Baseline = [.. all.Where(c => !_leftoverNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase))];
        return NextLeftover(session, clock);
    }

    /// <summary>The next leftover that is not pending deletion is selected for the screen's delete; with none left, Smoke enters to purge them.</summary>
    private ScenarioStatus NextLeftover(IGameSessionView session, double clock)
    {
        if (_leftoverDeletes.TryDequeue(out var name))
        {
            _leftoverCurrent = name;
            return StartOp(_screens.SelectCharacter(name), "leftover-select", clock);
        }

        return EnterSmoke(session, clock, "leftover");
    }

    // ------------------------------------------------------------------------------------------------ creation

    private ScenarioStatus AfterFinish(IGameSessionView session, double clock, string what, string dismissStage, bool second)
    {
        if (OpPoll(session, clock, "finishing with " + what, OperationSeconds) is { } early)
        {
            return early;
        }

        var text = _op!.Text;
        _op = null;
        if (text is null)
        {
            // The server accepted a name it should have refused: a character exists now that must not (recovery deletes it). The client logs a created character in at once.
            _mayHaveChangedAccount = true;
            _createPossible = true;
            return Fail(session, clock, second
                ? $"{what}: ACE accepted '{InvalidName}' and created a character (its creature-name check is off?)"
                : $"{what}: ACE accepted '{Smoke}' (it must say the name is taken)");
        }

        if (second && (string.IsNullOrWhiteSpace(text) || (_nameErrorTexts.Count > 0 && string.Equals(text.Trim(), _nameErrorTexts[0].Trim(), StringComparison.OrdinalIgnoreCase))))
        {
            // The dialog of the taken name came again: ACE said the name is in use (or the dialog is empty), not that the name is refused as a creature's name.
            return Fail(session, clock, $"{what}: the retail dialog said '{text}', the same as for a taken name (or nothing), not the invalid-name refusal (is '{InvalidName}' in use on the account?)");
        }

        NameErrors++;
        _nameErrorTexts.Add(text);
        _log($"{what}: ACE refused it; the retail dialog said: {text}");
        return StartOp(_screens.DismissErrorDialog(), dismissStage, clock);
    }

    private ScenarioStatus CreateAnswered(IGameSessionView session, double clock)
    {
        if (OpPoll(session, clock, "finishing with the throwaway's name", OperationSeconds) is { } early)
        {
            return early;
        }

        var text = _op!.Text;
        _op = null;
        if (text is not null)
        {
            return Fail(session, clock, "finishing with the throwaway's name: ACE refused it; the retail dialog said: " + text);
        }

        _mayHaveChangedAccount = true;
        _createPossible = true;
        CreatedAt = clock;
        _log("the throwaway was created; the client enters the world at once");
        return Go("arriving", clock);
    }

    private ScenarioStatus Arriving(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (state.Phase == SessionPhase.InWorld && state.Self.Travel == SelfTravel.Present && !state.Self.Hidden)
        {
            if (!string.Equals(state.Self.Name, Throwaway, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(session, clock, $"arriving: the character in the world is '{state.Self.Name}', not '{Throwaway}'");
            }

            PresentAt = clock;
            return Go("settling", clock);
        }

        if (state.Phase is SessionPhase.Failed)
        {
            return Fail(session, clock, $"arriving: the session failed ({state.FailureMessage ?? "no reason given"})");
        }

        return clock - _stageStart > ArrivalSeconds
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"arriving: the throwaway was not unhidden within {ArrivalSeconds:0} s of the create (phase {state.Phase}, travel {state.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus Settling(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (state.Phase != SessionPhase.InWorld || state.Self.Travel != SelfTravel.Present || state.Self.Hidden)
        {
            return Fail(session, clock, $"settling: the player did not stay in the world (phase {state.Phase}, travel {state.Self.Travel}, hidden {state.Self.Hidden})");
        }

        if (clock - PresentAt < SettleSeconds)
        {
            return ScenarioStatus.Running;
        }

        SettledAt = clock;
        return StartOp(_screens.OpenToolsPanel(), "tools-open", clock);
    }

    // ------------------------------------------------------------------------------------------------ tools

    private ScenarioStatus AfterWhere(IGameSessionView session, double clock)
    {
        if (OpPoll(session, clock, "Where am I", OperationSeconds) is { } early)
        {
            return early;
        }

        var metres = _op!.Number;
        _op = null;
        if (double.IsNaN(metres) || metres > WhereToleranceMetres)
        {
            return Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"Where am I: the server's position and the client body's are {(double.IsNaN(metres) ? "not compared" : metres.ToString("0.00", CultureInfo.InvariantCulture) + " m apart")} (want within {WhereToleranceMetres} m)"));
        }

        WhereMetres = metres;

        // The guard (owner decision D61): experience is added only to the throwaway. The driver checks it again before it presses the button.
        var inWorld = session.State.Self.Name;
        if (!string.Equals(inWorld, Throwaway, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(session, clock, $"Add XP refused: the character in the world is '{inWorld}', not '{Throwaway}' (D61)");
        }

        return StartOp(_screens.PressAddXp(Throwaway, XpAmount), "tools-xp", clock);
    }

    private ScenarioStatus AfterXp(IGameSessionView session, double clock)
    {
        if (OpPoll(session, clock, "Add XP", OperationSeconds) is { } early)
        {
            return early;
        }

        var added = _op!.Number;
        _op = null;
        if (double.IsNaN(added) || (long)added != XpAmount)
        {
            return Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"Add XP: the server added {(double.IsNaN(added) ? "no confirmed amount" : added.ToString("0", CultureInfo.InvariantCulture))}, not {XpAmount}"));
        }

        XpAdded = true;
        _teleportsAtPlace = _teleports;
        return StartOp(_screens.PressPlace(PlaceQuery), "tools-place", clock);
    }

    private ScenarioStatus AfterPlace(IGameSessionView session, double clock)
    {
        if (OpPoll(session, clock, "the Places teleport", OperationSeconds) is { } early)
        {
            return early;
        }

        _op = null;
        PlaceAskedAt = clock;
        return StartOp(_screens.CloseToolsPanel(), "place-close", clock);
    }

    private ScenarioStatus PlaceLeaving(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (_teleports > _teleportsAtPlace || state.Self.Travel != SelfTravel.Present)
        {
            return Go("place-arriving", clock);
        }

        return clock - _stageStart > AnswerSeconds
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"the Places teleport: the server sent no teleport within {AnswerSeconds:0} s of the click"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus PlaceArriving(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (state.Phase == SessionPhase.InWorld && state.Self.Travel == SelfTravel.Present && !state.Self.Hidden)
        {
            var cell = state.Self.Position?.CellId ?? 0;
            if (cell >> 16 != HoltburgLandblock)
            {
                return Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"the Places teleport: the player arrived in cell 0x{cell:X8}, not in Holtburg's landblock 0x{HoltburgLandblock:X4}"));
            }

            PlaceArrivedAt = clock;
            PlaceTeleported = true;
            return StartLogOff(session, clock, "throwaway");
        }

        return clock - _stageStart > TeleportSeconds
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"the Places teleport: the player was not unhidden within {TeleportSeconds:0} s (travel {state.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ log off, the character screen

    private ScenarioStatus StartLogOff(IGameSessionView session, double clock, string then)
    {
        _afterLogOff = then;
        _pending = _requestLogOff is null ? session.Actions.LogOff(ActionSource.Host) : _requestLogOff();
        return _pending is null ? Fail(session, clock, "logging off: the host's log-off key did not ask for a log off") : Go("logging-off", clock);
    }

    private ScenarioStatus LoggingOff(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (_pending is { Outcome: { } done })
        {
            _pending = null;
            if (done.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, clock, "logging off: " + Describe(done));
            }

            if (state.Phase != SessionPhase.CharacterSelect || state.Characters.Entries.Count == 0)
            {
                return Fail(session, clock, $"logging off: the log off was confirmed but the session is at {state.Phase} with {state.Characters.Entries.Count} characters, not at a character list");
            }

            return AfterLogOff(session, clock);
        }

        return clock - _stageStart > LogOffSeconds
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"logging off: the server did not return the character list within {LogOffSeconds:0} s (phase {state.Phase})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus AfterLogOff(IGameSessionView session, double clock)
    {
        var state = session.State;
        switch (_afterLogOff)
        {
            case "throwaway":
                if (FindThrowaway(state) is not { Pending: false })
                {
                    return Fail(session, clock, "after the log off: the throwaway is not on the list, ready");
                }

                foreach (var step in (ReadOnlySpan<string>)["select", "delete", "select", "restore", "select", "delete"])
                {
                    _charSteps.Enqueue(step);
                }

                return CharStepStart(session, clock);
            case "leftover":
                if (_leftoverNames.FirstOrDefault(n => FindSlot(state, n) is not null) is { } still)
                {
                    return Fail(session, clock, $"after the leftover's purge: '{still}' is still on the list");
                }

                _log("the leftover is gone; continuing with the leg");
                return StartOp(_screens.OpenCharGenSummary(), "chargen-open", clock);
            case "final":
                EndList = Snapshot(state);
                ListSame = SameList(Baseline, EndList, out var difference);
                _log("end list: " + string.Join(", ", EndList.Select(Describe)));
                if (ListSame != true)
                {
                    return Fail(session, clock, "the list at the end differs from the list at the start: " + difference);
                }

                _pending = session.Actions.Quit(ActionSource.Host);
                return Go("quitting", clock);
            default:
                return Fail(session, clock, "TODO(m2-throwaway): unknown log-off continuation " + _afterLogOff);
        }
    }

    private ScenarioStatus CharStepStart(IGameSessionView session, double clock)
    {
        if (_charSteps.Count == 0)
        {
            return EnterSmoke(session, clock, "final");
        }

        var step = _charSteps.Peek();
        return step switch
        {
            "select" => StartOp(_screens.SelectCharacter(Throwaway), "char-select", clock),
            "delete" => StartOp(_screens.DeleteSelected(), "char-delete", clock),
            "restore" => StartOp(_screens.RestoreSelected(), "char-restore", clock),
            _ => Fail(session, clock, "TODO(m2-throwaway): unknown character-screen step " + step),
        };
    }

    // Called when the operation of the head step ended: the next step (a select's operation just ended) or the verification (a delete's or a restore's, in their own stages).
    private ScenarioStatus CharStepRun(IGameSessionView session, double clock)
    {
        _charSteps.Dequeue(); // the select
        return CharStepStart(session, clock);
    }

    private ScenarioStatus AfterDelete(IGameSessionView session, double clock)
    {
        _charSteps.Dequeue();
        if (FindThrowaway(session.State) is not { Pending: true })
        {
            return Fail(session, clock, "deleting the throwaway: the list does not show it pending deletion");
        }

        Deletes++;
        return CharStepStart(session, clock);
    }

    private ScenarioStatus AfterRestore(IGameSessionView session, double clock)
    {
        _charSteps.Dequeue();
        if (FindThrowaway(session.State) is not { Pending: false })
        {
            return Fail(session, clock, "restoring the throwaway: the list does not show it ready again");
        }

        Restores++;
        return CharStepStart(session, clock);
    }

    // ------------------------------------------------------------------------------------------------ Smoke and the purge

    private ScenarioStatus EnterSmoke(IGameSessionView session, double clock, string task)
    {
        if (FindSlot(session.State, Smoke) is not { PendingDeletion: false } smoke)
        {
            return Fail(session, clock, $"the account has no ready '{Smoke}' to purge from");
        }

        _smokeTask = task;
        _enteringGuid = smoke.Guid;
        session.Actions.SelectCharacter(smoke.Guid, ActionSource.Host);
        _pending = session.Actions.EnterWorld(smoke.Guid, ActionSource.Host);
        return Go("entering", clock);
    }

    private ScenarioStatus Entering(IGameSessionView session, double clock)
    {
        if (Poll(session, clock, "entering the world as the graduated character", ArrivalSeconds) is { } early)
        {
            return early;
        }

        return _pending is { Outcome.Kind: ActionOutcomeKind.Confirmed } ? Go("smoke-arriving", clock) : Fail(session, clock, "entering the world as the graduated character: " + Describe(_pending?.Outcome));
    }

    private ScenarioStatus SmokeArriving(IGameSessionView session, double clock)
    {
        var state = session.State;
        if (state.Phase == SessionPhase.InWorld && state.Self.Travel == SelfTravel.Present && !state.Self.Hidden && state.Self.Guid == _enteringGuid)
        {
            _purgeNames.Clear();
            foreach (var name in _smokeTask == "leftover" ? _leftoverNames : [Throwaway])
            {
                _purgeNames.Enqueue(name);
            }

            return StartPurge(session, clock, _purgeNames.Dequeue(), 0);
        }

        if (state.Phase is SessionPhase.Failed or SessionPhase.CharacterSelect)
        {
            return Fail(session, clock, $"entering the world as the graduated character: the session left the world before the player was unhidden (phase {state.Phase})");
        }

        return clock - _stageStart > ArrivalSeconds
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"entering the world as the graduated character: not unhidden within {ArrivalSeconds:0} s (travel {state.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus StartPurge(IGameSessionView session, double clock, string name, int done)
    {
        _purgeName = name;
        _purgeCount = done;
        _purgeHandle = _purge is null ? session.Tools.PurgeCharacter(name, ActionSource.Host) : _purge(session, name);
        _pending = _purgeHandle;
        return Go("purging", clock);
    }

    private ScenarioStatus Purging(IGameSessionView session, double clock)
    {
        if (Poll(session, clock, "the purge", AnswerSeconds) is { } early)
        {
            return early;
        }

        var outcome = _purgeHandle!.Outcome!.Value;
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith("unknown-verb", StringComparison.Ordinal) == true)
        {
            return Fail(session, clock, "the purge: the installed server mod has no purge verb (install it with setup-server.ps1 -ModOnly, with the owner's go-ahead): " + Describe(outcome));
        }

        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Detail?.StartsWith("unknown-character", StringComparison.Ordinal) == true && _purgeCount > 0)
        {
            // Nothing of that name is left, stale expired rows included.
            _log($"purged '{_purgeName}': the verb now answers unknown-character after {_purgeCount} purge(s)");
            _purgeHandle = null;
            return _purgeNames.TryDequeue(out var next) ? StartPurge(session, clock, next, 0) : StartLogOff(session, clock, _smokeTask);
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed || _purgeHandle.Result is not { } result || (result.Characters <= 0 && result.PlayerBiotas <= 0))
        {
            return Fail(session, clock, "the purge: " + Describe(outcome) + (_purgeHandle.Result is { } r ? $" (removed {r.Characters} character records, {r.PlayerBiotas} player biotas)" : string.Empty));
        }

        _log($"purged '{_purgeName}': {result.Characters} character record(s), {result.PlayerBiotas} player biota(s), {result.Possessions} possession(s)");
        if (_purgeCount > 0)
        {
            StalePurges++;
        }
        else if (_smokeTask == "leftover")
        {
            LeftoverPurged ??= result;
        }
        else
        {
            Purged = result;
        }

        _purgeCount++;
        if (_purgeCount >= MaxPurges)
        {
            return Fail(session, clock, $"the purge: '{_purgeName}' was purged {MaxPurges} times and the verb still does not answer unknown-character");
        }

        // Ask again: a stale expired row of the same name (invisible in the list) is purged next, until the verb says no character of that name is left.
        return StartPurge(session, clock, _purgeName, _purgeCount);
    }

    // ------------------------------------------------------------------------------------------------ recovery

    /// <summary>
    /// A failure after the leg may have changed the account: log off if in the world, then delete (pending deletion, for the next run's leftover path to purge) the throwaway, or a
    /// character made under the invalid name, if it is on the list and ready. No other character is ever touched.
    /// </summary>
    private ScenarioStatus Recover(IGameSessionView session, double clock)
    {
        var state = session.State;
        switch (Stage)
        {
            case "recover-start":
                if (state.Phase is SessionPhase.InWorld or SessionPhase.EnteringWorld)
                {
                    if (state.Phase == SessionPhase.EnteringWorld && clock - _stageStart < LogOffSeconds)
                    {
                        return ScenarioStatus.Running; // the log off can only be asked for once the player is in
                    }

                    _pending = session.Actions.LogOff(ActionSource.Host);
                    _recoverLogOffAt = clock;
                    return Go("recover-logoff", clock);
                }

                if (state.Phase == SessionPhase.CharacterSelect && _createPossible && clock - _stageStart < CreateSettleSeconds)
                {
                    return ScenarioStatus.Running; // a create may have been answered with the client's Enter for it still on its way
                }

                return state.Phase == SessionPhase.CharacterSelect ? Go("recover-list", clock) : Finish(clock);
            case "recover-logoff":
                if (state.Phase == SessionPhase.CharacterSelect && state.Characters.Entries.Count > 0)
                {
                    return Go("recover-list", clock);
                }

                return clock - _recoverLogOffAt > LogOffSeconds ? Finish(clock) : ScenarioStatus.Running;
            case "recover-list":
                if (!_recoverScanned)
                {
                    foreach (var slot in state.Characters.Entries)
                    {
                        if (slot.PendingDeletion)
                        {
                            continue;
                        }

                        var ours = string.Equals(slot.Name, Throwaway, StringComparison.OrdinalIgnoreCase)
                            || (string.Equals(slot.Name, InvalidName, StringComparison.OrdinalIgnoreCase) && !_initialGuids.Contains(slot.Guid));
                        if (ours)
                        {
                            _recoverTargets.Enqueue(slot.Guid);
                            _log($"recovery: deleting '{slot.Name}' so the next run's leftover path purges it");
                        }
                    }

                    _recoverScanned = true;
                }

                if (_pending is not null)
                {
                    if (_pending.Outcome is null && clock - _stageStart < AnswerSeconds)
                    {
                        return ScenarioStatus.Running;
                    }

                    _pending = null;
                }

                if (_recoverTargets.TryDequeue(out var guid))
                {
                    _pending = session.Actions.DeleteCharacter(guid, ActionSource.Host);
                    _stageStart = clock;
                    return ScenarioStatus.Running;
                }

                return Finish(clock);
            default:
                return Finish(clock);
        }

        ScenarioStatus Finish(double at)
        {
            Stage = "failed";
            _stageStart = at;
            return ScenarioStatus.Failed;
        }
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private ScenarioStatus StartOp(ScreenOp op, string stage, double clock)
    {
        _op = op;
        return Go(stage, clock);
    }

    private ScenarioStatus AfterOp(IGameSessionView session, double clock, string what, Func<ScenarioStatus> next)
    {
        if (OpPoll(session, clock, what, OperationSeconds) is { } early)
        {
            return early;
        }

        _op = null;
        return next();
    }

    /// <summary>Null when the current operation ended well; otherwise the status to return (still running, or failed).</summary>
    private ScenarioStatus? OpPoll(IGameSessionView session, double clock, string what, double limit)
    {
        if (_op is null)
        {
            return Fail(session, clock, $"{what}: no screen operation was started");
        }

        if (!_op.Done)
        {
            return clock - _stageStart > limit
                ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"{what}: the screen operation did not finish within {limit:0} s"))
                : ScenarioStatus.Running;
        }

        return _op.Failure is { } failure ? Fail(session, clock, $"{what}: {failure}") : null;
    }

    /// <summary>Null when the pending action ended; otherwise the status to return.</summary>
    private ScenarioStatus? Poll(IGameSessionView session, double clock, string what, double limit)
    {
        if (_pending is null)
        {
            return Fail(session, clock, $"{what}: no action was started");
        }

        if (_pending.Outcome is not null)
        {
            return null;
        }

        return clock - _stageStart > limit
            ? Fail(session, clock, string.Create(CultureInfo.InvariantCulture, $"{what}: no answer within {limit:0} s"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus Go(string stage, double clock, ScenarioStatus status = ScenarioStatus.Running)
    {
        Stage = stage;
        _stageStart = clock;
        return status;
    }

    private ScenarioStatus Fail(IGameSessionView? session, double clock, string why)
    {
        if (Failure is null)
        {
            Failure = $"{Stage}: {why}";
            _log("FAILED at " + Failure);
        }

        _op = null;
        if (session is null || !_mayHaveChangedAccount)
        {
            // Nothing the leg did needs undoing, but a player in the world is logged off all the same.
            if (session?.State.Phase is SessionPhase.InWorld or SessionPhase.EnteringWorld)
            {
                _recovering = true;
                return Go("recover-start", clock);
            }

            Stage = "failed";
            return ScenarioStatus.Failed;
        }

        _recovering = true;
        _pending = null;
        return Go("recover-start", clock);
    }

    private static string Describe(in ActionOutcome? outcome) =>
        outcome is not { } o ? "no outcome" : o.Kind + (o.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{o.Code:X})")) + (o.Detail is null ? string.Empty : ": " + o.Detail);

    private static string Describe(ListedCharacter c) => c.Pending ? c.Name + " (pending deletion)" : c.Name;

    private static List<ListedCharacter> Snapshot(GameState state) => [.. state.Characters.Entries.Select(c => new ListedCharacter(c.Name, c.PendingDeletion))];

    private static CharacterSlot? FindSlot(GameState state, string name)
    {
        foreach (var slot in state.Characters.Entries)
        {
            if (string.Equals(slot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }
        }

        return null;
    }

    private static ListedCharacter? FindThrowaway(GameState state) =>
        FindSlot(state, Throwaway) is { } slot ? new ListedCharacter(slot.Name, slot.PendingDeletion) : null;
}
