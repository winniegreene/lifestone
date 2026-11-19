using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Fighter;

/// <summary>Where a <see cref="RecallHome"/> is.</summary>
public enum RecallStatus
{
    /// <summary>No recall has been begun, or the last one ended and nothing runs.</summary>
    Idle,

    /// <summary>A recall is under way: step again.</summary>
    Running,

    /// <summary>The character is at the lifestone's landblock and present (<see cref="RecallHome.Recalls"/> rose by one).</summary>
    Done,

    /// <summary>A step failed: <see cref="RecallHome.Failure"/> names it (<c>recall:&lt;step&gt;</c>), <see cref="RecallHome.Detail"/> says why.</summary>
    Failed,
}

/// <summary>
/// The way home (M4 P5a C1c, owner decisions D36 and D38): a character casts Lifestone Recall (spell 1635) and waits to arrive at the lifestone it is bound to, so the journeys do not run home. A Brawler or an Archer first swaps its
/// caster in (<see cref="CasterSwap"/>, <see cref="SwapDirection.In"/>: the weapon and ammunition to the pack, the caster wielded, the magic mode) and swaps back out when it has arrived (or when the recall failed after the swap in);
/// BSD Mage wields its wand already and casts alone (the magic mode is entered only when it is not in it). P5b's stations call it between stations; nothing here wires a journey.
/// <para>M4 P5b J1: the spell and the destination's landblock are parameters (defaults Lifestone Recall 1635 and Holtburg 0xA9B4), so Primary Portal Recall (spell 48, <see cref="PrimaryPortalRecallSpell"/>) to the tied portal's destination
/// (the Black Spawn Den, landblock 0x0105; the caller passes the ground's arrival landblock, <c>GroundRules.ArrivalLandblock</c>) runs the same steps; the journey's travel seam (<c>JourneyTravel</c>) uses both.</para>
/// </summary>
/// <remarks>
/// <para><b>Steps and the failure name.</b> The steps are <c>present</c> (the character must be in the world and not in portal space), <c>swap-in</c>, <c>spell</c> (the spellbook, once the description has arrived, lacks spell 1635), <c>mode</c>
/// (the magic mode, for a caster that has not entered it), <c>busy-wait</c> (M4 P5b fix FC-a1: the cast waits while <see cref="IActionApi.BusyReason"/> names a busy chain, at most <see cref="BusyWaitSeconds"/>; M6 14: an open ground container
/// is closed first, and past the bound the cast goes anyway, so this step no longer fails), <c>cast</c>, <c>teleport</c>, <c>arrival</c> and <c>swap-out</c>. A failure is <c>recall:&lt;step&gt;</c> with a sentence behind it; nothing is retried silently, but a fizzled cast (0x0402) is counted and cast again, at most <see cref="MaxFizzles"/> times a recall (M4 P5a fix F-abil, P5a-R4: Item Enchantment is built to the 0.9 chance rule, so about one cast in ten fizzles and a journey must not fail for it). The cast's refusals are named from ACE
/// @95ef426d: WeenieError 0x049E <c>YouMustLinkToLifestoneToRecall</c> (<c>WorldObject_Magic.cs</c> 1087-1093 sends it when the player has no LinkedLifestone; <c>WeenieError.cs</c> 658), 0x049F
/// <c>YouFailToRecallToLifestone</c> (:663), 0x04CC <c>YouHaveBeenInPKBattleTooRecently</c> (:834, <c>WorldObject_Magic.cs</c> 1054-1058), and the spell refusals 0x0400 (components), 0x0401 (mana), 0x0402 (fizzle) the Action API reports. Research
/// section 1's portal ones (0x04A3 <c>YouMustLinkToPortalToRecall</c>, 0x04AD <c>YouCannotRecallPortal</c>) are named too for the portal recall P5b may add. A refusal arrives as a chat line of the WeenieError kind, so the recall
/// listens for them from <see cref="Begin"/> to its end.</para>
/// <para><b>Arrival</b> is read from the server's own state, never assumed (R3): the player's travel went through <see cref="SelfTravel.TeleportAnnounced"/> or <see cref="SelfTravel.TeleportArrived"/> since the cast, and is
/// <see cref="SelfTravel.Present"/> again with its position in the lifestone's landblock (<see cref="HomeLandblock"/>, the high word of the cell id; Holtburg's lifestone is landblock 0xA9B4). ACE answers a recall with a 2 s delay and then the
/// teleport; the bounds are <see cref="TeleportSeconds"/> after the cast ended for the teleport to start, <see cref="ArrivalSeconds"/> more to be present, and <see cref="MaxSeconds"/> for the whole recall. Present in another landblock is
/// <c>recall:arrival</c> at once (the lifestone is not where the caller thought, or the position is not the destination's).</para>
/// <para><b>Fields.</b> <see cref="Fields"/> is <c>recalls=&lt;n&gt; recall_s=&lt;seconds&gt; recall_fizzles=&lt;n&gt;</c> (counted over every recall since construction; the seconds are the whole recalls', swap halves included; <c>recall_fizzles</c> is report-only) and, after a failure, <c>recall_why=recall:&lt;step&gt;</c>;
/// the swap's own fields are <see cref="Swap"/>.<see cref="CasterSwap.Fields"/>.</para>
/// </remarks>
public sealed class RecallHome : IDisposable
{
    /// <summary>The log category of the recall's lines.</summary>
    public const string LogCategory = "recall";

