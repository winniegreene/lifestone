using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 6 F2 (<c>m5-fight-death-midrun</c>, map-death.md section 5): the retreat on the ground, the run's vitae ledger and the run's own worn memory.
/// <list type="bullet">
/// <item><b>The retreat.</b> One <see cref="RetreatRule"/> a run is asked in the fight's stop decision (each frame), in the upkeep heal while the keeper's pass holds, and when the vitals guard finds nothing payable under its line
/// (<c>f23-guard-nothing-paid</c>'s answer). Its inputs are what the run already reads: the health share, the <see cref="HostileWatch"/>'s incoming rate, the guard's payable heal a second (<see cref="AutopilotVitalsGuard.AssessHeal"/>),
/// nothing payable, the guard's emergency set aside, and the ground's line (<see cref="RetreatLineFor"/>: the profile's <c>fight.retreat_health_percent</c>, 0 = never; without it the derived <see cref="HealthFloorPercent"/>, named).
/// A retreat is the way home (the Travel state, <see cref="AutopilotTravelRule.Home"/>: Lifestone Recall or the way home by portals; the keeper is not run before it), then the keeper's pass at home, then the known state, which goes
/// back out. The second retreat from one ground in one cycle stops <c>retreat:again:&lt;ground&gt;</c> (<c>TODO(m6-retreat-rung-drop)</c>: the ladder's step back to the rung below, placed item 10).</item>
/// <item><b>The way out</b> (M6 7 BE, <see cref="ManaWayOut"/>). On the ladder, with Lifestone Recall ready, attackers on her and the mana at the line (the recall's need plus one Drain Health Other I), she leaves by the same stop and the same way home,
/// named apart (<c>retreat: mana:</c>, the cycle's <c>why=retreat-mana</c>), whatever the ground's health line says: the Lodge's is 0. A retreat for mana counts in <c>retreats=</c> and never toward <c>retreat:again</c>: it is the rule working, not a rung too hard.</item>
/// <item><b>The vitae.</b> One <see cref="VitaeLedger"/> a run: its <see cref="VitaeLedger.Fields"/> close the cycle and done lines.</item>
/// <item><b>The worn set.</b> One <see cref="WornMemory"/> a run (the snapshot while alive, the checkpoint's seed and entries, the audit's restorer, the death station through <see cref="JourneyFacts.Worn"/> into
/// <see cref="DeathOptions.Worn"/>): two runs in one process never share one (B2's "Collection was modified" race in <see cref="WornRestorer"/>'s plan).</item>
/// </list>
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The prefix of the stop after the second retreat from one ground in one cycle (<c>retreat:again:&lt;ground&gt;</c>).</summary>
    public const string RetreatAgainPrefix = "retreat:again:";

    /// <summary>The retreats from one ground in one cycle at which the run stops (Lifestone's own: one retreat is a bad pull; a second on the same rung says the rung is too hard for the mage today).</summary>
    public const int RetreatsPerGroundCycle = 2;

    /// <summary>The cycle line's <c>why=</c> name of a retreat.</summary>
    public const string RetreatWhy = "retreat";

    /// <summary>M6 7 BE: the cycle line's <c>why=</c> name of a retreat for mana.</summary>
    public const string RetreatManaWhy = "retreat-mana";

    /// <summary>M6 7 BE: how often the book, the tie and the components are read for the way out, in seconds (Lifestone's own: they change with a learn or a trip, the mana is read every frame).</summary>
    public const double WayOutReadSeconds = 1;

    /// <summary>M6 7 BE: how long the way out stands aside when the check before the recall finds it unpaid, in seconds (Lifestone's own: long enough for a press to land or the mana to come back, short against a fight).</summary>
    public const double WayOutAsideSeconds = 10;

    private readonly RetreatRule _retreat = new();
    private readonly LiveFightLine _fightLine = new(); // M6 14 holding safety: the derived line on her live state
    private int _fightLineLogged = -1;
    private string _fightLineGround = string.Empty;
    private string _fightLineWhy = string.Empty;
    private string? _fightLineIgnored;
    private readonly VitaeLedger _vitae = new();
    private readonly WornMemory _worn = new();
    private IGameSessionView? _vitaeView;
    private bool _retreating;
    private bool _retreatHeal;
    private bool _retreatUnder;
    private bool _retreatAsideLogged;
    private string _retreatWhy = string.Empty;
    private int _retreatCycle;
    private string? _retreatGround;
    private int _retreatsHere;
    private string _guardNoRetreatLogged = string.Empty;
    private bool _retreatForMana; // the stop asked is the way out's, until it is taken or the state moves on
    private bool _manaRetreating; // the way home under way is the way out's
    private WayOutLine? _wayOutLine;
    private double _wayOutReadAt = double.NegativeInfinity;
    private double _wayOutAsideUntil = double.NegativeInfinity;
    private string _wayOutArmed = string.Empty;
    private string _wayOutDecided = string.Empty;

    /// <summary>This run's worn memory (never <see cref="WornMemory.Shared"/>).</summary>
    public WornMemory Worn => _worn;

    /// <summary>The retreats taken this run (the ended cycles and the one in progress).</summary>
    public int Retreats => _total.Retreats + _tally.Retreats;

    /// <summary>The vitae fields of the cycle and done lines (<c>vitae=&lt;pct|none&gt; vitae_steps=&lt;n&gt;</c>).</summary>
    public string VitaeFields => _vitae.Fields;

    /// <summary>
    /// The retreat line on <paramref name="ground"/>: the profile's <c>fight.retreat_health_percent</c> (0 = never), else <see cref="HealthFloorPercent"/>, the same derived line the run leg's retreat reads
    /// (<c>TravelLegSafety</c>); <paramref name="why"/> names which.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="why">Where the line comes from.</param>
    public static int RetreatLineFor(HuntingGround ground, out string why)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (ground.FightRetreatHealthPercent is { } percent)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"{ground.Key}'s fight.retreat_health_percent{(percent == 0 ? ": never" : string.Empty)}");
            return percent;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"derived: {ground.Key}'s profile has no fight.retreat_health_percent, the autopilot's {HealthFloorPercent:0}%");
        return (int)HealthFloorPercent;
    }

    /// <summary>
    /// M6 14 holding safety (the owner, 2026-11-13: every number about how she fights is computed at run time from her live state and the creatures' facts): the retreat line now. Where the place's targets carry their attack's facts the
    /// line is derived on her live state (<see cref="LiveFightLine"/>: her maximum health, her armour, her live resistance, so a landed Tusker's Gift raises it while it lasts, and the fight's measured damage a second) and wins over the
    /// profile's hand-sized <c>fight.retreat_health_percent</c>, which is logged as ignored; elsewhere <see cref="RetreatLineFor(HuntingGround, out string)"/>, as before.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="why">Where the line comes from.</param>
    /// <param name="on">M6 16 E3.1: the ground the fight asked about stands on when it is not the rung's (a quest's sub-fight on its step's ground); null: the rung's ground, as ever.</param>
    private int RetreatLineNow(IGameSessionView session, out string why, HuntingGround? on = null)
    {
        if (DerivedLineNow(session, on) is { } line)
        {
            why = _fightLineWhy;
            return line;
        }

        return RetreatLineFor(on ?? _ground, out why);
    }

    /// <summary>
    /// M6 14 holding safety: the upkeep's health floor now: the derived line where the place gives the facts (<see cref="RetreatLineNow"/>; under it one pack's hits while she leaves could take what she has left, so the keeper heals before the next pull),
    /// at most <see cref="JourneyPlans.FightHealthPercent"/>, the share the keeper's heal in place goes to: a floor over it could never be met and would stop every fight again after each heal (a landed Tusker's Gift lifts the line to 95); above
    /// it the retreat rule decides on the full line. Else <see cref="HealthFloorFor"/> as before.
    /// </summary>
    /// <param name="session">The session.</param>
    private double HealthFloorNow(IGameSessionView session) => DerivedLineNow(session) is { } line ? Math.Min(line, JourneyPlans.FightHealthPercent) : HealthFloorFor(_ground, _options);

    /// <summary>
    /// The derived line on her live state, or null when the place names no creature's attack facts. The line is logged when it changes (with every number), and a profile line it overrides is named once per ground. M6 14 retreat attackers
    /// (rule 1): on a derived place the creatures that hit her within <see cref="HostileWatch.AttackQuietSeconds"/> and stand within <see cref="HuntScenario.DefendReachMetres"/> are read too, each by its own facts and damage type
    /// (<see cref="LiveFightLine.ReadAttackers"/>; one with no facts gives the general fallback), and the line is the larger of the place's and theirs. A place that names no facts keeps the caller's own line (the den's D91 0 among them:
    /// the owner's rule for that ground stands until its profile carries facts).
    /// </summary>
    private int? DerivedLineNow(IGameSessionView session, HuntingGround? on = null)
    {
        var ground = on ?? _ground; // M6 16 E3.1: a quest's sub-fight is read on its own ground
        var read = _hostiles?.Read(_clock) ?? default;
        if (_hostiles is { } attack)
        {
            read = read with { BiggestHit = Math.Max(read.BiggestHit, attack.LargestOfAttack(_clock)) }; // M6 14 the losing race: the attack's largest hit, not only the last ten seconds' (a critical can come again in the way out)
        }

        var d = _fightLine.Read(session.State, ground, _clock, read);
        if (!d.Derived)
        {
            return null;
        }

        var percent = d.Percent;
        var why = d.Why();
        if (_hostiles is not null)
        {
            RetreatPresent(session); // M6 16 Q2: the threat board answers the retreat line's own question (AutopilotScenario.ThreatShadow.cs)
            if (_fightPresent.Count > 0)
            {
                var attackers = _fightLine.ReadAttackers(session.State, _fightPresent, _clock, read.PerSecond, read.BiggestHit);
                if (attackers.Percent > percent)
                {
                    percent = attackers.Percent;
                    why = attackers.Why() + string.Create(CultureInfo.InvariantCulture, $"; over the place's {d.Percent}%");
                }
            }
        }

        if (percent != _fightLineLogged || !string.Equals(_fightLineGround, ground.Key, StringComparison.Ordinal))
        {
            _fightLineLogged = percent;
            _fightLineGround = ground.Key;
            _fightLineWhy = why;
            _log.Info(LogCategory, "fight line: " + _fightLineWhy + " (M6 14 holding safety: the retreat line and the upkeep's health floor)");
        }

        if (ground.FightRetreatHealthPercent is { } sized && !string.Equals(_fightLineIgnored, ground.Key, StringComparison.Ordinal))
        {
            _fightLineIgnored = ground.Key;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"fight line: {ground.Key}'s fight.retreat_health_percent {sized} is ignored: a hand-sized number, and the line is derived from her live state and the place's facts (M6 14 holding safety)"));
        }

        return percent;
    }

    private readonly List<PresentAttacker> _fightPresent = []; // M6 14 retreat attackers: the creatures present the fight's line reads

    /// <summary>Binds the vitae ledger to the session (once per view: a reconnect's new view moves it and keeps the count).</summary>
    private void BindVitae(IGameSessionView session)
    {
        if (!ReferenceEquals(_vitaeView, session))
        {
            _vitaeView = session;
            _vitae.Start(session);
        }
    }

    /// <summary>
    /// Asks the retreat rule now. Under the line it reads the guard's payable heal and set-aside; above it, nothing more (one vitals read). The rule's line is logged once per change while under the line, and once on the way back over it.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="where">Where it is asked, for the line (<c>fight</c>, <c>upkeep heal</c>, <c>guard: &lt;state&gt;</c>).</param>
    /// <param name="nothingPaid">True when the guard has just found nothing payable under its line.</param>
    /// <param name="raceLost">M6 14 the losing race: true when the guard has just found the race lost and handed it to this rule (a state on the ground).</param>
    /// <param name="sub">M6 16 E3.1: a quest's sub-fight, asked as the ground fight is (<paramref name="where"/> then names it): the line is read on its own ground, the lost race is read, and its finishing bolt holds the retreat. Null: as ever.</param>
    private bool RetreatDue(IGameSessionView session, string where, bool nothingPaid = false, bool raceLost = false, FightScenario? sub = null)
    {
        var inFight = where == "fight" || sub is not null;
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || max == 0 || health == 0)
        {
            return false; // unread or dead: the death path, never a retreat
        }

        var line = RetreatLineNow(session, out var lineWhy, sub?.Ground);
        var share = health * 100.0 / max;
        var losing = raceLost || (inFight && FightRaceLost(session, health, out _));
        var under = (line > 0 && share < line) || losing;
        if (!under && !_retreatUnder)
        {
            _retreat.Decide(new RetreatInput(share, 0, 0, false, false, line, _clock)); // clears the hold
            return false;
        }

        var heal = 0.0;
        var nothing = nothingPaid;
        var aside = false;
        if (under && _guard is { } guard)
        {
            if (guard.AssessHeal(session, out var rate, out var payable))
            {
                heal = rate;
                nothing |= !payable;
            }

            aside = guard.EmergencyAside(_clock);
            if (inFight && !_retreatAsideLogged)
            {
                _retreatAsideLogged = true;
                _log.Warn(LogCategory, "retreat: in the fight, aside= is the vitals guard's set-aside: the fight's own emergency drain does not say when it set itself aside (IStoppableFight has no such read; TODO(m6-retreat-fight-aside))");
            }
        }

        var read = _hostiles?.Read(_clock) ?? default; // M6 7 Y: the recent hits, the fight's emergency's number
        var input = new RetreatInput(share, read.PerSecond, heal, nothing, aside, line, _clock) { NextHitKills = read.NextHitKills(health), Finishing = inFight && ((IStoppableFight?)sub ?? StoppableFight()) is FightScenario { FinishingBolt: true }, Losing = losing };
        var decided = _retreat.Decide(in input);
        if (_retreat.ChangedSince(in input, in decided))
        {
            _log.Info(LogCategory, $"{RetreatRule.Line(in input, in decided)} ({where}; why {decided.Why}; line: {lineWhy}{(losing ? "; the race: " + (_guard?.RaceWhy ?? _fightRaceWhy) : string.Empty)})");
        }

        _retreatUnder = under;
        if (decided.Retreat)
        {
            _retreatToken = decided.Why; // M6 16 E3.1: the arm's token, for a leave that is named by it
            _retreatWhy = string.Create(CultureInfo.InvariantCulture, $"{decided.Why} at health {health}/{max} under the {line}% line ({where})");
            if (string.Equals(decided.Why, RaceToken, StringComparison.Ordinal))
            {
                // M6 14 the losing race: the next state's first frames are under the guard's line: no heal starts before Lifestone Recall is cast
                _retreatWhy = string.Create(CultureInfo.InvariantCulture, $"{RaceToken} at health {health}/{max} ({where}): {(raceLost ? _guard?.RaceWhy : _fightRaceWhy)}");
                _guard?.RaceTaken(_clock);
                _guard?.HoldOff(_clock, $"the retreat rule took the losing race ({where})");
            }
        }

        return decided.Retreat;
    }

    private string _fightRaceWhy = string.Empty;
    private string _fightRaceClosed = string.Empty;
    private string _retreatToken = string.Empty;

    /// <summary>
    /// M6 14 the losing race in the fight (FightScenario's own emergency heals under its line, F16/F19/F21, and never leaves; the retreat rule's arms waited for the ground's line and then held three seconds, the hole the travel guard had):
    /// the guard's read of the race (<see cref="AutopilotVitalsGuard.Race"/>: the heal she can sustain against the incoming rate, the way out after one more heal). Read only when the incoming could take her under the way out's need
    /// within one more cast (a frame's check with no pick otherwise). Lost with Lifestone Recall paid (<see cref="UrgentRecallRead"/>): true, and the rule leaves at once (<see cref="RetreatRule"/>'s losing-race arm); lost with the recall
    /// unpaid: said once a reason, and the old order stands.
    /// </summary>
    private bool FightRaceLost(IGameSessionView session, uint health, out string why)
    {
        why = string.Empty;
        if (_guard is not { } guard || _hostiles is not { } watch || !SurvivalOn())
        {
            return false; // the ladder's job only, as the survival layer: the den's fights keep their order
        }

        var read = watch.Read(_clock);
        var biggest = Math.Max(read.BiggestHit, watch.LargestOfAttack(_clock));
        if (read.PerSecond <= 0 || health > biggest + (read.PerSecond * (RecallWayOut.Seconds + MeasuredHeals.DrainSecondsSeed[^1])))
        {
            return false; // the incoming cannot make the race lost before the next cast: no pick is read
        }

        if (!guard.Race(session, _clock, out var input, out var answer) || !answer.Lost)
        {
            return false;
        }

        _hourRaceLost = true; // M6 14 hour load: a losing race makes the cycle uncomfortable, whether or not the recall is paid

        var words = LosingRace.Words(in input, in answer);
        var (payable, paid) = UrgentRecallRead(session);
        if (!payable)
        {
            if (!string.Equals(_fightRaceClosed, paid, StringComparison.Ordinal))
            {
                _fightRaceClosed = paid;
                _log.Warn(LogCategory, $"retreat: fight: the race is lost ({words}), but Lifestone Recall is not payable ({paid}): the fight's own order stands (M6 14 the losing race)");
            }

            return false;
        }

        _fightRaceClosed = string.Empty;
        why = _fightRaceWhy = $"{words}; Lifestone Recall is paid ({paid})";
        return true;
    }

    /// <summary>The way out applies on the ladder's own job only: the den's and every other run keep their order (their fights never retreat for mana, and their dance is sized for level 80).</summary>
    private bool WayOutOn() => _options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job && _rung is not null;

    /// <summary>
    /// M6 7 BE: asks <see cref="ManaWayOut.Decide"/> in the fight's stop decision, each frame, and gives the fight the line it refuses spends by. The book, the tie and the components are read each <see cref="WayOutReadSeconds"/>; the mana every frame. Only the rule's change of mind is logged.
    /// </summary>
    private bool ManaRetreatDue(IGameSessionView session, IStoppableFight fight, string where = "fight")
    {
        var real = fight as FightScenario;
        if (!WayOutOn() || _clock < _wayOutAsideUntil)
        {
            real?.ArmWayOut(null);
            return false;
        }

        if (_clock >= _wayOutReadAt)
        {
            _wayOutReadAt = _clock + WayOutReadSeconds;
            _wayOutLine = WayOutLineNow(session);
        }

        real?.ArmWayOut(_wayOutLine);
        if (_wayOutLine is not { } line || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max))
        {
            return false;
        }

        var attackers = real?.AttackerOn(session, _clock) ?? (_hostiles is { } watch && _clock - watch.LastHitAt < HostileWatch.AttackQuietSeconds);
        var refused = real is { WayOutRefused: true };
        if (!attackers || (mana > line.Line && !refused))
        {
            _wayOutDecided = string.Empty;
            return false; // the frames that decide nothing read no more than this
        }

        var losing = session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && ManaWayOut.IsLosing(health, _hostiles?.IncomingPerSecond(_clock) ?? 0); // M6 8: the third death
        var input = new WayOutInput(true, mana, line, attackers, real?.WayOutKillPending(session, _clock) ?? false, real?.WayOutPress(session, _clock) ?? false, refused, real?.WayOutPotion(session) ?? false, losing);
        var decided = ManaWayOut.Decide(in input);
        if (!string.Equals(_wayOutDecided, decided.Why, StringComparison.Ordinal))
        {
            _wayOutDecided = decided.Why;
            _log.Info(LogCategory, ManaWayOut.DecideLine(in input, in decided));
        }

        if (!decided.Yes)
        {
            return false;
        }

        _retreatForMana = true;
        _retreatWhy = string.Create(CultureInfo.InvariantCulture, $"{decided.Why} at mana {mana}/{max}, {ManaWayOut.Named(in line)} ({where})");
        return true;
    }

    /// <summary>The line now, or null when Lifestone Recall is not ready (each reason named once, as the urgent recall names it): under its level she keeps the old order.</summary>
    private WayOutLine? WayOutLineNow(IGameSessionView session)
    {
        string? off = null;
        if (!RecallNeeds(session, out var cost, out var shortOf))
        {
            off = "the spell table does not hold it";
        }
        else
        {
            var (ready, why) = UrgentRecallPayable(session.State.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell), _context.Facts.LifestoneRecallUnavailable, _context.Facts.LifestoneTied, shortOf, manaRead: true, cost, cost); // the mana is the rule's, every frame
            off = ready ? null : why;
        }

        if (off is not null)
        {
            NoteWayOutArmed(string.Create(CultureInfo.InvariantCulture, $"way out: off: Lifestone Recall: {off}: the fight keeps its old order"));
            return null;
        }

        if (!_context.Spells.TryGetValue(FightScenario.EmergencySpellId, out var drain))
        {
            NoteWayOutArmed("way out: off: TODO(m6-way-out-drain-row): the spell table holds no Drain Health Other I, so the line cannot be worked out");
            return null;
        }

        var line = ManaWayOut.LineOf(cost, GearCasts.ManaCeiling(drain, 0));
        NoteWayOutArmed(string.Create(CultureInfo.InvariantCulture, $"way out: armed: {ManaWayOut.Named(in line)}: with attackers on her she leaves at it, and no bolt or buff takes the mana under it"));
        return line;
    }

    private void NoteWayOutArmed(string line)
    {
        if (!string.Equals(_wayOutArmed, line, StringComparison.Ordinal))
        {
            _wayOutArmed = line;
            _log.Info(LogCategory, line);
        }
    }

    /// <summary>
    /// M6 7 BE: the way out's retreat, check before: the fight has ended, so the book, the tie, the components and the mana are read again (<see cref="UrgentRecallRead"/>). Unpaid (a last cast took it), no recall is cast: the fight goes back on by name and the rule stands aside
    /// <see cref="WayOutAsideSeconds"/>. Paid: <see cref="TakeRetreat"/>. The check after is the station's: <c>RecallHome</c> reads the arrival, and a recall that fizzles out or is refused ends <c>failed:travel:recall-home:&lt;token&gt;</c> (<see cref="NoteManaRecallFailed"/>).
    /// </summary>
    private ScenarioStatus TakeManaRetreat(IGameSessionView session)
    {
        _retreatForMana = false;
        var (payable, why) = UrgentRecallRead(session);
        if (!payable)
        {
            _wayOutAsideUntil = _clock + WayOutAsideSeconds;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"retreat: mana: check before the recall: not payable ({why}): no recall is cast; she fights on and the rule is read again in {WayOutAsideSeconds:0} s"));
            return BackToTheFight("retreat: mana: the recall is not payable");
        }

        _log.Info(LogCategory, $"retreat: mana: check before the recall: {why}");
        return TakeRetreat(_retreatWhy, forMana: true);
    }

    /// <summary>M6 7 BE: the check after, when the way home of a retreat for mana failed: what happened and what comes next, by name.</summary>
    /// <param name="why">The station's failure.</param>
    private void NoteManaRecallFailed(string? why)
    {
        if (_manaRetreating && _retreating)
        {
            _manaRetreating = false;
            _log.Warn(LogCategory, $"retreat: mana: the way home did not land ({why ?? "failed"}): the stop's name is failed:travel:<station>:<token>; any failure but a refusal for components takes the station failure's recovery (the recall home again, then the known state), and stops by name when the recoveries are used");
        }
    }

    /// <summary>
    /// Takes the retreat: counted (<c>retreats=</c>), the rule reset, then the way home (<see cref="HomeSpec"/>) with no keeper's pass first; the second retreat from this ground in this cycle stops <c>retreat:again:&lt;ground&gt;</c>. M6 7 BE: a retreat for mana is counted the same, and never
    /// toward that stop (<paramref name="forMana"/>): the fight went as it should and the run goes on.
    /// </summary>
    private ScenarioStatus TakeRetreat(string why, bool forMana = false)
    {
        _retreat.Reset();
        _retreatUnder = false;
        _tally.Retreats++;
        if (forMana)
        {
            _retreating = true;
            _manaRetreating = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"retreat: taken for mana from {_ground.Key} in the {StateName(_state)} state, cycle {_cycle} ({why}): the way home, the keeper's pass at home, then out again; a retreat for mana does not count toward {RetreatAgainPrefix}{_ground.Key}"));
            return BeginRun(AutopilotState.Travel, RecallHomeSpec, "retreat: " + why); // by name, as the urgent recall: the way-home rule walks home from an outdoor ground, and she leaves because she must not stand or walk among them
        }

        if (_retreatCycle != _cycle || !string.Equals(_retreatGround, _ground.Key, StringComparison.Ordinal))
        {
            (_retreatCycle, _retreatGround, _retreatsHere) = (_cycle, _ground.Key, 0);
        }

        _retreatsHere++;
        if (_retreatsHere >= RetreatsPerGroundCycle)
        {
            _log.Error(LogCategory, string.Create(CultureInfo.InvariantCulture, $"retreat: the retreat {_retreatsHere} from {_ground.Key} in cycle {_cycle} ({why}): the run stops; the ladder's step back to the rung below is not built (TODO(m6-retreat-rung-drop), item 10)"));
            return Stop(RetreatAgainPrefix + _ground.Key, ok: false, string.Create(CultureInfo.InvariantCulture, $"the second retreat from {_ground.Key} in cycle {_cycle} ({why}); TODO(m6-retreat-rung-drop)"));
        }

        _retreating = true;
        _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"retreat: taken from {_ground.Key} in the {StateName(_state)} state, cycle {_cycle} (retreat {_retreatsHere} of {RetreatsPerGroundCycle - 1} this cycle on this ground; {why}): the way home, the keeper's pass at home, then out again"));
        return BeginRun(AutopilotState.Travel, HomeSpec(), "retreat: " + why);
    }

    /// <summary>The way home of a retreat ended: the keeper's pass at home (the heal in place's needs), then the known state.</summary>
    private ScenarioStatus RetreatHome(string how)
    {
        _retreating = false;
        _retreatHeal = true;
        _upkeepDetail = "the retreat's pass at home";
        _log.Info(LogCategory, $"retreat: home ({how}): the keeper's pass, then the known state goes back out");
        if (_manaRetreating)
        {
            _manaRetreating = false;
            _log.Info(LogCategory, $"retreat: mana: check after: home ({how}); the retreats of this run: {Retreats}");
        }

        return BeginHeal($"retreat: home ({how}): the keeper's pass");
    }

    /// <summary>
    /// The heal in place ended its keeper's pass with the health still under its need (the keeper found nothing more to pay): on the ground and under the retreat line, the rule is asked with nothing payable for at most
    /// <see cref="RetreatRule.RetreatHoldSeconds"/> plus a second (the phase waits meanwhile); its retreat goes home. Null: the heal's own named failure (<c>failed:upkeep:precondition:health</c>, through the station failure's recovery).
    /// </summary>
    private ScenarioStatus? HealRetreat(IGameSessionView session)
    {
        if (_retreatHeal || _state != AutopilotState.Upkeep || !InGround())
        {
            return null;
        }

        if (RetreatDue(session, "upkeep heal", nothingPaid: true))
        {
            _healRetreatAskedAt = double.NaN;
            NoteWhy(RetreatWhy);
            return TakeRetreat(_retreatWhy + ": the keeper's pass in place ended under the line");
        }

        // M6 6 FXM2 (6-M8): the window runs from this heal's first ask, not from the Peace phase's start (the rule's own hold of RetreatHoldSeconds starts at its first ask, often this call)
        if (!_retreatUnder)
        {
            _healRetreatAskedAt = double.NaN;
            return null;
        }

        if (double.IsNaN(_healRetreatAskedAt))
        {
            _healRetreatAskedAt = _clock;
        }

        if (_clock - _healRetreatAskedAt <= RetreatRule.RetreatHoldSeconds + 1)
        {
            return ScenarioStatus.Running;
        }

        _healRetreatAskedAt = double.NaN;
        return null;
    }

    private double _healRetreatAskedAt = double.NaN; // the clock of this heal's first nothing-paid ask under the line (NaN: none)

    /// <summary>
    /// M6 14 entry recharge (live 2026-11-13 19:29:08 UTC: the climber travelling OUT to the Tusker Barracks, health 103 of 180 under the guard's line with no heal payable, was told "the state is on its way home or is the death's"): why the
    /// guard's nothing-paid answer takes no retreat from a state that may not leave by it, named by the state and, travelling, by its direction. The way home (the rung change home, the recall home, the urgent recall, the recall
    /// before a stop) is on its way home. The way out is not: there the rung change's legs keep the ground's retreat line in their own fight-backs (the walks and the hops' walks, <c>TravelLegSafety</c>; the entry's recharge and the
    /// top-up before a hostile drop, <c>TravelLegRest</c>), whose retreat leaves by Lifestone Recall (live, 19:29:20: the run leg's own retreat at 88 of 180), and the survival layer holds a bar under its line with no hostile near; the guard
    /// lays no second retreat over theirs.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="eligible">The state is one the guard's retreat may leave (the known state, the upkeep, the buff), off the ground.</param>
    /// <param name="travel">The running travel station's name (<see cref="StationSpec.Name"/>), or null when the state is not the Travel state.</param>
    /// <param name="homeward">The travel is a way home.</param>
    internal static string GuardNoRetreatWhy(AutopilotState state, bool eligible, string? travel, bool homeward) => state switch
    {
        _ when eligible => "not on the ground",
        AutopilotState.Travel when homeward => "she is on her way home",
        AutopilotState.Travel when string.Equals(travel, RungOutSpec.Name, StringComparison.Ordinal) =>
            "she travels out to the ground, not home: the rung change's legs keep the ground's retreat line in their own fight-backs (the walks, the hops' walks, the entry's recharge, the top-up before a hostile drop) and the survival layer holds a bar under its line",
        AutopilotState.Travel => $"she travels by {travel ?? "a station"}: its own legs answer",
        AutopilotState.Death => "the state is the death's",
        _ => $"the {StateName(state)} state keeps its own way out",
    };

    /// <summary>
    /// <c>f23-guard-nothing-paid</c>'s answer: the guard found no Heal Self and no drain payable under its line. On the ground in a state a retreat may leave (the known state, the upkeep, the buff), the rule is asked with nothing
    /// payable and its retreat is taken; anywhere else (home, a trip, the way home, the death station) the state goes on, said once a state.
    /// </summary>
    private ScenarioStatus? GuardRetreat(IGameSessionView session)
    {
        var race = _guard is { RaceLost: true }; // M6 14 the losing race: on the ground the retreat rule takes the lost race at once (RetreatRule's losing-race arm)
        if ((_guard is not { NothingPaid: true } && !race) || _retreating)
        {
            return null;
        }

        if (_state == AutopilotState.Trip && SurvivalOn())
        {
            return null; // M6 8 K: on the ladder a Trip (its errands too) is the survival layer's leave (SurvivalLeave), read next in the same frame
        }

        var state = StateName(_state);
        var eligible = _state is AutopilotState.KnownState or AutopilotState.Upkeep or AutopilotState.Buff;
        if (!eligible || Classify(session.State, _ground) != AutopilotPlace.Ground)
        {
            if (_state == AutopilotState.Travel && (ReferenceEquals(_spec, RungHomeSpec) || ReferenceEquals(_spec, RecallHomeSpec)) && Classify(session.State, _ground) != AutopilotPlace.Home
                && LastResortNoWayHome(session, _spec!.Name, "on the way home the guard finds her under its line with no Heal Self and no drain payable") is { } leave)
            {
                return leave; // M6 8: the way home she is dying on is no way home
            }

            var homeward = ReferenceEquals(_spec, RungHomeSpec) || ReferenceEquals(_spec, RecallHomeSpec) || _urgentRecall || _finalStop is not null;
            var why = GuardNoRetreatWhy(_state, eligible, _state == AutopilotState.Travel ? _spec?.Name : null, homeward);
            if (!string.Equals(_guardNoRetreatLogged, state + why, StringComparison.Ordinal))
            {
                _guardNoRetreatLogged = state + why;
                _log.Info(LogCategory, $"retreat: guard: {state}: nothing payable under the line, but no retreat from here ({why}): the state goes on");
            }

            return null;
        }

        if (!RetreatDue(session, "guard: " + state, nothingPaid: !race, raceLost: race))
        {
            return null;
        }

        if (_run is not null)
        {
            var name = _spec?.Name ?? "?";
            DisposeRun();
            _phase = RunPhase.Done;
            _context.Observer.StationEnded(name, false, _clock);
        }

        NoteWhy(RetreatWhy);
        return TakeRetreat(_retreatWhy);
    }
}
