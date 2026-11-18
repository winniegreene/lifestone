using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>Why an <see cref="XpSpendRule"/> run ended (<see cref="XpSpendRule.End"/>).</summary>
public enum XpSpendEnd
{
    /// <summary>The run has not ended (or has not begun).</summary>
    None,

    /// <summary>The unassigned experience does not cover the next rank of any stat the plan still raises (each such stat is named once and passed over; the pool grows until one is covered). Not a failure: with nothing to cover, <c>spent_xp=0</c>.</summary>
    PoolCoversNothing,

    /// <summary>Every stat the planner raises is at its planned rank for the experience the character has earned, or (M6 item 14) is held to the next allocation by the account of the owner's caps: nothing is left to raise now.</summary>
    PlanReached,

    /// <summary>The raises of one call reached <see cref="XpSpendRule.MaxRaises"/> (Lifestone's own bound); more may be left, and the caller may call again.</summary>
    RaiseCap,

    /// <summary>The call's time bound (<see cref="XpSpendRule.MaxSeconds"/>, Lifestone's own) passed; no further raise was sent.</summary>
    TimeBound,

    /// <summary>A raise was refused or did not complete (<see cref="XpSpendRule.Failure"/> names it). Nothing is retried.</summary>
    Refused,

    /// <summary>The character's description has not arrived: its stats and experience are unknown (<see cref="XpSpendRule.Failure"/> says so).</summary>
    Unread,

    /// <summary>A raise ran under dry run: it was logged and not sent, so nothing was spent.</summary>
    DryRun,
}

