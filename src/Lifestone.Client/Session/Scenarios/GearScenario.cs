using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using DatReaderWriter.Enums;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M3 1.4's gear (<c>--scenario=gear</c>, spec 5.2 and 5.3): enter the world as BSD Mage (<see cref="MageTemplate.CharacterName"/>, nobody else), have the server mod grant the kit
/// (<see cref="PlayerTools.SpawnGear"/>, an idempotent top-up), wield the wand and wear the five pieces of the leather set through <see cref="IActionApi.Wield"/>, then log off and quit as
/// the other scenarios do. Idempotent and resumable: a piece already worn is skipped, so a ready mage sends no gear step (<c>gear_steps=0</c>; the component top-up is counted apart, <c>topup=</c>, and the cast, when on, still sends its own mode changes and cast). The gear does not level the mage (owner decision D3).
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>login</c>, <c>choosing</c>, <c>entering</c>, <c>arriving</c>, <c>spawning</c>, <c>wielding</c>, <c>logging-off</c>, <c>quitting</c>, <c>done</c>. A session that is
/// already in the world (a host that logged in itself, the offline stand-in) starts at <c>arriving</c>. The character must exist: an absent name fails the run (run <c>mage-build</c> first); the
/// scenario never creates, selects or deletes another character.</para>
/// <para><b>Spawning.</b> <c>SpawnGear</c> starts nothing while the inventory cannot be counted (no PlayerDescription yet, or a pack entry whose world object has not arrived: ACE sends a grant's
/// 0x0022 before its CreateObject), so the scenario asks again until it starts, within <see cref="SettleSeconds"/>; that retry sends nothing. A started spawn is waited for
/// <see cref="SpawnLimit"/> (the tools lane allows two requests a second, so a whole kit takes about half a minute on the stand-in; on ACE each grant answers in its own bundle) and any refusal
/// fails the run with the spawn's own words.</para>
/// <para><b>Wielding.</b> Wand first, then the pieces in kit order. Each goes out as GameAction 0x001A <c>GetAndWieldItem</c> {item, <see cref="GearItem.ValidLocations"/>} (the wand's mask is
/// <see cref="EquipMask.Held"/>, HeldCaster 0x01000000). ACE (<c>Player_Inventory.cs</c> <c>TryEquipObjectWithNetworking</c> 297-306, <c>Creature_Equipment.cs</c> 354-368) sends 0x02DA Wielder 0 and
/// 0x02CE 0 before GameEvent 0x0023 names the location, so membership comes from the 0x0023 and the 0x0022 only (spec 5.4, "wield"); a failure is GameEvent 0x00A0 and 0x02EB, which
/// <see cref="IActionApi.Wield"/> reports as a ServerError. A piece is skipped when the read model already shows a world object of its weenie equipped within its locations. One wield is in flight
/// at a time (lane Inventory), and every wait names its reason in the failure (<see cref="ReasonTimer"/>). <b>The swap (M3 4.2 P7):</b> before a kit line is wielded, a worn object that is not a kit line and occupies one of its locations (today's leather, the earlier wand)
/// is moved to the pack with <see cref="IActionApi.Unwield"/> (each counts as a <c>gear_steps</c>), so a mage geared with an older kit ends wearing exactly this one, and a second run sends nothing.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[gear] verdict ...</c>, printed by the headless host as <c>gear ...</c>):
/// <c>gear=wielded|failed worn=&lt;n&gt; kit=&lt;held&gt;/&lt;lines&gt; gear_steps=&lt;n&gt; topup=&lt;n&gt; cast=...</c>. <c>gear=wielded</c> means the read model shows the wand and every worn line of the kit equipped within its locations;
/// <c>worn</c> counts the worn pieces (0 to the kit's worn lines: 5 for the M3 loot kit, 1 for a ground's robe); <c>kit</c> counts the kit lines held in full (components summed over stacks); <c>gear_steps</c> counts the requests this run sent for the wand, the armour and the wields (0 on a ready mage); <c>topup</c> counts the
/// component requests (the kit's component lines the pack was short of: a cast burns components, so a mage that cast since the last run is topped up; report-only, M3 1.4 fix B, review 1.4-R1). No account or
/// character name is in it.</para>
/// <para><b>The first cast</b> (M3 1.4 s2; <see cref="GearCasts"/>). After the gear the scenario casts once: <see cref="IActionApi.SetCombatMode"/> to Magic (ACE answers a cast made in another mode with a bare UseDone,
/// <c>Player_Magic.cs</c> 84 and 275, and holds the Magic mode only with a wielded caster, <c>Player_Combat.cs</c> 872-880 @95ef426d: so the gear comes first), one Creature Enchantment self buff
/// through <see cref="IActionApi.Cast(uint, ActionSource)"/> (the highest level of the first profile line whose ACE chance from the read model's current skill is at least <see cref="GearCasts.MinChance"/>:
/// <c>GetCastingPreCheckStatus</c>, <c>Player_Magic.cs</c> 528-545, <c>SkillCheck.GetMagicSkillChance</c>, <c>SkillCheck.cs</c> 7-22), then Non-combat again. The verdict field
/// <c>cast=ok|fizzled|failed|n/a</c> is report-only (<c>gear=</c> stays the judged field): <c>ok</c> the enchantment arrived, <c>fizzled</c> a ServerError 0x0402, <c>failed</c> anything else, <c>n/a</c> no cast was
/// sent (the cast is off, or the read model cannot say which spell is safe: the skill is untrained or missing, or no level reaches the chance, or, with a SpellTable, no level the pack can pay for: M5 3 F6, <see cref="PackFloor"/>, the buff cycle's level floor, so a ground's kit that carries
/// the scarabs of its plan's levels only is never asked to cast a level it lacks; ACE refuses that 0x0400 before it looks at the skill, Player_Magic.cs:404-408 and 1227-1248 @95ef426d). A cast never fails the run, and it is not counted in
/// <c>gear_steps</c>. It burns components and mana each run (M3 1.4 fix B: a host that judges <c>gear_steps=0</c> on a second run passes <c>cast: false</c>, as the live tests do).</para>
/// <para><b>Mana</b> (M3 1.4 fix B, <see cref="GearCasts.ManaCeiling"/>): before the cast the scenario compares the read model's current mana with the most the cast can cost (ACE's <c>CalculateManaUsage</c>,
/// <c>Creature_Magic.cs</c> 14-60; Mana Conversion only lowers it) and, when short, waits up to <see cref="ManaWaitSeconds"/> for regeneration (a named precondition wait: nothing was cast, so it is no retry),
/// logging the numbers. If the mana never arrives, or the maximum is below the cost, the cast is reported <c>cast=failed</c> with the numbers in a warning (report-only: the run stays <c>gear=wielded</c>). A cast the server
/// ends (UseDone code, GameEvent 0x028A, or a bare UseDone 0) is logged with the code and the chat lines that arrived during it (<see cref="GearCasts.Explain"/>).</para>
/// </remarks>
public sealed class GearScenario : IScenario
{
    /// <summary>The scenario's token (<c>--scenario=gear</c>).</summary>
    public const string ScenarioName = "gear";

    /// <summary>The log category of the scenario's lines (<c>[gear] ...</c>).</summary>
    public const string LogCategory = "gear";

    /// <summary>How long a precondition the read model has not caught up with is waited for, in seconds on the session clock.</summary>
    public const double SettleSeconds = 10;

    /// <summary>The base of the limit on a started kit spawn, in seconds (see <see cref="SpawnLimit"/>).</summary>
    public const double SpawnBaseSeconds = 60;

    /// <summary>How often <see cref="PlayerTools.SpawnGear"/> is asked again while it cannot count the inventory, in seconds on the session clock (each ask recounts the pack and allocates; M3 1.4 fix B, review 1.4-R5).</summary>
    public const double SpawnAskSeconds = 0.5;

    /// <summary>The most the cast waits for the mana it needs to regenerate, in seconds on the session clock (a precondition wait with a bound, not a retry: nothing was cast).</summary>
    public const double ManaWaitSeconds = 120;

    private const string UnsettledReason = "the inventory cannot be counted";
    private const string NotInPackReason = "a kit piece is not in the pack";

