using System.Globalization;
using Lifestone.Client.Diagnostics;
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
/// </summary>
/// <remarks>
/// <para><b>Steps and the failure name.</b> The steps are <c>present</c> (the character must be in the world and not in portal space), <c>swap-in</c>, <c>spell</c> (the spellbook, once the description has arrived, lacks spell 1635), <c>mode</c>
/// (the magic mode, for a caster that has not entered it), <c>cast</c>, <c>teleport</c>, <c>arrival</c> and <c>swap-out</c>. A failure is <c>recall:&lt;step&gt;</c> with a sentence behind it; nothing is retried silently, but a fizzled cast (0x0402) is counted and cast again, at most <see cref="MaxFizzles"/> times a recall (M4 P5a fix F-abil, P5a-R4: Item Enchantment is built to the 0.9 chance rule, so about one cast in ten fizzles and a journey must not fail for it). The cast's refusals are named from ACE
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

    /// <summary>The landblock of Holtburg's lifestone (the BSD Mage's bind, M3 D34; the fighters bind there too in P5b's first station).</summary>
    public const uint HoltburgLandblock = 0xA9B4;

    /// <summary>Seconds after the cast ended for the teleport to start (ACE's recall delay is 2 s).</summary>
    public const double TeleportSeconds = 20.0;

    /// <summary>Seconds, once the teleport was seen, to be present at the destination.</summary>
    public const double ArrivalSeconds = 30.0;

    /// <summary>Seconds one whole recall may take, swap halves included.</summary>
    public const double MaxSeconds = 150.0;

    /// <summary>The fizzles one recall retries (a named cap, not retail's: Lifestone's own); the next fizzle fails the recall <c>recall:cast</c>. At the 0.9 chance rule five in a row is one in a hundred thousand.</summary>
    public const int MaxFizzles = 5;

    private readonly IGameSessionView _session;
    private readonly IActionApi _actions;
    private readonly GameState _state;
    private readonly CasterSwap? _swap;
    private readonly uint _homeLandblock;
    private readonly ILogSink _log;
    private readonly IDisposable _travelSubscription;
    private readonly IDisposable _chatSubscription;

    private Phase _phase;
    private ActionHandle? _pending;
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
    /// <param name="homeLandblock">The landblock of the lifestone the character is bound to (the high 16 bits of a cell id).</param>
    public RecallHome(IGameSessionView session, CasterSwap? swap, ILogSink log, uint homeLandblock = HoltburgLandblock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _actions = session.Actions;
        _state = session.State;
        _swap = swap;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _homeLandblock = homeLandblock & 0xFFFF;
        _travelSubscription = session.Events.Subscribe<SelfTravelChanged>(OnTravel);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>
    /// The recall for the character named <paramref name="characterName"/>: a Brawler or an Archer (over a <see cref="CasterSwap"/>), BSD Mage (alone), or null for any other character (the Hunter has no magic).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="characterName">The character's name as the read model holds it, or null.</param>
    /// <param name="log">The log.</param>
    /// <param name="homeLandblock">The lifestone's landblock.</param>
    public static RecallHome? ForCharacter(IGameSessionView session, string? characterName, ILogSink log, uint homeLandblock = HoltburgLandblock)
    {
        ArgumentNullException.ThrowIfNull(session);
        var template = FighterTemplate.ForName(characterName);
        if (template is { HasMagic: true })
        {
            return new RecallHome(session, CasterSwap.For(template, session.Actions, session.State, log), log, homeLandblock);
        }

        return string.Equals(characterName?.Trim(), MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase) ? new RecallHome(session, null, log, homeLandblock) : null;
    }

    /// <summary>Where the recall is.</summary>
    public RecallStatus Status { get; private set; }

    /// <summary>The caster swap the recall runs, or null for the mage.</summary>
    public CasterSwap? Swap => _swap;

    /// <summary>The lifestone's landblock (the high 16 bits of its cell ids).</summary>
    public uint HomeLandblock => _homeLandblock;

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
        _swapIn = false;
        _pending = null;
        _started = now;
        Status = RecallStatus.Running;
        _log.Info(LogCategory, "recall: begin" + (_swap is null ? string.Empty : " (with the caster swap)"));
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
        if (book.IsKnown && !book.Knows(LifestoneRecallSpell))
        {
            return Fail("spell", "Lifestone Recall (spell 1635) is not in the spellbook (rerun --scenario=fighter-build or mage-build: it teaches the profile's spells)", now);
        }

        if (_state.Self.Qualities.CombatMode == CombatModes.Magic)
        {
            _phase = Phase.Cast;
            _pending = null;
            return Status;
        }

        if (_pending is null)
        {
            _pending = _actions.SetCombatMode(CombatMode.Magic);
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

    private RecallStatus StepCast(double now)
    {
        if (_pending is null)
        {
            _log.Info(LogCategory, "recall: casting Lifestone Recall (" + LifestoneRecallSpell.ToString(CultureInfo.InvariantCulture) + ")");
            _pending = _actions.Cast(LifestoneRecallSpell);
            return Status;
        }

        if (_refusal != 0 && NameOfRefusal(_refusal) is { } named)
        {
            // ACE told the refusal as a WeenieError chat line; the cast's own outcome follows (UseDone).
            return Fail("cast", "ACE refused: " + named, now);
        }

        if (_pending.Outcome is not { } outcome)
        {
            return Status;
        }

        _pending = null;
        if (outcome.Kind == ActionOutcomeKind.ServerError && outcome.Code == GearCasts.FizzleCode)
        {
            // P5a-R4: a fizzle is counted and the cast retried inside the cap, not a failed recall (the same rule as the harm-heal script's).
            _fizzles++;
            if (++_fizzlesThisRecall > MaxFizzles)
            {
                return Fail("cast", $"Lifestone Recall fizzled {_fizzlesThisRecall} times in one recall, over the cap of {MaxFizzles}: " + Describe(outcome), now);
            }

            _log.Warn(LogCategory, $"recall: Lifestone Recall fizzled (fizzle {_fizzlesThisRecall} of at most {MaxFizzles}); casting again");
            return Status;
        }

        if (_refusal != 0)
        {
            return Fail("cast", "ACE refused: " + (NameOfRefusal(_refusal) ?? "WeenieError 0x" + _refusal.ToString("X4", CultureInfo.InvariantCulture)), now);
        }

        if (!outcome.Succeeded)
        {
            return Fail("cast", Describe(outcome), now);
        }

        _castEnded = now;
        _phase = Phase.Teleport;
        return Status;
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
