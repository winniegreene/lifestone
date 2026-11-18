using System.Globalization;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What <see cref="RetreatRule.Decide"/> reads (M6 6 F1).</summary>
/// <param name="HealthPercent">The health now, a percentage of the vitae-reduced maximum.</param>
/// <param name="IncomingPerSecond">The damage taken a second (the fight's measured rate).</param>
/// <param name="HealPerSecond">The health a second the payable heal gives (0 when nothing is payable).</param>
/// <param name="NothingPayable">True when no heal is payable (the guard's <c>TODO(f23-guard-nothing-paid)</c> case).</param>
/// <param name="EmergencyAside">True when the fight's emergency drain set itself aside (<see cref="FightEmergency"/>).</param>
/// <param name="RetreatPercent">The ground's retreat line (the profile's <c>fight.retreat_health_percent</c>; 0 is never; M6 14 holding safety: the line derived on her live state where the place's targets carry their attack's facts, <see cref="LiveFightLine"/>).</param>
/// <param name="Now">The session clock, in seconds.</param>
public readonly record struct RetreatInput(double HealthPercent, double IncomingPerSecond, double HealPerSecond, bool NothingPayable, bool EmergencyAside, int RetreatPercent, double Now)
{
    /// <summary>M6 7 Y: true when the biggest hit seen takes all the health left (<see cref="IncomingRead.NextHitKills"/>): a retreat the rule would take goes at once, with no hold.</summary>
    public bool NextHitKills { get; init; }

    /// <summary>M6 7 Y: true while the fight's emergency has a bolt on its way that is expected to kill the only attacker: the retreat waits for it, and its hold starts again after it.</summary>
    public bool Finishing { get; init; }

    /// <summary>
    /// M6 14 the losing race: true when the race is lost (<see cref="LosingRace"/>: the heal she can sustain is under the incoming, and one more heal would leave too little health for the way out) with Lifestone Recall paid: the retreat
    /// goes at once, whatever the line and with no hold (a hold of three seconds at the incoming rate is the health the way out needed).
    /// </summary>
    public bool Losing { get; init; }
}

/// <summary>What <see cref="RetreatRule.Decide"/> answers.</summary>
/// <param name="Retreat">True to leave the ground for home.</param>
/// <param name="Why">A fixed token naming the arm that holds (or the one that cleared it); never built per call.</param>
public readonly record struct RetreatDecision(bool Retreat, string Why);

/// <summary>
/// M6 6 F1 (m5-fight-death-midrun): the retreat rule. Pure and allocation-free; the instance keeps the hold timer. The caller (the autopilot, slice F2) asks it each tick of a fight and, on a retreat, stops the fight
/// with <see cref="StopReason"/> and walks, recalls or portals home. The line (<see cref="Line"/>) is logged once per change (<see cref="ChangedSince"/>).
/// </summary>
/// <remarks>
/// M6 14 the losing race: a lost race (<see cref="RetreatInput.Losing"/>) retreats at once, over the line or under it, with no hold (<see cref="LosingToken"/>); a finishing bolt still comes first.
/// Retreat when the health is under <see cref="RetreatInput.RetreatPercent"/> (0 = never) and one of: the incoming damage beats the payable heal, nothing is payable, or the emergency drain is set aside. The condition must hold
/// for <see cref="RetreatHoldSeconds"/> without a break, so one bad second does not send a healthy run home. M6 7 Y: when the next hit kills (<see cref="RetreatInput.NextHitKills"/>) the hold is waived; a finishing bolt on its way
/// (<see cref="RetreatInput.Finishing"/>) comes first: a dead attacker stops the incoming, and a retreat would throw the kill away with the mage still in reach of the blow.
/// </remarks>
public sealed class RetreatRule
{
    /// <summary>
    /// Seconds the condition must hold before the rule retreats. Lifestone's own tuning, not a retail or ACE figure: the incoming rate (<see cref="RecentHits"/>) reads the last few seconds and a heal cast takes about
    /// two (the gesture plus the server's round trip), so 3 s lets one cast land and show its effect before the run gives up, and is short enough that a creature hitting hard cannot finish the mage first.
    /// </summary>
    public const double RetreatHoldSeconds = 3.0;

    private const string Off = "off";
    private const string HealthOk = "health-ok";
    private const string HealthUnread = "health-unread";
    private const string Clear = "clear";
    private const string HoldIncoming = "hold:incoming-beats-heal";
    private const string HoldNothing = "hold:nothing-payable";
    private const string HoldAside = "hold:emergency-aside";
    private const string GoIncoming = "incoming-beats-heal";
    private const string GoNothing = "nothing-payable";
    private const string GoAside = "emergency-aside";
    private const string GoNextHit = "next-hit-kills";
    private const string ClearNextHit = "clear:next-hit-kills";
    private const string HoldFinishing = "hold:finishing-bolt";

    /// <summary>M6 14 the losing race: the token of the retreat a lost race takes.</summary>
    public const string LosingToken = "losing-race";

    private double _since = double.NaN;
    private bool _logged;
    private long _loggedHealth;
    private long _loggedIncoming;
    private long _loggedHeal;
    private bool _loggedPayable;
    private bool _loggedAside;
    private int _loggedLine;
    private bool _loggedRetreat;
    private bool _loggedUnread;
    private bool _loggedNextHit;
    private bool _loggedFinishing;
    private bool _loggedLosing;

    /// <summary>The fight stop reason that routes to the way home (<c>retreat</c>).</summary>
    public static FightStopReason StopReason { get; } = new("retreat");

