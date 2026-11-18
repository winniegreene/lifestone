using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 item 0 a3a (D5, D6, D7): the climber's creation, <c>--scenario=climber-create --fresh-character</c> on the account <c>test</c>. It is <see cref="MageBuildScenario"/>'s fresh-character pattern (M4 D44, D45) without a build: the live Winnie is never entered; it is deleted
/// from the character list through the retail character delete, the list must show it pending, the new Winnie is created with <see cref="MageTemplate.ClimberCreationIntent"/> (the traditional mage's attributes, War Magic and Life Magic specialised (no Alchemy, D34), no Melee Defense, the credits of
/// <see cref="MageTemplate.PlanClimberCreation"/>), entered, checked against <see cref="MageTemplate.ClimberCheck"/> (level 1) and only then does the server mod's char purge remove the old copies of the name (<see cref="FreshPurge"/>: the purge only after the creation succeeded, until the
/// mod answers unknown-character, never another name). Nothing is granted and nobody is placed: the character stays where the server put it (the Training Academy's start) and logs off.
/// <para><b>Verdict</b> (<c>[climber] verdict ...</c>): <c>fresh=recreated|created purged=&lt;n&gt; granted=&lt;n&gt; template=ok|short level=&lt;n&gt;</c>. <c>granted</c> is the mod-verb tally (<see cref="ModVerbCounts.Granted"/>) since the session began, not a literal: the run asks for no item, no experience and no spell.</para>
/// </summary>
public sealed class ClimberCreateScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=climber-create</c>).</summary>
    public const string ScenarioName = "climber-create";

    /// <summary>The log category of the scenario's lines (<c>[climber] ...</c>).</summary>
    public const string LogCategory = "climber";

    /// <summary>The character this scenario creates, purges and enters: the ladder's (<see cref="MageTemplate.ClimberName"/>), named once; its identity, not a policy.</summary>

    /// <summary>How long any one wait (the list, the arrival, the model) may take, in seconds on the session clock.</summary>
    public const double WaitSeconds = 30;

    private readonly IReadOnlyList<uint> _retailClasses;
    private readonly MageTemplate.ClimberCreationPlan _plan;
    private readonly ILogSink _log;
    private readonly FreshPurge _purge;
    private readonly double _arrivalSeconds;
    private readonly string? _orderLine;
    private ActionHandle? _pending;
    private ToolHandle<ToolStatus>? _status;
    private uint _oldGuid;
    private bool _decided;
    private bool _createTried;
    private string? _how;
    private TemplateCheck? _check;
    private readonly string _name;
    private int _level = -1;
    private ModVerbCounts _mark;
    private bool _marked;
    private ModVerbCounts _now;
    private double _stageStart;

    /// <summary>The creation scenario.</summary>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats.</param>
    /// <param name="plan">What the creation credits pay for (<see cref="PlanFromDats"/>).</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="climber">Which climber to create; null: Winnie.</param>
    /// <param name="orderLine">M6 item 14 S3: the line that says which skill order the plan was made in and why (<see cref="MageTemplate.ClimberOrderLine"/>), logged once at the start; null: none is logged.</param>
    public ClimberCreateScenario(IReadOnlyList<uint> retailClasses, MageTemplate.ClimberCreationPlan plan, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, string? climber = null, string? orderLine = null)
    {
        _orderLine = orderLine;
        _retailClasses = retailClasses ?? throw new ArgumentNullException(nameof(retailClasses));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _arrivalSeconds = arrivalSeconds;
        _name = climber ?? MageTemplate.ClimberName;
        _purge = new FreshPurge(_name, log, LogCategory);
    }

    /// <summary>
    /// The creation credits' plan read from the dats: the Aluvian heritage's skill credits and the SkillTable's trained and specialised costs through <c>CharGenData</c> (<c>ACCharGenData::GetSkillTrainedCost</c> and <c>GetSkillSpecializedCost</c>, 11.4186 0x005C26D0).
    /// </summary>
    /// <param name="dats">The opened dats.</param>
    /// <param name="buffBot">M6 item 14 S3: the buff bot's option (<c>--autopilot-buff-bot</c>, <see cref="Autopilot.AutopilotOptions.BuffBot"/>): the order the credits are planned in (<see cref="MageTemplate.ClimberOrder"/>).</param>
    public static MageTemplate.ClimberCreationPlan PlanFromDats(DatSet dats, bool buffBot)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var charGen = CharGenData.Load(dats);
        var skills = SkillTableData.Load(dats);
        var heritage = charGen.GetHeritage(HeadlessCharacter.Heritage);
        return MageTemplate.PlanClimberCreation(
            (int)heritage.SkillCredits,
            id => charGen.GetSkillTrainedCost(id, HeadlessCharacter.Heritage, skills),
            id => charGen.GetSkillSpecializedCost(id, HeadlessCharacter.Heritage, skills),
            buffBot);
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    public long Unmatched => 0;

    /// <summary>The creation plan the run uses.</summary>
    public MageTemplate.ClimberCreationPlan Plan => _plan;

    /// <summary>The template check after the arrival, or null before it.</summary>
    public TemplateCheck? Check => _check;

    /// <summary>How many old copies were purged.</summary>
    public int Purged => _purge.Purged;

    /// <summary>The mod-verb grants since the session began (the verdict's <c>granted=</c>: the counter's tally, never a literal).</summary>
    public int Granted => (_now.Since(_mark)).Granted;

    /// <summary><c>fresh=recreated|created purged=&lt;n&gt; granted=&lt;n&gt; template=ok|short level=&lt;n&gt;</c> (and <c>template_short=</c> when short).</summary>
    public string VerdictFields()
    {
        var template = _check is { AllMet: true } ? "template=ok" : "template=short" + (_check is null ? " template_short=not_entered" : " template_short=" + MageBuildScenario.ShortField(_check));
        return string.Create(CultureInfo.InvariantCulture, $"fresh={_how ?? "none"} purged={_purge.Purged} granted={Granted} {template} level={_level}");
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

        if (!_marked)
        {
            _marked = true;
            _mark = session.Tools.Verbs.Counts;
            if (_orderLine is not null)
            {
                _log.Info(LogCategory, _orderLine);
            }

            foreach (var line in _plan.LoudLines)
            {
                _log.Warn(LogCategory, line);
            }

            _log.Info(LogCategory, "creation credits: " + string.Join(", ", _plan.Paid.Select(p => $"{MageTemplate.SkillName(p.Skill)} {(p.Class == HeadlessCharacter.SpecializedSkill ? "specialised" : "trained")} {p.Cost}")) + string.Create(CultureInfo.InvariantCulture, $"; {_plan.Spent} of {_plan.Credits} spent, {_plan.Left} left"));
        }

        _now = session.Tools.Verbs.Counts;
        var actions = session.Actions;
        switch (Stage)
        {
            case "start":
                _pending = actions.Login(ActionSource.Host);
                return Go(session, "login");

            case "login":
                if (Settled(out var login))
                {
                    return login.Kind == ActionOutcomeKind.Confirmed ? Go(session, "choosing") : Fail(session, "login", login);
                }

                break;

            case "choosing":
                return Choose(session);

            case "recreate-delete":
                if (Settled(out var deleted))
                {
                    return deleted.Kind == ActionOutcomeKind.Confirmed ? Go(session, "recreate-verify") : Fail(session, "fresh-character: deleting the old character through the client's retail character delete", deleted);
                }

                break;

            case "recreate-verify":
                foreach (var listed in session.State.Characters.Entries)
                {
                    if (listed.Guid == _oldGuid && listed.PendingDeletion)
                    {
                        _how = "recreated";
                        _log.Warn(LogCategory, "fresh=recreated: the old " + _name + " is listed as pending deletion (restorable until the new copy has been created and checked); creating the climber");
                        return Go(session, "choosing");
                    }
                }

                return Waiting(session, "the server has not listed the old character as pending deletion");

            case "creating":
                if (Settled(out var created))
                {
                    if (created.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "creating the character", created);
                    }

                    _log.Info(LogCategory, "created " + _name);
                    return Go(session, "choosing");
                }

                break;

            case "entering":
                if (Settled(out var entered))
                {
                    return entered.Kind == ActionOutcomeKind.Confirmed ? Go(session, "arriving") : Fail(session, "entering the world", entered);
                }

                break;

            case "arriving":
                if (session.State.Self.Travel == SelfTravel.Present)
                {
                    _status = session.Tools.Status(ActionSource.Host);
                    _pending = _status;
                    return Go(session, "status");
                }

                if (session.Clock.Now - _stageStart > _arrivalSeconds)
                {
                    return Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {_arrivalSeconds:0.#} s (travel {session.State.Self.Travel})"));
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

                    if (!reported.Verbs.Contains(FreshPurge.RequiredVerb, StringComparer.Ordinal))
                    {
                        return Fail(session, $"status: the server mod does not report the '{FreshPurge.RequiredVerb}' verb the fresh character's purge uses (a stale mod: server setup -ModOnly)");
                    }

                    return Go(session, "checking");
                }

                break;

            case "checking":
                return Checking(session);

            case "purging":
                return Purging(session);

            case "logging-off":
                if (Settled(out var loggedOff))
                {
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off", loggedOff);
                    }

                    _pending = actions.Quit(ActionSource.Host);
                    return Go(session, "quitting");
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
                    Stage = "done";
                    return ScenarioStatus.Succeeded;
                }

                break;

            default:
                return Fail(session, $"TODO(state): unknown climber-create stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus Choose(IGameSessionView session)
    {
        var characters = session.State.Characters;
        foreach (var c in characters.Entries)
        {
            if (!string.Equals(c.Name, _name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (c.PendingDeletion)
            {
                continue; // a pending row of the name is an old copy; the new copy purges it
            }

            if (!_decided)
            {
                // M4 D44: the live copy is never entered. It is deleted through the retail delete and the new copy purges it once created and checked (D45).
                _decided = true;
                _oldGuid = c.Guid;
                _log.Warn(LogCategory, "fresh-character: deleting the live " + _name + " from the character list through the retail character delete (it is not entered); the new copy purges it after the creation");
                _pending = session.Actions.DeleteCharacter(c.Guid, ActionSource.Host);
                return Go(session, "recreate-delete");
            }

            _log.Info(LogCategory, "entering the character just created " + _name);
            _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
            return Go(session, "entering");
        }

        if (_createTried)
        {
            return Fail(session, "choosing: the character the server confirmed is not in the character list");
        }

        if (characters.SlotCount > 0 && characters.Entries.Count >= characters.SlotCount)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"choosing: the account has no free character slot ({characters.Entries.Count} of {characters.SlotCount} used), so {_name} was not created; no other character is touched"));
        }

        _createTried = true;
        if (!_decided)
        {
            _decided = true;
            _how = "created"; // no live copy was listed, so there is nothing to delete
        }

        _log.Info(LogCategory, "absent; creating " + _name + " with the climber's creation intent");
        _pending = session.Actions.CreateCharacter(MageTemplate.ClimberCreationIntent(_name, _retailClasses, _plan), ActionSource.Host);
        return Go(session, "creating");
    }

    private ScenarioStatus Checking(IGameSessionView session)
    {
        var self = session.State.Self;
        if (!self.Qualities.IsKnown || self.Name is null)
        {
            return Waiting(session, "the player description has not arrived");
        }

        if (!string.Equals(self.Name, _name, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(session, "the entered character is not " + _name + ": nothing is sent for it");
        }

        _level = self.Qualities.Level;
        _check = MageTemplate.ClimberCheck(_name, self.Qualities, _plan);
        _log.Info(LogCategory, "template check: " + _check.Summary);
        if (!_check.AllMet)
        {
            // The purge comes only after the creation succeeded: a short creation leaves the old copy restorable.
            return Fail(session, "template " + _check.Summary);
        }

        return Go(session, "purging");
    }

    private ScenarioStatus Purging(IGameSessionView session)
    {
        switch (_purge.Poll(session, out var failure))
        {
            case FreshPurgeProgress.Asking:
                return ScenarioStatus.Running;
            case FreshPurgeProgress.Failed:
                if (_how == "created" && _purge.RefusedAsOwnCharacter)
                {
                    // This run deleted nothing, so it has no old copy to clear. A re-create of this name needs the mod that lists it, and still fails here without it.
                    _log.Warn(LogCategory, "purge: the installed mod answered own-character, so it does not list " + _name + " as re-created; this run deleted nothing, so there is nothing to purge. Re-creating this character will need the mod updated");
                    _pending = session.Actions.LogOff(ActionSource.Host);
                    return Go(session, "logging-off");
                }

                return Fail(session, failure!);
            default:
                if (_how == "recreated" && _purge.Purged == 0)
                {
                    return Fail(session, "purge: the old copy was not purged (the mod answered unknown-character for the first ask, though the retail delete left the old " + _name + " pending deletion)");
                }

                _pending = session.Actions.LogOff(ActionSource.Host);
                return Go(session, "logging-off");
        }
    }

    private ScenarioStatus Waiting(IGameSessionView session, string reason) =>
        session.Clock.Now - _stageStart > WaitSeconds ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"{Stage}: still waiting after {WaitSeconds:0.#} s: {reason}")) : ScenarioStatus.Running;

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

    private ScenarioStatus Go(IGameSessionView session, string stage)
    {
        Stage = stage;
        _stageStart = session.Clock.Now;
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(IGameSessionView session, string step, in ActionOutcome outcome) =>
        Fail(session, step + ": " + outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail));

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        _now = session.Tools.Verbs.Counts;
        Failure = why;
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        return ScenarioStatus.Failed;
    }
}