    /// <summary>Lifestone Recall (Item Enchantment, power 100; <c>SpellStage.cs</c> names it; <c>FighterMagicDatTests</c> pins its power and school against the SpellTable).</summary>
    public const uint LifestoneRecallSpell = 1635;

    /// <summary>Primary Portal Recall (Item Enchantment): ACE teleports to the tied portal's destination 2 s after the cast, or answers 0x04A3 when no portal is tied (M4 P5b W0, pinned live).</summary>
    public const uint PrimaryPortalRecallSpell = 48;

    /// <summary>The landblock of Holtburg's lifestone (the BSD Mage's bind, M3 D34; the fighters bind there too in P5b's first station).</summary>
    public const uint HoltburgLandblock = 0xA9B4;

    /// <summary>Seconds after the cast ended for the teleport to start (ACE's recall delay is 2 s).</summary>
    public const double TeleportSeconds = 20.0;

    /// <summary>Seconds, once the teleport was seen, to be present at the destination.</summary>
    public const double ArrivalSeconds = 30.0;

    /// <summary>Seconds one whole recall may take, swap halves included.</summary>
    public const double MaxSeconds = 150.0;

    /// <summary>The <see cref="Failure"/> step of a recall the kill switch stopped (<c>recall:kill-switch</c>, M5 D13).</summary>
    public const string KillSwitchStep = "kill-switch";

    /// <summary>The fizzles one recall retries (a named cap, not retail's: Lifestone's own); the next fizzle fails the recall <c>recall:cast</c>. At the 0.9 chance rule five in a row is one in a hundred thousand.</summary>
    public const int MaxFizzles = 5;

    /// <summary>
    /// Seconds the recall waits, before it sends the cast, for the player to stop being busy by the Action API's lane rule (<see cref="IActionApi.BusyReason"/>; M4 P5b fix FC-a1). M6 14 recall unblock (m6-recall-preempt): past it
    /// the cast is sent anyway and ACE's own answer judges it (a busy refusal is retried once, <see cref="BusyRetries"/>); the way out never fails for a wait. An open ground container is closed first
    /// (<see cref="OpenContainerPreempt"/>), so its held Use lane is never waited out: only an action truly in flight (or a short chain after one) is.
    /// Lifestone's own bound, not retail's: the longest chain the rule knows is a corpse pickup's 1.35 s after its return stance, and a held lane is let go at its tracker's timeout; 15 s covers a loot's last move and a heal's Ready with room.
    /// </summary>
    public const double BusyWaitSeconds = 15.0;

    /// <summary>
    /// M6 14 recall unblock: the busy refusals (0x001D, ACE's <c>VerifyBusy</c>, <c>Player_Magic.cs</c> 375-383 @95ef426d) the recall retries: once, after the busy clears (the Action API's rule reads free, and at least
    /// <see cref="CastRules.DefaultBusyWaitSeconds"/> after the refusal); the next one fails <c>recall:cast</c>, named.
    /// </summary>
    public const int BusyRetries = 1;