/// <summary>
/// The mage's experience spending (M5 item 3 m4; spec section 2 DoD 6, section 5.5): whenever the unassigned experience covers the next rank of a stat <see cref="MageSpendingPlanner"/>'s order still raises, raise it (the first
/// stat of the order that the pool covers) through ACE's raise actions (<see cref="IActionApi.RaiseSkill"/>, <see cref="IActionApi.RaiseAttribute"/>, <see cref="IActionApi.RaiseVital"/>), one rank and one action at a time,
/// waiting for the server's answer (the stat's private update, or ACE's told refusal, or the action's timeout) before the next. A small runner a station or a loop calls: <see cref="Begin(IGameSessionView, IActionApi)"/>, <see cref="Tick"/> until <see cref="Done"/>, then
/// <see cref="Fields"/> (<c>spent_xp=&lt;sum&gt; raises=&lt;n&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>The order.</b> The planner (<see cref="MageSpendingPlanner.Compute"/>) turns an amount of experience into target ranks, listed in its order: the four magic skills, Focus and Self, Mana Conversion, Max Mana, then the
/// pool of the rest. The rule computes the plan for <i>all</i> the experience the character has earned (<c>TotalExperience</c>: what is spent on stats plus the unassigned pool; ACE spends only the available part), so the
/// plan is the character as the planner would have built it from nothing. Walking the plan in its order, the first stat whose ranks are under its target is the one raised next, by one rank, for exactly that rank's
/// cost (the rank's cumulative XpTable entry less the experience already spent on it). The price list is the stat's own class (a specialised skill's list prices a specialised skill). A stat whose next rank the pool does not
/// cover is passed over, named once (<see cref="Unaffordable"/>, <see cref="FirstUnaffordable"/>), and the walk goes on down the order to the first stat the pool does cover (M5 item 3 fix F3, review 3-R7: the spec's "whenever
/// the unassigned pool covers the next rank" is read per stat, so one dear rank never keeps a cheaper planned rank unbought). When no planned stat's next rank is covered the run ends with
/// <see cref="XpSpendEnd.PoolCoversNothing"/> and <see cref="PoolNote"/> names the pool and the cheapest next rank; the pool grows until one is covered.</para>
/// <para><b>ACE's rules this rests on</b> (all ACEmulator/ACE@95ef426d, <c>Source/ACE.Server/WorldObjects</c>). The raise actions: <c>Player_Attributes.cs</c> <c>HandleActionRaiseAttribute</c> 13-58 (an amount over the available
/// experience is dropped, 21-26; a refused spend sends "Your attempt to raise {attribute} has failed.", 29-33), <c>Player_Vitals.cs</c> <c>HandleActionRaiseVital</c> 20-64 (an amount over the available experience sends the same
/// line, 28-39), <c>Player_Skills.cs</c> <c>HandleActionRaiseSkill</c> 21-62 (a skill that is not trained or specialised, or an amount over the available experience, is dropped with a log line only, 25-35: no refusal
/// reaches the client). Each spend (<c>SpendAttributeXp</c> 75-107, <c>SpendVitalXp</c> 67-99, <c>SpendSkillXp</c> 69-110): a stat at its maximum rank is refused (80, 72, 81), an amount over the experience left to the
/// maximum rank is dropped whole (86-91, 78-83, 87-92), the experience comes off the pool by <c>Player_Xp.cs</c> <c>SpendXP</c> 352-363 (which also sends the AvailableExperience update, the first answer to a raise), then
/// the experience spent is added (102, 94, 103) and the ranks are recomputed from the experience spent in total (105, 97, 106: <c>CalcAttributeRank</c>, <c>CalcVitalRank</c>, <c>CalcSkillRank</c>). So a rank's price is
/// the XpTable's cumulative difference, ACE takes any amount up to the maximum rank (one rank at a time is this rule's choice), and the ranks follow the experience spent, not the number of raises.</para>
/// <para><b>Never off the plan.</b> Only a stat the plan lists with ranks above the character's is raised. A stat the planner does not raise is never touched. A skill the plan lists but the character holds untrained
/// (or does not have), and a stat at its rank cap, are skipped and counted (<see cref="Skipped"/>, <see cref="FirstSkip"/>): ACE tells no refusal for an untrained skill's raise, so it is not sent.</para>
/// <para><b>The account (M6 item 14; the owner's standing rule, D112).</b> For the ladder's character the owner's caps are hard rules of the spend, not only of the plan: Max Health and Endurance about 5% of the experience earned each, no attribute over 10%, the rest to
/// the magic skills of the named list (<see cref="MageTemplate.OnClimberList"/>: a stat off it is never raised, whatever a plan lists). Every raise of a bound stat is checked twice (<see cref="Account"/>): against its running share of all the experience earned, and
/// against the same percent of this allocation (the unassigned experience the first call found; a call that follows a raise-cap end goes on with the same allocation), so no allocation catches a stat up to its cap in one go. <b>Floors against caps:</b> the cap wins.
/// The Strength floor is cut to the 10% line and the health floor to 5% a stat, a floor the account holds does not make the pool wait, and a floor that wants more than its cap allows is said once by name (<c>xp: cap:</c>, <see cref="CapConflicts"/>). The one
/// exception is a load she cannot run under (<see cref="StrengthFloor.CannotRun"/>): the least Strength that cures it (<see cref="StrengthFloor.CureWant"/>) is bought over both checks and named at every call (<c>xp: cure:</c>). What no rule's next rank takes stays
/// unassigned (banked) and the summary line says so (<see cref="SharesNote"/>, <c>xp: shares:</c>).</para>
/// <para><b>The caster's points (M6 14, <c>m6-caster-spend</c>; the owner's ruling, 2026-11-14: she should definitely use the fire wand she has).</b> For the ladder's character, given the casters she holds (<c>casterFacts</c>): when one short of a skill-base
/// wield requirement beats her held caster by the swap margin as if wielded, and its points can be bought from this allocation and the next one or two, the best of them by its gain for the experience has its ranks pushed in the magic skills'
/// share (<see cref="CasterSpend"/>): after the floors and before every share, each raise named (<c>xp: raise ... (the caster's points ...)</c>), the decision and every other caster on their lines (<c>xp: caster:</c>). The caps stand (no skill is bound
/// by them) and the rest of the allocation is spent as before. One out of reach is logged with its numbers and changes nothing.</para>
/// <para><b>Arcane Lore (M6 14, <c>m6-lore-spend</c>; the owner, 2026-11-14: Arcane Lore is trained and rises with experience, the spend raises it when gear she holds needs it).</b> For the ladder's character, given the pieces she holds whose spells her
/// Arcane Lore alone keeps off (<c>loreFacts</c>): the lore toward the lowest ItemDifficulty whose piece the wear pick would wear with it there, when that gain beats what the same experience brings her magic skills and the ranks are in reach, is
/// bought after the floors and before every share (<see cref="LoreSpend"/>; the planner's <c>beforeShares</c>). The one skill off the named list her spend may raise, and only so. The target is the lore ACE reads at the wield, the CURRENT one
/// (<see cref="StandingLore"/>: buffed, the bot's lines and the worn pieces' lasting spells counted, her own buffs not). Each call logs every piece's reading (<c>xp: lore:</c>) and each raise names the piece (<c>xp: raise Arcane Lore ... (the Arcane Lore rule ...)</c>).</para>
/// <para><b>Bounds</b> (Lifestone's own numbers, not retail's): <see cref="MaxRaisesPerCall"/> raises and <see cref="MaxSecondsPerCall"/> seconds of the session clock a call. The time bound stops new sends; a raise in flight
/// finishes first (the Action API times it out itself).</para>
/// <para><b>Source.</b> The raises are <see cref="ActionSource.Automation"/>: the kill switch cancels them and dry run logs them without sending (<see cref="XpSpendEnd.DryRun"/>).</para>
/// <para><b>Not a failure:</b> a pool that covers no planned next rank ends the run with <c>spent_xp=0 raises=0</c> (or the sum of what it did cover) and no <see cref="Failure"/>. A refusal ends it with <see cref="Failure"/> naming the stat, the outcome and ACE's line.</para>
/// </remarks>
public sealed class XpSpendRule
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "xp";

    /// <summary>The raises one call may send. Lifestone's own bound: a long-neglected pool would otherwise be spent in one unbroken run.</summary>
    public const int MaxRaisesPerCall = 60;

    /// <summary>The seconds, on the session clock, after which one call sends no further raise. Lifestone's own bound.</summary>
    public const double MaxSecondsPerCall = 120;

    private readonly XpTableData _table;
    private readonly ILogSink? _log;
    private IGameSessionView? _view;
    private IActionApi? _actions;
    private SpendingPlan? _plan;
    private readonly HashSet<(MageStatKind Kind, uint Id)> _skipped = [];
    private readonly HashSet<(MageStatKind Kind, uint Id)> _unaffordable = [];
    private (string Name, int Ranks, ulong Cost)? _cheapest;
    private long _pool;
    private ActionHandle? _pending;
    private StatTarget? _pendingTarget;
    private ulong _pendingCost;
    private double _start;
    private bool _begun;

    // M6 item 14: the allocation's account (see the remarks' "The account").
    private readonly Dictionary<(MageStatKind Kind, uint Id), long> _spentHere = [];
    private readonly HashSet<(MageStatKind Kind, uint Id)> _held = [];
    private readonly List<string> _heldNames = [];
    private readonly List<(string Key, string Line)> _said = [];
    private long _allocation = -1;
    private long _allocationSpent;
    private Grounds.Ladder? _ladder;
    private int _cureRanks;
    private readonly Func<GameState, long>? _sellLootBurden;

    /// <summary>A rule over the XpTable.</summary>
    /// <param name="table">The dats' XpTable (the planner's and the prices').</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="maxRaises">The raises one call may send (default <see cref="MaxRaisesPerCall"/>).</param>
    /// <param name="maxSeconds">The time bound of one call (default <see cref="MaxSecondsPerCall"/>).</param>
    /// <param name="followLevel">M5 7 F3 (D87): plan with the shares of the character's level (<see cref="MageTemplate.SharesAt"/>) and leave out the skills of <see cref="MageTemplate.TrainedLater"/> it has not trained (the soak mage at level 80 has no Item Enchantment); false (the default): the D19 split over every skill.</param>
    /// <param name="burdenTripPercent">M6 1 b (<c>m6-climber-strength</c>): the ground's <c>burden_trip_percent</c> (<see cref="Grounds.GroundSupplies.BurdenTripPercent"/>, data): the plan raises Strength first until the capacity carries the measured
    /// burden under <see cref="StrengthFloor.LinePercent"/> of it (<see cref="StrengthFloor"/>), logged on its own line (<c>xp: strength: burden= capacity= want=</c>); null (the default): no floor.</param>
    /// <param name="healthLadder">M6 7 R: the ladder whose health floor the plan raises first (<see cref="ClimberHealthRule"/>); null (the default): the war mage's ladder for the ladder's character (<see cref="MageTemplate.ClimberName"/>, every spend she makes: the level-up's, the trip's), no floor for any other (a test passes its own).</param>
    /// <param name="standingBot">M6 item 14 (armour instead of the robe): null (the default): the Strength floor reads the base Strength alone, as before. Given: the floor also counts the Strength that stands beside the ranks
    /// (<see cref="StandingStrength"/>: a worn piece's own lasting line, and the buff bot's line while this answers true, the stop at the bot applying to the run), so the armour set's burden is not paid in ranks before every share.</param>
    /// <param name="casterFacts">M6 14 (<c>m6-caster-spend</c>): the casters she holds short of a wield requirement, scored as if wielded, and her measured rate (<see cref="CasterSpendFacts"/>), read at each call's plan for the ladder's character:
    /// the points of the best one worth it and in reach are bought first (<see cref="CasterSpend"/>, the remarks' "The caster's points"). Null (the default): no caster is read, the plan of before.</param>
    /// <param name="loreFacts">M6 14 (<c>m6-lore-spend</c>): the pieces she holds whose spells her Arcane Lore alone keeps off, with the wear pick's verdict at their ItemDifficulty, her standing lore and the pick's terms (<see cref="LoreSpendFacts"/>), read at each
    /// call's plan for the ladder's character: the Arcane Lore toward the lowest difficulty whose piece pays is bought after the floors and before every share (<see cref="LoreSpend"/>, the remarks' "Arcane Lore"). Null (the default): no lore is read.</param>
    /// <param name="sellLootBurden">
    /// M6 14 hour load (<c>m6-hour-load</c>): the burden of the sell loot she carries (<see cref="CantripRoom.SellLootBurden"/>), which the Strength floor leaves out of the burden it carries: the trip sells it, and on a comfortable ground the hour's
    /// line carries it to 150% of the capacity by design (the trip's spend runs before the sale; live ladder37 03:17:02 UTC read 18,279 there). Null (the default): the measured burden whole, as before.
    /// </param>
    public XpSpendRule(XpTableData table, ILogSink? log = null, int maxRaises = MaxRaisesPerCall, double maxSeconds = MaxSecondsPerCall, bool followLevel = false, int? burdenTripPercent = null, Grounds.Ladder? healthLadder = null, Func<bool>? standingBot = null, Func<CasterSpendFacts?>? casterFacts = null, Func<LoreSpendFacts?>? loreFacts = null, Func<GameState, long>? sellLootBurden = null)
    {
        _sellLootBurden = sellLootBurden;
        _loreFacts = loreFacts;
        _casterFacts = casterFacts;
        _standingBot = standingBot;
        _healthLadder = healthLadder;
        _followLevel = followLevel;
        _burdenTripPercent = burdenTripPercent;
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRaises, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeconds);
        _table = table;
        _log = log;
        MaxRaises = maxRaises;
        MaxSeconds = maxSeconds;
    }

    /// <summary>The raises one call may send.</summary>
    public int MaxRaises { get; }

    /// <summary>The time bound of one call, in session-clock seconds.</summary>
    public double MaxSeconds { get; }

    /// <summary>True once the run has ended (<see cref="End"/> says why); false before <c>Begin</c> and while it runs.</summary>
    public bool Done { get; private set; }

    /// <summary>Why the run ended, or <see cref="XpSpendEnd.None"/> while it runs.</summary>
    public XpSpendEnd End { get; private set; }

    /// <summary>The experience spent by the raises this call confirmed.</summary>
    public long SpentXp { get; private set; }

    /// <summary>The raises this call confirmed.</summary>
    public int Raises { get; private set; }

    /// <summary>The stats the plan lists above the character's ranks that were skipped (untrained, missing, at the rank cap, or priced inconsistently).</summary>
    public int Skipped => _skipped.Count;

    /// <summary>The first skip, named, or null.</summary>
    public string? FirstSkip { get; private set; }

    /// <summary>The stats the plan raises whose next rank the pool did not cover, each named once this call and passed over for the next stat of the order (not skips: a later call may cover them).</summary>
    public int Unaffordable => _unaffordable.Count;

    /// <summary>The first stat the pool did not cover, as its log line names it ("... does not cover the next rank of X (a -> b, n)"), or null.</summary>
    public string? FirstUnaffordable { get; private set; }

    /// <summary>
    /// When the run ended <see cref="XpSpendEnd.PoolCoversNothing"/>: the unassigned experience and the price of the cheapest next rank of the stats the plan still raises
    /// (<c>xp_pool=&lt;n&gt; next_rank_xp=&lt;n&gt; (&lt;stat&gt; a -&gt; b)</c>), so a station can say why <c>spent_xp</c> is 0 (review 3-R7); null otherwise.
    /// </summary>
    public string? PoolNote => End == XpSpendEnd.PoolCoversNothing && _cheapest is { } c
        ? string.Create(CultureInfo.InvariantCulture, $"xp_pool={_pool} next_rank_xp={c.Cost} ({c.Name} {c.Ranks} -> {c.Ranks + 1})")
        : null;

    /// <summary>The named reason the run could not go on (a refused raise, an unread character), or null; a pool that covers nothing is not a failure.</summary>
    public string? Failure { get; private set; }

    /// <summary>The plan this call spends toward (for the earned experience at <c>Begin</c>), or null before it.</summary>
    public SpendingPlan? Plan => _plan;

    /// <summary>The verdict fields: <c>spent_xp=&lt;sum&gt; raises=&lt;n&gt;</c>.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"spent_xp={SpentXp} raises={Raises}");

    /// <summary>Begins a call over the session, sending through its own Action API.</summary>
    /// <param name="view">The session view (the read model and the clock).</param>
    public void Begin(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Begin(view, view.Actions);
    }

    /// <summary>Begins a call: the fields are zeroed, the plan is made for the experience earned so far, and the next <see cref="Tick"/> sends the first raise the pool covers.</summary>
    /// <param name="view">The session view (the read model and the clock).</param>
    /// <param name="actions">The Action API the raises go through.</param>
    public void Begin(IGameSessionView view, IActionApi actions)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(actions);
        _view = view;
        _actions = actions;
        _pending = null;
        _pendingTarget = null;
        _start = view.Clock.Now;
        if (!(_begun && End == XpSpendEnd.RaiseCap))
        {
            // M6 item 14: a new allocation. A call that follows one the raise cap ended goes on with the same allocation and its account, so the callers' second and third calls cannot each take a stat's share again.
            _spentHere.Clear();
            _held.Clear();
            _heldNames.Clear();
            _allocation = -1;
            _allocationSpent = 0;
        }

        _begun = true;
        Done = false;
        End = XpSpendEnd.None;
        SpentXp = 0;
        Raises = 0;
        _skipped.Clear();
        _unaffordable.Clear();
        _cheapest = null;
        _pool = 0;
        FirstUnaffordable = null;
        FirstSkip = null;
        Failure = null;
        var q = view.State.Self.Qualities;
        StrengthNote = null;
        HealthNote = null;
        SharesNote = null;
        CasterDecision = null;
        LoreDecision = null;
        _plan = q.IsKnown ? PlanFor(q) : null;
    }

    /// <summary>
    /// M6 item 14: the allocation's one summary line (<c>shares: allocation= spent= banked= earned= | &lt;stat&gt; &lt;xp&gt; &lt;percent of the allocation&gt; run &lt;percent of the experience earned&gt;[/&lt;its cap&gt;] | ...</c>), written when the call ends; null before
    /// that and for an unread character. A watcher checks a spend against the owner's caps from this line alone.
    /// </summary>
    public string? SharesNote { get; private set; }

    /// <summary>M6 item 14: the unassigned experience the call left (banked: no rule's next rank took it), or 0 before the call ends.</summary>
    public long Banked { get; private set; }

    /// <summary>M6 item 14: the stats the account held to the next allocation this allocation (a share of the allocation, or of the experience earned, would pass the stat's cap), in the order they were held.</summary>
    public IReadOnlyList<string> Held => _heldNames;

    /// <summary>M6 item 14: the lines that say a floor wanted more than a cap allows, each said once by this rule (<c>cap: Strength: ...</c>, <c>cap: health: ...</c>), in the order said.</summary>
    public IReadOnlyList<string> CapConflicts => [.. _said.Select(s => s.Line)];

    /// <summary>The Strength floor's line of this call (<c>strength: burden=&lt;b&gt; capacity=&lt;c&gt; want=&lt;s&gt; ...</c>), or null with no floor asked or before the plan.</summary>
    public string? StrengthNote { get; private set; }

    /// <summary>M6 7 R: the health floor's line of this call (<c>health: now= want= ...</c>), or null with no floor on the rung or before the plan.</summary>
    public string? HealthNote { get; private set; }

    /// <summary>The XpTable a spend prices with: the session's (the game session loads it from the dats), else the journey's dats', else null (<c>TODO(m5-logistics-xptable)</c>: the caller fails by name, nothing is priced).</summary>
    /// <param name="view">The session view.</param>
    /// <param name="dats">The journey's dats, or null.</param>
    public static XpTableData? TableOf(IGameSessionView view, Lifestone.Data.Dat.DatSet? dats)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.XpTable ?? (dats is { } d ? XpTableData.Load(d) : null);
    }

    /// <summary>
    /// M6 1 b: the Strength floor's ranks for the character now (<see cref="StrengthFloor"/>): the measured burden (the server's EncumbranceValue, <see cref="PlayerInventory.Burden"/>), the carrying augmentations and the base
    /// Strength's capacity, against the ground's line. 0 with no floor asked; 0 and a loud line when the burden or the Strength is not read (<c>TODO(m6-strength-burden-unread)</c>).
    /// </summary>
    private int FloorRanks(PlayerQualities q)
    {
        _cureRanks = 0;
        _floorBurden = null;
        if (_burdenTripPercent is not { } trip)
        {
            return 0;
        }

        var line = StrengthFloor.LinePercent(trip);
        var burden = _view?.State.Self.Inventory.Burden;
        if (burden is not { } b || !q.TryGetAttribute(MageTemplate.Strength, out var strength))
        {
            StrengthNote = $"strength: TODO(m6-strength-burden-unread): the {(burden is null ? "burden (EncumbranceValue)" : "Strength")} is not read, so no floor is raised this call";
            _log?.Warn(LogCategory, "xp: " + StrengthNote);
            return 0;
        }

        _ = q.TryGetInt(Fighter.LootContext.AugmentationCarryingProperty, out var augmentations);
        var current = (int)(strength.Start + strength.Ranks);
        // M6 item 14: the Strength that stands beside the ranks (a worn piece's own lasting line; the buff bot's while its stop applies) carries burden too: the floor wants the base Strength whose capacity WITH it carries the burden.
        var standingWhy = string.Empty;
        var standing = _standingBot is { } bot ? StandingStrength.Points(q.Enchantments, (uint)current, bot(), out standingWhy) : 0;
        var capacity = Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity(current + standing, augmentations);
        var percent = capacity > 0 ? (100.0 * b / capacity).ToString("0", CultureInfo.InvariantCulture) : "?";
        var sell = _sellLootBurden is { } read && _view is { } view ? Math.Clamp(read(view.State), 0, b) : 0; // M6 14 hour load: the loot the trip sells is no load the floor carries
        var counted = (int)(b - sell);
        var want = Math.Max(1, StrengthFloor.Want(counted, augmentations, line) - standing);
        var ranks = StrengthFloor.Ranks(want, strength.Start);
        _floorBurden = (b, capacity, current, want);
        if (StrengthFloor.CannotRun(b, capacity))
        {
            // M6 item 14, the one case a floor passes the cap: a load she cannot run under. The least Strength that puts it under the run line is bought whatever the caps say, and it is named at every call that buys it.
            var cure = Math.Max(1, StrengthFloor.CureWant(b, augmentations) - standing);
            _cureRanks = StrengthFloor.Ranks(cure, strength.Start);
            _log?.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: cure: Strength: burden {b} is {percent}% of capacity {capacity}, at or over the run line {StrengthFloor.RunLinePercent}%: she cannot run; base Strength {current} -> {cure} ({_cureRanks} ranks over the start) is bought whatever the caps say, the least that brings her under the line; the rest of the floor (to {want}) stays under the cap"));
        }

        var verdict = want > current ? string.Create(CultureInfo.InvariantCulture, $": the floor raises Strength {current} -> {want} ({ranks} ranks over the start) before the shares") : ": carried, no floor rank to buy";
        StrengthNote = string.Create(CultureInfo.InvariantCulture, $"strength: burden={b}{(_sellLootBurden is null ? string.Empty : $" sell_loot={sell} counted={counted}")} capacity={capacity} want={want} (base Strength {current}{(_standingBot is null ? string.Empty : ", " + standingWhy)}, {percent}% now; the line {line}% is the ground's burden_trip_percent {trip}, at most 100){verdict}");
        _log?.Info(LogCategory, "xp: " + StrengthNote);
        return ranks;
    }

    /// <summary>
    /// M6 7 R: the health floor for the character now (<see cref="ClimberHealthRule"/>): the ladder's need at its level against the base health read from its own ranks, the purchases priced from those ranks. Null with no floor on the rung, or for a character the
    /// ladder does not bind; null and a loud line when Endurance or Max Health is not read (<c>TODO(m6-health-floor-unread)</c>).
    /// </summary>
    private HealthBuy? HealthFloorBuy(PlayerQualities q)
    {
        if (_followLevel && _healthLadder is null && _view?.State.Self.Name is null)
        {
            _log?.Warn(LogCategory, "xp: health: TODO(m6-health-floor-name): the character's name is not read, so this call cannot tell the ladder's character: no floor");
        }

        var ladder = _ladder;
        if (ladder is null || ClimberHealthRule.NeedAt(ladder, q.Level) is not { } need)
        {
            return null;
        }

        if (!q.TryGetAttribute(MageTemplate.Endurance, out var endurance) || !q.TryGetVital(MageTemplate.MaxHealth, out var health))
        {
            HealthNote = "health: TODO(m6-health-floor-unread): Endurance or Max Health is not in the description, so no floor is raised this call";
            _log?.Warn(LogCategory, "xp: " + HealthNote);
            return null;
        }

        var ranks = (int)health.Ranks;
        var now = ClimberHealthRule.BaseHealth(endurance.Start + endurance.Ranks, (int)health.Start + ranks);
        var buy = ClimberHealthRule.Buy(_table, need.Want - (int)health.Start, endurance.Start, (int)endurance.Ranks, ranks, ladder.StatCap?.Aim((ulong)Math.Max(0, q.TotalExperience)) ?? ClimberHealthRule.Budget((ulong)Math.Max(0, q.TotalExperience), need.PayPercent));
        HealthNote = ClimberHealthRule.Line(need, now, buy with { Health = buy.Health + (int)health.Start });
        _log?.Info(LogCategory, "xp: " + HealthNote);
        var reach = Math.Max(now, buy.Health + (int)health.Start);
        if (buy.Limited && reach < need.Want && ladder.StatCap is { } cap)
        {
            SayOnce("health", string.Create(CultureInfo.InvariantCulture, $"cap: health: the floor wants base health {need.Want} on the rung {need.Rung} and the cap allows {reach} ({need.Want - reach} short): Max Health and Endurance are each held to {cap.AimPercent}% of the experience earned ({cap.Aim((ulong)Math.Max(0, q.TotalExperience)):N0} of {q.TotalExperience:N0}); the cap wins (the owner's rule, D112): what the floor cannot buy goes to her magic"));
        }

        return buy;
    }

    /// <summary>
    /// The plan for the character's experience (M5 7 F3, D87): the shares of its level (<see cref="MageTemplate.SharesAt"/>) over a plan that leaves out the skills of <see cref="MageTemplate.TrainedLater"/> the character has not trained, so
    /// the share of a skill the build's credits could not pay for goes to the stats that can be raised instead of being skipped.
    /// </summary>
    private SpendingPlan PlanFor(PlayerQualities q)
    {
        _ladder = _healthLadder ?? (_followLevel && CharacterPolicy.For(_view?.State.Self.Name).WarLadder ? Grounds.LadderLoader.WarMage : null);
        if (_allocation < 0)
        {
            _allocation = Math.Max(0, q.AvailableExperience); // M6 item 14: the allocation is the unassigned experience its first call found
        }

        var floor = FloorRanks(q);
        if (!_followLevel)
        {
            return MageSpendingPlanner.Compute(_table, (ulong)Math.Max(0, q.TotalExperience), strengthFloorRanks: floor);
        }

        var cap = _ladder?.StatCap;
        var health = HealthFloorBuy(q);
        var pushes = PushOf(q);
        IReadOnlyList<StatTarget>? first = null;
        SpendingPlan Compute(IReadOnlyList<SkillPush> push) => MageSpendingPlanner.Compute(
            _table,
            (ulong)Math.Max(0, q.TotalExperience),
            [.. MageTemplate.TrainedLater.Where(id => !q.TryGetSkill(id, out var skill) || skill.AdvancementClass < HeadlessCharacter.TrainedSkill)],
            MageTemplate.SharesFor(q.Level, cap is not null), // M6 item 14: the ladder's character spends by her own split (MageShares.Climber: the named list), every other mage as before
            q.TryGetSkill(MageTemplate.MeleeDefense, out var melee) && melee.AdvancementClass >= HeadlessCharacter.TrainedSkill ? MageBuild.Gate126 : MageBuild.Traditional,
            floor,
            health,
            push,
            cap,
            _cureRanks,
            first);
        var plan = Compute(pushes);
        if (_ladder is not null && _loreFacts?.Invoke() is { } lore)
        {
            // M6 14 (m6-lore-spend): the Arcane Lore toward a held piece's ItemDifficulty, after the floors (their owed experience comes off this allocation first) and before every share; the caster's points after it.
            var decision = LoreSpend.Decide(_table, q, lore, q.AvailableExperience, FloorsOwed(q, plan));
            LoreDecision = decision;
            _log?.Info(LogCategory, "xp: lore: " + decision.Line);
            foreach (var other in decision.Others)
            {
                _log?.Info(LogCategory, "xp: lore: " + other);
            }

            if (decision.Target is { } target)
            {
                first = [target];
                plan = Compute(pushes);
            }
        }

        if (_ladder is not null && _casterFacts?.Invoke() is { } facts)
        {
            // M6 14 (m6-caster-spend): the caster's points before every share, after the floors (the floors' owed experience comes off this allocation first, and the Arcane Lore rule's); the buff pushes after them in the same share.
            var decision = CasterSpend.Decide(_table, q, facts, q.AvailableExperience, FloorsOwed(q, plan) + (LoreDecision?.Cost ?? 0), MageTemplate.OnClimberList);
            CasterDecision = decision;
            _log?.Info(LogCategory, "xp: caster: " + decision.Line);
            foreach (var other in decision.Others)
            {
                _log?.Info(LogCategory, "xp: caster: " + other);
            }

            if (decision.Pushes.Count > 0)
            {
                plan = Compute([.. decision.Pushes, .. pushes]);
            }
        }

        if (cap is not null && _floorBurden is { } f && floor > 0 && q.TryGetAttribute(MageTemplate.Strength, out var strength))
        {
            var planned = plan.Targets.FirstOrDefault(t => t.Floor && t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Strength)?.Ranks ?? 0;
            if (planned < floor && floor > (int)strength.Ranks)
            {
                // M6 item 14: the floor wants more than the cap allows. The cap wins (the owner's rule, D112), and it is said once by name so the lead sees the load she is left with.
                var total = (ulong)Math.Max(0, q.TotalExperience);
                var list = _table.AttributeXpList;
                var price = floor < list.Count ? list[floor].ToString("N0", CultureInfo.InvariantCulture) : "past the rank cap";
                SayOnce("strength", string.Create(CultureInfo.InvariantCulture, $"cap: Strength: the floor wants base Strength {f.Want} ({floor} ranks over the start, {price} in all) and the cap allows {Math.Max(planned, (int)strength.Ranks)} ranks: no attribute over {cap.MaxPercent}% of the experience earned ({cap.Max(total):N0} of {total:N0}); the cap wins (the owner's rule, D112): she carries {f.Burden} on capacity {f.Capacity} ({(f.Capacity > 0 ? 100L * f.Burden / f.Capacity : 0)}%), slower above 100%, until a trip lightens her{(_cureRanks > 0 ? "; the run-line cure is bought over the cap (the line before)" : string.Empty)}"));
            }
        }

        return plan;
    }

    /// <summary>
    /// M6 14 (<c>m6-caster-spend</c>): the experience the floors' targets of <paramref name="plan"/> still want over what she has spent on them (the Strength floor, the health floor's two): what the walk takes from the pool before any share, so the
    /// caster's points are priced against the rest.
    /// </summary>
    private long FloorsOwed(PlayerQualities q, SpendingPlan plan)
    {
        long owed = 0;
        foreach (var t in plan.Targets)
        {
            if (t.Floor && Read(q, t, out var ranks, out var spent, out var list, out _) && ranks < t.Ranks && t.Ranks < list!.Count)
            {
                owed += Math.Max(0, (long)list[t.Ranks] - spent);
            }
        }

        return owed;
    }

    /// <summary>M6 14 (<c>m6-caster-spend</c>): the caster decision of this call (<see cref="CasterSpend.Decide"/>), or null when no caster was read (no facts, or not the ladder's character).</summary>
    public CasterSpendDecision? CasterDecision { get; private set; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the Arcane Lore decision of this call (<see cref="LoreSpend.Decide"/>), or null when no lore was read (no facts, or not the ladder's character).</summary>
    public LoreSpendDecision? LoreDecision { get; private set; }

    private readonly Func<CasterSpendFacts?>? _casterFacts;
    private readonly Func<LoreSpendFacts?>? _loreFacts;

    /// <summary>M6 14 (<c>m6-lore-spend</c>): true for the Arcane Lore rule's target of this call: the one stat off the named list the ladder's character's spend may raise.</summary>
    private bool IsLoreTarget(StatTarget t) => LoreDecision?.Target is { } lore && t.Kind == lore.Kind && t.Id == lore.Id;

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the raise line's note for the Arcane Lore rule's ranks, else empty.</summary>
    private string LoreRaiseNote(StatTarget t, int ranks) =>
        LoreDecision is { Chosen: { } piece, Target: { } lore } && IsLoreTarget(t) && ranks < lore.Ranks
            ? string.Create(CultureInfo.InvariantCulture, $" (the Arcane Lore rule: toward '{piece.Name}' 0x{piece.Guid:X8}'s ItemDifficulty {piece.Difficulty}, before the shares)")
            : string.Empty;

    /// <summary>M6 14 (<c>m6-caster-spend</c>): the raise line's note when <paramref name="t"/> is a skill the caster decision pushes and <paramref name="ranks"/> is under its push, else empty.</summary>
    private string CasterRaiseNote(StatTarget t, int ranks) =>
        CasterDecision is { Chosen: { } chosen } d && t.Kind == MageStatKind.Skill && d.Pushes.Any(p => p.Skill == t.Id && ranks < p.Ranks)
            ? string.Create(CultureInfo.InvariantCulture, $" (the caster's points: toward '{chosen.Name}' 0x{chosen.Guid:X8}'s wield requirement, before the shares)")
            : string.Empty;

    /// <summary>Says a cap-against-floor line once for this rule (one spend), by its floor's name.</summary>
    private void SayOnce(string key, string line)
    {
        if (_said.Any(s => s.Key == key))
        {
            return;
        }

        _said.Add((key, line));
        _log?.Warn(LogCategory, "xp: " + line);
    }

    private (int Burden, int Capacity, int Current, int Want)? _floorBurden;

    /// <summary>
    /// M6 7 AK (the owner: a mage keeps pushing for stronger buffs): the ranks that bring the next buff level under the ladder's buff bar with the buffs the book holds (<see cref="BuffReach.Push"/>), for the ladder's character only; each logged on its line (<c>xp: push:</c>). The health floor comes
    /// first and stands as it was: the push is paid out of the magic skills' share.
    /// </summary>
    private IReadOnlyList<SkillPush> PushOf(PlayerQualities q)
    {
        var ladder = _ladder;
        if (ladder is null || _view is null)
        {
            return [];
        }

        var book = _view.State.Self.Spellbook;
        var catalog = Grounds.ScrivenerCatalog.Default;
        var pushes = BuffReach.Push(q, ladder.PlanBuffChance, (line, level) => catalog.LineSpells(line).FirstOrDefault(s => s.Level == level) is { } spell && book.Knows(spell.SpellId), BuffBotCover.For(_view.State).On); // M6 item 14 S3: the bot's lines in the bootstrap's place
        foreach (var push in pushes)
        {
            _log?.Info(LogCategory, "xp: push: " + push.Why + string.Create(CultureInfo.InvariantCulture, $" (ranks {push.Ranks})"));
        }

        return pushes;
    }

    private readonly bool _followLevel;
    private readonly int? _burdenTripPercent;
    private readonly Func<bool>? _standingBot;
    private readonly Grounds.Ladder? _healthLadder;

    /// <summary>Advances the run by one step; true once it is <see cref="Done"/>. Call it every frame while it runs.</summary>
    public bool Tick()
    {
        if (!_begun || Done)
        {
            return Done;
        }

        var q = _view!.State.Self.Qualities;
        if (!q.IsKnown)
        {
            return Finish(XpSpendEnd.Unread, "the character's description has not arrived: its stats and experience are unknown");
        }

        if (_pending is { } handle)
        {
            if (!handle.IsCompleted)
            {
                return false;
            }

            var outcome = handle.Outcome!.Value;
            var target = _pendingTarget!;
            _pending = null;
            if (outcome.Kind == ActionOutcomeKind.DryRun)
            {
                return Finish(XpSpendEnd.DryRun, null);
            }

            if (!outcome.Succeeded)
            {
                return Finish(XpSpendEnd.Refused, string.Create(CultureInfo.InvariantCulture, $"the raise of {target.Name} for {_pendingCost:N0} experience ended {outcome.Kind}{(outcome.Code != 0 ? $" (0x{outcome.Code:X4})" : string.Empty)}: {outcome.Detail}"));
            }

            SpentXp += (long)_pendingCost;
            Raises++;
            _spentHere[(target.Kind, target.Id)] = _spentHere.GetValueOrDefault((target.Kind, target.Id)) + (long)_pendingCost; // M6 item 14: the allocation's account
            _allocationSpent += (long)_pendingCost;
        }

        _plan ??= PlanFor(q);
        if (Raises >= MaxRaises)
        {
            return Finish(XpSpendEnd.RaiseCap, null);
        }

        var now = _view.Clock.Now;
        if (now - _start > MaxSeconds)
        {
            return Finish(XpSpendEnd.TimeBound, null);
        }

        switch (Next(q, out var next, out var ranks, out var cost))
        {
            case Pick.PlanReached:
                return Finish(XpSpendEnd.PlanReached, null);
            case Pick.PoolCoversNothing:
                _pool = q.AvailableExperience;
                return Finish(XpSpendEnd.PoolCoversNothing, null);
        }

        _log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: raise {next.Name} {ranks} -> {ranks + 1} for {cost:N0} of {q.AvailableExperience:N0} unassigned{(next.Floor ? (next.Id == MageTemplate.Strength && next.Kind == MageStatKind.Attribute ? (_cureOverCap ? " (the Strength floor's run-line cure, over the cap: she cannot run under this load)" : " (the Strength floor, before the shares)") : " (the health floor, before the shares)") : CasterRaiseNote(next, ranks) + LoreRaiseNote(next, ranks))}"));
        _pendingTarget = next;
        _pendingCost = cost;
        _pending = next.Kind switch
        {
            MageStatKind.Attribute => _actions!.RaiseAttribute(next.Id, (uint)cost, ActionSource.Automation),
            MageStatKind.Vital => _actions!.RaiseVital(next.Id, (uint)cost, ActionSource.Automation),
            _ => _actions!.RaiseSkill(next.Id, (uint)cost, ActionSource.Automation),
        };
        return false;
    }

    /// <summary>What <see cref="Next"/> found.</summary>
    private enum Pick
    {
        /// <summary>A stat the plan still raises, whose next rank the pool covers.</summary>
        Found,

        /// <summary>Stats remain under their target but the pool covers none of their next ranks.</summary>
        PoolCoversNothing,

        /// <summary>No stat that can be raised is under its target.</summary>
        PlanReached,
    }

    /// <summary>
    /// The first stat of the plan, in its order, whose ranks are under its target, that can be raised and whose next rank the pool covers: that rank's price. Skips (and counts) a stat that cannot be raised, and names (once)
    /// a stat the pool does not cover, going on down the order. The cheapest next rank of the stats not covered is kept for <see cref="PoolNote"/>.
    /// </summary>
    private Pick Next(PlayerQualities q, out StatTarget target, out int ranks, out ulong cost)
    {
        var pool = q.AvailableExperience;
        var under = 0;
        _cheapest = null;
        foreach (var t in _plan!.Targets)
        {
            if (!Read(q, t, out ranks, out var spent, out var list, out var why))
            {
                if (ranks < t.Ranks)
                {
                    Skip(t, why);
                }

                continue;
            }

            if (ranks >= t.Ranks)
            {
                continue;
            }

            if (_ladder is not null && !MageTemplate.OnClimberList(t.Kind, t.Id) && !IsLoreTarget(t))
            {
                OffList(t); // M6 item 14: the named list is a rule of the spend, whatever a plan lists; M6 14 (m6-lore-spend): the Arcane Lore rule's target is the one exception
                continue;
            }

            if (ranks + 1 >= list!.Count)
            {
                Skip(t, "it is at its rank cap");
                continue;
            }

            var goal = list[ranks + 1];
            if (spent >= goal)
            {
                Skip(t, string.Create(CultureInfo.InvariantCulture, $"it shows {ranks} ranks but {spent:N0} spent, at or over the {goal:N0} of rank {ranks + 1} (the read model and the XpTable disagree)"));
                continue;
            }

            var price = goal - spent;
            var cure = false;
            if (_ladder?.StatCap is { } cap && StatCap.Binds(t.Kind, t.Id) && Account(cap, q, t, ranks, goal, price) is { } heldWhy)
            {
                // M6 item 14: the cap wins over a share and over a floor alike; the walk goes on to the stats after it (a floor the account holds does not make the pool wait). The one exception is the run-line cure.
                cure = t.Floor && t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Strength && ranks < _cureRanks;
                if (!cure)
                {
                    Hold(t, ranks, heldWhy);
                    continue;
                }
            }

            if (pool < (long)price)
            {
                under++;
                NameUnaffordable(t, ranks, price, pool);
                if (t.Floor)
                {
                    _cheapest = (t.Name, ranks, price); // M6 1 b: a floor comes before every share: the pool waits for it, no later stat is raised first
                    break;
                }

                if (_cheapest is not { } c || price < c.Cost)
                {
                    _cheapest = (t.Name, ranks, price);
                }

                continue;
            }

            target = t;
            cost = price;
            _cureOverCap = cure;
            return Pick.Found;
        }

        target = null!;
        ranks = 0;
        cost = 0;
        return under > 0 ? Pick.PoolCoversNothing : Pick.PlanReached;
    }

    private bool _cureOverCap;

    /// <summary>
    /// M6 item 14, the account of a stat the cap binds: null when its next rank may be bought, else why it is held. Two checks, both the owner's caps read as hard rules: the running one (the stat's cost in all, with this rank, stays within its share of all the
    /// experience earned: <see cref="StatCap.Limit"/>) and the allocation's (what the stat has taken of this allocation, with this rank, stays within the same percent of the allocation; its first rank of an allocation is always allowed, or a rank dearer than the share
    /// would never be bought). The second is what stops one allocation catching a stat up to its cap: the allocation of 2026-11-11 gave Endurance 12% of itself with the running share still under 10%.
    /// </summary>
    private string? Account(StatCap cap, PlayerQualities q, StatTarget t, int ranks, uint goal, ulong price)
    {
        var c = CultureInfo.InvariantCulture;
        var total = (ulong)Math.Max(0, q.TotalExperience);
        var percent = cap.LimitPercent(t.Kind, t.Id);
        var limit = cap.Limit(t.Kind, t.Id, total);
        if (goal > limit)
        {
            return string.Create(c, $"rank {ranks + 1} would bring its cost to {goal:N0}, over its {percent}% of the {total:N0} earned ({limit:N0})");
        }

        var here = _spentHere.GetValueOrDefault((t.Kind, t.Id));
        var share = (_allocation / 100 * percent) + (_allocation % 100 * percent / 100);
        return here > 0 && here + (long)price > share
            ? string.Create(c, $"it has taken {here:N0} of this allocation's {_allocation:N0}, and rank {ranks + 1} ({price:N0}) would pass its {percent}% of the allocation ({share:N0})")
            : null;
    }

    /// <summary>Names (once an allocation) a stat the account holds to the next allocation.</summary>
    private void Hold(StatTarget t, int ranks, string why)
    {
        if (!_held.Add((t.Kind, t.Id)))
        {
            return;
        }

        _heldNames.Add(t.Name);
        _log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: held: {t.Name} {ranks} -> {ranks + 1}{(t.Floor ? " (a floor's rank)" : string.Empty)}: {why}: the cap wins; it waits for the next allocation and the stats after it are tried"));
    }

    private readonly HashSet<(MageStatKind Kind, uint Id)> _offList = [];

    /// <summary>The stats a plan listed that are not on the ladder's character's named list (<see cref="MageTemplate.OnClimberList"/>): never raised, each named once.</summary>
    public int OffListed => _offList.Count;

    private void OffList(StatTarget t)
    {
        if (_offList.Add((t.Kind, t.Id)))
        {
            _log?.Warn(LogCategory, $"xp: not on the list: {t.Name}: the plan lists it, and the ladder's character's experience goes only to the named stats (MageTemplate.OnClimberList): never raised");
        }
    }

    /// <summary>Names (once a call) a stat whose next rank the pool does not cover; the walk goes on to the next stat of the order.</summary>
    private void NameUnaffordable(StatTarget t, int ranks, ulong price, long pool)
    {
        if (!_unaffordable.Add((t.Kind, t.Id)))
        {
            return;
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"xp: {pool:N0} unassigned does not cover the next rank of {t.Name} ({ranks} -> {ranks + 1}, {price:N0}); the next stat of the order is tried");
        FirstUnaffordable ??= text;
        _log?.Info(LogCategory, text);
    }

    /// <summary>Reads the stat's ranks, experience spent and price list. False (with <paramref name="why"/>) for a stat the character lacks or holds untrained.</summary>
    private bool Read(PlayerQualities q, StatTarget t, out int ranks, out uint spent, out IReadOnlyList<uint>? list, out string why)
    {
        (ranks, spent, list, why) = (0, 0, null, string.Empty);
        switch (t.Kind)
        {
            case MageStatKind.Attribute:
                if (!q.TryGetAttribute(t.Id, out var attribute))
                {
                    why = "the attribute is not in the character's description";
                    return false;
                }

                (ranks, spent, list) = ((int)attribute.Ranks, attribute.Experience, _table.AttributeXpList);
                return true;
            case MageStatKind.Vital:
                if (!q.TryGetVital(t.Id, out var vital))
                {
                    why = "the vital is not in the character's description";
                    return false;
                }

                (ranks, spent, list) = ((int)vital.Ranks, vital.Experience, _table.VitalXpList);
                return true;
            default:
                if (!q.TryGetSkill(t.Id, out var skill))
                {
                    why = "the skill is not in the character's description";
                    return false;
                }

                if (skill.AdvancementClass < SkillRules.TrainedClass)
                {
                    why = "the skill is untrained (ACE tells no refusal for it, so no raise is sent)";
                    return false;
                }

                (ranks, spent, list) = (skill.Ranks, skill.Experience, skill.AdvancementClass >= SkillRules.SpecializedClass ? _table.SpecializedSkillXpList : _table.TrainedSkillXpList);
                return true;
        }
    }

    private void Skip(StatTarget t, string why)
    {
        if (_skipped.Add((t.Kind, t.Id)) && FirstSkip is null)
        {
            FirstSkip = t.Name + ": " + why;
            _log?.Warn(LogCategory, "xp: skipped " + FirstSkip);
        }
    }

    private bool Finish(XpSpendEnd end, string? failure)
    {
        End = end;
        Done = true;
        Failure = failure;
        var text = string.Create(CultureInfo.InvariantCulture, $"xp: ended {end} ({Fields}{(Skipped > 0 ? $", {Skipped} skipped" : string.Empty)}{(PoolNote is { } note ? ", " + note : string.Empty)}){(failure is null ? string.Empty : ": " + failure)}");
        if (failure is null)
        {
            _log?.Info(LogCategory, text);
        }
        else
        {
            _log?.Warn(LogCategory, text);
        }

        if (end != XpSpendEnd.Unread && _view?.State.Self.Qualities is { IsKnown: true } q && _allocation >= 0)
        {
            Banked = Math.Max(0, q.AvailableExperience);
            SharesNote = Shares(q, end);
            _log?.Info(LogCategory, "xp: " + SharesNote);
        }

        return true;
    }

    /// <summary>
    /// M6 item 14: the allocation's summary line. Each stat: the experience this allocation spent on it, that as a percent of the allocation, and <c>run</c>, the stat's cost in all as a percent of the experience earned (the server's own figures), with the cap after a
    /// slash for a stat a cap binds and <c>over</c> when the running share is past it (a character built before the cap, or the run-line cure). Then the groups and what is banked, with its reason.
    /// </summary>
    private string Shares(PlayerQualities q, XpSpendEnd end)
    {
        var c = CultureInfo.InvariantCulture;
        var total = Math.Max(0, q.TotalExperience);
        var cap = _ladder?.StatCap;
        var parts = new List<string>();
        string Percent(long part, long whole) => whole > 0 ? (100.0 * part / whole).ToString("0.0", c) + "%" : "n/a";
        string Pair(long here, long run) => string.Create(c, $"{here} {Percent(here, _allocation)} run {Percent(run, total)}");
        long Here(MageStatKind kind, uint id) => _spentHere.GetValueOrDefault((kind, id));
        void Bound(MageStatKind kind, uint id, string name, long run)
        {
            var limit = cap is not null && StatCap.Binds(kind, id) ? string.Create(c, $"/{cap.LimitPercent(kind, id)}%{(cap.Limit(kind, id, (ulong)total) < (ulong)run ? " over" : string.Empty)}") : string.Empty;
            parts.Add(string.Create(c, $"{name} {Pair(Here(kind, id), run)}{limit}"));
        }

        long attributesHere = 0, attributesRun = 0;
        foreach (var (id, name) in new[] { (MageTemplate.Strength, "Strength"), (MageTemplate.Endurance, "Endurance"), (MageTemplate.Coordination, "Coordination"), (MageTemplate.Quickness, "Quickness"), (MageTemplate.Focus, "Focus"), (MageTemplate.Self, "Self") })
        {
            if (q.TryGetAttribute(id, out var a))
            {
                Bound(MageStatKind.Attribute, id, name, a.Experience);
                attributesHere += Here(MageStatKind.Attribute, id);
                attributesRun += a.Experience;
            }
        }

        foreach (var (id, name) in new[] { (MageTemplate.MaxHealth, "Max Health"), (MageTemplate.MaxStamina, "Max Stamina"), (MageTemplate.MaxMana, "Max Mana") })
        {
            if (q.TryGetVital(id, out var v))
            {
                Bound(MageStatKind.Vital, id, name, v.Experience);
            }
        }

        var listed = _ladder is not null ? MageTemplate.ClimberSpendSkills : [.. _plan?.Targets.Where(t => t.Kind == MageStatKind.Skill).Select(t => t.Id).Distinct() ?? []];
        long listedHere = 0, listedRun = 0, otherHere = 0, otherRun = 0;
        foreach (var id in listed)
        {
            if (!q.TryGetSkill(id, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
            {
                parts.Add(SkillLabel(id) + " untrained");
                continue;
            }

            parts.Add(SkillLabel(id) + " " + Pair(Here(MageStatKind.Skill, id), skill.Experience));
            listedHere += Here(MageStatKind.Skill, id);
            listedRun += skill.Experience;
        }

        var others = new List<string>();
        foreach (var (id, skill) in q.Skills.OrderBy(k => k.Key))
        {
            if (listed.Contains(id) || (skill.Experience == 0 && Here(MageStatKind.Skill, id) == 0))
            {
                continue;
            }

            others.Add(SkillLabel(id));
            otherHere += Here(MageStatKind.Skill, id);
            otherRun += skill.Experience;
        }

        var why = end switch
        {
            XpSpendEnd.PoolCoversNothing => _cheapest is { } cheapest ? string.Create(c, $"no rule's next rank fits: the cheapest is {cheapest.Name} {cheapest.Ranks} -> {cheapest.Ranks + 1} for {cheapest.Cost:N0}") : "no rule's next rank fits",
            XpSpendEnd.PlanReached => _heldNames.Count > 0 ? "every rank the caps allow is bought: held to the next allocation: " + string.Join(", ", _heldNames) : "every planned rank is bought at this total",
            XpSpendEnd.RaiseCap => "the raise cap ended this call: the allocation goes on in the next",
            _ => "the call ended " + end,
        };
        return string.Create(
            c,
            $"shares: allocation={_allocation} spent={_allocationSpent} banked={Banked} earned={total} caps={(cap is null ? "none" : string.Create(c, $"health<={cap.AimPercent}%,attribute<={cap.MaxPercent}%"))} | {string.Join(" | ", parts)} | other skills{(others.Count > 0 ? " (" + string.Join(", ", others) + ")" : string.Empty)} {Pair(otherHere, otherRun)} | groups: attributes {Pair(attributesHere, attributesRun)}; listed skills {Pair(listedHere, listedRun)} | banked {Banked}: {why}");
    }

    private static string SkillLabel(uint id) =>
        id is MageTemplate.MeleeDefense or MageTemplate.ArcaneLore or MageTemplate.MagicDefense or MageTemplate.ManaConversion or MageTemplate.Jump or MageTemplate.Run or MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment
            or MageTemplate.LifeMagic or MageTemplate.WarMagic or MageTemplate.Loyalty or MageTemplate.Salvaging or MageTemplate.Alchemy
            ? MageTemplate.SkillName(id)
            : string.Create(CultureInfo.InvariantCulture, $"skill {id}");
}
