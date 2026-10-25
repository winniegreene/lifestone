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
/// <see cref="FighterTemplate.Name"/> or create it with <see cref="FighterTemplate.CreationIntent"/>, enter the world as that character and
/// nobody else, and bring it to the template, the kit and Holtburg. Idempotent and resumable on <see cref="MageBuildScenario"/>'s rule: every step is
/// sent only if the read model still shows it is needed, so a ready fighter is only verified and a half-built one gets just the missing steps, in order.
/// Then it logs off and quits as the other scenarios do.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>): <c>login</c>, <c>choosing</c>, <c>creating</c> (only when the name is absent), <c>entering</c>, <c>arriving</c>,
/// <c>status</c> (the access level must be Player, D11, and the mod must report <c>xp</c> and <c>item</c>), <c>building</c>, <c>refilling</c> and <c>purging</c> (only with <c>freshCharacter</c>), <c>placing</c>,
/// <c>logging-off</c>, <c>quitting</c>, <c>done</c>; <c>recreate-logoff</c>, <c>recreate-delete</c> and <c>recreate-verify</c> are the re-creation's (<c>recreate</c>, or <c>freshCharacter</c> without the first). The scenario never selects, deletes or enters another character; an absent name with no free slot fails the
/// run, and the entered character's name is checked against the template's before every step that acts on it (<see cref="NameCheck"/>).</para>
/// <para><b>Fresh character</b> (<c>freshCharacter</c>, <c>--fresh-character</c>, M4 fresh B1, owner decisions D44 and D45): the live gate's re-create. The template's live character, when the list shows it, is never entered: it is deleted from the character list through the retail delete,
/// the build waits for the list to show that guid pending, creates the template's character and builds it as usual; with no live copy it only creates it. After the build has ended met, and before the placing, <see cref="FreshRefill"/> fills the vitals by ACE's own level-up rule (the build grants its experience first, so its raises lifted
/// the maxima over the current values; M4 fresh F1, the item's gate round 1 finding), then <see cref="FreshPurge"/> asks the server mod to purge the old copies of the name
/// (the one just deleted and any older pending or expired ones) until the verb answers <c>unknown-character</c>, so a failed build leaves the old copy restorable. A re-created character whose old copy was not purged fails the run. The verdict gains <c>fresh=recreated|created purged=&lt;n&gt; refill=none|level-up:&lt;a&gt;-&gt;&lt;b&gt;|failed</c>
/// (the gate's judge reads <c>fresh=</c> and <c>purged=</c> on the same line). The refill is one level-up: <b>a fresh Brawler or Archer ends one level above its template's (56), its extra experience unspent</b> (owner decisions D44, D45; the template's level check is <c>&gt;=</c>, so the journey's embedded build-verify accepts it).</para>
/// <para><b>Building</b>, in this order, each only if still needed: (a) the mod's <c>xp</c> for exactly <see cref="XpTableData.XpForLevel"/>(<see cref="FighterTemplate.Level"/>: 40 for the Hunter, 55 for the Brawler and the Archer since M4 P5a, D33 and D38) minus
/// <see cref="PlayerQualities.TotalExperience"/> (never when the fighter is at or past it, except that the shortfall of the plan's remaining raises is granted too); (b) <see cref="IActionApi.TrainSkill"/> for each template skill
/// the read model shows untrained, with <see cref="TrainCredits"/> (<c>TODO(m4-healing-minlevel)</c>: s0 found Healing has MinLevel 2 in the SkillTable; the
/// creation asks for it trained, and if ACE refuses that the creation fails by name and the remedy is this train step, so the creation moves Healing to
/// <see cref="FighterTemplate.TrainedLater"/>; the live build proves which); (c) the raise of each <see cref="FighterTemplate.Targets"/> entry in plan order
/// with <see cref="StatTarget.XpRemaining"/> (for the Brawler and the Archer: the base plan's stats first, then Quickness, Run, Focus, Self, the three schools and Max Mana; a stat already at or above its rank is left, so a rerun only adds); (c2) <see cref="PlayerTools.LearnSpells"/> of the fighter's
/// <see cref="FighterProfile.SpellsToLearn"/> in chunks (M4 P5a, owner decision D33: the Hunter has no profile and learns nothing; a ready fighter's chunks all answer known and teach nothing); (d) the kit, one <see cref="PlayerTools.SpawnItem"/> at a time over <see cref="FighterKit.Items"/> (it does not
/// stack, so the two healing kits are two grants; a stack line is topped up by the missing count, and a top-up is not merged: the count is what is judged; with a SpellTable and an account the kit also holds the spell components of the profile's spells,
/// <see cref="FighterComponents"/>, so a rerun grants exactly what casts have burned since: the build only adds, and <c>kit=</c> names a short component as <c>Name:held/needed</c>);
/// (e) the Mace wielded and the five leather pieces worn by <see cref="IActionApi.Wield"/> on <see cref="GearScenario"/>'s pattern (a worn piece that is not a
/// kit line and covers the slots goes to the pack first); (e3) only when asked (<c>rechargeGear</c>: the journey's build-verify), the magic gear's mana, appraised and recharged with the kit's mana charge when a piece runs low (<see cref="GearRecharge"/>;
/// it runs alone until it ends, never fails the build, and its fields <c>gear_mana_min= recharged=</c> follow <c>spells_active=</c>); (f) <see cref="PlayerTools.Place"/> to Holtburg's point of interest when the player is not in its
/// landblock (no bind: item 1.2's hunt binds). Every action waits for its outcome; anything but Confirmed fails the run naming the step and the outcome.
/// A precondition the read model has not caught up with is waited for <see cref="SettleSeconds"/> on the session clock per reason, then fails by name;
/// nothing is ever sent twice.</para>
/// <para><b>The verdict</b> (<see cref="VerdictFields"/>, logged as <c>[fighter] verdict ...</c>):
/// <c>fighter=ready|built|failed level=&lt;n&gt; template=ok|short [template_short=...] kit=ok|&lt;missing&gt; wielded=&lt;wcid&gt;|0 worn=&lt;count&gt;</c>, then, for a template that trains magic (the Brawler and the Archer), <c>magic=trained|short:&lt;schools&gt;|not_entered spells=&lt;known+learned&gt;/&lt;ids&gt;</c>
/// (<c>trained</c> when Creature Enchantment and Life Magic are both trained).
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

    /// <summary>The mod verbs the build uses (the experience grant, the item grant and, since M4 P5a, the spellbook), which the status stage requires the mod to report.</summary>
    internal static readonly string[] RequiredVerbs = [ToolVerbs.Xp, ToolVerbs.Item, ToolVerbs.Spell + " " + ToolVerbs.SpellLearn];

    /// <summary>The most spell ids one <c>spell learn</c> carries.</summary>
    public const int SpellChunk = PlayerTools.MaxSpellIds;

    /// <summary>
    /// The SkillTable's trained cost (credits) of each skill the template trains, Aluvian (<c>FighterTemplateDatTests</c> and <c>FighterMagicDatTests</c> pin them against the dats): Melee Defense 10, Missile Weapons 6, Healing 6, Creature Enchantment 8, Item Enchantment 8, Life Magic 12, Mana Conversion 6 (M4 P5a C7; the same 6 as the mage's, pinned by <c>MageTemplateDatTests</c> and <c>FighterMagicDatTests</c>).
    /// ACE refuses any other number. Heavy Weapons (12) is specialised at creation and cannot be specialised later.
    /// </summary>
    public static IReadOnlyDictionary<uint, int> TrainCredits { get; } = new Dictionary<uint, int>
    {
        [FighterTemplate.MeleeDefense] = 10,
        [FighterTemplate.MissileWeapons] = 6,
        [FighterTemplate.Healing] = 6,
        [FighterTemplate.CreatureEnchantment] = 8,
        [FighterTemplate.ItemEnchantment] = 8,
        [FighterTemplate.LifeMagic] = 12,
        [FighterTemplate.ManaConversion] = 6,
    };

    /// <summary>The short name of a magic school's skill in the verdict's <c>magic=</c> (creature, item, life).</summary>
    internal static string SchoolShortName(uint skillId) => skillId switch
    {
        FighterTemplate.CreatureEnchantment => "creature",
        FighterTemplate.ItemEnchantment => "item",
        FighterTemplate.LifeMagic => "life",
        FighterTemplate.ManaConversion => "mana",
        _ => throw new ArgumentOutOfRangeException(nameof(skillId), skillId, "TODO(m4-template-skill): a magic skill the verdict has no short name for"),
    };

    private readonly bool _endSession;
    private readonly FighterTemplate _template;
    private readonly XpTableData _table;
    private readonly IReadOnlyList<uint> _retailClasses;
    private readonly ILogSink _log;
    private readonly HashSet<string> _sent = [];
    private readonly List<string> _steps = [];
    private readonly List<uint> _scratch = [];
    private readonly ReasonTimer _wait = new(SettleSeconds);
    private readonly uint _holtburgLandblock;
    private readonly TeleportDestination _holtburg;
    private readonly FighterProfile? _profile;
    private readonly FighterKit _gear;
    private readonly IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? _spellInfos;
    private readonly GearRecharge? _recharge;
    private GearReading? _gearReading;
    private int _pack = -1;
    private bool _gearNoted;
    private ActionHandle? _pending;
    private InFlight? _inFlight;
    private ToolHandle<ToolStatus>? _status;
    private bool _createTried;
    private readonly bool _recreate;
    private bool _recreated;
    private readonly bool _fresh;
    private bool _freshDecided;
    private string? _freshHow;
    private bool _purged;
    private readonly FreshPurge? _purge;
    private readonly FreshRefill? _refill;
    private string? _mismatch;
    private uint _oldGuid;
    private bool _xpSent;
    private int _spellChunk;
    private int _spellsKnown;
    private int _spellsLearned;
    private string _magic = "not_entered";
    private double _arrivalStart = double.NaN;
    private double _placeStart = double.NaN;
    private bool _finished;
    private int _level;
    private TemplateCheck? _check;
    private string _kit = "not_entered";
    private string _caster = "not_entered";
    private uint _wielded;
    private int _worn;

    /// <summary>The fighter build.</summary>
    /// <param name="table">The XpTable (<see cref="XpTableData.Load"/>).</param>
    /// <param name="retailClasses"><see cref="HeadlessCharacter.RetailSkillClasses"/> of the dats (for the creation).</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival.</param>
    /// <param name="template">The fighter to build (default <see cref="FighterTemplate.Hunter"/>).</param>
    /// <param name="spells">
    /// The SpellTable's spells (<see cref="Lifestone.Data.Magic.SpellTableData.Spells"/>) and <paramref name="account"/> the account's name as the server stores it (M4 P5a fix F-abil): given together, a fighter that has a spell profile (the Brawler and the Archer)
    /// is also granted the spell components its spells burn (<see cref="FighterComponents"/>, in the formula that account casts; the name is hashed and never stored or logged). Given neither, the kit has no components and a TODO is logged.
    /// </param>
    /// <param name="account">The account's name (see <paramref name="spells"/>).</param>
    /// <param name="endSession">True (the headless host's): log in, build, place in Holtburg, log off and quit. False (M4 P5b J3b, the journey's <c>build-verify</c> station): the session already holds the character in the world, so the scenario skips the login, never places (the journey puts the character where the station needs it) and never logs off, and ends at <c>done</c> in the world. Not with <paramref name="recreate"/>.</param>
    /// <param name="rechargeGear">True (the journey's <c>build-verify</c> station; closes <c>TODO(m4-gear-mana)</c>): after the kit is worn and packed, appraise the magic gear's mana and use the kit's mana charge when a piece runs low (<see cref="GearRecharge"/>, step (e3)); the verdict adds <c>gear_mana_min= recharged=</c>. For a template with gear only; report-only, it never fails the build.</param>
    /// <param name="recreate">M4 P5a C5 (owner decision D40): when the entered character's innate attributes differ from the template's, delete it through the client's retail character delete (ACE frees the name once it is pending deletion), wait for the list to show it pending, create the template's character and build that; the mod's purge is never used. Without it a mismatch fails the build (<c>template=mismatch:...</c>) before anything is bought. For the Brawler and the Archer only.</param>
    /// <param name="freshCharacter">M4 fresh B1 (owner decisions D44, D45): delete the template's live character from the character list (never entering it), create the template's and build it, then refill the vitals by one level-up (M4 fresh F1: the character ends one level above the template's) and purge the old copies of the name with the mod before the placing; the verdict gains <c>fresh=recreated|created purged=&lt;n&gt; refill=none|level-up:&lt;a&gt;-&gt;&lt;b&gt;|failed</c>. For the Brawler and the Archer only, with <paramref name="endSession"/> and not with <paramref name="recreate"/>.</param>
    public FighterBuildScenario(XpTableData table, IReadOnlyList<uint> retailClasses, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, FighterTemplate? template = null, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? spells = null, string? account = null, bool recreate = false, bool endSession = true, bool rechargeGear = false, bool freshCharacter = false)
    {
        _endSession = endSession;
        _template = template ?? FighterTemplate.Hunter;
        _recreate = recreate;
        _fresh = freshCharacter;
        if (recreate && _template.Key == FighterTemplate.Hunter.Key)
        {
            throw new ArgumentException("--fighter-recreate is for the Brawler and the Archer only (test-melee, test-archer); the Hunter is never re-created", nameof(recreate));
        }

        if (freshCharacter && _template.Key == FighterTemplate.Hunter.Key)
        {
            throw new ArgumentException("--fresh-character is for the Brawler and the Archer only (test-melee, test-archer); the Hunter is never re-created", nameof(freshCharacter));
        }

        if (freshCharacter && recreate)
        {
            throw new ArgumentException("--fresh-character and --fighter-recreate exclude each other: the first deletes the live character whatever it is, the second only when its attributes differ", nameof(freshCharacter));
        }

        if (freshCharacter && !endSession)
        {
            throw new ArgumentException("--fresh-character needs the character list: it is for the session-ending (headless) build, not the journey's embedded one", nameof(freshCharacter));
        }

        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(retailClasses);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrivalSeconds);
        if (table.CharacterLevelXpList.Count <= _template.Level)
        {
            throw new ArgumentException($"TODO(m3-xp-table): the XpTable ends before level {_template.Level}", nameof(table));
        }

        _profile = FighterProfile.For(_template);
        if ((spells is null) != string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("the SpellTable and the account name are given together or not at all", spells is null ? nameof(spells) : nameof(account));
        }

        if (_profile is not null && spells is not null)
        {
            _gear = _template.Kit.WithComponents(FighterComponents.For(_profile, spells, account!));
        }
        else
        {
            _gear = _template.Kit;
            if (_profile is not null)
            {
                log.Warn(LogCategory, "TODO(m4-fighter-components): no SpellTable and account were given, so the kit carries no spell components: a Harm Self, Heal Self or buff cast would be refused (0x0400 YouDontHaveAllTheComponents)");
            }
        }

        _spellInfos = spells;
        if (rechargeGear && _template.Kit.Gear is { } gearSet && _gear.ChargeWcid != 0)
        {
            _recharge = new GearRecharge(gearSet, _gear.ChargeWcid, _gear.ManaCharges, spells, log, LogCategory, _steps.Add);
        }

        _table = table;
        _retailClasses = [.. retailClasses];
        _log = log;
        if (freshCharacter)
        {
            _purge = new FreshPurge(_template.Name, log, LogCategory);
            _refill = new FreshRefill(table, log, LogCategory);
        }

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

    /// <summary>The steps counted in the verdict's ready or built decision (every step sent; a spell chunk counts when it taught at least one spell).</summary>
    public int BuiltSteps => _steps.Count - _knownChunks;

    private int _knownChunks;

    /// <summary>The spells the mod answered as known or learned, over the chunks asked so far.</summary>
    public int SpellsAccounted => _spellsKnown + _spellsLearned;

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario sends no movement.</remarks>
    public long Unmatched => 0;

    /// <summary>The kit the build grants: the template's, with the spell components of the profile's spells when the scenario was given a SpellTable and an account (M4 P5a fix F-abil).</summary>
    public FighterKit Kit => _gear;

    /// <summary>The verdict fields (no character name, no account).</summary>
    public string VerdictFields()
    {
        var failed = Failure is not null || !_finished;
        var state = failed ? "failed" : BuiltSteps > 0 ? "built" : "ready";
        var template = _mismatch is not null ? "template=mismatch:" + _mismatch : _check is { AllMet: true } ? "template=ok" : "template=short";
        if (_mismatch is not null)
        {
            // the innate attributes differ from the template's: nothing was bought, so there is no short list to name
        }
        else if (_check is null)
        {
            template += " template_short=not_entered";
        }
        else if (!_check.AllMet)
        {
            template += " template_short=" + MageBuildScenario.ShortField(_check);
        }

        var magic = _profile is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" magic={_magic} spells={SpellsAccounted}/{_profile.SpellsToLearn.Count}");
        var recreated = _recreated ? " recreated=yes old=pending-deletion" : string.Empty;
        // M4 fresh B1: fresh=recreated|created purged=<n> (fresh=none before the run decided; the gate's pass needs recreated or created). M4 fresh F1: refill=none|level-up:<a>-><b>|failed last (none until the refilling stage has ended).
        var fresh = _fresh ? string.Create(CultureInfo.InvariantCulture, $" fresh={_freshHow ?? "none"} purged={_purge?.Purged ?? 0} refill={_refill?.Field ?? "none"}") : string.Empty;
        // M4 P5a C6: the gear (report-only in its first gate): gear=<in place>/<pieces> gear_magic=on|off|n/a spells_active=<active>/<wanted> pack=<main-pack items>/102.
        var gear = _template.Kit.Gear is null ? string.Empty : _gearReading is { } g ? string.Create(CultureInfo.InvariantCulture, $" gear={g.Worn}/{g.Total} gear_magic={g.Magic} spells_active={g.SpellsField}") : " gear=not_entered";
        var pack = _pack < 0 || _template.Kit.Gear is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" pack={_pack}/{GearKit.PackCapacity}");
        // The gear's mana (closes TODO(m4-gear-mana); report-only in its first gate): gear_mana_min=<the lowest piece's percent after the recharge> recharged=<charges used>[:<problem>], only when the caller asked for the recharge.
        var mana = _recharge?.VerdictFields() ?? string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"fighter={state} level={_level} {template} kit={_kit} wielded={_wielded} worn={_worn}{magic}{gear}{mana}{pack}{recreated}{fresh}");
    }

    /// <summary>
    /// Where the kit's caster is after the last snapshot (M4 P5a C1b): <c>pack</c> (the build keeps it there; <see cref="CasterSwap"/> wields it for a pass), <c>hand</c> (the build fails: it takes it off first), <c>none</c> (<c>kit=</c> names it as short),
    /// <c>n/a</c> for a template with no caster (the Hunter) or <c>not_entered</c> before the inventory is known. Not part of the verdict line.
    /// </summary>
    public string Caster => _caster;

    /// <summary>True when <paramref name="name"/> is the fighter template's character (case-insensitive, as the character list matches).</summary>
    public static bool NameCheck(string? name, FighterTemplate? template = null) => string.Equals(name, (template ?? FighterTemplate.Hunter).Name, StringComparison.OrdinalIgnoreCase);

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
                    // Already in the world (a host that logged in itself, the journey, or the offline stand-in): nothing to log in. The build acts on the template's character and nobody else.
                    if (WrongCharacter(session) is { } wrong)
                    {
                        return wrong;
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
                        return Fail(session, "creating the character (TODO(m4-healing-minlevel): if ACE refuses Healing trained at level 1, move it to FighterTemplate.TrainedLater and create again)", created);
                    }

                    Created = true;
                    _log.Info(LogCategory, "created " + _template.Name);
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

            case "recreate-logoff":
                if (Settled(out var offOutcome))
                {
                    if (offOutcome.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "recreate: logging off to the character list", offOutcome);
                    }

                    _pending = actions.DeleteCharacter(_oldGuid, ActionSource.Host);
                    return Go("recreate-delete");
                }

                break;

            case "recreate-delete":
                if (Settled(out var deleteOutcome))
                {
                    if (deleteOutcome.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, (_fresh ? "fresh-character" : "recreate") + ": deleting the old character through the client's retail character delete", deleteOutcome);
                    }

                    return Go("recreate-verify");
                }

                break;

            case "recreate-verify":
                foreach (var listed in session.State.Characters.Entries)
                {
                    if (listed.Guid == _oldGuid && listed.PendingDeletion)
                    {
                        _createTried = false;
                        Created = false;
                        if (_fresh)
                        {
                            _freshHow = "recreated";
                            _log.Warn(LogCategory, "fresh=recreated: the old " + _template.Name + " is listed as pending deletion (restorable until the new copy has been built and purges it); creating the template's character");
                            return Go("choosing");
                        }

                        _recreated = true;
                        _log.Warn(LogCategory, "recreated=yes old=pending-deletion: the old " + _template.Name + " is listed as pending deletion (restorable in ACE's window); creating the template's character");
                        return Go("choosing");
                    }
                }

                return Waiting(session, "the server has not listed the old character as pending deletion");

            case "status":
                return StatusStep(session);

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
            if (NameCheck(c.Name, _template))
            {
                if (c.PendingDeletion)
                {
                    if (_recreate || _fresh)
                    {
                        continue; // M4 P5a C5, M4 fresh B1: a re-creating run ignores the old character that is pending deletion
                    }

                    return Fail(session, "choosing: the fighter is pending deletion (restore it first)");
                }

                if (_fresh && !_freshDecided)
                {
                    // M4 fresh B1 (D44): the live copy is never entered. It is deleted from the list through the retail delete (the same as C5's, without the log off), and the new copy purges it once built (D45).
                    _freshDecided = true;
                    _oldGuid = c.Guid;
                    _log.Warn(LogCategory, "fresh-character: deleting the live " + _template.Name + " from the character list through the retail character delete (it is not entered); the new copy purges it after the build");
                    _pending = session.Actions.DeleteCharacter(c.Guid, ActionSource.Host);
                    return Go("recreate-delete");
                }

                EnteredGuid = c.Guid;
                _log.Info(LogCategory, (Created ? "entering the character just created" : "entering the existing character") + " " + _template.Name);
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
                string.Create(CultureInfo.InvariantCulture, $"choosing: the account has no free character slot ({characters.Entries.Count} of {characters.SlotCount} used), so {_template.Name} was not created; no other character is touched"));
        }

        _createTried = true;
        if (_fresh && !_freshDecided)
        {
            _freshDecided = true;
            _freshHow = "created"; // M4 fresh B1: no live copy was listed, so there is nothing to delete
        }

        _log.Info(LogCategory, "absent; creating " + _template.Name + " with the template's creation intent");
        _pending = session.Actions.CreateCharacter(_template.CreationIntent(_template.Name, _retailClasses), ActionSource.Host);
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

        if (_fresh && !reported.Verbs.Contains(FreshPurge.RequiredVerb, StringComparer.Ordinal))
        {
            return Fail(session, $"status: the server mod does not report the '{FreshPurge.RequiredVerb}' verb the fresh character's purge uses (a stale mod: server setup -ModOnly)");
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

        return NameCheck(name, _template) ? null : Fail(session, "the entered character is not " + _template.Name + ": nothing is sent for it");
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
            if (step.OnConfirmed?.Invoke() is { } problem)
            {
                return Fail(session, problem);
            }
        }

        if (!q.IsKnown)
        {
            return Waiting(session, "the player description has not arrived");
        }

        if (WrongCharacter(session) is { } wrong)
        {
            return wrong;
        }

        // (e3), running: the gear's recharge has the stage to itself until it ends, so (d) tops a charge it used up only afterwards.
        if (_recharge is { InProgress: true } recharging)
        {
            return recharging.Step(session) ?? ScenarioStatus.Running;
        }

        var actions = session.Actions;

        // M4 P5a C5 (owner decision D40): a character created with another attribute split than the template's gets nothing bought on top.
        _mismatch = InnateMismatch(q);
        if (_mismatch is not null)
        {
            if (_fresh || !_recreate || _recreated)
            {
                return Fail(
                    session,
                    "building: the character's innate (creation) attributes differ from the " + _template.Name + " template (" + _mismatch + (_recreated || _fresh ? "; the re-created character is wrong too" : "; run with --fighter-recreate to delete it and create the template's") + "); nothing is bought on top");
            }

            _oldGuid = EnteredGuid;
            _log.Warn(LogCategory, "template=mismatch:" + _mismatch + "; --fighter-recreate: logging off, deleting the old " + _template.Name + " through the retail character delete (never the mod's purge) and creating the template's");
            _pending = actions.LogOff(ActionSource.Host);
            return Go("recreate-logoff");
        }

        // (a) experience to the target level, once; and, for a fighter built to an earlier plan (one of its stats may sit above this plan's rank), what the plan's raises still cost beyond the experience it has to spend (M4 P5a C1c: only adds, never resets).
        var wanted = _template.LevelXp(_table);
        var total = (ulong)Math.Max(0, q.TotalExperience);
        var grant = total < wanted ? wanted - total : 0UL;
        var available = (ulong)Math.Max(0, q.AvailableExperience);
        var needed = RemainingPlanCost(q);
        if (needed > available + grant)
        {
            grant = needed - available;
        }

        if (grant > 0 && !_xpSent)
        {
            if (total < wanted && (q.Level < 1 || q.Level >= _template.Level || _table.XpForLevel(q.Level) > total || total >= _table.XpForLevel(q.Level + 1)))
            {
                return Fail(
                    session,
                    string.Create(CultureInfo.InvariantCulture, $"building: the read model's level and experience disagree (level {q.Level}, total experience {total:N0}); the xp grant is not sent"));
            }

            _xpSent = true;
            return Send(
                string.Create(CultureInfo.InvariantCulture, $"xp {grant:N0} (to {Math.Max(wanted, total + grant):N0}, level {_template.Level}; has {total:N0})"),
                session.Tools.AddXp((long)grant, ActionSource.Host));
        }

        if (total < wanted)
        {
            return Waiting(session, "the total experience is below the target level's after the xp verb");
        }

        if (q.Level < _template.Level)
        {
            return Waiting(session, "the level is below the target with the experience granted");
        }

        // (b) a template skill the read model shows untrained, with the SkillTable's credits (a partial character, or the Healing remedy).
        foreach (var target in _template.Targets.Where(t => t.Kind == MageStatKind.Skill))
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
        foreach (var target in _template.Targets)
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

        // (c2) the profile's spellbook, in chunks the mod accepts. A ready fighter's chunks all answer known and teach nothing.
        if (_profile is not null)
        {
            var ids = _profile.SpellsToLearn;
            var first = _spellChunk * SpellChunk;
            if (first < ids.Count)
            {
                var chunk = ids.Skip(first).Take(SpellChunk).ToArray();
                var learn = session.Tools.LearnSpells(chunk, ActionSource.Host);
                return Send(
                    string.Create(CultureInfo.InvariantCulture, $"learn spells {first + 1}-{first + chunk.Length} of {ids.Count}"),
                    learn,
                    onConfirmed: () => TallySpells(learn, chunk.Length));
            }
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

        // (e2) the component stacks into the side packs, so the main pack keeps its headroom (M4 P5a C6, D41).
        if (PackStep(session) is { } packed)
        {
            return packed;
        }

        // (e3) the gear's mana (closes TODO(m4-gear-mana)): appraised, and the kit's charge used when a piece runs low; only when the caller asked (the journey's build-verify). It never fails the build.
        if (_recharge?.Step(session) is { } charging)
        {
            return charging;
        }

        // (f) Holtburg.
        Snapshot(session);
        if (_check is { AllMet: false } check)
        {
            return Fail(session, "the build ended short of the template: " + check.Summary);
        }

        if (_purge is not null && !_purged)
        {
            // M4 fresh B1 (D45): the old copies are purged only once the build has ended met (the same checks as the end of the run), so a failed build leaves the old copy restorable. M4 fresh F1: the vitals are refilled first (the build's raises lifted the maxima over the current values).
            if (EndChecks(session) is { } notMet)
            {
                return notMet;
            }

            _log.Info(LogCategory, "the build ended met; refilling the vitals by a level-up, then purging the old copies of " + _template.Name + " with the server mod");
            return Go("refilling");
        }

        return PlaceOrFinish(session);
    }

    private ScenarioStatus PlaceOrFinish(IGameSessionView session)
    {
        if (!_endSession && !InHoltburg(session))
        {
            // A journey's station never places by itself (P5b contract 5): the journey put the character where the station's needs say.
            _log.Warn(LogCategory, "the fighter is not in Holtburg; an embedded build does not place (the journey's travel does)");
        }

        if (_endSession && !InHoltburg(session))
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

    /// <summary>
    /// M4 fresh F1: fills the vitals after the build by ACE's own level-up rule (<see cref="FreshRefill"/>: one grant of exactly the experience to the next level, only when a vital is below its maximum, never twice). The name check comes before the grant. Runs after the build has ended met and before the purge.
    /// </summary>
    private ScenarioStatus Refilling(IGameSessionView session)
    {
        if (WrongCharacter(session) is { } wrong)
        {
            return wrong;
        }

        switch (_refill!.Poll(session, out var failure))
        {
            case FreshRefillProgress.Asking:
                return ScenarioStatus.Running;
            case FreshRefillProgress.Failed:
                return Fail(session, failure!);
            default:
                if (_refill.Granted > 0)
                {
                    _steps.Add(string.Create(CultureInfo.InvariantCulture, $"refill xp {_refill.Granted:N0} (a level-up: {_refill.Field})"));
                }

                _log.Info(LogCategory, "the vitals are full (refill=" + _refill.Field + "); purging the old copies of " + _template.Name + " with the server mod");
                return Go("purging");
        }
    }

    /// <summary>
    /// M4 fresh B1 (D45): asks the mod to purge the old copies of the template's name until it answers <c>unknown-character</c> (<see cref="FreshPurge"/>). The name check comes before every ask. A re-created character whose old copy was not purged fails the run.
    /// </summary>
    private ScenarioStatus Purging(IGameSessionView session)
    {
        if (WrongCharacter(session) is { } wrong)
        {
            return wrong;
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
                    return Fail(session, "purge: the old copy was not purged (the mod answered unknown-character for the first ask, though the retail delete left the old " + _template.Name + " pending deletion)");
                }

                _purged = true;
                return PlaceOrFinish(session);
        }
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
        if (result.Learned == 0)
        {
            _knownChunks++; // a chunk that taught nothing is a check, not a build step: a second run adds nothing
        }

        return null;
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
        if (EndChecks(session) is { } notMet)
        {
            return notMet;
        }

        _finished = true;
        if (BuiltSteps == 0)
        {
            _log.Info(LogCategory, "nothing to add: the fighter already has its level, its stats, its magic and its kit");
        }

        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            return Go("done", ScenarioStatus.Succeeded);
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        return Go("logging-off");
    }

    /// <summary>The checks the build must pass to finish (the kit, the gear, the caster, the launcher), or the failure; null when it may finish. Changes nothing, so M4 fresh B1 runs it before the purge as well.</summary>
    private ScenarioStatus? EndChecks(IGameSessionView session)
    {
        Snapshot(session);
        if (_kit != "ok" || _wielded != _template.WieldedWeaponWcid || _worn != _template.Kit.Worn.Count(i => i.Role == FighterRole.Armour))
        {
            return Fail(session, $"the build ended short of the kit: kit={_kit} wielded={_wielded} worn={_worn}");
        }

        if (FinishGear(session) is { } gearFailure)
        {
            return gearFailure;
        }

        if (_caster == "hand")
        {
            return Fail(session, "the build ended with the caster in the hand: a fighter keeps it in the pack (CasterSwap wields it for a pass)");
        }

        var stillWielded = FighterItems.WieldedLauncher(session.State, _scratch) is { } launcher ? "the launcher '" + launcher.Name + "'" : FighterItems.WieldedAmmunition(session.State) != 0 ? "ammunition" : null;
        if (stillWielded is not null)
        {
            return Fail(session, $"the build ended with {stillWielded} still wielded: ACE refuses the Mace while a launcher is");
        }

        return null;
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

        foreach (var line in _gear.Items)
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

        foreach (var line in _template.Kit.Worn)
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

            if (_template.Kit.Gear?.PieceOf(item.Wcid) is { } gearPiece && FighterGearCheck.Refused(_template.Kit.Gear, FighterGearCheck.ArcaneLore(state.Self.Qualities)).Contains(gearPiece))
            {
                // M4 P5a C6: a named refusal, never a silent skip: ACE would equip the piece and leave its spells off (WorldObject_Use.cs:205-209 at ACE 95ef426d).
                return Fail(session, $"wielding: the {item.Name} (weenie {item.Wcid}) needs Arcane Lore {gearPiece.ItemDifficulty} and the character has {FighterGearCheck.ArcaneLore(state.Self.Qualities)?.ToString(CultureInfo.InvariantCulture) ?? "none (untrained or unknown)"}: train Arcane Lore (--fighter-recreate) before the gear");
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
    /// M4 3.1 fix2, the repair tool for a hunter a half-done weapon swap left wielding the Shortbow: ACE refuses the Mace (<c>CheckWeaponCollision</c>, <c>Player_Inventory.cs:1878-2056</c> at ACE 95ef426d, the melee branch from 1962: WeenieError 0) while a launcher is wielded, so a wielded
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

        // M4 P5a C1b: a buff pass that stopped half-way (CasterSwap) can leave the kit's caster in the hand, and ACE refuses the Mace while a Held item is wielded (CheckWeaponCollision, Player_Inventory.cs:1979-1997 at ACE 95ef426d).
        if (_template.Kit.HasCaster && FighterItems.Find(state, _scratch, _template.Kit.CasterWcid, out var caster, out var casterWielded) && casterWielded)
        {
            return TakeOff(session, caster, "the wielded caster");
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
    private uint BlockingPiece(IGameSessionView session, GearItem item)
    {
        var worn = session.State.Self.Inventory.Equipped;
        for (var i = 0; i < worn.Count; i++)
        {
            var e = worn[i];
            if (e.Location == 0 || (e.Location & (uint)item.ValidLocations) == 0 || !session.State.Objects.TryGet(e.Guid, out var o) || _template.Kit.ItemOf(o.Weenie.WeenieClassId) is not null)
            {
                continue;
            }

            return e.Guid;
        }

        return 0;
    }

    // ------------------------------------------------------------------------------------------------ the side packs and the gear's check (M4 P5a C6)

    /// <summary>
    /// Merges the next component stack into its line's stack in a side pack, or moves it into a side pack with room (<see cref="IActionApi.MoveToPack"/>), and returns Running, or null when every line is one stack in a side pack. The kit grants the packs before the components
    /// (<see cref="FighterKit.Items"/> order). M4 P5a fix r4: the item tool creates a top-up as a new stack ("created Gold Scarab ... in your pack"), so a rerun's top-up is merged into the line's stack in a side pack (<see cref="IActionApi.MergeStack"/>, GameAction 0x0054)
    /// instead of taking a slot of its own; round 4's Brawler had filled three packs of 24 with its 44 lines and the reruns' fragments. A fragment already in a side pack is merged the same way, so a character built before this fix is tidied by its next build.
    /// A ready character sends nothing. A pack with no room, or a merge or move the model does not show, fails by name.
    /// </summary>
    private ScenarioStatus? PackStep(IGameSessionView session)
    {
        var state = session.State;
        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown || !_gear.SidePackLines.Any())
        {
            return null;
        }

        var componentWcids = new HashSet<uint>(_gear.SidePackLines.Select(c => c.Wcid));
        if (MergeStep(session, componentWcids) is { } merged)
        {
            return merged;
        }

        foreach (var entry in inventory.Pack)
        {
            if (!state.Objects.TryGet(entry.Guid, out var o) || !componentWcids.Contains(o.Weenie.WeenieClassId) || inventory.Find(entry.Guid).Kind != InventoryPlaceKind.MainPack)
            {
                continue;
            }

            uint target = 0;
            for (var i = 0; i < inventory.SidePacks.Count && target == 0; i++)
            {
                if (inventory.ItemsIn(inventory.SidePacks[i].Guid).Count < FighterKit.SidePackSlots)
                {
                    target = inventory.SidePacks[i].Guid;
                }
            }

            if (target == 0)
            {
                return inventory.SidePacks.Count < _gear.SidePacks
                    ? Waiting(session, "the side packs are not in the inventory model yet")
                    : Fail(session, $"pack-full: no side pack has room for the {o.Weenie.Name} ({inventory.SidePacks.Count} side packs of {FighterKit.SidePackSlots} slots hold the {_gear.ComponentSlots} component lines; the kit carries {_gear.SidePacks})");
            }

            if (!_sent.Add("pack " + entry.Guid.ToString("X8", CultureInfo.InvariantCulture)))
            {
                return Fail(session, $"the side packs: the {o.Weenie.Name} was moved but the inventory model still shows it in the main pack");
            }

            return Send($"put the {o.Weenie.Name} into the side pack 0x{target:X8}", session.Actions.MoveToPack(entry.Guid, target, 0, ActionSource.Host));
        }

        return null;
    }

    /// <summary>
    /// M4 P5a fix r4: merges a component stack (in the main pack or a side pack) whole into another stack of its weenie that lies in a side pack and has room for it (the lowest guid first, so every rerun picks the same keeper), and returns Running, or null when no two stacks of a line can merge.
    /// ACE merges a whole stack only within its MaxStackSize (<c>Player_Inventory.cs</c> <c>HandleActionStackableMerge</c>, ACE 95ef426d); a pair that would pass it stays two stacks.
    /// </summary>
    private ScenarioStatus? MergeStep(IGameSessionView session, HashSet<uint> componentWcids)
    {
        var state = session.State;
        var inventory = state.Self.Inventory;
        var stacks = new List<(uint Guid, uint Wcid, int Size, int Max, bool InSide)>();
        foreach (var guid in HeldGuids(inventory))
        {
            if (state.Objects.TryGet(guid, out var o) && componentWcids.Contains(o.Weenie.WeenieClassId) && o.Weenie.StackSize is { } size && o.Weenie.MaxStackSize is { } max)
            {
                stacks.Add((guid, o.Weenie.WeenieClassId, (int)size, (int)max, inventory.Find(guid).Kind == InventoryPlaceKind.InSidePack));
            }
        }

        foreach (var keeper in stacks.Where(k => k.InSide).OrderBy(k => k.Guid))
        {
            foreach (var from in stacks)
            {
                if (from.Wcid != keeper.Wcid || from.Guid == keeper.Guid || (from.InSide && from.Guid < keeper.Guid) || keeper.Size + from.Size > keeper.Max)
                {
                    continue;
                }

                var name = state.Objects.TryGet(from.Guid, out var fo) ? fo.Weenie.Name : "stack";
                if (!_sent.Add(string.Create(CultureInfo.InvariantCulture, $"merge {from.Guid:X8} {keeper.Guid:X8}")))
                {
                    return Fail(session, $"the side packs: the {name} was merged but the inventory model still shows the stack 0x{from.Guid:X8}");
                }

                return Send(string.Create(CultureInfo.InvariantCulture, $"merge the {name} x {from.Size} into its stack 0x{keeper.Guid:X8} ({keeper.Size} of {keeper.Max})"), session.Actions.MergeStack(from.Guid, keeper.Guid, from.Size, ActionSource.Host));
            }
        }

        return null;
    }

    /// <summary>The guids of the items in the main pack and in every side pack.</summary>
    private static IEnumerable<uint> HeldGuids(PlayerInventory inventory)
    {
        foreach (var entry in inventory.Pack)
        {
            yield return entry.Guid;
        }

        for (var i = 0; i < inventory.SidePacks.Count; i++)
        {
            foreach (var entry in inventory.ItemsIn(inventory.SidePacks[i].Guid))
            {
                yield return entry.Guid;
            }
        }
    }

    /// <summary>
    /// The end checks of the gear and the pack: every piece in place (a named list otherwise), the main pack inside <see cref="GearKit.PackCapacity"/> minus <see cref="FighterKit.MainPackHeadroom"/> (the loot, a bought kit and the caster swap's unwield need the slots: a full pack
    /// refused all of them in live round 2), and the silent pieces named (a worn piece none of whose wearer spells is active: its mana ran out or it never activated; the journey's build-verify recharges the gear and wears such a piece again, <see cref="GearRecharge"/>). Null when the build may finish.
    /// </summary>
    private ScenarioStatus? FinishGear(IGameSessionView session)
    {
        if (_pack > GearKit.PackCapacity - FighterKit.MainPackHeadroom)
        {
            return Fail(session, $"pack-full: the main pack holds {_pack} of {GearKit.PackCapacity} items; the build keeps at least {FighterKit.MainPackHeadroom} slots free after the kit, so {GearKit.PackCapacity - FighterKit.MainPackHeadroom} is the most (the old loot is the cause: re-create the fighter or clear the pack)");
        }

        if (_gearReading is not { } reading)
        {
            return null;
        }

        if (!reading.Complete)
        {
            return Fail(session, $"the build ended short of the gear: gear={reading.Worn}/{reading.Total}, missing {string.Join(", ", reading.Short.Select(p => p.Name + " (weenie " + p.Wcid + ")"))}");
        }

        if (!_gearNoted)
        {
            _gearNoted = true;
            foreach (var piece in reading.Silent)
            {
                _log.Warn(LogCategory, $"the {piece.Name} (weenie {piece.Wcid}) is worn but none of its wearer spells is active: its mana ({piece.ItemMana} full, {piece.ManaHours:0.#} h at its rate) ran out or it never activated; {(_recharge is null ? "this build does not recharge the gear (the journey's build-verify does: GearRecharge)" : "the recharge did not wake it (see the gear mana lines)")}");
            }

            _log.Info(LogCategory, $"gear: {reading.Worn}/{reading.Total} in place, gear_magic={reading.Magic}, spells_active={reading.SpellsField}, pack={_pack}/{GearKit.PackCapacity}");
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------ the record

    private static readonly string[] AttributeNames = ["Strength", "Endurance", "Quickness", "Coordination", "Focus", "Self"];

    /// <summary>The first attribute whose creation value is not the template's, as <c>Name have/want</c>, or null when the six match (or the attributes are not known).</summary>
    private string? InnateMismatch(PlayerQualities q)
    {
        if (!q.IsKnown)
        {
            return null;
        }

        for (uint id = FighterTemplate.Strength; id <= FighterTemplate.Self; id++)
        {
            if (q.TryGetAttribute(id, out var attribute) && attribute.Start != _template.CreationValue(id))
            {
                return string.Create(CultureInfo.InvariantCulture, $"{AttributeNames[id - 1]} {attribute.Start}/{_template.CreationValue(id)}");
            }
        }

        return null;
    }

    private void Snapshot(IGameSessionView session)
    {
        var state = session.State;
        var q = state.Self.Qualities;
        if (q.IsKnown)
        {
            _mismatch = InnateMismatch(q);
            _level = q.Level;
            _check = _template.IsMet(q);
            if (_template.HasMagic)
            {
                // M4 P5a C1c (D38): the verdict names the schools the character has trained, in skill id order (creature, item, life).
                var trained = _template.TrainedLater.Where(id => q.TryGetSkill(id, out var skill) && skill.AdvancementClass >= HeadlessCharacter.TrainedSkill).ToArray();
                _magic = (trained.Length == _template.TrainedLater.Count ? "trained" : "short") + ":" + (trained.Length == 0 ? "none" : string.Join('+', trained.Select(SchoolShortName)));
            }
            else
            {
                _magic = "none";
            }
        }

        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return;
        }

        var counts = GearInventory.CountAll(state, out _);
        var short_ = new List<string>();
        foreach (var line in _gear.Items)
        {
            var have = counts.GetValueOrDefault(line.Wcid);
            if (have < line.Item.Stack)
            {
                short_.Add(string.Create(CultureInfo.InvariantCulture, $"{line.Item.Name.Replace(' ', '_')}:{have}/{line.Item.Stack}"));
            }
        }

        _kit = short_.Count == 0 ? "ok" : string.Join(',', short_);
        _caster = !_template.Kit.HasCaster ? "n/a" : FighterItems.Find(state, _scratch, _template.Kit.CasterWcid, out _, out var casterEquipped) ? (casterEquipped ? "hand" : "pack") : "none";
        _wielded = 0;
        _worn = 0;
        var wornArmour = new List<uint>();
        GearInventory.Collect(state, _scratch);
        foreach (var line in _template.Kit.Worn)
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

        _worn = _template.Kit.CountWornArmour(wornArmour);
        _pack = inventory.Pack.Count;
        if (_template.Kit.Gear is { } gearSet)
        {
            _gearReading = FighterGearCheck.Read(state, gearSet, _spellInfos, _scratch);
        }
    }

    /// <summary>The experience every raise of the plan still costs from the read model's spent experience (a stat at or above its target costs nothing; a stat the description lacks is left to the raise step to fail by name).</summary>
    private ulong RemainingPlanCost(PlayerQualities q)
    {
        ulong sum = 0;
        foreach (var target in _template.Targets)
        {
            if (ReadStat(q, target, out var ranks, out var spent) && ranks < target.Ranks)
            {
                sum += target.XpRemaining(_table, spent);
            }
        }

        return sum;
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

    private ScenarioStatus Send(string label, ActionHandle handle, Func<string?>? onConfirmed = null)
    {
        _wait.Reset();
        _log.Info(LogCategory, label);
        _steps.Add(label);
        _inFlight = new InFlight(label, handle, onConfirmed);
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

    private sealed record InFlight(string Label, ActionHandle Handle, Func<string?>? OnConfirmed = null);
}