    /// <summary>The cast's retry rules (M5 item 1 lane K4, D13): <see cref="CastRules.Travel(int)"/> of <see cref="MaxFizzles"/> same-level retries, sent at once with no mana check between them, as the recall always did. A busy refusal ends the attempt: the recall itself retries it (<see cref="BusyRetries"/>).</summary>
    private static readonly CastRules FizzleRules = CastRules.Travel(MaxFizzles) with { MaxBusy = 0 };

    private readonly IGameSessionView _session;
    private readonly IActionApi _actions;
    private readonly CastAttempt _attempt;
    private readonly uint[] _castIds;
    private readonly GameState _state;
    private readonly CasterSwap? _swap;
    private readonly uint _homeLandblock;
    private readonly uint _spell;
    private readonly string _spellName;
    private readonly ILogSink _log;
    private readonly IDisposable _travelSubscription;
    private readonly IDisposable _chatSubscription;

    private Phase _phase;
    private ActionHandle? _pending;
    private bool _casting;
    private int _fizzlesSeen;
    private double _started;
    private double _castEnded;
    private double _teleportSeenAt;
    private bool _teleportSeen;
    private uint _refusal;
    private bool _swapIn;
    private string? _firstFailure;
    private string? _firstDetail;
    private double _seconds;
    private int _recalls;
    private int _fizzlesThisRecall;
    private int _fizzles;
    private double _busySince = double.NaN;
    private readonly OpenContainerPreempt _preempt;
    private int _busyRefusals;
    private double _busyRetryAt = double.NaN;

    private enum Phase
    {
        None,
        SwapIn,
        Mode,
        Cast,
        Teleport,
        SwapOut,
    }

