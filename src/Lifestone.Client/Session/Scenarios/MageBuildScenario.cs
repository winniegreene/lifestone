using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.CharacterCreation;
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
/// <c>arriving</c>, <c>status</c> (the access level must be Player, owner decision D11), <c>building</c>, <c>logging-off</c>,
/// <c>quitting</c>, <c>done</c>. The scenario never selects, deletes or enters another character; an absent name with no free slot
/// fails the run (<see cref="CharacterListState.SlotCount"/>).</para>
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

    /// <summary>The mod verbs the build uses (the experience grant and the spellbook), which the status stage requires the mod to report.</summary>
    internal static readonly string[] RequiredVerbs = [ToolVerbs.Xp, ToolVerbs.Spell + " " + ToolVerbs.SpellLearn];

    /// <summary>The most spell ids one <c>spell learn</c> carries.</summary>
    public const int SpellChunk = PlayerTools.MaxSpellIds;

    private readonly XpTableData _table;
    private readonly IReadOnlyList<uint> _retailClasses;
    private readonly ILogSink _log;
    private readonly HashSet<string> _sent = [];
    private readonly List<string> _steps = [];
    private ActionHandle? _pending;
    private InFlight? _inFlight;
    private ToolHandle<ToolStatus>? _status;
    private bool _createTried;
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
    public MageBuildScenario(XpTableData table, IReadOnlyList<uint> retailClasses, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds)
    {
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

        return string.Create(
            CultureInfo.InvariantCulture,
            $"mage={state} level={_level} {template} spells={SpellsAccounted}/{ArenaProfile.SpellsToLearn.Count} built_steps={BuiltSteps}");
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

                    return Go("building");
                }

                break;

            case "building":
                return Build(session);

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
                    return Fail(session, "choosing: the mage is pending deletion (restore it first)");
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

        _finished = true;
        _pending = actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

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