    private readonly GearKit _kit;
    private readonly bool _endSession;
    private readonly bool _castEnabled;
    private readonly IReadOnlyDictionary<uint, SpellInfo>? _spells;
    private readonly ILogSink _log;
    private readonly ReasonTimer _wait = new(SettleSeconds);
    private readonly ReasonTimer _spawnWait;
    private readonly List<uint> _scratch = [];
    private readonly List<GearItem> _gear = [];
    private ActionHandle? _pending;
    private CastAttempt? _cast;
    private readonly uint[] _castLevel = new uint[1];
    private GearSpawn? _spawn;
    private ActionHandle? _wield;
    private string? _wielding;
    private int _wieldSteps;
    private int _next;
    private double _arrivalStart = double.NaN;
    private bool _finished;
    private int _worn;
    private int _kitHeld;
    private readonly List<string> _missingKit = [];
    private bool _snapshotted;
    private bool _asked;
    private double _lastAsk;
    private string _lastWhy = string.Empty;
    private double _manaStart = double.NaN;
    private uint _manaNeeded;
    private long _chatMark;

    /// <summary>The gear scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components are those of the profile's spells.</param>
    /// <param name="account">The account name the kit is built for (ACE customises the taper slots from it; it is hashed and never stored, logged or printed).</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log off and quit when done; false: end at <c>done</c> in the world.</param>
    /// <param name="cast">True (the default): cast once after the gear (see the class remarks); false: the cast is off and <c>cast=n/a</c> (a test that casts for itself).</param>
    /// <param name="heroRing">True for the hero mage's gear station only: the kit carries Cassius' Ring of Fire's components (<see cref="GearKit.Build"/>).</param>
    public GearScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, bool cast = true, bool heroRing = false)
        : this(GearKit.Build(spells, account, heroRing), log, arrivalSeconds, endSession, cast, spells)
    {
        Account ??= account; // M6 1 h2: the first cast's level floor reads the account's full formula for a school without its focus
    }

    /// <summary>The gear scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="endSession">True (the headless host's): log off and quit when done. False: stay in the world and end at <c>done</c> (a host that owns the session, and the tests that run the scenario twice on one stand-in).</param>
    /// <param name="cast">True (the default): cast once after the gear (see the class remarks); false: <c>cast=n/a</c>.</param>
    /// <param name="spells">The SpellTable's spells, for the cast's mana check (<see cref="GearCasts.ManaCeiling"/>) and its form (<see cref="GearCasts.FormOf"/>); null: the mana check is skipped, logged as a TODO.</param>
    public GearScenario(GearKit kit, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, bool cast = true, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        _kit = kit;
        _log = log;
        _endSession = endSession;
        _castEnabled = cast;
        _spells = spells;
        ArrivalSeconds = arrivalSeconds;
        SpawnLimit = SpawnBaseSeconds + kit.Items.Count;
        _spawnWait = new ReasonTimer(SpawnLimit);
        foreach (var item in kit.Items)
        {
            // The wield steps see only the wand and the armour: the other lines (components, foci, the stock) are carried, never wielded.
            if (item.Kind is GearKind.Wand or GearKind.Armour)
            {
                _gear.Add(item);
            }
        }
    }

    /// <summary>
    /// Why the player in the world is not one of the two cheat-free mages, BSD Mage and Winnie (its name, or that the name is not known yet), or null when it is: the journey's grant rule (<c>ScriptLeg</c>: a mage that plays cheat-free is never
    /// granted an item it lacks; the check fails, named) and the cast-facts station's character check. M6 item 0 a6: Winnie (D7) joined BSD Mage here, so a missing item on the climber is a failed check, never a grant. The build and gear scenarios'
    /// own check stays BSD Mage's alone (<see cref="WrongBuildCharacter"/>); the headless re-entry (<c>ChooseCharacter</c> here and in the death scenario) is BSD Mage's too, the play host entering Winnie through its own login.
    /// </summary>
    /// <param name="session">The session.</param>
    public static string? WrongCharacter(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var name = session.State.Self.Name;
        return string.Equals(name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase) || CharacterPolicy.For(name).ScenarioMage
            ? null
            : $"the character in the world is '{name ?? "(name not known)"}', not {MageTemplate.CharacterName} or {MageTemplate.ClimberName}: nothing is sent for it";
    }

    /// <summary>Why the player in the world is not BSD Mage (its name, or that the name is not known yet), or null when it is: the check of <see cref="RequireCharacter"/> (the gear scenario grants, so only BSD Mage passes).</summary>
    /// <param name="session">The session.</param>
    public static string? WrongBuildCharacter(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var name = session.State.Self.Name;
        return string.Equals(name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"the character in the world is '{name ?? "(name not known)"}', not {MageTemplate.CharacterName}: nothing is sent for it";
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The limit on the arrival, in seconds.</summary>
    public double ArrivalSeconds { get; }

    /// <summary>
    /// True (a host that keeps one session for several scenarios, M4 P4): a player already in the world must be <see cref="MageTemplate.CharacterName"/>, else the run fails at once and sends nothing (no log off, the session is not this
    /// scenario's). False (the default; the offline stand-in's player has its own name): the player in the world is taken as the mage.
    /// </summary>
    public bool RequireCharacter { get; set; }

    /// <summary>
    /// True (M5 item 3 c1, DoD 6: play grants nothing, the build does): the scenario verifies the kit the pack holds and wields it, and never asks the mod for an item. A worn or wielded kit line (the wand, the armour) or a focus that is not in the
    /// pack fails the run, named: <c>gear: &lt;item&gt; is not in the pack and play grants nothing (M5 DoD 6)</c>; a component line held short of its stack is only named in a warning (a cast burns components, and buying them is the resupply's).
    /// False (the default, the build's own gear stage and the fighters' plans): <see cref="PlayerTools.SpawnGear"/> tops the kit up.
    /// </summary>
    public bool NoGrant { get; init; }

    /// <summary>
    /// M6 1 h2 (closes <c>m6-account-formula-callers</c> for the first-cast check): the account's name as the server stores it (the login name, not the secrets key); the account constructor sets it, a host that builds the scenario over a kit sets it here.
    /// Null: the first cast's level floor applies only to a school whose focus the pack holds (the foci formula). Hashed by <see cref="AccountFormula"/>, never stored in a record, logged or printed.
    /// </summary>
    public string? Account { get; init; }

    /// <summary>
    /// M5 7 D146 (the owner's own exception to DoD 6 and D143, for one test run): grant exactly this many of this one item over the <c>item</c> verb and nothing else (no kit top-up, no wield, no cast),
    /// count the pack before and after, then log off. Null (the default): the kit as above. <c>--gear-grant-only=WCID:COUNT</c> on the headless <c>--scenario=gear</c>.
    /// </summary>
    public (uint Wcid, int Count)? GrantOnly { get; init; }

    private ToolHandle<ItemResult>? _grantOnly;
    private int _grantHeldBefore = -1;

    /// <summary>The limit on a started kit spawn, in seconds: <see cref="SpawnBaseSeconds"/> plus a second a kit line (the lane allows two requests a second).</summary>
    public double SpawnLimit { get; }

    /// <summary>The character entered (its guid), or 0.</summary>
    public uint EnteredGuid { get; private set; }

    /// <summary>The requests this run sent for the wand and the armour, and the wields (<c>gear_steps</c>); the component top-up is <see cref="TopUp"/>.</summary>
    public int GearSteps => GrantsOf(GrantClass.Gear) + _wieldSteps;

    /// <summary>The component requests this run sent (<c>topup</c>): the kit's component lines the pack was short of, e.g. after a cast burned some.</summary>
    public int TopUp => GrantsOf(GrantClass.Component);

    private enum GrantClass
    {
        Gear,
        Component,
    }

    private int GrantsOf(GrantClass wanted)
    {
        var n = 0;
        if (_spawn is { } spawn)
        {
            var grants = spawn.Grants;
            for (var i = 0; i < grants.Count; i++)
            {
                var kind = grants[i].Item.Kind;
                var of = kind == GearKind.Component ? GrantClass.Component : GrantClass.Gear;
                if (of == wanted)
                {
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>The wields sent, in order, as logged (the tests').</summary>
    public IReadOnlyList<string> SentWields => _sentWields;

    /// <summary>The kit lines not held at the last count (name, weenie, held of wanted), for a failure message; empty when the kit is whole.</summary>
    public IReadOnlyList<string> MissingKit => _missingKit;

    private readonly List<string> _sentWields = [];

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The verdict fields, <c>gear=... worn=... kit=... gear_steps=... topup=... cast=...</c> (no name, no account; <c>topup</c> and <c>cast</c> are report-only).</summary>
    public string VerdictFields()
    {
        var bare = _finished && Failure is null && _gear.Count == 0; // M6 0 lead (live probe 7): the bare kit wields nothing, and says so
        var wielded = _finished && Failure is null && _snapshotted && _worn == _gear.Count - 1 && _wandWielded;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"gear={(bare ? "bare" : wielded ? "wielded" : "failed")} worn={_worn} kit={_kitHeld}/{_kit.Items.Count} gear_steps={GearSteps} topup={TopUp} cast={Cast}");
    }

    /// <summary>The cast field of the verdict: <c>ok</c>, <c>fizzled</c>, <c>failed</c> or <c>n/a</c> (see the class remarks).</summary>
    public string Cast { get; private set; } = "n/a";

    /// <summary>The spell the cast chose, as logged, or null when none was sent (the tests').</summary>
    public string? CastSpell { get; private set; }

    /// <summary>The spell id the cast sent, or 0.</summary>
    public uint CastSpellId { get; private set; }

    private bool _wandWielded;
    private bool _failLogOff;

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Failure is not null)
        {
            // A failed run in the world logs off first (see Fail), so ACE does not keep the character in the world for the next run.
            if (_failLogOff && Settled(out var left))
            {
                _failLogOff = false;
                _log.Info(LogCategory, "the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            }

            return _failLogOff ? ScenarioStatus.Running : ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var actions = session.Actions;
        switch (Stage)
        {
            case "start":
                if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
                {
                    if (RequireCharacter && WrongBuildCharacter(session) is { } wrong)
                    {
                        // Nothing was sent: no grant, no wield, no log off (the shared session belongs to the host).
                        Failure = wrong;
                        _log.Error(LogCategory, Failure);
                        _log.Info(LogCategory, "verdict " + VerdictFields());
                        return Go("failed", ScenarioStatus.Failed);
                    }

                    // Already in the world (a host that logged in itself, or the offline stand-in): nothing to log in.
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
                    _wait.Reset();
                    return Go("spawning");
                }

                if (session.Clock.Now - _arrivalStart > ArrivalSeconds)
                {
                    return Fail(
                        session,
                        string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {ArrivalSeconds:0.#} s (travel {session.State.Self.Travel})"));
                }

                break;

            case "spawning":
                return Spawn(session);

            case "wielding":
                return Wield(session);

            case "cast-mana":
                return ManaWait(session);

            case "cast-mode":
            case "casting":
            case "cast-peace":
                return CastStep(session);

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
                return Fail(session, $"TODO(state): unknown gear stage '{Stage}'");
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ character

    private ScenarioStatus Choose(IGameSessionView session)
    {
        foreach (var c in session.State.Characters.Entries)
        {
            if (string.Equals(c.Name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                if (c.PendingDeletion)
                {
                    return Fail(session, "choosing: the mage is pending deletion (restore it first)");
                }

                EnteredGuid = c.Guid;
                _log.Info(LogCategory, "entering the existing character " + MageTemplate.CharacterName);
                _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
                return Go("entering");
            }
        }

        return Fail(session, "choosing: " + MageTemplate.CharacterName + " is not in the character list (run --scenario=mage-build first; the gear scenario creates nobody)");
    }

    // ------------------------------------------------------------------------------------------------ spawning

    private ScenarioStatus Spawn(IGameSessionView session)
    {
        if (GrantOnly is { } only)
        {
            return SpawnOnly(session, only.Wcid, only.Count);
        }

        if (NoGrant)
        {
            return VerifyHeld(session);
        }

        if (_spawn is null)
        {
            var now = session.Clock.Now;
            if (_asked && now - _lastAsk < SpawnAskSeconds)
            {
                // Between asks nothing is recounted or built (M3 1.4 fix B, 1.4-R5): only the bounded wait is checked.
                return _wait.Expired(now, UnsettledReason) ? Fail(session, $"spawning: still not started after {SettleSeconds:0.#} s: {_lastWhy}") : ScenarioStatus.Running;
            }

            _asked = true;
            _lastAsk = now;
            var spawn = session.Tools.SpawnGear(_kit, ActionSource.Host);
            if (spawn.NotStarted is { } why)
            {
                // Nothing was requested; the inventory just cannot be counted yet. Ask again after it settles (at most every SpawnAskSeconds).
                _lastWhy = why;
                return _wait.Expired(now, UnsettledReason) ? Fail(session, $"spawning: still not started after {SettleSeconds:0.#} s: {why}") : ScenarioStatus.Running;
            }

            _spawn = spawn;
            _spawnWait.Reset();
            _log.Info(LogCategory, "spawning the kit: " + spawn.Describe());
            return ScenarioStatus.Running;
        }

        if (!_spawn.IsDone)
        {
            return _spawnWait.Expired(session.Clock.Now, "the kit's requests")
                ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"spawning: the kit was not complete within {SpawnLimit:0.#} s: {_spawn.Describe()}"))
                : ScenarioStatus.Running;
        }

        if (!_spawn.Succeeded)
        {
            return Fail(session, "spawning: " + _spawn.Describe());
        }

        _log.Info(LogCategory, "kit in the pack: " + _spawn.Describe());
        _wait.Reset();
        return Go("wielding");
    }

    /// <summary>The one-item grant (<see cref="GrantOnly"/>, D146): counts the item once the pack can be counted, sends one <c>item</c> request, waits for it and for the pack's new count, then concludes.</summary>
    private ScenarioStatus SpawnOnly(IGameSessionView session, uint wcid, int count)
    {
        var now = session.Clock.Now;
        var state = session.State;
        int? have = null;
        if (state.Self.Inventory.IsKnown)
        {
            var held = GearInventory.CountAll(state, out var unsettled);
            if (unsettled == 0)
            {
                have = held.GetValueOrDefault(wcid);
            }
        }

        if (_grantOnly is null)
        {
            if (have is not { } before)
            {
                return _wait.Expired(now, UnsettledReason) ? Fail(session, $"grant-only: the pack was not countable within {SettleSeconds:0.#} s") : ScenarioStatus.Running;
            }

            _grantHeldBefore = before;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"grant-only (D146, the owner's exception to M5 DoD 6 for one test run): weenie {wcid} held {before}: granting {count} over the item verb, nothing else"));
            _grantOnly = session.Tools.SpawnItem(wcid, count, ActionSource.Host);
            _spawnWait.Reset();
            return ScenarioStatus.Running;
        }

        if (!_grantOnly.IsCompleted)
        {
            return _spawnWait.Expired(now, "the grant") ? Fail(session, $"grant-only: the item request did not end within {SpawnLimit:0.#} s") : ScenarioStatus.Running;
        }

        if (_grantOnly.Result is null)
        {
            return Fail(session, "grant-only: the item request failed: " + _grantOnly.Outcome);
        }

        if (have is not { } after || after < _grantHeldBefore + count)
        {
            return _spawnWait.Expired(now, "the granted stack's arrival") ? Fail(session, $"grant-only: the pack shows {have?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} of weenie {wcid}, not {_grantHeldBefore + count}") : ScenarioStatus.Running;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"grant-only (D146): weenie {wcid} granted {count}: held {_grantHeldBefore} before, {after} now (read from the pack)"));
        _finished = true;
        return Conclude(session);
    }

    /// <summary>The no-grant mode's spawning stage (<see cref="NoGrant"/>): counts what the pack holds once it can be counted, names what is missing, sends nothing.</summary>
    private ScenarioStatus VerifyHeld(IGameSessionView session)
    {
        var now = session.Clock.Now;
        if (_asked && now - _lastAsk < SpawnAskSeconds)
        {
            return _wait.Expired(now, UnsettledReason) ? Fail(session, $"verifying: the pack was not countable within {SettleSeconds:0.#} s: {_lastWhy}") : ScenarioStatus.Running;
        }

        _asked = true;
        _lastAsk = now;
        var state = session.State;
        Dictionary<uint, int>? held = null;
        if (!state.Self.Inventory.IsKnown)
        {
            _lastWhy = "inventory-unknown: no PlayerDescription has told the pack yet";
        }
        else
        {
            held = GearInventory.CountAll(state, out var unsettled);
            if (unsettled > 0)
            {
                held = null;
                _lastWhy = "inventory-unsettled: " + unsettled.ToString(CultureInfo.InvariantCulture) + " held items have no world object yet";
            }
        }

        if (held is null)
        {
            return _wait.Expired(now, UnsettledReason) ? Fail(session, $"verifying: the pack was not countable within {SettleSeconds:0.#} s: {_lastWhy}") : ScenarioStatus.Running;
        }

        string? missing = null;
        foreach (var item in _kit.Items)
        {
            var have = held.GetValueOrDefault(item.Wcid);
            if (item.Counted)
            {
                if (have < item.Stack)
                {
                    _log.Warn(LogCategory, $"gear: {item.Name} (weenie {item.Wcid.ToString(CultureInfo.InvariantCulture)}) is held {have.ToString(CultureInfo.InvariantCulture)} of {item.Stack.ToString(CultureInfo.InvariantCulture)} and play grants nothing (M5 DoD 6): the resupply buys it");
                }
            }
            else if (have == 0)
            {
                missing ??= $"gear: {item.Name} is not in the pack and play grants nothing (M5 DoD 6)";
                _log.Error(LogCategory, $"gear: {item.Name} (weenie {item.Wcid.ToString(CultureInfo.InvariantCulture)}) is not in the pack and play grants nothing (M5 DoD 6)");
            }
        }

        if (missing is not null)
        {
            return Fail(session, missing);
        }

        _log.Info(LogCategory, "kit verified in the pack (no grant, M5 DoD 6)");
        _wait.Reset();
        return Go("wielding");
    }

    // ------------------------------------------------------------------------------------------------ wielding

    private ScenarioStatus Wield(IGameSessionView session)
    {
        if (_wield is { } handle)
        {
            if (handle.Outcome is not { } outcome)
            {
                return ScenarioStatus.Running;
            }

            _wield = null;
            if (outcome.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, _wielding ?? "wielding", outcome);
            }

            _log.Info(LogCategory, (_wielding ?? "wield") + ": confirmed");
        }

        var inventory = session.State.Self.Inventory;
        while (_next < _gear.Count)
        {
            var item = _gear[_next];
            var equipped = false;
            uint carried = 0;
            GearInventory.Collect(session.State, _scratch);
            for (var i = 0; i < _scratch.Count; i++)
            {
                var guid = _scratch[i];
                if (!session.State.Objects.TryGet(guid, out var o) || o.Weenie.WeenieClassId != item.Wcid)
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

            if (!equipped && PickedCaster(session.State, item) is var picked and not 0)
            {
                equipped = true;
                if (!_pickNoted)
                {
                    _pickNoted = true;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"the held caster 0x{picked:X8} is not the kit's {item.Name} but the self-equip's pick: it keeps the hands (no grant, M5 7 D139 m)"));
                }
            }

            if (equipped)
            {
                _next++;
                continue;
            }

            // M3 4.2 P7: the mage wears exactly the kit's armour and holds the kit's caster, so a worn piece that is not a kit line and covers the slots of this one (today's leather, the old wand) goes to the pack first
            // (Event_PutItemInContainer 0x006ACC80 through IActionApi.Unwield, as the retail pack screen's drag does); the loop comes back to this item once the model shows the pack move.
            var blocker = BlockingPiece(session, item);
            if (blocker != 0)
            {
                _wait.Reset();
                _wielding = "take off a piece that is not in the kit, which holds the place of the " + item.Name;
                _log.Info(LogCategory, _wielding + " (object 0x" + blocker.ToString("X8", CultureInfo.InvariantCulture) + ")");
                _sentWields.Add(_wielding);
                _wieldSteps++;
                _wield = session.Actions.Unwield(blocker, ActionSource.Host);
                return ScenarioStatus.Running;
            }

            if (carried == 0)
            {
                return _wait.Expired(session.Clock.Now, NotInPackReason) // a constant reason: the text is built only on the failure (1.4-R5)
                    ? Fail(session, $"wielding: the {item.Name} (weenie {item.Wcid.ToString(CultureInfo.InvariantCulture)}) is not in the inventory model {SettleSeconds:0.#} s after the kit arrived")
                    : ScenarioStatus.Running;
            }

            _wait.Reset();
            _wielding = (item.Kind == GearKind.Wand ? "wield " : "wear the ") + item.Name + " at 0x" + ((uint)item.ValidLocations).ToString("X", CultureInfo.InvariantCulture);
            _log.Info(LogCategory, _wielding);
            _sentWields.Add(_wielding);
            _wieldSteps++;
            _next++;
            _wield = session.Actions.Wield(carried, (EquipMask)WieldSlotChoice.Choose(inventory, (uint)item.ValidLocations), ActionSource.Host);
            return ScenarioStatus.Running;
        }

        // Everything is sent and confirmed: the read model must show the whole set.
        Snapshot(session);
        if (_gear.Count == 0)
        {
            // M6 0 lead (live probe 4): the bare kit (GearKit.Bare: an outdoor ground with no vendor kit, the climber's) lists nothing to wield or wear; the caster in the hands is the
            // autopilot audit's wield-caster fix, not this stage's. Nothing is judged short.
            _log.Info(LogCategory, "the kit is bare (the ground lists no vendor kit): nothing to wield or wear here; the hands' caster is the audit's");
        }
        else if (!_wandWielded || _worn != _gear.Count - 1)
        {
            return Fail(session, $"the wield ended short: the read model shows the wand {(_wandWielded ? "wielded" : "not wielded")} and {_worn} of {_gear.Count - 1} pieces worn");
        }

        _finished = true;
        return _castEnabled ? BeginCast(session) : Conclude(session);
    }

    /// <summary>Ends the run: the verdict and done in the world, or the log off.</summary>
    private ScenarioStatus Conclude(IGameSessionView session)
    {
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            return Go("done", ScenarioStatus.Succeeded);
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    // ------------------------------------------------------------------------------------------------ the first cast (report-only)

    private ScenarioStatus BeginCast(IGameSessionView session)
    {
        // M5 3 F6: the check cast is one the pack can pay for. A level whose foci-formula components the pack lacks is passed over (the ground's kit carries the scarabs of the plan's levels only; ACE refuses the others 0x0400 before it
        // looks at the skill, Player_Magic.cs:404-408 and 1227-1248 @95ef426d), named here, and the level below it that the pack can pay for is the check. Without a SpellTable the formulas are unknown: no floor, a named TODO.
        var floor = _spells is null ? null : new PackFloor(session.State, _spells, Account);
        var pick = GearCasts.Pick(session.State.Self.Qualities, ProfileRole.CreatureBuff, out var why, floor);
        if (floor is not null)
        {
            foreach (var passedOver in floor.Skipped)
            {
                _log.Info(LogCategory, "cast: " + passedOver);
            }
        }

        if (pick is null)
        {
            _log.Info(LogCategory, "cast skipped (cast=n/a): " + why);
            return Conclude(session);
        }

        SpellInfo? info = null;
        if (_spells is not null && !_spells.TryGetValue(pick.Spell.Id, out info))
        {
            _log.Info(LogCategory, "cast skipped (cast=n/a): spell " + pick.Spell.Id.ToString(CultureInfo.InvariantCulture) + " is not in the SpellTable given to the scenario");
            return Conclude(session);
        }

        if (info is not null && GearCasts.FormOf(info) != CastForm.Self)
        {
            // The gear scenario casts one Creature Enchantment self buff; a spell that targets an item is cast by the caller that has the item (CastLiveTests).
            _log.Info(LogCategory, "cast skipped (cast=n/a): " + pick.Spell.Name + " targets an item (NonComponentTargetType 0x" + info.NonComponentTargetType.ToString("X", CultureInfo.InvariantCulture) + "), not the caster");
            return Conclude(session);
        }

        CastSpell = pick.Describe();
        CastSpellId = pick.Spell.Id;
        _manaStart = double.NaN;
        if (info is null)
        {
            _manaNeeded = 0;
            _log.Warn(LogCategory, "TODO(m3-cast-mana-unknown): no SpellTable was given to the scenario, so the cast's mana is not checked before it and no level is passed over for the pack's components (M5 3 F6)");
        }
        else
        {
            _manaNeeded = GearCasts.ManaCeiling(info, session.State.Self.Inventory.Equipped.Count);
        }

        _log.Info(LogCategory, "cast: " + CastSpell + ", at most " + _manaNeeded.ToString(CultureInfo.InvariantCulture) + " mana (the magic combat mode first)");
        return Go("cast-mana");
    }

    /// <summary>The mana precondition (M3 1.4 fix B): wait, bounded, for the regeneration that brings the current mana to the cast's ceiling. Nothing has been cast, so waiting is no retry.</summary>
    private ScenarioStatus ManaWait(IGameSessionView session)
    {
        var now = session.Clock.Now;
        if (_manaNeeded == 0)
        {
            // No SpellTable was given (logged as TODO(m3-cast-mana-unknown)): nothing to compare.
            _chatMark = session.State.Chat.Total;
            _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            return Go("cast-mode");
        }

        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max))
        {
            Cast = "failed";
            _log.Warn(LogCategory, "cast not sent (cast=failed): the read model has no mana vital");
            return Conclude(session);
        }

        if (mana >= _manaNeeded)
        {
            if (!double.IsNaN(_manaStart))
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"cast: mana {mana} reached the {_manaNeeded} needed after {now - _manaStart:0.#} s"));
            }

            _chatMark = session.State.Chat.Total;
            _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            return Go("cast-mode");
        }

        if (max < _manaNeeded)
        {
            Cast = "failed";
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"cast not sent (cast=failed): {CastSpell} costs up to {_manaNeeded} mana and the maximum is {max} (current {mana})"));
            return Conclude(session);
        }

        if (double.IsNaN(_manaStart))
        {
            _manaStart = now;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"cast: waiting for mana to regenerate: {mana} of {max}, up to {_manaNeeded} needed, at most {ManaWaitSeconds:0.#} s"));
        }

        if (now - _manaStart > ManaWaitSeconds)
        {
            Cast = "failed";
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"cast not sent (cast=failed): the mana did not reach {_manaNeeded} within {ManaWaitSeconds:0.#} s: {mana} of {max}"));
            return Conclude(session);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus CastStep(IGameSessionView session)
    {
        if (Stage == "casting")
        {
            return CastingStep(session);
        }

        if (!Settled(out var outcome))
        {
            return ScenarioStatus.Running;
        }

        switch (Stage)
        {
            case "cast-mode":
                if (outcome.Kind is ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local)
                {
                    // M5 1 K3: the check cast goes through the cast policy as Automation (D13), Once: "a check never retries silently".
                    _chatMark = session.State.Chat.Total;
                    _castLevel[0] = CastSpellId;
                    _cast ??= new CastAttempt(session.Actions, session.State, _log, _spells);
                    _cast.Start(new CastRequest { Levels = _castLevel, Rules = CastRules.Once, Purpose = "gear check" }, session.Clock.Now);
                    return Go("casting");
                }

                Cast = "failed";
                _log.Warn(LogCategory, "cast: the magic combat mode was not entered: " + DescribeOutcome(outcome));
                return Conclude(session);

            default:
                if (outcome.Kind is not (ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local))
                {
                    _log.Warn(LogCategory, "cast: the player was left in the magic combat mode: " + DescribeOutcome(outcome));
                }

                return Conclude(session);
        }
    }

    /// <summary>The check cast's end: the attempt's answer is the old single cast's (ok when the projectile or enchantment is confirmed, fizzled on 0x0402, failed otherwise), then the combat mode back to NonCombat.</summary>
    private ScenarioStatus CastingStep(IGameSessionView session)
    {
        if (_cast!.Step(session.Clock.Now) == CastEnd.Running)
        {
            return ScenarioStatus.Running;
        }

        var report = _cast.Report;
        var outcome = _cast.LastOutcome;
        Cast = outcome is { Kind: ActionOutcomeKind.Confirmed } ? "ok" : report.End == CastEnd.Fizzled ? "fizzled" : "failed";
        _log.Info(
            LogCategory,
            "cast " + CastSpell + ": " + Cast + " (" + (outcome is not { } o ? report.End + ": " + report.Why : Cast == "failed" ? GearCasts.Explain(o, session.State, _chatMark, _manaNeeded) : DescribeOutcome(o)) + ")");
        _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        return Go("cast-peace");
    }

    private static string DescribeOutcome(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" code 0x{outcome.Code:X}")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

    /// <summary>
    /// The guid of a worn object that is not a line of the kit and occupies a location <paramref name="item"/> would take (0 when none): what the swap takes off first. No allocation: loops over the model's lists.
    /// </summary>
    private uint BlockingPiece(IGameSessionView session, GearItem item)
    {
        var worn = session.State.Self.Inventory.Equipped;
        for (var i = 0; i < worn.Count; i++)
        {
            var e = worn[i];
            if (e.Location == 0 || (e.Location & (uint)item.ValidLocations) == 0 || !session.State.Objects.TryGet(e.Guid, out var o) || InKit(o.Weenie.WeenieClassId))
            {
                continue;
            }

            return e.Guid;
        }

        return 0;
    }

    /// <summary>
    /// M5 7 D139 (m): under <see cref="NoGrant"/> (M5's play), the caster the player holds when it is not the kit's wand line: the self-equip's pick (<c>SelfEquipJudge.BestCaster</c>), which keeps the hands; 0 otherwise. Round 13
    /// (2026-10-30 03:01:33 UTC): the start-up's gear stage took the picked Fire Sceptre off for the kit's Wand ("take off a piece that is not in the kit"), left it unwielded in the main pack, and the trip's pick then swapped it back in
    /// over a full kit pack. The build's own gear stage (grants on) still wields exactly the kit. Allocation-free.
    /// </summary>
    private uint PickedCaster(GameState state, GearItem item)
    {
        if (!NoGrant || item.Kind != GearKind.Wand || state.Self.Inventory.EquippedAt((uint)EquipMask.Held) is not { } held)
        {
            return 0;
        }

        return state.Objects.TryGet(held, out var o) && (o.ItemType & CombatModeGuard.ItemTypeCaster) != 0 ? held : 0;
    }

    private bool _pickNoted;

    private bool InKit(uint wcid)
    {
        for (var i = 0; i < _kit.Items.Count; i++)
        {
            if (_kit.Items[i].Wcid == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="guid"/> is equipped at a location within the item's <see cref="GearItem.ValidLocations"/> (the 0x0023 names it; spec 5.4).</summary>
    private static bool IsWornWithin(PlayerInventory inventory, uint guid, GearItem item) =>
        inventory.TryGetEquipped(guid, out var e) && e.Location != 0 && (e.Location & ~(uint)item.ValidLocations) == 0;

    private void Snapshot(IGameSessionView session)
    {
        var state = session.State;
        var inventory = state.Self.Inventory;
        _worn = 0;
        _wandWielded = false;
        GearInventory.Collect(state, _scratch);
        foreach (var item in _gear)
        {
            var at = false;
            for (var i = 0; i < _scratch.Count && !at; i++)
            {
                at = state.Objects.TryGet(_scratch[i], out var o) && o.Weenie.WeenieClassId == item.Wcid && IsWornWithin(inventory, _scratch[i], item);
            }

            if (!at && PickedCaster(state, item) == 0)
            {
                continue;
            }

            if (item.Kind == GearKind.Wand)
            {
                _wandWielded = true;
            }
            else
            {
                _worn++;
            }
        }

        var counts = GearInventory.CountAll(state, out _);
        _kitHeld = 0;
        _missingKit.Clear();
        foreach (var item in _kit.Items)
        {
            if (counts.GetValueOrDefault(item.Wcid) >= (item.Counted ? item.Stack : 1))
            {
                _kitHeld++;
            }
            else
            {
                _missingKit.Add(string.Create(CultureInfo.InvariantCulture, $"{item.Name} (weenie {item.Wcid}, held {counts.GetValueOrDefault(item.Wcid)} of {(item.Counted ? item.Stack : 1)})"));
            }
        }

        _snapshotted = true;
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
        if (session.State.Self.Inventory.IsKnown)
        {
            Snapshot(session);
        }

        Failure = why;
        _log.Error(LogCategory, why);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        if (_endSession && session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // A run that fails in the world must still leave it: ACE keeps a character whose client simply disconnected in the world for a while, and the next login is refused as CharacterInWorld
            // (the live gate: CastLiveTests could not enter 3 s after a failed GearLiveTests). Send the log off a successful run sends (Conclude) and wait for ACE's answer (the tracker's own limit is
            // ActionPolicy.LogOffSeconds) before reporting the failure; Failure is already set, so the verdict above stands whatever the log off does.
            _log.Info(LogCategory, "logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _failLogOff = true;
            return ScenarioStatus.Running;
        }

        return ScenarioStatus.Failed;
    }
}

/// <summary>How a spell is cast (<see cref="GearCasts.FormOf"/>): on the caster, or at an item.</summary>
public enum CastForm
{
    /// <summary>A self cast (GameAction 0x0048); the proof is the player's own enchantment (GameEvent 0x02C2).</summary>
    Self,

    /// <summary>A cast at an item (GameAction 0x004A); ACE sends no 0x02C2 to the player, so the proof is the UseDone and ACE's chat line.</summary>
    AtItem,
}

/// <summary>
/// One spell the first casts chose: its profile line and level, the character's current skill in the line's school and ACE's chance to pass the pre-check at that skill.
/// </summary>
/// <param name="Line">The profile line.</param>
/// <param name="Spell">The level of the line.</param>
/// <param name="Skill">The current skill the choice was made from.</param>
/// <param name="Chance">ACE's chance (<see cref="GearCasts.Chance"/>).</param>
public sealed record CastPick(ProfileLine Line, ProfileSpell Spell, uint Skill, double Chance)
{
    /// <summary>The choice as one log phrase (the spell's own name and id, its power, the skill and the chance).</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Spell.Name} ({Spell.Id}, power {Spell.Power}) from {Line.School} skill {Skill}, chance {Chance:0.00000}");
}

/// <summary>
/// The first casts' arithmetic (M3 1.4 s2): the character's current magic skill from the read model, ACE's cast pre-check and the choice of a spell that passes it (plan decision D10's rule at the
/// threshold <see cref="MinChance"/>). Pure functions of <see cref="PlayerQualities"/> and <see cref="ArenaProfile"/>.
/// </summary>
/// <remarks>
/// <para><b>The pre-check</b> is ACE's <c>Player.GetCastingPreCheckStatus</c> (<c>Player_Magic.cs</c> 528-545): the cast fails outright when the skill is 0 or below the spell's <c>Power</c> minus 50, else it passes
/// with the chance <c>SkillCheck.GetMagicSkillChance</c> (<c>SkillCheck.cs</c> 7-22): 1 - 1 / (1 + e^(0.07 (skill - power))), the exponent in single precision as ACE computes it.</para>
/// <para><b>The current skill</b> is ACE's <c>CreatureSkill.Current</c> (<c>Entity/CreatureSkill.cs</c> 154-195) for the four magic skills: the skill's formula, (Focus + Self) / 4 rounded half away from zero
/// (<c>MageTemplate.MagicSkillDivisor</c>; the SkillTable pins it, <c>MageTemplateDatTests</c>), over the attributes <i>with their enchantments</i>, plus the skill's start level (<c>SkillDesc.Init</c>, ACE's
/// <c>InitLevel</c>) and ranks, scaled by the multiplicative skill enchantments and the vitae, plus the additive ones, floored at 0. Each enchantment list keeps the strongest enchantment of one spell category.
/// The augmentation bonuses (<c>GetAugBonus_Base</c> and <c>GetAugBonus_Current</c>, <c>CreatureSkill.cs</c> 169 and 184) are <see cref="SkillRules.AugBonusBase"/> and <see cref="SkillRules.AugBonusCurrent"/> (the BSD mage has none). The arithmetic is <see cref="SkillRules"/> (M3 2.2 a2), which also applies the attack and defence skill groups. A skill below Trained is not usable (the formula does not
/// apply, ACE's <c>IsUsable</c>): no value.</para>
/// </remarks>
public static class GearCasts
{
    /// <summary>The cast chance the first casts demand (no retry: a check never retries silently).</summary>
    public const double MinChance = 0.999;

    /// <summary>The weenie error of a fizzle (0x0402): <see cref="ActionOutcomeKind.ServerError"/>'s code.</summary>
    public const uint FizzleCode = Actions.Trackers.CastTracker.FizzleCode;

    /// <summary>How far below a spell's power the skill may be before ACE fails the cast outright (<c>Player_Magic.cs</c> 534).</summary>
    public const int FizzleMargin = 50;

    /// <summary>The factor of <c>SkillCheck.GetMagicSkillChance</c> (<c>SkillCheck.cs</c> 20).</summary>
    public const float MagicChanceFactor = 0.07f;

    /// <summary>The mana a cast that fizzles at the pre-check costs (ACE <c>Player.CalculateManaUsage</c>, <c>Player_Magic.cs</c> 566-572: <c>CastFailed</c> uses 5, "todo: verify with retail").</summary>
    public const uint FizzleMana = 5;

    /// <summary>
    /// How a spell is cast, from the SpellTable (M3 1.4 fix B). A spell with <see cref="SpellFlags.SelfTargeted"/> or a <c>NonComponentTargetType</c> that is no item is a self cast (GameAction 0x0048): ACE
    /// refuses a SelfTargeted spell at any other target (<c>Player.IsInvalidTarget</c>, <c>Player_Magic.cs</c> 435-437) and puts a self-targeted enchantment on the caster (<c>WorldObject.HandleCastSpell</c>,
    /// <c>WorldObject_Magic.cs</c> 261). The end-of-retail "Aura" spells of the wand lines (Hermetic Link, Defender) are such spells: they land as the player's own enchantment (GameEvent 0x02C2). A spell whose
    /// <c>NonComponentTargetType</c> names items (<c>ItemType</c> Vestements, Weapon, Caster, WeaponOrCaster or Item, ACE <c>VerifyNonComponentTargetType</c>, <c>Player_Magic.cs</c> 1379-1425) and that is not self-targeted
    /// is cast at an item (GameAction 0x004A).
    /// </summary>
    /// <param name="info">The spell.</param>
    public static CastForm FormOf(SpellInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if ((info.Flags & SpellFlags.SelfTargeted) != 0)
        {
            return CastForm.Self;
        }

        const uint vestements = 0x6; // Armor | Clothing
        const uint weapon = 0x101; // MeleeWeapon | MissileWeapon
        const uint caster = 0x8000;
        const uint weaponOrCaster = weapon | caster;
        const uint item = 0x1 | 0x2 | 0x4 | 0x8 | 0x20 | 0x40 | 0x80 | 0x100 | 0x200 | 0x800 | 0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x10000 | 0x40000 | 0x80000 | 0x200000; // ItemType.Item, ACE ItemType.cs
        return info.NonComponentTargetType is vestements or weapon or caster or weaponOrCaster or item ? CastForm.AtItem : CastForm.Self;
    }

    /// <summary>
    /// The most mana a cast of <paramref name="info"/> can cost the caster: ACE's <c>Creature.CalculateManaUsage</c> (<c>Creature_Magic.cs</c> 14-60) starts from the spell's base mana (plus, for an armour-category
    /// Item Enchantment on a player, the spell's <c>ManaMod</c> for each worn item) and Mana Conversion (<c>GetManaCost</c>, lines 62-92) only ever lowers it, to 1 at least. So this is the amount the caster needs
    /// to be sure of casting, not the amount it will pay.
    /// </summary>
    /// <param name="info">The spell.</param>
    /// <param name="wornItems">How many items the caster has equipped (an upper bound of ACE's clothing and shield count).</param>
    public static uint ManaCeiling(SpellInfo info, int wornItems)
    {
        ArgumentNullException.ThrowIfNull(info);
        var cost = info.BaseMana;
        if (info.School == MagicSchool.ItemEnchantment && info.MetaSpellType == SpellType.Enchantment && info.Category is >= 160 and <= 189)
        {
            cost += info.ManaMod * (uint)Math.Max(1, wornItems); // SpellCategory ArmorValueRaising (160) to AcidicResistanceLowering (189)
        }

        return Math.Max(cost, 1);
    }

    /// <summary>
    /// The chance that one cast of <paramref name="info"/> burns no component: ACE's <c>Spell.TryBurnComponents</c> (<c>Entity/Spell.cs</c> 157-189) rolls every slot of the caster's formula against
    /// <c>ComponentLoss x CDM x min(1, Power / skill)</c>, so the chance is the product of one minus that rate over the slots. The formula is the account's (the taper slots are customised).
    /// </summary>
    /// <param name="info">The spell.</param>
    /// <param name="account">The account name the formula is customised for (never stored or printed).</param>
    /// <param name="skill">The caster's current skill in the spell's school when it cast.</param>
    /// <param name="foci">True (M5 3 F6): the pack held the school's focus item, so ACE rolled the burn over the foci formula's slots (<see cref="GearKit.FociFormulaCounts"/>: the scarab, chorizite and the Prismatic Tapers), not the account's customised formula, which the ground's kit never casts.</param>
    public static double NoBurnProbability(SpellInfo info, string account, uint skill, bool foci = false)
    {
        ArgumentNullException.ThrowIfNull(info);
        var skillMod = skill == 0 ? 1.0 : Math.Min(1.0, info.Power / (double)skill);
        var none = 1.0;
        if (foci)
        {
            foreach (var (id, slots) in GearKit.FociFormulaCounts(info.Formula))
            {
                if (ComponentWeenies.TryGetByComponent(id, out var fociWeenie))
                {
                    none *= Math.Pow(1.0 - Math.Min(1.0, info.ComponentLoss * (double)fociWeenie.Cdm * skillMod), slots);
                }
            }

            return none;
        }

        foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account))
        {
            if (id == 0 || !ComponentWeenies.TryGetByComponent(id, out var weenie))
            {
                continue;
            }

            none *= 1.0 - Math.Min(1.0, info.ComponentLoss * (double)weenie.Cdm * skillMod);
        }

        return none;
    }

    /// <summary>
    /// What the server said during a cast that did not do its work, in one string (M3 1.4 fix B): the outcome's kind and code (the UseDone code or the GameEvent 0x028A weenie error, named when it is a known one:
    /// 0x0400 components, 0x0401 mana, 0x0402 fizzle), the outcome's own words, and the chat lines that arrived since <paramref name="chatMark"/>.
    /// </summary>
    /// <param name="outcome">The cast's outcome.</param>
    /// <param name="state">The game state (the chat log and the current mana).</param>
    /// <param name="chatMark">The chat log's <c>Total</c> when the cast was sent.</param>
    /// <param name="manaNeeded">The mana the cast was expected to cost at most (named with a 0x0401).</param>
    public static string Explain(in ActionOutcome outcome, GameState state, long chatMark, uint manaNeeded)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = new StringBuilder();
        text.Append(outcome.Kind);
        if (outcome.Code != 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" code 0x{outcome.Code:X4}");
            switch (outcome.Code)
            {
                case Actions.Trackers.CastTracker.ComponentsCode:
                    text.Append(" (UseDone: the player lacks a component)");
                    break;
                case Actions.Trackers.CastTracker.ManaCode:
                    state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max);
                    text.Append(CultureInfo.InvariantCulture, $" (NOT ENOUGH MANA: {mana} of {max}, up to {manaNeeded} needed)");
                    break;
                case FizzleCode:
                    text.Append(" (GameEvent 0x028A: the spell fizzled)");
                    break;
            }
        }

        if (outcome.Detail is { } detail)
        {
            text.Append(": ").Append(detail);
        }

        var fresh = (int)Math.Min(state.Chat.Count, state.Chat.Total - chatMark);
        if (fresh <= 0)
        {
            text.Append("; no chat line arrived during the cast");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"; the {fresh} chat lines during the cast:");
            for (var i = state.Chat.Count - fresh; i < state.Chat.Count; i++)
            {
                text.Append(" [").Append(state.Chat[i].Text).Append(']');
            }
        }

        return text.ToString();
    }

    /// <summary>The skill id of a school (Creature Enchantment 31, Item Enchantment 32, Life Magic 33, War Magic 34), or 0 for a school the mage does not cast.</summary>
    /// <param name="school">The school.</param>
    public static uint SkillOf(MagicSchool school) => school switch
    {
        MagicSchool.CreatureEnchantment => MageTemplate.CreatureEnchantment,
        MagicSchool.ItemEnchantment => MageTemplate.ItemEnchantment,
        MagicSchool.LifeMagic => MageTemplate.LifeMagic,
        MagicSchool.WarMagic => MageTemplate.WarMagic,
        _ => 0,
    };

    /// <summary>ACE's chance to pass the cast pre-check: 0 when the skill is 0 or more than <see cref="FizzleMargin"/> below the power, else the logistic of <c>GetMagicSkillChance</c>.</summary>
    /// <param name="skill">The current skill.</param>
    /// <param name="power">The spell's <c>Power</c>.</param>
    public static double Chance(uint skill, uint power)
    {
        if (skill == 0 || (long)skill < (long)power - FizzleMargin)
        {
            return 0.0;
        }

        var exponent = MagicChanceFactor * ((int)skill - (int)power); // float times int, as ACE's float factor
        return Math.Clamp(1.0 - (1.0 / (1.0 + Math.Exp(exponent))), 0.0, 1.0);
    }

    /// <summary>The current value of a magic skill from the read model (see the class remarks), or false with the reason in <paramref name="why"/>.</summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">One of the four magic skills.</param>
    /// <param name="value">The skill's current value.</param>
    /// <param name="why">Why there is none.</param>
    public static bool TryCurrentSkill(PlayerQualities q, uint skillId, out uint value, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        value = 0;
        why = string.Empty;
        if (skillId is not (MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment or MageTemplate.LifeMagic or MageTemplate.WarMagic))
        {
            why = $"skill {skillId} is not one of the four magic skills";
            return false;
        }

        if (!q.IsKnown || !q.TryGetSkill(skillId, out var skill))
        {
            why = $"the read model has no skill {skillId}";
            return false;
        }

        if (skill.AdvancementClass < HeadlessCharacter.TrainedSkill)
        {
            why = $"skill {skillId} is not trained";
            return false;
        }

        if (!SkillRules.TryCurrent(q, skillId, MagicFormula, SkillRules.TrainedClass, out value))
        {
            why = "the read model has no Focus or Self";
            return false;
        }

        return true;
    }

    /// <summary>M6 7 AK: the base of a magic skill (raw attributes, InitLevel, ranks: <see cref="SkillRules.TryBase"/>); false when it cannot be read.</summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The magic skill.</param>
    /// <param name="value">The base value.</param>
    public static bool TryBaseSkill(PlayerQualities q, uint skillId, out uint value) => SkillRules.TryBase(q, skillId, MagicFormula, SkillRules.TrainedClass, out value);

    /// <summary>The formula of the four magic skills: (Focus + Self) / 4 (the SkillTable's, pinned by <c>MageTemplateDatTests</c>), so <see cref="TryCurrentSkill"/> needs no SkillTable.</summary>
    private static readonly Lifestone.Data.CharacterCreation.SkillTableFormula MagicFormula = new(0, 1, 1, MageTemplate.MagicSkillDivisor, MageTemplate.Focus, MageTemplate.Self);

    /// <summary>
    /// The arithmetic of <see cref="TryCurrentSkill"/> over plain numbers (factored out in M3 2.1 s0a so a planner can call it without a read model; the behaviour is unchanged): the formula
    /// <c>round((focus + self) / 4)</c> (half away from zero) plus <paramref name="initPlusRanks"/>, scaled by <paramref name="product"/> (the multiplicative skill enchantments) and the vitae factor
    /// when there is one, plus <paramref name="additive"/>, rounded half away from zero and floored at 0.
    /// </summary>
    /// <param name="focus">Focus with its enchantments.</param>
    /// <param name="self">Self with its enchantments.</param>
    /// <param name="initPlusRanks">The skill's start level plus its ranks.</param>
    /// <param name="product">The product of the strongest multiplicative skill enchantments (1 for none).</param>
    /// <param name="vitae">The vitae's factor, or null when the character has none.</param>
    /// <param name="additive">The sum of the additive skill enchantments, each truncated to an int.</param>
    public static uint SkillValue(uint focus, uint self, uint initPlusRanks, double product, double? vitae, int additive)
    {
        var formula = (uint)Math.Round((focus + self) / (double)MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero);
        return SkillRules.Compose(formula, initPlusRanks, product, vitae, additive);
    }

    /// <summary>
    /// The first profile line of <paramref name="role"/> (the spec's order) that has a level whose chance from the current skill in the line's school is at least <see cref="MinChance"/>,
    /// and that line's highest such level (the highest <c>Power</c>). Null with the reason when the skill is unavailable or no level qualifies. M5 3 F6: with <paramref name="floor"/> a level the pack cannot pay for
    /// is passed over, named in <paramref name="why"/> (the first one passed over when a lower level is picked; all of them in <see cref="PackFloor.Skipped"/>), and a line with no level the pack can pay for is a named miss.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="role">The kind of line: <see cref="ProfileRole.CreatureBuff"/>, <see cref="ProfileRole.LifeBuff"/> or <see cref="ProfileRole.ItemBuffWand"/>.</param>
    /// <param name="why">Why there is none (or, with a pick, the level that was passed over for the pack's sake, else empty).</param>
    /// <param name="floor">What the pack holds and the spell table (<see cref="PackFloor"/>); null: no level is judged by the pack (the earlier behaviour).</param>
    public static CastPick? Pick(PlayerQualities q, ProfileRole role, out string why, PackFloor? floor = null)
    {
        ArgumentNullException.ThrowIfNull(q);
        why = $"no {role} line in the profile";
        foreach (var line in ArenaProfile.Lines)
        {
            if (line.Role != role)
            {
                continue;
            }

            if (!TryCurrentSkill(q, SkillOf(line.School), out var skill, out var noSkill))
            {
                why = noSkill;
                return null;
            }

            var held = floor?.HeldFor(line.School);
            List<(ProfileSpell Spell, string Text)>? passed = null;
            ProfileSpell? best = null;
            foreach (var spell in line.Spells)
            {
                if (Chance(skill, spell.Power) < MinChance)
                {
                    continue;
                }

                if (floor is not null && floor.Passes(held, spell, out var text))
                {
                    (passed ??= []).Add((spell, text));
                    continue;
                }

                if (best is null || spell.Power > best.Power)
                {
                    best = spell;
                }
            }

            // The levels passed over that matter are those the rule would have chosen: the ones above the pick (all of them when none can be paid for). The highest is named in why, like the cycle's first skip.
            (ProfileSpell Spell, string Text)? top = null;
            foreach (var p in passed ?? [])
            {
                if (best is not null && p.Spell.Power < best.Power)
                {
                    continue;
                }

                floor!.Note(line, p.Text);
                if (top is null || p.Spell.Power > top.Value.Spell.Power)
                {
                    top = p;
                }
            }

            if (best is not null)
            {
                why = top is { } above ? $"{line.Name}: {above.Text}" : string.Empty;
                return new CastPick(line, best, skill, Chance(skill, best.Power));
            }

            why = top is { } highest
                ? $"{line.Name}: no level the pack can pay for ({highest.Text})"
                : $"no level of {line.Name} reaches a chance of {MinChance} at {line.School} skill {skill}";
        }

        return null;
    }

    /// <summary>
    /// The certain fizzle (M3 1.4 fix B, review 1.4-R11): the cheapest spell the character <i>knows</i> (<paramref name="book"/>, the read model's spellbook: an unknown spell is refused by ACE with its own
    /// code, not a fizzle) among the creature and life lines whose <c>Power</c> is more than <see cref="FizzleMargin"/> above the current skill in its school, so ACE fails the cast outright
    /// (<c>Player_Magic.cs</c> 534-535). Null with the numbers in <paramref name="report"/> when there is none.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="book">The player's spellbook.</param>
    /// <param name="report">The skills and highest known powers seen, when there is no such spell; else empty.</param>
    /// <param name="floor">M5 3 F6: what the pack holds and the spell table. ACE checks a cast's components before its skill (<c>CreatePlayerSpell</c>, Player_Magic.cs:1004 then 1026 @95ef426d: <c>ValidateSpell</c> 404-408, <c>HasComponentsForSpell</c> 1227-1248, then <c>GetCastingPreCheckStatus</c>), so a spell the pack cannot pay for ends 0x0400, not 0x0402: it is passed over and named in <see cref="PackFloor.Skipped"/>. Null: no level is judged by the pack.</param>
    public static CastPick? PickFizzle(PlayerQualities q, Spellbook book, out string report, PackFloor? floor = null)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        CastPick? best = null;
        var seen = new StringBuilder();
        foreach (var line in ArenaProfile.Lines)
        {
            if (line.Role is not (ProfileRole.CreatureBuff or ProfileRole.LifeBuff))
            {
                continue;
            }

            if (!TryCurrentSkill(q, SkillOf(line.School), out var skill, out var noSkill))
            {
                seen.Append(noSkill).Append("; ");
                continue;
            }

            uint highestKnown = 0;
            var held = floor?.HeldFor(line.School);
            foreach (var spell in line.Spells)
            {
                if (!book.Knows(spell.Id))
                {
                    continue;
                }

                highestKnown = Math.Max(highestKnown, spell.Power);
                if ((long)spell.Power - skill > FizzleMargin && (best is null || spell.Power < best.Spell.Power))
                {
                    if (floor is not null && floor.Passes(held, spell, out var text))
                    {
                        floor.Note(line, text);
                        continue;
                    }

                    best = new CastPick(line, spell, skill, Chance(skill, spell.Power));
                }
            }

            seen.Append(CultureInfo.InvariantCulture, $"{line.Name}: {line.School} skill {skill}, highest known power {highestKnown}; ");
        }

        report = best is null
            ? $"no known profile spell has a Power more than {FizzleMargin} above the current skill{(floor is { Skipped.Count: > 0 } ? " that the pack can pay for" : string.Empty)}: {seen}{(floor is { Skipped.Count: > 0 } ? "passed over: " + string.Join(" | ", floor.Skipped) : string.Empty)}"
            : string.Empty;
        return best;
    }
}

