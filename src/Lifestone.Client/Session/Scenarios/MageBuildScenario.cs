using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M3's level-126 combat mage (<c>--scenario=mage-build</c>, spec 5.3, owner decision D3): find the test account's
/// <see cref="MageTemplate.CharacterName"/> or create it with <see cref="MageTemplate.CreationIntent"/>, enter the world as that
/// character and nobody else, and bring it to the template. Idempotent and resumable: every step is sent only if the read model
/// (<see cref="PlayerQualities"/>) still shows it is needed, so a ready mage is only verified and a half-built one gets just the
/// missing steps, in order. Then it logs off and quits as the other scenarios do.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>login</c>, <c>choosing</c>, <c>creating</c> (only when the name is absent), <c>entering</c>,
/// <c>arriving</c>, <c>status</c> (the access level must be Player, owner decision D11), <c>building</c>, <c>refilling</c> and <c>purging</c> (both only with <c>freshCharacter</c>), <c>placing</c> (the fresh character), <c>logging-off</c>,
/// <c>quitting</c>, <c>done</c>. The scenario never selects, deletes or enters another character; an absent name with no free slot
/// fails the run (<see cref="CharacterListState.SlotCount"/>).</para>
/// <para><b>Fresh character</b> (<c>freshCharacter</c>, <c>--fresh-character</c>, M4 fresh B1, owner decisions D44 and D45): the live gate's re-create, on <see cref="FighterBuildScenario"/>'s pattern. The BSD Mage, when the list shows it live, is never entered: it is deleted from the character list through
/// the retail delete (stage <c>recreate-delete</c>), the build waits for the list to show that guid pending (<c>recreate-verify</c>), creates the template's character and builds it as usual; with no live copy it only creates it. After the build has ended met, <c>refilling</c> fills the vitals by ACE's own
/// level-up rule (<see cref="FreshRefill"/>: the build grants its experience first, so its raises lifted the maxima over the current values; M4 fresh F1, the item's gate round 1 finding), then <c>purging</c> asks the server mod to purge
/// the old copies of the name (<see cref="FreshPurge"/>), so a failed build leaves the old copy restorable; a re-created character whose old copy was not purged fails the run. A fresh character starts outside Holtburg, so <c>placing</c> then teleports it to Holtburg's point of interest
/// (as the fighter build does) before the log off. The verdict gains <c>fresh=recreated|created purged=&lt;n&gt; refill=none|level-up:&lt;a&gt;-&gt;&lt;b&gt;|failed</c> (the gate's judge reads <c>fresh=</c> and <c>purged=</c> on the same line). The refill is one level-up: <b>a fresh mage ends one level above the template's
/// (127), its extra experience unspent</b> (owner decisions D44, D45; the template's level check is <c>&gt;=</c>, so the journey's embedded build-verify accepts it). Only the session-ending (headless) build takes it.</para>
/// <para><b>Building</b>, in this order, each only if still needed: (a) the mod's <c>xp</c> for exactly
/// <see cref="XpTableData.XpForLevel"/>(126) minus <see cref="PlayerQualities.TotalExperience"/> (never when the mage is at or past it:
/// the loops' kills may have taken it further); (b) <see cref="IActionApi.TrainSkill"/> for each of <see cref="MageTemplate.TrainedLater"/>
/// not yet trained, with <see cref="MageTemplate.TrainCredits"/> (ACE refuses any other number); (c) for each of
/// <see cref="MageTemplate.Targets"/> in plan order below its target ranks, the raise action of its kind with
/// <see cref="StatTarget.XpRemaining"/> from the stat's own spent experience; (d) <see cref="PlayerTools.LearnSpells"/> of
/// <see cref="ArenaProfile.SpellsToLearn"/> in chunks of at most <see cref="PlayerTools.MaxSpellIds"/> (the mod answers an
/// already-known spell as known, so a ready mage's chunks verify the spellbook and teach nothing). Every action waits for its tracker's
/// outcome; anything but Confirmed fails the run naming the action, the stat and the outcome. A precondition the read model has not
/// caught up with yet (the level, the credits, the experience, right after the <c>xp</c> reply) is waited for
/// <see cref="SettleSeconds"/> on the session clock and then fails by name; nothing is ever sent twice.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[mage] verdict ...</c>; additive to the headless line):
/// <c>mage=ready|built|failed level=&lt;n&gt; template=ok|short [template_short=&lt;TemplateCheck.Summary&gt;] spells=&lt;known+learned&gt;/&lt;ids&gt;
/// built_steps=&lt;n&gt;</c>. <c>built_steps</c> counts the xp, train and raise actions sent and the spell chunks that taught at least one
/// spell; <c>mage=ready</c> means none was needed, <c>built</c> that some were and everything ended met.</para>
/// </remarks>
public sealed class MageBuildScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=mage-build</c>).</summary>
    public const string ScenarioName = "mage-build";

    /// <summary>The log category of the scenario's lines (<c>[mage] ...</c>).</summary>
    public const string LogCategory = "mage";

    /// <summary>How long a precondition the read model has not caught up with is waited for, in seconds on the session clock.</summary>
    public const double SettleSeconds = 5;

    /// <summary>How long the teleport to Holtburg may take, in seconds on the session clock (M4 fresh B1: the fresh character's placing).</summary>
    public const double PlaceSeconds = 30;

    /// <summary>The mod verbs the build uses (the experience grant and the spellbook), which the status stage requires the mod to report.</summary>
    internal static readonly string[] RequiredVerbs = [ToolVerbs.Xp, ToolVerbs.Spell + " " + ToolVerbs.SpellLearn];

    /// <summary>The most spell ids one <c>spell learn</c> carries.</summary>
    public const int SpellChunk = PlayerTools.MaxSpellIds;

    private readonly bool _endSession;
    private readonly XpTableData _table;
    private readonly IReadOnlyList<uint> _retailClasses;
    private readonly ILogSink _log;
    private readonly HashSet<string> _sent = [];
    private readonly List<string> _steps = [];
    private ActionHandle? _pending;
    private InFlight? _inFlight;
    private ToolHandle<ToolStatus>? _status;
    private bool _createTried;
    private readonly bool _fresh;
    private bool _freshDecided;
    private string? _freshHow;
    private uint _oldGuid;
    private bool _purged;
    private readonly FreshPurge? _purge;
    private readonly FreshRefill? _refill;
    private readonly TeleportDestination _holtburg;
    private readonly uint _holtburgLandblock;
    private double _placeStart = double.NaN;
    private bool _xpSent;
    private int _spellChunk;
    private int _spellsKnown;
    private int _spellsLearned;
    private double _arrivalStart = double.NaN;
    private readonly ReasonTimer _wait = new(SettleSeconds);
    private bool _finished;
    private int _level;
    private TemplateCheck? _check;

    /// <summary>The mage build.</summary>
    /// <param name="table">The XpTable (<see cref="XpTableData.Load"/>).</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats (for the creation).</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log in, build, log off and quit. False (M4 P5b J3b, the journey's <c>build-verify</c> station): the session already holds the character in the world, so the scenario skips the login, never logs off and ends at <c>done</c> in the world, as the hunt does.</param>
    /// <param name="freshCharacter">M4 fresh B1 (owner decisions D44, D45): delete the BSD Mage's live character from the character list (never entering it), create the template's and build it, purge the old copies of the name with the mod, then place it in Holtburg; the verdict gains <c>fresh=recreated|created purged=&lt;n&gt; refill=none|level-up:&lt;a&gt;-&gt;&lt;b&gt;|failed</c> (M4 fresh F1: one level-up refills the vitals before the purge, so the mage ends at level 127). Only with <paramref name="endSession"/>.</param>
    public MageBuildScenario(XpTableData table, IReadOnlyList<uint> retailClasses, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, bool freshCharacter = false)
    {
        _endSession = endSession;
        _fresh = freshCharacter;
        if (freshCharacter && !endSession)
        {
            throw new ArgumentException("--fresh-character needs the character list: it is for the session-ending (headless) build, not the journey's embedded one", nameof(freshCharacter));
        }

        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(retailClasses);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        if (table.CharacterLevelXpList.Count <= MageTemplate.TargetLevel)
        {
            throw new ArgumentException($"TODO(m3-xp-table): the XpTable ends before level {MageTemplate.TargetLevel}", nameof(table));
        }

        _table = table;
        _retailClasses = [.. retailClasses];
        _log = log;
        ArrivalSeconds = arrivalSeconds;
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
        _holtburgLandblock = _holtburg.Location.Position.CellId >> 16;
        if (freshCharacter)
        {
            _purge = new FreshPurge(MageTemplate.CharacterName, log, LogCategory);
            _refill = new FreshRefill(table, log, LogCategory);
        }
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The limit on the arrival, in seconds.</summary>
    public double ArrivalSeconds { get; }

    /// <summary>The character entered (its guid), or 0.</summary>
    public uint EnteredGuid { get; private set; }

    /// <summary>True when the scenario created the character on this run.</summary>
    public bool Created { get; private set; }

    /// <summary>The steps sent, in order (<c>xp</c>, <c>train</c>, <c>raise</c> and <c>learn</c> lines, as logged), for the run's record and the tests.</summary>
    public IReadOnlyList<string> SentSteps => _steps;

    /// <summary>The steps counted in the verdict's <c>built_steps</c>.</summary>
    public int BuiltSteps { get; private set; }

    /// <summary>The spells the mod answered as known or learned, over the chunks asked so far.</summary>
    public int SpellsAccounted => _spellsKnown + _spellsLearned;

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The verdict fields, <c>mage=... level=... template=... spells=... built_steps=...</c> (no character name, no account).</summary>
    public string VerdictFields()
    {
        var failed = Failure is not null || !_finished;
        var check = _check;
        var state = failed ? "failed" : BuiltSteps > 0 ? "built" : "ready";
        var template = check is { AllMet: true } ? "template=ok" : "template=short";
        if (check is null)
        {
            template += " template_short=not_entered";
        }
        else if (!check.AllMet)
        {
            template += " template_short=" + ShortField(check);
        }

        // M4 fresh B1: fresh=recreated|created purged=<n> (fresh=none before the run decided; the gate's pass needs recreated or created). M4 fresh F1: refill=none|level-up:<a>-><b>|failed last (none until the refilling stage has ended).
        var fresh = _fresh ? string.Create(CultureInfo.InvariantCulture, $" fresh={_freshHow ?? "none"} purged={_purge?.Purged ?? 0} refill={_refill?.Field ?? "none"}") : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"mage={state} level={_level} {template} spells={SpellsAccounted}/{ArenaProfile.SpellsToLearn.Count} built_steps={BuiltSteps}{fresh}");
    }

    /// <summary>
    /// The short checks as one key=value-safe token: no spaces and no semicolons (each check's text with its spaces as underscores, the
    /// checks joined with commas), so the headless <c>mage-build ...</c> line parses as key=value.
    /// </summary>
    public static string ShortField(TemplateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return string.Join(',', check.Short.Select(c => c.ToString().Replace(' ', '_')));
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null)
        {
            return ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        switch (Stage)
        {
            case "start":
                if (_fresh && session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
                {
                    return Fail(session, "start: --fresh-character deletes the live character from the character list, but the session is already in the world (M4 fresh B1: a host that logged in itself cannot re-create)");
                }

                if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
                {
                    // Already in the world (a host that logged in itself, the journey, or the offline stand-in): nothing to log in. The build acts on the BSD Mage and nobody else.
                    if (session.State.Self.Name is { } already && !string.Equals(already, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(session, "the entered character is not " + MageTemplate.CharacterName + ": nothing is sent for it");
                    }

                    EnteredGuid = session.State.Self.Guid;
                    _arrivalStart = session.Clock.Now;
                    return Go("arriving");
                }

                _pending = actions.Login(ActionSource.Host);
                return Go("login");

            case "login":
                if (Settled(out var login))
                {
                    return login.Kind == ActionOutcomeKind.Confirmed ? Go("choosing") : Fail(session, "login", login);
                }

                break;

            case "choosing":
                return Choose(session);

            case "creating":
                if (Settled(out var created))
                {
                    if (created.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "creating the character", created);
                    }

                    Created = true;
                    _log.Info(LogCategory, "created " + MageTemplate.CharacterName);
                    return Go("choosing");
                }

                break;

            case "entering":
                if (Settled(out var entered))
                {
                    if (entered.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "entering the world", entered);
                    }

                    _arrivalStart = session.Clock.Now;
                    return Go("arriving");
                }

                break;

            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present)
                {
                    _status = session.Tools.Status(ActionSource.Host);
                    _pending = _status;
                    return Go("status");
                }

                if (session.Clock.Now - _arrivalStart > ArrivalSeconds)
                {
                    return Fail(
                        session,
                        string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {ArrivalSeconds:0.#} s (travel {session.State.Self.Travel})"));
                }

                break;

            case "status":
                if (Settled(out var status))
                {
                    if (status.Kind != ActionOutcomeKind.Confirmed || _status?.Result is not { } reported)
                    {
                        return Fail(session, "asking the server mod for its status", status);
                    }

                    if (!string.Equals(reported.Access, PlayerTools.PlayerAccess, StringComparison.Ordinal))
                    {
                        return Fail(session, $"status: the account's access level is {reported.Access}, above {PlayerTools.PlayerAccess} (owner decision D11)");
                    }

                    foreach (var verb in RequiredVerbs)
                    {
                        if (!reported.Verbs.Contains(verb, StringComparer.Ordinal))
                        {
                            return Fail(session, $"status: the server mod does not report the '{verb}' verb the build uses (a stale mod: server setup -ModOnly)");
                        }
                    }

                    if (_fresh && !reported.Verbs.Contains(FreshPurge.RequiredVerb, StringComparer.Ordinal))
                    {
                        return Fail(session, $"status: the server mod does not report the '{FreshPurge.RequiredVerb}' verb the fresh character's purge uses (a stale mod: server setup -ModOnly)");
                    }

                    return Go("building");
                }

                break;

            case "recreate-delete":
                if (Settled(out var deleteOutcome))
                {
                    if (deleteOutcome.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "fresh-character: deleting the old character through the client's retail character delete", deleteOutcome);
                    }

                    return Go("recreate-verify");
                }

                break;

            case "recreate-verify":
                foreach (var listed in session.State.Characters.Entries)
                {
                    if (listed.Guid == _oldGuid && listed.PendingDeletion)
                    {
                        _freshHow = "recreated";
                        _createTried = false;
                        Created = false;
                        _log.Warn(LogCategory, "fresh=recreated: the old " + MageTemplate.CharacterName + " is listed as pending deletion (restorable until the new copy has been built and purges it); creating the template's character");
                        return Go("choosing");
                    }
                }

                return Waiting(session, "the server has not listed the old character as pending deletion");

            case "building":
                return Build(session);

            case "refilling":
                return Refilling(session);

            case "purging":
                return Purging(session);

            case "placing":
                return Placing(session);

            case "logging-off":
                if (Settled(out var loggedOff))
                {
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off", loggedOff);
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    return Go("quitting");
                }

                break;

            case "quitting":
                if (Settled(out var quit))
                {
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        return Fail(session, "quitting", quit);
                    }

                    _log.Info(LogCategory, "verdict " + VerdictFields());
                    return Go("done", ScenarioStatus.Succeeded);
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown mage-build stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ character

    private ScenarioStatus Choose(IGameSessionView session)
    {
        var characters = session.State.Characters;
        foreach (var c in characters.Entries)
        {
            if (string.Equals(c.Name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                if (c.PendingDeletion)
                {
                    if (_fresh)
                    {
                        continue; // M4 fresh B1: a pending row of the name is an old copy; the new copy purges it
                    }

                    return Fail(session, "choosing: the mage is pending deletion (restore it first)");
                }

                if (_fresh && !_freshDecided)
                {
                    // M4 fresh B1 (D44): the live copy is never entered. It is deleted from the list through the retail delete, and the new copy purges it once built (D45).
                    _freshDecided = true;
                    _oldGuid = c.Guid;
                    _log.Warn(LogCategory, "fresh-character: deleting the live " + MageTemplate.CharacterName + " from the character list through the retail character delete (it is not entered); the new copy purges it after the build");
                    _pending = session.Actions.DeleteCharacter(c.Guid, ActionSource.Host);
                    return Go("recreate-delete");
                }

                EnteredGuid = c.Guid;
                _log.Info(LogCategory, (Created ? "entering the character just created" : "entering the existing character") + " " + MageTemplate.CharacterName);
                _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
                return Go("entering");
            }
        }

        if (_createTried)
        {
            return Fail(session, "choosing: the character the server confirmed is not in the character list");
        }

        if (characters.SlotCount > 0 && characters.Entries.Count >= characters.SlotCount)
        {
            return Fail(
                session,
                string.Create(CultureInfo.InvariantCulture, $"choosing: the account has no free character slot ({characters.Entries.Count} of {characters.SlotCount} used), so {MageTemplate.CharacterName} was not created; no other character is touched"));
        }

        _createTried = true;
        if (_fresh && !_freshDecided)
        {
            _freshDecided = true;
            _freshHow = "created"; // M4 fresh B1: no live copy was listed, so there is nothing to delete
        }

        _log.Info(LogCategory, "absent; creating " + MageTemplate.CharacterName + " with the template's creation intent");
        _pending = session.Actions.CreateCharacter(MageTemplate.CreationIntent(MageTemplate.CharacterName, _retailClasses), ActionSource.Host);
        return Go("creating");
    }

    // ------------------------------------------------------------------------------------------------ building

    private ScenarioStatus Build(IGameSessionView session)
    {
        var q = session.State.Self.Qualities;
        if (_inFlight is { } step)
        {
            if (step.Handle.Outcome is not { } outcome)
            {
                return ScenarioStatus.Running;
            }

            _inFlight = null;
            if (outcome.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, step.Label, outcome);
            }

            if (step.OnConfirmed?.Invoke() is { } problem)
            {
                return Fail(session, problem);
            }

            _log.Info(LogCategory, step.Label + ": confirmed" + (outcome.Detail is null ? string.Empty : " (" + outcome.Detail + ")"));
        }

        if (!q.IsKnown)
        {
            return Waiting(session, "the player description has not arrived");
        }

        var actions = session.Actions;

        // (a) experience to level 126, once.
        var wanted = _table.XpForLevel(MageTemplate.TargetLevel);
        var total = (ulong)Math.Max(0, q.TotalExperience);
        if (total < wanted)
        {
            if (_xpSent)
            {
                return Waiting(session, string.Create(CultureInfo.InvariantCulture, $"total experience is {total:N0}, below level {MageTemplate.TargetLevel}'s {wanted:N0} after the xp verb"));
            }

            if (q.Level < 1 || q.Level >= MageTemplate.TargetLevel || _table.XpForLevel(q.Level) > total || total >= _table.XpForLevel(q.Level + 1))
            {
                return Fail(
                    session,
                    string.Create(CultureInfo.InvariantCulture, $"building: the read model's level and experience disagree (level {q.Level}, total experience {total:N0}); the xp grant is not sent"));
            }

            _xpSent = true;
            var amount = wanted - total;
            return Send(
                string.Create(CultureInfo.InvariantCulture, $"xp {amount:N0} (to {wanted:N0}, level {MageTemplate.TargetLevel}; has {total:N0})"),
                session.Tools.AddXp((long)amount, ActionSource.Host),
                counted: true);
        }

        if (q.Level < MageTemplate.TargetLevel)
        {
            return Waiting(session, $"the level is {q.Level}, below {MageTemplate.TargetLevel} with {total:N0} experience");
        }

        // (b) the four magic skills not yet trained, with the SkillTable's credits.
        foreach (var skill in MageTemplate.TrainedLater)
        {
            if (!q.TryGetSkill(skill, out var desc))
            {
                return Fail(session, $"training skill {skill}: the skill is not in the character's description");
            }

            if (desc.AdvancementClass >= HeadlessCharacter.TrainedSkill)
            {
                continue;
            }

            if (!MageTemplate.TrainCredits.TryGetValue(skill, out var credits))
            {
                return Fail(session, $"training skill {skill}: the template names no credit cost for it (TODO(m3-template-credits))");
            }

            if (q.AvailableSkillCredits < credits)
            {
                return Waiting(session, $"training skill {skill} needs {credits} skill credits, the character has {q.AvailableSkillCredits}");
            }

            if (!_sent.Add("train " + skill))
            {
                return Fail(session, $"training skill {skill}: the train was confirmed but the read model still shows it untrained");
            }

            return Send($"train skill {skill} for {credits} credits", actions.TrainSkill(skill, credits, ActionSource.Host), counted: true);
        }

        // (c) the plan's raises, in order, each to its target ranks.
        foreach (var target in MageTemplate.Targets)
        {
            if (!Read(q, target, out var ranks, out var spent, out var untrained))
            {
                return Fail(session, $"raising {target.Name}: the stat is not in the character's description");
            }

            if (ranks >= target.Ranks)
            {
                continue;
            }

            if (untrained)
            {
                return Fail(session, $"raising {target.Name}: the skill is not trained, so it cannot be raised");
            }

            var cost = target.XpRemaining(_table, spent);
            if (cost == 0 || cost > uint.MaxValue)
            {
                return Fail(session, string.Create(CultureInfo.InvariantCulture, $"raising {target.Name}: ranks {ranks} of {target.Ranks} but the experience still to spend is {cost:N0} (the read model and the XpTable disagree)"));
            }

            if (q.AvailableExperience < (long)cost)
            {
                return Waiting(session, string.Create(CultureInfo.InvariantCulture, $"raising {target.Name} needs {cost:N0} experience, the character has {q.AvailableExperience:N0} to spend"));
            }

            if (!_sent.Add("raise " + target.Kind + " " + target.Id))
            {
                return Fail(session, $"raising {target.Name}: the raise was confirmed but the read model still shows {ranks} of {target.Ranks} ranks");
            }

            var label = string.Create(CultureInfo.InvariantCulture, $"raise {target.Name} {ranks} -> {target.Ranks} for {cost:N0} experience");
            ActionHandle handle = target.Kind switch
            {
                MageStatKind.Attribute => actions.RaiseAttribute(target.Id, (uint)cost, ActionSource.Host),
                MageStatKind.Vital => actions.RaiseVital(target.Id, (uint)cost, ActionSource.Host),
                _ => actions.RaiseSkill(target.Id, (uint)cost, ActionSource.Host),
            };
            return Send(label, handle, counted: true);
        }

        // (d) the spellbook, in chunks the mod accepts. A ready mage's chunks all answer known.
        var ids = ArenaProfile.SpellsToLearn;
        var first = _spellChunk * SpellChunk;
        if (first < ids.Count)
        {
            var chunk = ids.Skip(first).Take(SpellChunk).ToArray();
            var handle = session.Tools.LearnSpells(chunk, ActionSource.Host);
            return Send(
                string.Create(CultureInfo.InvariantCulture, $"learn spells {first + 1}-{first + chunk.Length} of {ids.Count}"),
                handle,
                counted: false,
                onConfirmed: () => TallySpells(handle, chunk.Length));
        }

        // Done: the read model must show the whole template.
        Snapshot(q);
        if (_check is { AllMet: false } check)
        {
            return Fail(session, "the build ended short of the template: " + check.Summary);
        }

        if (_purge is not null && !_purged)
        {
            // M4 fresh B1 (D45): the old copies are purged only once the build has ended met, so a failed build leaves the old copy restorable. M4 fresh F1: the vitals are refilled first (the build's raises lifted the maxima over the current values).
            _log.Info(LogCategory, "the build ended met; refilling the vitals by a level-up, then purging the old copies of " + MageTemplate.CharacterName + " with the server mod");
            return Go("refilling");
        }

        return Finish(session);
    }

    private ScenarioStatus Finish(IGameSessionView session)
    {
        _finished = true;
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            return Go("done", ScenarioStatus.Succeeded);
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    /// <summary>
    /// M4 fresh F1: fills the vitals after the build by ACE's own level-up rule (<see cref="FreshRefill"/>: one grant of exactly the experience to the next level, only when a vital is below its maximum, never twice). The name check comes before the grant. Runs after the build has ended met and before the purge.
    /// </summary>
    private ScenarioStatus Refilling(IGameSessionView session)
    {
        if (session.State.Self.Name is not { } name)
        {
            return Waiting(session, "the entered character's name is not known");
        }

        if (!string.Equals(name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(session, "the entered character is not " + MageTemplate.CharacterName + ": nothing is sent for it");
        }

        switch (_refill!.Poll(session, out var failure))
        {
            case FreshRefillProgress.Asking:
                return ScenarioStatus.Running;
            case FreshRefillProgress.Failed:
                return Fail(session, failure!);
            default:
                Snapshot(session.State.Self.Qualities); // the verdict's level= is the refilled character's (the template's plus the one level-up)
                if (_refill.Granted > 0)
                {
                    _steps.Add(string.Create(CultureInfo.InvariantCulture, $"refill xp {_refill.Granted:N0} (a level-up: {_refill.Field})")); // recorded, not a built step: BuiltSteps counts the template's
                }

                _log.Info(LogCategory, "the vitals are full (refill=" + _refill.Field + "); purging the old copies of " + MageTemplate.CharacterName + " with the server mod");
                return Go("purging");
        }
    }

    /// <summary>
    /// M4 fresh B1 (D45): asks the mod to purge the old copies of the BSD Mage's name until it answers <c>unknown-character</c> (<see cref="FreshPurge"/>). The name check comes before every ask. A re-created character whose old copy was not purged fails the run;
    /// a fresh character then starts outside Holtburg, so it is teleported there before the log off.
    /// </summary>
    private ScenarioStatus Purging(IGameSessionView session)
    {
        if (session.State.Self.Name is not { } name)
        {
            return Waiting(session, "the entered character's name is not known");
        }

        if (!string.Equals(name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(session, "the entered character is not " + MageTemplate.CharacterName + ": nothing is sent for it");
        }

        switch (_purge!.Poll(session, out var failure))
        {
            case FreshPurgeProgress.Asking:
                return ScenarioStatus.Running;
            case FreshPurgeProgress.Failed:
                return Fail(session, failure!);
            default:
                if (_freshHow == "recreated" && _purge.Purged == 0)
                {
                    return Fail(session, "purge: the old copy was not purged (the mod answered unknown-character for the first ask, though the retail delete left the old " + MageTemplate.CharacterName + " pending deletion)");
                }

                _purged = true;
                if (!InHoltburg(session))
                {
                    _placeStart = session.Clock.Now;
                    var label = "place to Holtburg (" + _holtburg.Name + ")";
                    _log.Info(LogCategory, label);
                    _steps.Add(label);
                    _pending = session.Tools.Place(_holtburg, ActionSource.Host);
                    return Go("placing");
                }

                return Finish(session);
        }
    }

    private ScenarioStatus Placing(IGameSessionView session)
    {
        if (_pending is { Outcome: { } placed })
        {
            _pending = null;
            if (placed.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, "placing the mage in Holtburg", placed);
            }
        }

        if (_pending is null && session.State.Self.Travel == SelfTravel.Present && InHoltburg(session))
        {
            _log.Info(LogCategory, "the mage stands in Holtburg");
            return Finish(session);
        }

        return session.Clock.Now - _placeStart > PlaceSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"placing: the player was not present in Holtburg within {PlaceSeconds:0.#} s (travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private bool InHoltburg(IGameSessionView session) =>
        session.State.Self.Position is { } at && (at.CellId >> 16) == _holtburgLandblock;

    private string? TallySpells(ToolHandle<SpellLearnResult> handle, int asked)
    {
        if (handle.Result is not { } result || result.Spells.Count != asked)
        {
            return $"learning spells: the mod answered {handle.Result?.Spells.Count.ToString(CultureInfo.InvariantCulture) ?? "no result"} for {asked} ids";
        }

        if (!result.Ok)
        {
            return "learning spells: ids not in the spell table: " + string.Join(", ", result.UnknownIds);
        }

        _spellsKnown += result.Known;
        _spellsLearned += result.Learned;
        _spellChunk++;
        if (result.Learned > 0)
        {
            BuiltSteps++;
        }

        return null;
    }

    private ScenarioStatus Send(string label, ActionHandle handle, bool counted, Func<string?>? onConfirmed = null)
    {
        _wait.Reset();
        _log.Info(LogCategory, label);
        _steps.Add(label);
        if (counted)
        {
            BuiltSteps++;
        }

        _inFlight = new InFlight(label, handle, onConfirmed);
        return ScenarioStatus.Running;
    }

    /// <summary>Waits for a precondition the read model has not caught up with, then fails by name.</summary>
    private ScenarioStatus Waiting(IGameSessionView session, string reason)
    {
        return _wait.Expired(session.Clock.Now, reason)
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"building: still waiting after {SettleSeconds:0.#} s: {reason}"))
            : ScenarioStatus.Running;
    }

    private static bool Read(PlayerQualities q, StatTarget target, out int ranks, out uint spent, out bool untrained)
    {
        (ranks, spent, untrained) = (0, 0, false);
        switch (target.Kind)
        {
            case MageStatKind.Attribute:
                if (!q.TryGetAttribute(target.Id, out var attribute))
                {
                    return false;
                }

                (ranks, spent) = ((int)attribute.Ranks, attribute.Experience);
                return true;
            case MageStatKind.Vital:
                if (!q.TryGetVital(target.Id, out var vital))
                {
                    return false;
                }

                (ranks, spent) = ((int)vital.Ranks, vital.Experience);
                return true;
            default:
                if (!q.TryGetSkill(target.Id, out var skill))
                {
                    return false;
                }

                (ranks, spent, untrained) = (skill.Ranks, skill.Experience, skill.AdvancementClass < HeadlessCharacter.TrainedSkill);
                return true;
        }
    }

    private void Snapshot(PlayerQualities q)
    {
        if (q.IsKnown)
        {
            _level = q.Level;
            _check = MageTemplate.IsMet(q);
        }
    }

    // ------------------------------------------------------------------------------------------------ plumbing

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

    private ScenarioStatus Fail(IGameSessionView session, string step, in ActionOutcome outcome) =>
        Fail(
            session,
            step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) +
            (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Snapshot(session.State.Self.Qualities);
        Failure = why;
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        return ScenarioStatus.Failed;
    }

    private sealed record InFlight(string Label, ActionHandle Handle, Func<string?>? OnConfirmed);
}

/// <summary>
/// The wait for a precondition the read model has not caught up with (M3 1.1 fix, 1.1-R6): the clock is keyed by the reason, so a second,
/// different precondition gets its own full allowance instead of inheriting the first one's elapsed time. A changed reason restarts it;
/// <see cref="Reset"/> (a step sent) clears it.
/// </summary>
/// <param name="seconds">How long one reason may wait.</param>
internal sealed class ReasonTimer(double seconds)
{
    private double _since = double.NaN;
    private string? _reason;

    /// <summary>True when <paramref name="reason"/> has been waited on for more than the allowance by <paramref name="now"/>.</summary>
    public bool Expired(double now, string reason)
    {
        if (double.IsNaN(_since) || !string.Equals(_reason, reason, StringComparison.Ordinal))
        {
            _since = now;
            _reason = reason;
        }

        return now - _since > seconds;
    }

    /// <summary>Clears the wait (a precondition passed or a step was sent).</summary>
    public void Reset()
    {
        _since = double.NaN;
        _reason = null;
    }
}