    /// <summary>A recall for a character over a session. <paramref name="swap"/> is the fighter's caster swap, or null for a character that wields its caster.</summary>
    /// <param name="session">The session (its Action API, read model and event bus).</param>
    /// <param name="swap">The caster swap over the same session, or null (the mage).</param>
    /// <param name="log">Where the recall logs.</param>
    /// <param name="homeLandblock">The landblock of the lifestone the character is bound to (the high 16 bits of a cell id), or the tied portal destination's for <see cref="PrimaryPortalRecallSpell"/>.</param>
    /// <param name="spellId">The recall spell: <see cref="LifestoneRecallSpell"/> (the default) or <see cref="PrimaryPortalRecallSpell"/>.</param>
    public RecallHome(IGameSessionView session, CasterSwap? swap, ILogSink log, uint homeLandblock = HoltburgLandblock, uint spellId = LifestoneRecallSpell)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _actions = session.Actions;
        _state = session.State;
        _swap = swap;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _attempt = new CastAttempt(session.Actions, session.State, log);
        _castIds = [spellId];
        _homeLandblock = homeLandblock & 0xFFFF;
        _spell = spellId;
        _spellName = SpellName(spellId);
        _travelSubscription = session.Events.Subscribe<SelfTravelChanged>(OnTravel);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat);
        _preempt = new OpenContainerPreempt(session.Actions, session.State, log, LogCategory, "recall");
    }

    /// <summary>The ground containers the recall closed before its cast (M6 14 recall unblock, m6-recall-preempt).</summary>
    public int PreemptCloses => _preempt.Closes;

    /// <summary>
    /// The recall for the character named <paramref name="characterName"/>: a Brawler or an Archer (over a <see cref="CasterSwap"/>), BSD Mage or Winnie (alone; M6 item 0 a6), or null for any other character (the Hunter has no magic).
    /// Winnie's spellbook may not hold the recall yet (Lifestone Recall is a level 3 Item spell): the cast then fails <c>recall:spell</c> by name (the attempt's own check), and the journey's way home walks instead (<see cref="Scenarios.Journey.WayHomeRule"/>).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="characterName">The character's name as the read model holds it, or null.</param>
    /// <param name="log">The log.</param>
    /// <param name="homeLandblock">The lifestone's landblock (or the tied portal destination's).</param>
    /// <param name="spellId">The recall spell (<see cref="LifestoneRecallSpell"/> by default, or <see cref="PrimaryPortalRecallSpell"/>).</param>
    public static RecallHome? ForCharacter(IGameSessionView session, string? characterName, ILogSink log, uint homeLandblock = HoltburgLandblock, uint spellId = LifestoneRecallSpell)
    {
        ArgumentNullException.ThrowIfNull(session);
        var template = FighterTemplate.ForName(characterName);
        if (template is { HasMagic: true })
        {
            return new RecallHome(session, CasterSwap.For(template, session.Actions, session.State, log), log, homeLandblock, spellId);
        }

        var name = characterName?.Trim();
        return string.Equals(name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase) || CharacterPolicy.For(name).ScenarioMage ? new RecallHome(session, null, log, homeLandblock, spellId) : null;
    }

    /// <summary>Where the recall is.</summary>
    public RecallStatus Status { get; private set; }

    /// <summary>The caster swap the recall runs, or null for the mage.</summary>
    public CasterSwap? Swap => _swap;

    /// <summary>The lifestone's landblock (the high 16 bits of its cell ids).</summary>
    public uint HomeLandblock => _homeLandblock;

    /// <summary>The recall spell this object casts (1635 or 48).</summary>
    public uint SpellId => _spell;

    /// <summary>The recall refusal ACE sent during the last recall (0x049E, 0x049F, 0x04A3, 0x04AD or 0x04CC), or 0: the journey reads the ties from it (M4 P5b contract 4).</summary>
    public uint Refusal => _refusal;

    /// <summary>
    /// True when the last recall failed because ACE refused the cast for a missing spell component (WeenieError 0x0400, YouDontHaveAllTheComponents): the Action API's outcome names it in <see cref="Detail"/> (live 2026-11-18 20:29:54 UTC:
    /// <c>ServerError (code 0x400) YouDontHaveAllTheComponents 0x0400</c>). It is no recall refusal of <see cref="Refusal"/>'s list, so the journey reads it here and sets <c>JourneyFacts.LifestoneRecallUnavailable</c> (M6 7 AV).
    /// </summary>
    public bool RefusedForComponents => Detail is { } detail && (detail.Contains("0x0400", StringComparison.Ordinal) || detail.Contains("0x400)", StringComparison.Ordinal));

    /// <summary>The name of a recall spell for the log: Lifestone Recall, Primary Portal Recall, or "spell N".</summary>
    /// <param name="spellId">The spell id.</param>
    public static string SpellName(uint spellId) => spellId switch
    {
        LifestoneRecallSpell => "Lifestone Recall",
        PrimaryPortalRecallSpell => "Primary Portal Recall",
        _ => "spell " + spellId.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The first failure as <c>recall:&lt;step&gt;</c> (<c>recall:cast</c>), or null. Cleared by the next <see cref="Begin"/>.</summary>
    public string? Failure { get; private set; }

    /// <summary>The sentence behind <see cref="Failure"/>, or null.</summary>
    public string? Detail { get; private set; }

    /// <summary>The recalls that arrived, over the object's life (<c>recalls=</c>).</summary>
    public int Recalls => _recalls;

    /// <summary>The session seconds the recalls took, ended or failed, over the object's life (<c>recall_s=</c>).</summary>
    public double Seconds => _seconds;

    /// <summary>The fizzled casts the recalls retried (or failed on), over the object's life (<c>recall_fizzles=</c>, report-only).</summary>
    public int Fizzles => _fizzles;

    /// <summary>The verdict fields: <c>recalls=&lt;n&gt; recall_s=&lt;seconds&gt; recall_fizzles=&lt;n&gt;</c>, and <c>recall_why=recall:&lt;step&gt;</c> after a failure.</summary>
    public string Fields =>
        string.Create(CultureInfo.InvariantCulture, $"recalls={_recalls} recall_s={_seconds:0.0} recall_fizzles={_fizzles}{(Failure is null ? string.Empty : " recall_why=" + Failure)}");

    /// <summary>The name of an ACE WeenieError a recall can meet, with where ACE sends it, or null for a code that is not a recall's.</summary>
    /// <param name="code">The WeenieError code.</param>
    public static string? NameOfRefusal(uint code) => code switch
    {
        0x049E => "YouMustLinkToLifestoneToRecall 0x049E (the character is bound to no lifestone; ACE @95ef426d WorldObject_Magic.cs:1087-1093, WeenieError.cs:658)",
        0x049F => "YouFailToRecallToLifestone 0x049F (WeenieError.cs:663)",
        0x04A3 => "YouMustLinkToPortalToRecall 0x04A3 (the character is tied to no portal; WorldObject_Magic.cs:1107-1111, WeenieError.cs:683)",
        0x04AD => "YouCannotRecallPortal 0x04AD (the tied portal's destination forbids recall; WeenieError.cs:730)",
        0x04CC => "YouHaveBeenInPKBattleTooRecently 0x04CC (the PK timer is active; WorldObject_Magic.cs:1054-1058, WeenieError.cs:834)",
        0x0400 => "YouDontHaveAllTheComponents 0x0400",
        0x0401 => "YouDontHaveEnoughManaToCast 0x0401",
        0x0402 => "YourSpellFizzled 0x0402",
        _ => null,
    };

    /// <summary>Begins a recall. Returns false when one is already running. A character that is not present ends it at once as <c>recall:present</c> (the call returns true and the status is <see cref="RecallStatus.Failed"/>).</summary>
    /// <param name="now">The session clock's time.</param>
    public bool Begin(double now)
    {
        if (Status == RecallStatus.Running)
        {
            return false;
        }

        Failure = null;
        Detail = null;
        _firstFailure = null;
        _firstDetail = null;
        _teleportSeen = false;
        _refusal = 0;
        _fizzlesThisRecall = 0;
        _busySince = double.NaN;
        _busyRefusals = 0;
        _busyRetryAt = double.NaN;
        _preempt.Reset();
        _swapIn = false;
        _pending = null;
        _rearm = null;
        _rearmed = false;
        _casting = false;
        _started = now;
        Status = RecallStatus.Running;
        _log.Info(LogCategory, "recall: begin " + _spellName + (_swap is null ? string.Empty : " (with the caster swap)"));
        if (_state.Phase != SessionPhase.InWorld || _state.Self.Travel != SelfTravel.Present)
        {
            EndFailed("present", "the character is " + (_state.Phase != SessionPhase.InWorld ? "not in the world" : "in " + _state.Self.Travel + " (portal space or arriving)"), now, restore: false);
            return true;
        }

        if (_swap is not null)
        {
            _phase = Phase.SwapIn;
            _swap.Begin(SwapDirection.In, now);
        }
        else
        {
            _phase = Phase.Mode;
        }

        return true;
    }

    /// <summary>Advances the recall: call every frame while <see cref="RecallStatus.Running"/>. Allocates nothing while an action is in flight.</summary>
    /// <param name="now">The session clock's time.</param>
    public RecallStatus Step(double now)
    {
        if (Status != RecallStatus.Running)
        {
            return Status;
        }

        if (now - _started > MaxSeconds && _phase != Phase.SwapOut)
        {
            return Fail("teleport", $"the whole recall took more than {MaxSeconds:0} s", now);
        }

        if (_actions.KillSwitch && (_phase is Phase.SwapIn or Phase.Mode || (_phase == Phase.Cast && !_casting)))
        {
            // M5 D13: the recall's mode change and cast are automation, so the kill switch stops the recall before it sends them. The caster swap is not restored (its halves are the swap's own, not the kill switch's to stop, and
            // restoring is a send); a cast already in flight finishes and is judged by the attempt (Cancelled once it does).
            return EndFailed(KillSwitchStep, "the kill switch is on: the recall sends nothing more", now, restore: false);
        }

        switch (_phase)
        {
            case Phase.SwapIn:
                return StepSwapIn(now);
            case Phase.Mode:
                return StepMode(now);
            case Phase.Cast:
                return StepCast(now);
            case Phase.Teleport:
                return StepTeleport(now);
            case Phase.SwapOut:
                return StepSwapOut(now);
            default:
                return Status;
        }
    }

    /// <summary>Stops listening for the travel and the chat lines.</summary>
    public void Dispose()
    {
        _travelSubscription.Dispose();
        _chatSubscription.Dispose();
    }

    private RecallStatus StepSwapIn(double now)
    {
        var status = _swap!.Step(now);
        switch (status)
        {
            case SwapStatus.Done:
                _swapIn = true;
                _phase = Phase.Mode;
                return Status;
            case SwapStatus.Failed:
                // The swap restored the weapon itself.
                return EndFailed("swap-in", _swap.Failure + (_swap.Detail is null ? string.Empty : ": " + _swap.Detail), now, restore: false);
            default:
                return Status;
        }
    }

    private RecallStatus StepMode(double now)
    {
        var book = _state.Self.Spellbook;
        if (book.IsKnown && !book.Knows(_spell))
        {
            return Fail("spell", $"{_spellName} (spell {_spell.ToString(CultureInfo.InvariantCulture)}) is not in the spellbook (rerun --scenario=fighter-build or mage-build: it teaches the profile's spells)", now);
        }

        if (_state.Self.Qualities.CombatMode == CombatModes.Magic)
        {
            _phase = Phase.Cast;
            _pending = null;
            return Status;
        }

        if (_rearm is { } rearm)
        {
            if (rearm.Outcome is not { } wielded)
            {
                return Status;
            }

            _rearm = null;
            _log.Info(LogCategory, $"recall: rearm: {Describe(wielded)}; a caster held now: {(HeldCaster.Held(_state) ? "yes" : "no")}");
        }

        if (_pending is null)
        {
            // M5 7 D139 (l): ACE answers a magic mode with no caster held by setting peace (Player_Combat.cs 872-882), so the mage wields the best caster of its packs first, once per recall (a fighter's caster is the swap's).
            if (_swap is null && !_rearmed && CombatModeGuard.Read(_state) is { Caster: null, Unknown: 0 })
            {
                _rearmed = true;
                if (HeldCaster.Rearm(_state, Quartermaster.Wardrobe.For(_state).RearmJudge(), out var why) is not { } caster)
                {
                    return Fail("mode", "no caster held: " + why, now);
                }

                _log.Info(LogCategory, "recall: no caster held: wield " + why + " before the magic mode");
                _rearm = _actions.Wield(caster.Guid, (EquipMask)SelfEquipJudge.Held, ActionSource.Automation);
                return Status;
            }

            _pending = _actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            return Status;
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Status;
        }

        _pending = null;
        if (!outcome.Succeeded)
        {
            return Fail("mode", Describe(outcome), now);
        }

        return Status;
    }

    private ActionHandle? _rearm;
    private bool _rearmed;

    private RecallStatus StepCast(double now)
    {
        if (!_casting)
        {
            // M6 14 recall unblock (m6-recall-preempt): an open ground container is closed first, and the server's answer checked: its open's held Use lane is then free, never waited out.
            if (_preempt.Step(_spellName, now))
            {
                return Status;
            }

            // FC-a1: ACE answers a cast sent into the player's busy chain (a corpse pickup's return stance, a heal's Ready) 0x001D; wait for the Action API's rule to clear, bounded. M6 14: past the bound the cast goes anyway, and
            // ACE's own answer judges it (a busy refusal is retried once, after the busy clears).
            var busy = _actions.BusyReason;
            if (busy is not null || now < _busyRetryAt)
            {
                if (double.IsNaN(_busySince))
                {
                    _busySince = now;
                    _log.Info(LogCategory, busy is null
                        ? string.Create(CultureInfo.InvariantCulture, $"recall:busy-wait: the retry after ACE's busy refusal waits {_busyRetryAt - now:0.0#} s more (at least {CastRules.DefaultBusyWaitSeconds:0} s after the refusal)")
                        : $"recall:busy-wait: the player is busy ({busy}); the cast waits up to {BusyWaitSeconds:0} s, then goes anyway");
                }

                if (now - _busySince <= BusyWaitSeconds)
                {
                    return Status;
                }

                _log.Warn(LogCategory, $"recall:busy-wait: the player is still busy after {BusyWaitSeconds:0} s ({busy ?? "the retry's wait"}): the cast is sent anyway; the Action API sends it when its own lane rule frees, and ACE's answer judges it (m6-recall-preempt)");
                _busySince = double.NaN;
                _busyRetryAt = double.NaN;
            }
            else if (!double.IsNaN(_busySince))
            {
                _log.Info(LogCategory, $"recall:busy-wait: the player is free after {now - _busySince:0.00} s");
                _busySince = double.NaN;
            }

            _log.Info(LogCategory, "recall: casting " + _spellName + " (" + _spell.ToString(CultureInfo.InvariantCulture) + ")");
            _casting = true;
            _fizzlesSeen = 0;
            _attempt.Start(new CastRequest { Levels = _castIds, Rules = FizzleRules, Purpose = "recall " + _spellName }, now);
            return Status;
        }

        if (_refusal != 0 && NameOfRefusal(_refusal) is { } named)
        {
            // ACE told the refusal as a WeenieError chat line; the cast's own outcome follows (UseDone).
            _casting = false;
            _attempt.Stop("ACE refused: " + named, now);
            return Fail("cast", "ACE refused: " + named, now);
        }

        // P5a-R4: a fizzle is counted and the cast retried inside the cap, not a failed recall (the same rule as the harm-heal script's); the retries are the attempt's now (Travel: MaxFizzles of them, sent at once).
        var running = _attempt.Step(now) == CastEnd.Running;
        NoteFizzles(_attempt.Report.Fizzles);
        if (running)
        {
            return Status;
        }

        _casting = false;
        var report = _attempt.Report;
        var last = _attempt.LastOutcome;
        var described = last is { } outcome ? Describe(outcome) : $"{report.End.Name()}: {report.Why}";
        if (report.End == CastEnd.Busy && _refusal == 0)
        {
            // M6 14 recall unblock: ACE refused the cast as too busy (0x001D): retried once, after the busy clears; the next refusal is named and fails the recall.
            if (++_busyRefusals <= BusyRetries)
            {
                _busyRetryAt = now + CastRules.DefaultBusyWaitSeconds;
                _busySince = double.NaN;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"recall: ACE answered You're too busy (0x001D; VerifyBusy, Player_Magic.cs 375-383 @95ef426d): {_spellName} is retried once, after the busy clears and at least {CastRules.DefaultBusyWaitSeconds:0} s from now (busy refusal {_busyRefusals} of {BusyRetries})"));
                return Status;
            }

            return Fail("cast", $"ACE answered You're too busy (0x001D) again after the busy cleared ({_busyRefusals} busy refusals, over the {BusyRetries} retried): " + described, now);
        }

        if (report.End == CastEnd.Fizzled)
        {
            return Fail("cast", $"{_spellName} fizzled {_fizzlesThisRecall} times in one recall, over the cap of {MaxFizzles}: " + described, now);
        }

        if (_refusal != 0)
        {
            return Fail("cast", "ACE refused: " + (NameOfRefusal(_refusal) ?? "WeenieError 0x" + _refusal.ToString("X4", CultureInfo.InvariantCulture)), now);
        }

        if (report.End == CastEnd.Cancelled && _actions.KillSwitch)
        {
            return EndFailed(KillSwitchStep, "the kill switch is on: the recall sends nothing more (" + report.Why + ")", now, restore: false);
        }

        if (!report.End.Succeeded())
        {
            return Fail("cast", described, now);
        }

        _castEnded = now;
        _phase = Phase.Teleport;
        return Status;
    }

    // Counts and logs each fizzle the attempt has met since the last look (one line a fizzle, as when each was its own cast); the one over the cap fails the recall instead of retrying.
    private void NoteFizzles(int total)
    {
        while (_fizzlesSeen < total)
        {
            _fizzlesSeen++;
            _fizzles++;
            if (++_fizzlesThisRecall <= MaxFizzles)
            {
                _log.Warn(LogCategory, $"recall: {_spellName} fizzled (fizzle {_fizzlesThisRecall} of at most {MaxFizzles}); casting again");
            }
        }
    }

    private RecallStatus StepTeleport(double now)
    {
        if (_refusal != 0 && NameOfRefusal(_refusal) is { } named)
        {
            return Fail("cast", "ACE refused: " + named, now);
        }

        var travel = _state.Self.Travel;
        if (travel is SelfTravel.TeleportAnnounced or SelfTravel.TeleportArrived)
        {
            NoteTeleport(now);
        }

        if (!_teleportSeen)
        {
            return now - _castEnded > TeleportSeconds
                ? Fail("teleport", $"no teleport began within {TeleportSeconds:0} s of the cast ending (the travel is {travel}); the spell may have taken effect elsewhere or ACE did not recall", now)
                : Status;
        }

        if (travel != SelfTravel.Present)
        {
            return now - _teleportSeenAt > ArrivalSeconds
                ? Fail("arrival", $"the teleport began but the character was not present within {ArrivalSeconds:0} s (the travel is {travel})", now)
                : Status;
        }

        var landblock = _state.Self.Position is { } at ? at.CellId >> 16 : 0;
        if (landblock != _homeLandblock)
        {
            return Fail("arrival", $"present in landblock 0x{landblock:X4}, not the lifestone's 0x{_homeLandblock:X4} (cell {(_state.Self.Position is { } p ? "0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture) : "unknown")})", now);
        }

        _log.Info(LogCategory, $"recall: arrived in landblock 0x{landblock:X4} after {now - _started:0.0} s");
        if (_swap is not null && _swapIn)
        {
            _phase = Phase.SwapOut;
            _swap.Begin(SwapDirection.Out, now);
            return Status;
        }

        return EndDone(now);
    }

    private RecallStatus StepSwapOut(double now)
    {
        var status = _swap!.Step(now);
        switch (status)
        {
            case SwapStatus.Done:
                return _firstFailure is null ? EndDone(now) : EndFailedWith(now);
            case SwapStatus.Failed:
                return _firstFailure is null
                    ? EndFailed("swap-out", _swap.Failure + (_swap.Detail is null ? string.Empty : ": " + _swap.Detail), now, restore: false)
                    : EndFailedWith(now);
            default:
                return Status;
        }
    }

    /// <summary>A failure after the swap in restores the weapon first (the swap's Out half), then ends with the first failure.</summary>
    private RecallStatus Fail(string step, string detail, double now) => EndFailed(step, detail, now, restore: true);

    private RecallStatus EndFailed(string step, string detail, double now, bool restore)
    {
        _firstFailure ??= "recall:" + step;
        _firstDetail ??= detail;
        _log.Error(LogCategory, $"recall: failed at {step}: {detail}");
        _casting = false;
        _attempt.Stop("the recall failed at " + step, now);
        if (restore && _swap is not null && _swapIn && _phase != Phase.SwapOut)
        {
            _phase = Phase.SwapOut;
            _pending = null;
            _swap.Begin(SwapDirection.Out, now);
            return Status;
        }

        return EndFailedWith(now);
    }

    private RecallStatus EndFailedWith(double now)
    {
        Failure = _firstFailure;
        Detail = _firstDetail;
        Status = RecallStatus.Failed;
        _phase = Phase.None;
        _seconds += now - _started;
        return Status;
    }

    private RecallStatus EndDone(double now)
    {
        _recalls++;
        _seconds += now - _started;
        Status = RecallStatus.Done;
        _phase = Phase.None;
        return Status;
    }

    private void NoteTeleport(double now)
    {
        if (!_teleportSeen)
        {
            _teleportSeen = true;
            _teleportSeenAt = now;
        }
    }

    private void OnTravel(in SelfTravelChanged e)
    {
        if (Status == RecallStatus.Running && _phase is Phase.Cast or Phase.Teleport && e.Travel is SelfTravel.TeleportAnnounced or SelfTravel.TeleportArrived)
        {
            NoteTeleport(_session.Clock.Now);
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (Status == RecallStatus.Running && e.Line.Kind == ChatKind.WeenieError && e.Line.ChatType is 0x049E or 0x049F or 0x04A3 or 0x04AD or 0x04CC)
        {
            _refusal = e.Line.ChatType;
        }
    }

    private static string Describe(ActionOutcome outcome)
    {
        var named = outcome.Code != 0 ? NameOfRefusal(outcome.Code) : null;
        return outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (named is null ? string.Empty : " " + named) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
    }
}