/// <summary>
/// What the first casts judge a level by (M5 3 F6): the read model's pack and the SpellTable's spells, so <see cref="GearCasts.Pick"/> and <see cref="GearCasts.PickFizzle"/> apply the same level floor as the buff cycle
/// (<see cref="LevelFloor"/>): when the pack holds the school's focus, a level whose foci-formula components the pack lacks is passed over. One instance per call (it collects the levels it passed over).
/// </summary>
/// <param name="state">The read model (the pack).</param>
/// <param name="spells">The SpellTable's spells (the formulas).</param>
/// <param name="accountName">M6 1 h2: the account's name as the server stores it (the login name), or null: the floor then applies only to a school whose focus the pack holds (the foci formula). With it a school without its focus is judged on the account's full formula (<see cref="AccountFormula"/>); hashed there, never stored or printed.</param>
public sealed class PackFloor(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, string? accountName = null)
{
    private readonly List<string> _skipped = [];

    /// <summary>Every level passed over that the rule would have chosen (the levels above the pick), once, as <c>Strength Self: level V needs Gold Scarab 687, which the pack does not hold; skipped</c>.</summary>
    public IReadOnlyList<string> Skipped => _skipped;

    /// <summary>What the pack holds when the floor applies to <paramref name="school"/> (<see cref="LevelFloor.HeldFor"/>), else null.</summary>
    internal Dictionary<uint, int>? HeldFor(MagicSchool school) => LevelFloor.HeldForAccount(state, school, accountName);

    /// <summary>True, with the reason in <paramref name="text"/> (no line name), when the floor applies (<paramref name="held"/> is not null) and the pack cannot pay for <paramref name="spell"/>.</summary>
    internal bool Passes(Dictionary<uint, int>? held, ProfileSpell spell, out string text)
    {
        text = string.Empty;
        if (held is null || !spells.TryGetValue(spell.Id, out var info) || !LevelFloor.ShortOf(held, info, out var need, accountName))
        {
            return false;
        }

        text = LevelFloor.SkipText(spell, need);
        return true;
    }

    /// <summary>Notes a level of <paramref name="line"/> that was passed over and mattered (once), for <see cref="Skipped"/>.</summary>
    internal void Note(ProfileLine line, string text)
    {
        var named = line.Name + ": " + text;
        if (!_skipped.Contains(named))
        {
            _skipped.Add(named);
        }
    }
}