    /// <summary>Decides for this tick. Allocation-free: the <see cref="RetreatDecision.Why"/> is one of the fixed tokens.</summary>
    /// <param name="i">The inputs.</param>
    public RetreatDecision Decide(in RetreatInput i)
    {
        if (i.RetreatPercent <= 0)
        {
            _since = double.NaN;
            return new RetreatDecision(false, Off);
        }

        if (double.IsNaN(i.HealthPercent))
        {
            // M6 6 FXD3 (6-D15): an unread health is not a healthy one; its own token, logged as a change by ChangedSince. No retreat is taken on a number nobody read.
            _since = double.NaN;
            return new RetreatDecision(false, HealthUnread);
        }

        if (i.Finishing && (i.Losing || i.HealthPercent < i.RetreatPercent))
        {
            _since = double.NaN;
            return new RetreatDecision(false, HoldFinishing);
        }

        if (i.Losing)
        {
            _since = double.IsNaN(_since) ? i.Now : _since;
            return new RetreatDecision(true, LosingToken); // M6 14 the losing race: the way out's health is what a hold would spend
        }

        if (!(i.HealthPercent < i.RetreatPercent))
        {
            _since = double.NaN;
            return new RetreatDecision(false, HealthOk);
        }

        var incoming = i.IncomingPerSecond > i.HealPerSecond && i.IncomingPerSecond > 0;
        if (!incoming && !i.NothingPayable && !i.EmergencyAside)
        {
            _since = double.NaN;
            return new RetreatDecision(false, i.NextHitKills ? ClearNextHit : Clear);
        }

        if (i.NextHitKills)
        {
            _since = double.IsNaN(_since) ? i.Now : _since;
            return new RetreatDecision(true, GoNextHit); // M6 7 Y: a hold of three seconds is a hit too many
        }

        if (double.IsNaN(_since))
        {
            _since = i.Now;
        }

        var held = i.Now - _since >= RetreatHoldSeconds;
        if (i.NothingPayable)
        {
            return new RetreatDecision(held, held ? GoNothing : HoldNothing);
        }

        if (i.EmergencyAside)
        {
            return new RetreatDecision(held, held ? GoAside : HoldAside);
        }

        return new RetreatDecision(held, held ? GoIncoming : HoldIncoming);
    }

    /// <summary>Forgets the hold timer and the last logged line (a new fight, or the retreat was taken).</summary>
    public void Reset()
    {
        _since = double.NaN;
        _logged = false;
    }

    /// <summary>
    /// True when the line for <paramref name="i"/> and <paramref name="d"/> differs from the last one this returned true for (health to the whole percent, the rates to a tenth, the flags, the line, the verdict), and
    /// remembers it. Allocation-free; the caller logs <see cref="Line"/> only when it is true.
    /// </summary>
    /// <param name="i">The inputs.</param>
    /// <param name="d">The decision for them.</param>
    public bool ChangedSince(in RetreatInput i, in RetreatDecision d)
    {
        var health = (long)Math.Round(i.HealthPercent, MidpointRounding.AwayFromZero);
        var incoming = (long)Math.Round(i.IncomingPerSecond * 10, MidpointRounding.AwayFromZero);
        var heal = (long)Math.Round(i.HealPerSecond * 10, MidpointRounding.AwayFromZero);
        var payable = !i.NothingPayable;
        if (_logged && health == _loggedHealth && incoming == _loggedIncoming && heal == _loggedHeal && payable == _loggedPayable
            && i.EmergencyAside == _loggedAside && i.RetreatPercent == _loggedLine && d.Retreat == _loggedRetreat && double.IsNaN(i.HealthPercent) == _loggedUnread
            && i.NextHitKills == _loggedNextHit && i.Finishing == _loggedFinishing && i.Losing == _loggedLosing)
        {
            return false;
        }

        _logged = true;
        _loggedHealth = health;
        _loggedIncoming = incoming;
        _loggedHeal = heal;
        _loggedPayable = payable;
        _loggedAside = i.EmergencyAside;
        _loggedLine = i.RetreatPercent;
        _loggedRetreat = d.Retreat;
        _loggedUnread = double.IsNaN(i.HealthPercent);
        _loggedNextHit = i.NextHitKills;
        _loggedFinishing = i.Finishing;
        _loggedLosing = i.Losing;
        return true;
    }

    /// <summary>The log line: <c>retreat: decide health=&lt;h&gt;% incoming=&lt;x&gt;/s heal=&lt;y&gt;/s payable=&lt;yes|no&gt; aside=&lt;yes|no&gt; line=&lt;p&gt;% -&gt; &lt;retreat|hold&gt;</c>.</summary>
    /// <param name="i">The inputs.</param>
    /// <param name="d">The decision for them.</param>
    public static string Line(in RetreatInput i, in RetreatDecision d) => string.Create(
        CultureInfo.InvariantCulture,
        $"retreat: decide health={(double.IsNaN(i.HealthPercent) ? "unread" : i.HealthPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%")} incoming={i.IncomingPerSecond:0.#}/s heal={i.HealPerSecond:0.#}/s payable={(i.NothingPayable ? "no" : "yes")} aside={(i.EmergencyAside ? "yes" : "no")} line={i.RetreatPercent}%{(i.NextHitKills ? " next-hit-kills" : string.Empty)}{(i.Finishing ? " finishing-bolt" : string.Empty)}{(i.Losing ? " losing-race" : string.Empty)} -> {(d.Retreat ? "retreat" : "hold")}");
}
