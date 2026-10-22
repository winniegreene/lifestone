using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M4's level-40 fighter (<c>--scenario=fighter-build</c>, spec 5.10, plan 4.1 s2, owner decision D1): find the test account's
/// <see cref="FighterTemplate.CharacterName"/> or create it with <see cref="FighterTemplate.CreationIntent"/>, enter the world as that character and
/// nobody else, and bring it to the template, the kit and Holtburg. Idempotent and resumable on <see cref="MageBuildScenario"/>'s rule: every step is
/// sent only if the read model still shows it is needed, so a ready fighter is only verified and a half-built one gets just the missing steps, in order.
/// Then it logs off and quits as the other scenarios do.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>login</c>, <c>choosing</c>, <c>creating</c> (only when the name is absent), <c>entering</c>, <c>arriving</c>,
/// <c>status</c> (the access level must be Player, D11, and the mod must report <c>xp</c> and <c>item</c>), <c>building</c>, <c>placing</c>,
/// <c>logging-off</c>, <c>quitting</c>, <c>done</c>. The scenario never selects, deletes or enters another character; an absent name with no free slot fails the
/// run, and the entered character's name is checked against the template's before every step that acts on it (<see cref="NameCheck"/>).</para>
/// <para><b>Building</b>, in this order, each only if still needed: (a) the mod's <c>xp</c> for exactly <see cref="XpTableData.XpForLevel"/>(40) minus
/// <see cref="PlayerQualities.TotalExperience"/> (never when the fighter is at or past it); (b) <see cref="IActionApi.TrainSkill"/> for each template skill
/// the read model shows untrained, with <see cref="TrainCredits"/> (<c>TODO(m4-healing-minlevel)</c>: s0 found Healing has MinLevel 2 in the SkillTable; the
/// creation asks for it trained, and if ACE refuses that the creation fails by name and the remedy is this train step, so the creation moves Healing to
/// <see cref="FighterTemplate.TrainedLater"/>; the live build proves which); (c) the raise of each <see cref="FighterTemplate.Targets"/> entry in plan order
/// with <see cref="StatTarget.XpRemaining"/>; (d) the kit, one <see cref="PlayerTools.SpawnItem"/> at a time over <see cref="FighterKit.Items"/> (it does not
/// stack, so the two healing kits are two grants; a stack line is topped up by the missing count, and a top-up is not merged: the count is what is judged);
/// (e) the Mace wielded and the five leather pieces worn by <see cref="IActionApi.Wield"/> on <see cref="GearScenario"/>'s pattern (a worn piece that is not a
/// kit line and covers the slots goes to the pack first); (f) <see cref="PlayerTools.Place"/> to Holtburg's point of interest when the player is not in its
/// landblock (no bind: item 1.2's hunt binds). Every action waits for its outcome; anything but Confirmed fails the run naming the step and the outcome.
/// A precondition the read model has not caught up with is waited for <see cref="SettleSeconds"/> on the session clock per reason, then fails by name;
/// nothing is ever sent twice.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[fighter] verdict ...</c>):
/// <c>fighter=ready|built|failed level=&lt;n&gt; template=ok|short [template_short=...] kit=ok|&lt;missing&gt; wielded=&lt;wcid&gt;|0 worn=&lt;count&gt;</c>.
/// <c>kit</c> is <c>ok</c> or the lines still short as <c>Name:held/needed</c> joined by commas (no spaces); <c>wielded</c> is the Mace's weenie class id when it
/// is wielded; <c>worn</c> counts the leather pieces worn. <c>ready</c> means no step was needed, <c>built</c> that some were and everything ended met.</para>
/// </remarks>
public sealed class FighterBuildScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=fighter-build</c>).</summary>
    public const string ScenarioName = "fighter-build";

    /// <summary>The log category of the scenario's lines (<c>[fighter] ...</c>).</summary>
    public const string LogCategory = "fighter";

    /// <summary>How long a precondition the read model has not caught up with is waited for, in seconds on the session clock.</summary>
    public const double SettleSeconds = 10;

    /// <summary>How long the teleport to Holtburg may take, in seconds on the session clock.</summary>
    public const double PlaceSeconds = 30;

    /// <summary>The mod verbs the build uses (the experience grant and the item grant), which the status stage requires the mod to report.</summary>
    internal static readonly string[] RequiredVerbs = [ToolVerbs.Xp, ToolVerbs.Item];

    /// <summary>
    /// The SkillTable's trained cost (credits) of each skill the template trains, Aluvian (<c>FighterTemplateDatTests</c> pins them against the dats): Melee Defense 10, Missile Weapons 6, Healing 6.
    /// ACE refuses any other number. Heavy Weapons (12) is specialised at creation and cannot be specialised later.
    /// </summary>
    public static IReadOnlyDictionary<uint, int> TrainCredits { get; } = new Dictionary<uint, int>
    {
        [FighterTemplate.MeleeDefense] = 10,
        [FighterTemplate.MissileWeapons] = 6,
        [FighterTemplate.Healing] = 6,
    };

    private readonly XpTableData _table;
    private readonly IReadOnlyList<uint> _retailClasses;
    private readonly ILogSink _log;
    private readonly HashSet<string> _sent = [];
    private readonly List<string> _steps = [];
    private readonly List<uint> _scratch = [];
    private readonly ReasonTimer _wait = new(SettleSeconds);
    private readonly uint _holtburgLandblock;
    private readonly TeleportDestination _holtburg;
    private ActionHandle? _pending;
    private InFlight? _inFlight;
    private ToolHandle<ToolStatus>? _status;
    private bool _createTried;
    private bool _xpSent;
    private double _arrivalStart = double.NaN;
    private double _placeStart = double.NaN;
    private bool _finished;
    private int _level;
    private TemplateCheck? _check;
    private string _kit = "not_entered";
    private uint _wielded;
    private int _worn;

    /// <summary>The fighter build.</summary>
    /// <param name="table">The XpTable (<see cref="XpTableData.Load"/>).</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats (for the creation).</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    public FighterBuildScenario(XpTableData table, IReadOnlyList<uint> retailClasses, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(retailClasses);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        if (table.CharacterLevelXpList.Count <= FighterTemplate.TargetLevel)
        {
            throw new ArgumentException($"TODO(m3-xp-table): the XpTable ends before level {FighterTemplate.TargetLevel}", nameof(table));
        }

        _table = table;
        _retailClasses = [.. retailClasses];
        _log = log;
        ArrivalSeconds = arrivalSeconds;
        _holtburg = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
        _holtburgLandblock = _holtburg.Location.Position.CellId >> 16;
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

    /// <summary>The steps sent, in order (<c>xp</c>, <c>train</c>, <c>raise</c>, <c>item</c>, <c>wield</c> and <c>place</c> lines, as logged), for the run's record and the tests.</summary>
    public IReadOnlyList<string> SentSteps => _steps;

    /// <summary>The steps counted in the verdict's ready or built decision (every step sent).</summary>
    public int BuiltSteps => _steps.Count;

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The verdict fields (no character name, no account).</summary>
    public string VerdictFields()
    {
        var failed = Failure is not null || !_finished;
        var state = failed ? "failed" : BuiltSteps > 0 ? "built" : "ready";
        var template = _check is { AllMet: true } ? "template=ok" : "template=short";
        if (_check is null)
        {
            template += " template_short=not_entered";
        }
        else if (!_check.AllMet)
        {
            template += " template_short=" + MageBuildScenario.ShortField(_check);
        }

        return string.Create(CultureInfo.InvariantCulture, $"fighter={state} level={_level} {template} kit={_kit} wielded={_wielded} worn={_worn}");
    }

    /// <summary>True when <paramref name="name"/> is the fighter template's character (case-insensitive, as the character list matches).</summary>
    public static bool NameCheck(string? name) => string.Equals(name, FighterTemplate.CharacterName, StringComparison.OrdinalIgnoreCase);

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
                        return Fail(session, "creating the character (TODO(m4-healing-minlevel): if ACE refuses Healing trained at level 1, move it to FighterTemplate.TrainedLater and create again)", created);
                    }

                    Created = true;
                    _log.Info(LogCategory, "created " + FighterTemplate.CharacterName);
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
                return StatusStep(session);

            case "building":
                return Build(session);

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
                return Fail(session, $"TODO(state): unknown fighter-build stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ character

    private ScenarioStatus Choose(IGameSessionView session)
    {
        var characters = session.State.Characters;
        foreach (var c in characters.Entries)
        {
            if (NameCheck(c.Name))
            {
                if (c.PendingDeletion)
                {
                    return Fail(session, "choosing: the fighter is pending deletion (restore it first)");
                }

                EnteredGuid = c.Guid;
                _log.Info(LogCategory, (Created ? "entering the character just created" : "entering the existing character") + " " + FighterTemplate.CharacterName);
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
                string.Create(CultureInfo.InvariantCulture, $"choosing: the account has no free character slot ({characters.Entries.Count} of {characters.SlotCount} used), so {FighterTemplate.CharacterName} was not created; no other character is touched"));
        }

        _createTried = true;
        _log.Info(LogCategory, "absent; creating " + FighterTemplate.CharacterName + " with the template's creation intent");
        _pending = session.Actions.CreateCharacter(FighterTemplate.CreationIntent(FighterTemplate.CharacterName, _retailClasses), ActionSource.Host);
        return Go("creating");
    }

    private ScenarioStatus StatusStep(IGameSessionView session)
    {
        if (!Settled(out var status))
        {
            return ScenarioStatus.Running;
        }

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

    /// <summary>Null when the entered character is the template's; else the step fails (the build acts on nobody else).</summary>
    private ScenarioStatus? WrongCharacter(IGameSessionView session)
    {
        var name = session.State.Self.Name;
        if (name is null)
        {
            return Waiting(session, "the entered character's name is not known");
        }

        return NameCheck(name) ? null : Fail(session, "the entered character is not " + FighterTemplate.CharacterName + ": nothing is sent for it");
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

            _log.Info(LogCategory, step.Label + ": confirmed" + (outcome.Detail is null ? string.Empty : " (" + outcome.Detail + ")"));
        }

        if (!q.IsKnown)
        {
            return Waiting(session, "the player description has not arrived");
        }

        if (WrongCharacter(session) is { } wrong)
        {
            return wrong;
        }

        var actions = session.Actions;

        // (a) experience to level 40, once.
        var wanted = _table.XpForLevel(FighterTemplate.TargetLevel);
        var total = (ulong)Math.Max(0, q.TotalExperience);
        if (total < wanted)
        {
            if (_xpSent)
            {
                return Waiting(session, "the total experience is below the target level's after the xp verb");
            }

            if (q.Level < 1 || q.Level >= FighterTemplate.TargetLevel || _table.XpForLevel(q.Level) > total || total >= _table.XpForLevel(q.Level + 1))
            {
                return Fail(
                    session,
                    string.Create(CultureInfo.InvariantCulture, $"building: the read model's level and experience disagree (level {q.Level}, total experience {total:N0}); the xp grant is not sent"));
            }

            _xpSent = true;
            var amount = wanted - total;
            return Send(
                string.Create(CultureInfo.InvariantCulture, $"xp {amount:N0} (to {wanted:N0}, level {FighterTemplate.TargetLevel}; has {total:N0})"),
                session.Tools.AddXp((long)amount, ActionSource.Host));
        }

        if (q.Level < FighterTemplate.TargetLevel)
        {
            return Waiting(session, "the level is below the target with the experience granted");
        }

        // (b) a template skill the read model shows untrained, with the SkillTable's credits (a partial character, or the Healing remedy).
        foreach (var target in FighterTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill))
        {
            if (!q.TryGetSkill(target.Id, out var desc))
            {
                return Fail(session, $"training skill {target.Name}: the skill is not in the character's description");
            }

            if (desc.AdvancementClass >= HeadlessCharacter.TrainedSkill)
            {
                continue;
            }

            if (target.Specialized || !TrainCredits.TryGetValue(target.Id, out var credits))
            {
                return Fail(session, $"training skill {target.Name}: it is untrained and cannot be trained here (TODO(m4-specialize-later): a specialised skill is chosen at creation; or no credit cost is named for it)");
            }

            if (q.AvailableSkillCredits < credits)
            {
                return Waiting(session, "training a skill needs more skill credits than the character has");
            }

            if (!_sent.Add("train " + target.Id))
            {
                return Fail(session, $"training skill {target.Name}: the train was confirmed but the read model still shows it untrained");
            }

            return Send($"train skill {target.Name} for {credits} credits", actions.TrainSkill(target.Id, credits, ActionSource.Host));
        }

        // (c) the plan's raises, in order, each to its target ranks.
        foreach (var target in FighterTemplate.Targets)
        {
            if (!ReadStat(q, target, out var ranks, out var spent))
            {
                return Fail(session, $"raising {target.Name}: the stat is not in the character's description");
            }

            if (ranks >= target.Ranks)
            {
                continue;
            }

            var cost = target.XpRemaining(_table, spent);
            if (cost == 0 || cost > uint.MaxValue)
            {
                return Fail(session, string.Create(CultureInfo.InvariantCulture, $"raising {target.Name}: ranks {ranks} of {target.Ranks} but the experience still to spend is {cost:N0} (the read model and the XpTable disagree)"));
            }

            if (q.AvailableExperience < (long)cost)
            {
                return Waiting(session, "a raise needs more experience than the character has to spend");
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
            return Send(label, handle);
        }

        // (d) the kit, one grant at a time.
        if (KitStep(session) is { } kit)
        {
            return kit;
        }

        // (e) the Mace wielded and the leather worn.
        if (WieldStep(session) is { } wield)
        {
            return wield;
        }

        // (f) Holtburg.
        Snapshot(session);
        if (_check is { AllMet: false } check)
        {
            return Fail(session, "the build ended short of the template: " + check.Summary);
        }

        if (!InHoltburg(session))
        {
            _placeStart = session.Clock.Now;
            _wait.Reset();
            var label = "place to Holtburg (" + _holtburg.Name + ")";
            _log.Info(LogCategory, label);
            _steps.Add(label);
            _pending = session.Tools.Place(_holtburg, ActionSource.Host);
            return Go("placing");
        }

        return Finish(session);
    }

    private ScenarioStatus Placing(IGameSessionView session)
    {
        if (_pending is { Outcome: { } placed })
        {
            _pending = null;
            if (placed.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, "placing the fighter in Holtburg", placed);
            }
        }

        if (_pending is null && session.State.Self.Travel == SelfTravel.Present && InHoltburg(session))
        {
            _log.Info(LogCategory, "the fighter stands in Holtburg");
            Snapshot(session);
            return Finish(session);
        }

        return session.Clock.Now - _placeStart > PlaceSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"placing: the player was not present in Holtburg within {PlaceSeconds:0.#} s (travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private bool InHoltburg(IGameSessionView session) =>
        session.State.Self.Position is { } at && (at.CellId >> 16) == _holtburgLandblock;

    private ScenarioStatus Finish(IGameSessionView session)
    {
        Snapshot(session);
        if (_kit != "ok" || _wielded != FighterKit.MaceWcid || _worn != FighterKit.Worn.Count(i => i.Role == FighterRole.Armour))
        {
            return Fail(session, $"the build ended short of the kit: kit={_kit} wielded={_wielded} worn={_worn}");
        }

        var stillWielded = FighterItems.WieldedLauncher(session.State, _scratch) is { } launcher ? "the launcher '" + launcher.Name + "'" : FighterItems.WieldedAmmunition(session.State) != 0 ? "ammunition" : null;
        if (stillWielded is not null)
        {
            return Fail(session, $"the build ended with {stillWielded} still wielded: ACE refuses the Mace while a launcher is");
        }

        _finished = true;
        _pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    // ------------------------------------------------------------------------------------------------ the kit

    /// <summary>Sends the next missing grant and returns Running, waits (Running) for the inventory to settle, fails by name, or returns null when the kit is held.</summary>
    private ScenarioStatus? KitStep(IGameSessionView session)
    {
        var state = session.State;
        if (!state.Self.Inventory.IsKnown)
        {
            return Waiting(session, "the inventory is not known");
        }

        var counts = GearInventory.CountAll(state, out var unsettled);
        if (unsettled > 0)
        {
            return Waiting(session, "a held item has no world object yet");
        }

        foreach (var line in FighterKit.Items)
        {
            var have = counts.GetValueOrDefault(line.Wcid);
            var need = line.Item.Stack;
            if (have >= need)
            {
                continue;
            }

            int? stack = line.Item.MaxStack is not null ? need - have : null;
            if (!_sent.Add(string.Create(CultureInfo.InvariantCulture, $"item {line.Wcid} held {have}")))
            {
                return Fail(session, $"the kit: the {line.Item.Name} was granted but the inventory model still holds {have} of {need}");
            }

            var label = string.Create(CultureInfo.InvariantCulture, $"item {line.Item.Name} (weenie {line.Wcid}){(stack is { } n ? " x " + n : string.Empty)}, held {have} of {need}");
            return Send(label, session.Tools.SpawnItem(line.Wcid, stack, ActionSource.Host));
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ wielding

    /// <summary>Sends the next wield (or the take-off of a piece in the way) and returns Running, waits, fails by name, or returns null when the Mace is wielded and the leather worn.</summary>
    private ScenarioStatus? WieldStep(IGameSessionView session)
    {
        var state = session.State;
        var inventory = state.Self.Inventory;
        if (ClearWeaponSlots(session) is { } cleared)
        {
            return cleared;
        }

        foreach (var line in FighterKit.Worn)
        {
            var item = line.Item;
            var equipped = false;
            uint carried = 0;
            GearInventory.Collect(state, _scratch);
            for (var i = 0; i < _scratch.Count; i++)
            {
                var guid = _scratch[i];
                if (!state.Objects.TryGet(guid, out var o) || o.Weenie.WeenieClassId != item.Wcid)
                {
                    continue;
                }

                if (IsWornWithin(inventory, guid, item))
                {
                    equipped = true;
                    break;
                }

                if (carried == 0 && inventory.Find(guid).Kind != InventoryPlaceKind.Equipped)
                {
                    carried = guid;
                }
            }

            if (equipped)
            {
                continue;
            }

            var blocker = BlockingPiece(session, item);
            if (blocker != 0)
            {
                if (!_sent.Add("unwield " + blocker.ToString("X8", CultureInfo.InvariantCulture)))
                {
                    return Fail(session, $"wielding: the piece in the way of the {item.Name} was taken off but the inventory model still shows it worn");
                }

                return Send(
                    "take off a piece that is not in the kit, which holds the place of the " + item.Name + " (object 0x" + blocker.ToString("X8", CultureInfo.InvariantCulture) + ")",
                    session.Actions.Unwield(blocker, ActionSource.Host));
            }

            if (carried == 0)
            {
                return Waiting(session, "a kit piece is not in the pack");
            }

            if (!_sent.Add("wield " + item.Wcid))
            {
                return Fail(session, $"wielding: the {item.Name} was confirmed but the inventory model does not show it worn");
            }

            var label = (line.Role == FighterRole.Weapon ? "wield " : "wear the ") + item.Name + " at 0x" + ((uint)item.ValidLocations).ToString("X", CultureInfo.InvariantCulture);
            return Send(label, session.Actions.Wield(carried, (EquipMask)WieldSlotChoice.Choose(inventory, (uint)item.ValidLocations), ActionSource.Host));
        }

        return null;
    }

    /// <summary>
    /// M4 3.1 fix2, the repair tool for a hunter a half-done weapon swap left wielding the Shortbow: ACE refuses the Mace (<c>CheckWeaponCollision</c>, <c>Player_Inventory.cs</c>: WeenieError 0) while a launcher is wielded, so a wielded
    /// ammunition stack and then a wielded launcher are taken off into the pack, one at a time and each waited for (the ammunition first, as the hunt's restore does), before the Mace is wielded. The build ends with no launcher and
    /// no ammunition wielded. Nothing is sent when none is wielded, so a ready character still costs no step. Returns Running (a step sent, or a loud failure) or null when the slots are clear.
    /// </summary>
    private ScenarioStatus? ClearWeaponSlots(IGameSessionView session)
    {
        var state = session.State;
        var ammunition = FighterItems.WieldedAmmunition(state);
        if (ammunition != 0)
        {
            return TakeOff(session, ammunition, "the wielded ammunition");
        }

        if (FighterItems.WieldedLauncher(state, _scratch) is { } launcher)
        {
            return TakeOff(session, launcher.Guid, $"the wielded launcher '{launcher.Name}'");
        }

        return null;
    }

    private ScenarioStatus TakeOff(IGameSessionView session, uint guid, string what)
    {
        var hex = guid.ToString("X8", CultureInfo.InvariantCulture);
        if (!_sent.Add("unwield " + hex))
        {
            return Fail(session, $"wielding: {what} was taken off but the inventory model still shows it wielded");
        }

        return Send($"take off {what} to the pack (ACE refuses the Mace while a launcher is wielded) (object 0x{hex})", session.Actions.Unwield(guid, ActionSource.Host));
    }

    private static bool IsWornWithin(PlayerInventory inventory, uint guid, GearItem item) =>
        inventory.TryGetEquipped(guid, out var e) && e.Location != 0 && (e.Location & ~(uint)item.ValidLocations) == 0;

    /// <summary>The guid of a worn object that is not a line of the kit and occupies a location <paramref name="item"/> would take (0 when none).</summary>
    private static uint BlockingPiece(IGameSessionView session, GearItem item)
    {
        var worn = session.State.Self.Inventory.Equipped;
        for (var i = 0; i < worn.Count; i++)
        {
            var e = worn[i];
            if (e.Location == 0 || (e.Location & (uint)item.ValidLocations) == 0 || !session.State.Objects.TryGet(e.Guid, out var o) || FighterKit.ItemOf(o.Weenie.WeenieClassId) is not null)
            {
                continue;
            }

            return e.Guid;
        }

        return 0;
    }

    // ------------------------------------------------------------------------------------------------ the record

    private void Snapshot(IGameSessionView session)
    {
        var state = session.State;
        var q = state.Self.Qualities;
        if (q.IsKnown)
        {
            _level = q.Level;
            _check = FighterTemplate.IsMet(q);
        }

        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return;
        }

        var counts = GearInventory.CountAll(state, out _);
        var short_ = new List<string>();
        foreach (var line in FighterKit.Items)
        {
            var have = counts.GetValueOrDefault(line.Wcid);
            if (have < line.Item.Stack)
            {
                short_.Add(string.Create(CultureInfo.InvariantCulture, $"{line.Item.Name.Replace(' ', '_')}:{have}/{line.Item.Stack}"));
            }
        }

        _kit = short_.Count == 0 ? "ok" : string.Join(',', short_);
        _wielded = 0;
        _worn = 0;
        var wornArmour = new List<uint>();
        GearInventory.Collect(state, _scratch);
        foreach (var line in FighterKit.Worn)
        {
            for (var i = 0; i < _scratch.Count; i++)
            {
                if (state.Objects.TryGet(_scratch[i], out var o) && o.Weenie.WeenieClassId == line.Wcid && IsWornWithin(inventory, _scratch[i], line.Item))
                {
                    if (line.Role == FighterRole.Weapon)
                    {
                        _wielded = line.Wcid;
                    }
                    else
                    {
                        wornArmour.Add(line.Wcid);
                    }

                    break;
                }
            }
        }

        _worn = FighterKit.CountWornArmour(wornArmour);
    }

    private static bool ReadStat(PlayerQualities q, StatTarget target, out int ranks, out uint spent)
    {
        (ranks, spent) = (0, 0);
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

                (ranks, spent) = (skill.Ranks, skill.Experience);
                return true;
        }
    }

    // ------------------------------------------------------------------------------------------------ plumbing

    private ScenarioStatus Send(string label, ActionHandle handle)
    {
        _wait.Reset();
        _log.Info(LogCategory, label);
        _steps.Add(label);
        _inFlight = new InFlight(label, handle);
        return ScenarioStatus.Running;
    }

    /// <summary>Waits for a precondition the read model has not caught up with (the reason is a constant text: the clock is keyed by it), then fails by name.</summary>
    private ScenarioStatus Waiting(IGameSessionView session, string reason) =>
        _wait.Expired(session.Clock.Now, reason)
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"building: still waiting after {SettleSeconds:0.#} s: {reason}"))
            : ScenarioStatus.Running;

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
        Snapshot(session);
        Failure = why;
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        return ScenarioStatus.Failed;
    }

    private sealed record InFlight(string Label, ActionHandle Handle);
}
