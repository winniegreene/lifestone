using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 F23 (soak round 2's death: 50 s in the upkeep with health 110, 95, 72, 35, 24, 1 of 195 and the mana at 313-334, and nothing cast). The vitals guard runs in every autopilot state but the fight (which has its own emergency, F16/F19/F21)
/// while the mage is in the world and alive: under the emergency line (<see cref="FightEmergency"/>, the fight's own line and options, <c>--fight-emergency-floor/-safe/-margin</c>, the line raised by the incoming rate the
/// <see cref="HostileWatch"/> measures) it runs the fight's heal pick (<see cref="HealPick"/> over <see cref="FightHealPick"/>'s candidates: Heal Self against each drain level on the fullest target whose REAL health the server gave, never an
/// unknown read as full, D92) at once, and again after each cast, until the safe line. It preempts the state's own casts: while it holds, the autopilot does not step the state's station, so the station starts no cast; the busy rules hold, as
/// the guard's own cast waits for the Action API's lanes to be free (an action of the station in flight ends first). Every decision is one line, <c>guard: &lt;state&gt;: ...</c>.
/// <para>M6 14 the losing race (live 2026-11-15 04:13:09 to 04:13:27 UTC, <c>cache/m6/14/live/ladder25</c>: on the Tusker Holding's approach the guard held the travel state under its line and cast Drain Health Other I eight times in a row,
/// each landing 8 to 12 health, while six attackers took her from 121 to 0; the travel leg's own retreat never ran because the guard held the state): at each pick the guard reads the race (<see cref="LosingRace"/>): the heal she can
/// sustain, measured where it can be (<see cref="MeasuredHeals"/>: the heals ACE gave, the drains' ratio to the formula, the share of casts that landed), against the incoming rate the <see cref="HostileWatch"/> measures. Losing, it heals
/// only while one more heal still leaves her the way out (<see cref="RecallWayOut"/>: Lifestone Recall's cast and ACE's recall delay, at the incoming rate, after the attack's largest hit); past that it says so with its numbers, lets
/// go of the state and asks the leave (<see cref="LeaveRoad"/>: the survival layer's, or the retreat rule's, by Lifestone Recall while the recall is paid), and starts no heal while the recall is cast (<see cref="HoldOff"/>). With no
/// leave from where she is, it heals on and says so. The lead, 2026-11-15 (live ladder27 06:04:07 UTC: recalled at 166 of 206 and rising, one wasp of two left, on 15.7 heal against 16.2 incoming): the race reads both sides (<see cref="LosingRaceInput"/>):
/// her health's measured trend (<see cref="HealthTrend"/>: rising or level is not losing), the incoming of the attackers still alive (<see cref="HostileWatch.LivingShare"/>), and the kill side: the attackers on her with their known
/// remaining health against her measured damage a second (<see cref="KillRate"/>). Losing the heal race but killing first, it lets the fight-back's bolts go on (<see cref="FightOnSeconds"/>); lost only when both sides fail.</para>
/// <para>M6 14 the losing race, the target's health: each drain target is read from the one picture of its health (<see cref="KnownTargetHealth"/>: its facts' maximum health, the server's last report as the base it arrived at, less
/// her damage lines and the takes of her drains at it, the takes worked back from ACE's gain lines), its distance from the body the screen draws (<see cref="IGameSessionView.Live"/>); the candidates are the ground's targets, the
/// fight-back's target and every creature heard attacking her, the fullest by what a drain can take from it. M6 16 Q5: the health number itself is the session's one picture's (<see cref="PictureHealth"/>,
/// <c>AutopilotVitalsGuard.HealthPicture.cs</c>); the trackings say who is offered and carry the facts.</para>
/// </summary>
internal sealed partial class AutopilotVitalsGuard
{
    private const uint CreatureItemType = 0x10;

    /// <summary>M6 14 the losing race: the seconds the guard starts no heal after it handed the race to the leave: the way out's (<see cref="RecallWayOut.Seconds"/>) and a retry's.</summary>
    public const double HoldOffSeconds = RecallWayOut.Seconds + RetrySeconds;

    private readonly Dictionary<uint, KnownTargetHealth> _known = [];
    private readonly List<uint> _gone = [];
    private readonly HashSet<uint> _unknownNoted = [];
    private readonly double[] _formulas = new double[FightDrain.MaxLevels];
    private long _chatScanned = -1;
    private uint _drainTarget;
    private string _drainSpell = string.Empty;
    private string _drainName = string.Empty;
    private TransferRow _drainRow;
    private double _drainFormula;
    private double _holdOffUntil = double.NegativeInfinity;
    private bool _holdOffLogged;
    private string _raceLogged = string.Empty;
    private readonly HealthTrend _trend = new();
    private readonly KillRate _kills;
    private readonly Func<string, bool> _drainOwns;
    private double _fightOnUntil = double.NegativeInfinity;
    private string _raceVerdictLogged = string.Empty;
    private double _raceLoggedAt = double.NegativeInfinity;

    /// <summary>M6 14 the losing race: the seconds the guard lets a fight she wins by killing go on before it reads the race again (Lifestone's own: one bolt's cast, about 3 s live, is longer; a second keeps the read close).</summary>
    public const double FightOnSeconds = 1.0;

    /// <summary>M6 14 the losing race: the least seconds between two race lines of the same verdict (Lifestone's own: a fight the guard lets go on reads the race each second).</summary>
    public const double RaceLogSeconds = 3.0;

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly HuntingGround _ground;
    private IReadOnlyList<GroundTarget>? _targets; // M6 0 a5: the fight's targets (FightTargets.Of): an outdoor ground's outdoor.creatures within its band; built once
    private readonly ILogSink _log;
    private readonly HostileWatch _watch;
    private readonly FightEmergency _emergency;
    private readonly FightDrain _drain;
    private readonly FightRefill _payable;
    private readonly MeasuredHeals _heals = new();
    private readonly double _castChance;
    private readonly string? _accountName;
    private readonly double _damageWeight;
    private readonly HealCandidate[] _candidates = new HealCandidate[FightDrain.MaxLevels];
    private readonly HealCandidate[] _nowCandidates = new HealCandidate[FightDrain.MaxLevels]; // M6 14 tactics: the candidates at their measured landing share, for the pick
    private readonly int[] _levelIndex = new int[FightDrain.MaxLevels];
    private string _state = string.Empty;
    private bool _waitLogged;
    private bool _nothingLogged;
    private bool _noCasterLogged;
    private bool _settleLogged;
    private bool _reserveLogged;
    private double _castAt;
    private double _retryAt = double.NegativeInfinity;
    private double _modeAskedAt = double.NegativeInfinity;
    private string _refusalLogged = string.Empty;
    private double _asideUntil = double.NegativeInfinity;
    private uint _aMana;
    private uint _aTarget;
    private uint _aTargetHealth;
    private float _aDistance;
    private string _aName = string.Empty;
    private int _aCount;
    private bool _aCanSelf;
    private HealCandidate _aSelf;
    private double _aSelfRate;
    private int _aBest;
    private double _aBestRate;
    private double _aChosenScore;

    /// <summary>The least seconds between two attempts after a refused cast or an unanswered mode change (M5 7 F24: the corpse trip's guard retried dozens of times in one frame).</summary>
    public const double RetrySeconds = 1.0;

    /// <summary>The guard of an autopilot run.</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="ground">The ground (its targets' max health and magic defence for the drains).</param>
    /// <param name="options">The autopilot's options (the fight's emergency line and the heal pick's damage weight, as the fight reads them).</param>
    /// <param name="castChance">The least cast chance of the Heal Self level (the buff options').</param>
    /// <param name="watch">The hostiles and the incoming rate.</param>
    /// <param name="log">Where it logs (the autopilot's category).</param>
    public AutopilotVitalsGuard(IReadOnlyDictionary<uint, SpellInfo> spells, HuntingGround ground, AutopilotOptions options, double castChance, HostileWatch watch, ILogSink log)
    {
        (_spells, _ground, _watch, _log, _castChance) = (spells, ground, watch, log, castChance);
        _emergency = new FightEmergency(options.EmergencyFloorPercent, options.EmergencySafePercent, options.EmergencyMarginPercent);
        _damageWeight = options.HealDamageWeight;
        _drain = new FightDrain(spells, log, 1, 100);
        _payable = new FightRefill(spells, log, 0, options.Buff?.AccountName);
        _kills = new KillRate(spells);
        _drainOwns = DrainOwns;
        _accountName = options.Buff?.AccountName; // M6 1 h1: the autopilot sets it from its journey context
    }

    /// <summary>True while the guard holds the state (under the line, waiting for the lanes or casting).</summary>
    public bool Holding { get; private set; }

    /// <summary>M6 8 K: true while the guard's own heal or drain is being cast: the survival layer does not leave under it.</summary>
    public bool CastRunning => _drain.Cast is { IsRunning: true };

    /// <summary>The guard's heals and drains that landed.</summary>
    public int Landed { get; private set; }

    /// <summary>The guard's casts that ended in ACE's YoureTooBusy (0x001D) and wait a second for the next try (M5 7 F30).</summary>
    public int Busy { get; private set; }

    /// <summary>The guard's casts started.</summary>
    public int Casts { get; private set; }

    /// <summary>
    /// M6 6 F2 (closes <c>f23-guard-nothing-paid</c>): true while the guard's last pick under its line found no Heal Self and no drain payable. The autopilot answers it with the retreat rule (<c>AutopilotScenario.GuardRetreat</c>):
    /// on the ground the retreat takes the mage home; elsewhere the state goes on.
    /// </summary>
    public bool NothingPaid { get; private set; }

    /// <summary>
    /// M6 14 retreat attackers (rule 4): the mana a travel leg's fight-back holds back for its way out (<see cref="Scenarios.Journey.JourneyFacts.FightBackReserve"/>), read at each heal pick, or null: none. A heal that would take the mana
    /// under Lifestone Recall's cost while it stands is refused (<see cref="ManaWayOut.Spend"/>, the heal's arm) and counts as nothing paid.
    /// </summary>
    public Func<WayOutLine?>? Reserve { get; set; }

    /// <summary>
    /// M6 14 walk drains (the owner, 2026-11-15: the runs drain as the grounds do; live ladder23 02:51:43 UTC: "no drain offered ... no target of known health in reach" with Reedshark Ravagers on her): a travel leg's fight-back target
    /// with its health as data (<see cref="Scenarios.Journey.JourneyFacts.FightBackTarget"/>), or null. When no target of the ground is in reach, the heal pick offers Drain Health Other on it, its take from its known health and its kind's
    /// drain resistance (<see cref="DrainResist.Of"/>: a kind that lets no drain through, an Armored Tusker, is never offered); the way out's reserve still holds (<see cref="Reserve"/>).
    /// </summary>
    public Func<KnownTargetHealth?>? WalkTarget { get; set; }

    /// <summary>
    /// M6 14 the losing race: true from the pick where the guard found the race lost and a leave open (<see cref="LeaveRoad"/>) until the autopilot takes the leave (<see cref="RaceTaken"/>), the hold off ends or the health is back at the
    /// safe line. The survival layer reads it (<c>SurvivalInput.GuardRaceLost</c>), and the retreat rule from a state on the ground.
    /// </summary>
    public bool RaceLost { get; private set; }

    /// <summary>M6 14 the losing race: the race in words with its numbers, as the guard last read it lost (empty before).</summary>
    public string RaceWhy { get; private set; } = string.Empty;

    /// <summary>
    /// M6 14 the losing race: the leave the state she is in can take now, asked when the race is lost: open with the road in words (the survival layer's leave, or the retreat rule's, by Lifestone Recall while the recall is paid), or
    /// closed with why not (the guard then heals on). Null: no leave is wired (a run off the ladder's job), the guard heals on.
    /// </summary>
    public Func<(bool Open, string Why)>? LeaveRoad { get; set; }

    /// <summary>M6 14 the losing race: the autopilot took the leave the race asked for: the flag is down, and no heal starts before the hold off ends (the recall's cast and ACE's delay).</summary>
    /// <param name="clock">The session clock.</param>
    public void RaceTaken(double clock)
    {
        RaceLost = false;
        _holdOffUntil = Math.Max(_holdOffUntil, clock + HoldOffSeconds);
    }

    /// <summary>
    /// M6 14 the losing race: no heal starts for <see cref="HoldOffSeconds"/>: a leave by Lifestone Recall is under way (the fight's retreat for the race: under the guard's line the next state's first frames would otherwise heal before the
    /// recall is cast). Logged once a hold off.
    /// </summary>
    /// <param name="clock">The session clock.</param>
    /// <param name="why">Why, for the line.</param>
    public void HoldOff(double clock, string why)
    {
        _holdOffUntil = Math.Max(_holdOffUntil, clock + HoldOffSeconds);
        _holdOffLogged = true;
        _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: holds off for {HoldOffSeconds:0.##} s: {why}: no heal starts while Lifestone Recall is cast and ACE's recall delay runs (M6 14 the losing race)"));
    }

    /// <summary>True while a hold off stands (<see cref="HoldOff"/>, <see cref="RaceTaken"/>).</summary>
    /// <param name="clock">The session clock.</param>
    public bool HoldingOff(double clock) => clock < _holdOffUntil;

    /// <summary>M6 6 F2: true while the guard's emergency is set aside (<see cref="FightEmergency.MaxFailsInARow"/> casts in a row that did not land, for <see cref="FightEmergency.AsideSeconds"/>): the retreat rule's <c>aside=</c>.</summary>
    /// <param name="clock">The session clock.</param>
    public bool EmergencyAside(double clock) => clock < _asideUntil;

    /// <summary>
    /// M6 6 F2: the retreat rule's heal read, with no cast: the heal pick the guard would make now (<see cref="HealPick.Choose"/> over Heal Self and the drains, the same candidates as its own pick). False when the mana, the health or
    /// Life Magic is unread (an unread is never "nothing payable").
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="healPerSecond">The chosen heal's health a second (0 when nothing is payable).</param>
    /// <param name="payable">False when nothing is payable.</param>
    public bool AssessHeal(IGameSessionView session, out double healPerSecond, out bool payable)
    {
        ArgumentNullException.ThrowIfNull(session);
        (healPerSecond, payable) = (0, true);
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || max == 0)
        {
            return false;
        }

        ReadChat(session.State.Chat);
        Sample(session, session.Clock.Now);
        var choice = Assess(session, session.Clock.Now, health, max);
        if (choice == NotRead)
        {
            return false;
        }

        payable = choice != HealPick.Nothing;
        healPerSecond = _aChosenScore; // the chosen heal's score as the retreat rule has always read it (M6 14 tactics changed the pick, not this read)
        return true;
    }

    /// <summary>
    /// M6 14 the losing race: the race now, with no cast (the retreat rule's read in the fight and on the ground): the heal pick the guard would make (<see cref="Assess"/>), the most health a second its candidates sustain, the incoming
    /// rate and the attack's largest hit, against the way out. False when the health, the mana or Life Magic is unread.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="input">What the race read.</param>
    /// <param name="answer">The race's answer.</param>
    public bool Race(IGameSessionView session, double clock, out LosingRaceInput input, out LosingRaceAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(session);
        (input, answer) = (default, default);
        if (!session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || max == 0 || health == 0)
        {
            return false;
        }

        ReadChat(session.State.Chat);
        Sample(session, clock);
        var choice = Assess(session, clock, health, max);
        if (choice == NotRead)
        {
            return false;
        }

        input = RaceInput(session, clock, health, choice);
        answer = LosingRace.Decide(in input);
        return true;
    }

    /// <summary>
    /// The race's inputs after an <see cref="Assess"/>, both sides. Her side: the trend of her health (<see cref="HealthTrend"/>); the heal she can sustain, the best of Heal Self and the drain candidates by what they give her a second
    /// (<see cref="HealPick.Sustained(in HealCandidate, double)"/>: the chance it lands, the resist roll against the target's Magic Defense in a drain's and the measured share of its casts that landed on that kind taking over, <see cref="MeasuredHeals.LandShare(string, string, double)"/>;
    /// its heal; its cast seconds, Heal Self's measured; and its mana's seconds at the dance's rate); the chosen heal's seconds (the way out starts after it); the incoming of the living attackers (<see cref="IncomingNow"/>) and the larger
    /// of the ten seconds' largest hit and the attack's (<see cref="HostileWatch.LargestOfAttack"/>). The kill side: the attackers on her and the seconds her bolts need to kill them (<see cref="AttackersOnHer"/>).
    /// </summary>
    private LosingRaceInput RaceInput(IGameSessionView session, double clock, uint health, int choice)
    {
        var read = _watch.Read(clock);
        var biggest = Math.Max(read.BiggestHit, _watch.LargestOfAttack(clock));
        var selfSeconds = _heals.Seconds(_aSelf.Name, _aSelf.CastSeconds); // Heal Self's measured seconds where it has landed, else ACE's for its level (MeasuredHeals.SelfSecondsSeed), as the pick reads them
        _aBestSustained = 0.0;
        if (_aCanSelf)
        {
            _aBestSustained = HealPick.Sustained(_aSelf with { Chance = _heals.LandShare(_aSelf.Name, null, _aSelf.Chance), CastSeconds = selfSeconds }, FightRates.SeedSecondsPerMana);
        }

        for (var i = 0; i < _aCount; i++)
        {
            var rate = HealPick.Sustained(_candidates[i] with { Chance = _heals.LandShare(_candidates[i].Name, _aName, _candidates[i].Chance) }, FightRates.SeedSecondsPerMana);
            if (rate > _aBestSustained)
            {
                _aBestSustained = rate;
            }
        }

        var heal = choice == HealPick.HealSelf ? HealPick.Sustained(_aSelf with { Chance = _heals.LandShare(_aSelf.Name, null, _aSelf.Chance), CastSeconds = selfSeconds }, FightRates.SeedSecondsPerMana)
            : choice >= 0 ? HealPick.Sustained(_candidates[choice] with { Chance = _heals.LandShare(_candidates[choice].Name, _aName, _candidates[choice].Chance) }, FightRates.SeedSecondsPerMana) : 0;
        var next = choice == HealPick.HealSelf ? selfSeconds : choice >= 0 ? _candidates[choice].CastSeconds : 0;
        var incoming = IncomingNow(session, clock, out var why);
        var attackers = AttackersOnHer(session, clock, out var remaining, out var killSeconds, out var unknown, out var detail);
        ShadowAttackers(session, clock, attackers); // M6 16 E2b: the threat board is asked the same beside it (report-only)
        double? trend = _trend.TryRead(clock, out var slope, out _) ? slope : null;
        var killUnknown = attackers == 0 ? "no attacker read on her" : unknown;
        return new LosingRaceInput(health, incoming, biggest, Math.Max(heal, _aBestSustained), next, RecallWayOut.Seconds)
        {
            TrendPerSecond = trend,
            IncomingAllPerSecond = _watch.AverageOfAll(clock),
            IncomingWhy = why,
            Attackers = attackers,
            AttackersHealth = killUnknown is null ? remaining : null,
            KillPerSecond = killUnknown is null && killSeconds > 0 && !double.IsPositiveInfinity(killSeconds) ? remaining / killSeconds : null,
            KillUnknown = killUnknown ?? (double.IsPositiveInfinity(killSeconds) ? "her bolts land on none of them" : null),
            KillDetail = detail,
        };
    }

    private double _aBestSustained;
    private readonly LiveFightLine _line = new();
    private readonly System.Text.StringBuilder _detail = new();

    /// <summary>
    /// M6 14 attack timing (the owner, 2026-11-15: every fight number computed, never guessed): the creatures' attack timings each attacker's derived rate reads (<see cref="CreatureAttackTimes.For"/> of the session's dats: ACE's attack loop over each one's motion and
    /// combat tables); <see cref="CreatureAttackTimes.None"/> (the default, no dats) gives each the named 2.75 s fallback.
    /// </summary>
    public CreatureAttackTimes Times { get => _line.Times; init => _line.Times = value; }

    /// <summary>
    /// M6 14 the losing race (the lead, 2026-11-15, live 06:17:03 to 06:17:07 UTC: an Infected Assailer's first hits came while the incoming read only a Reaper's, the ten seconds' mean diluting it): the incoming of the attackers still
    /// alive. Each kind heard within <see cref="HostileWatch.AttackQuietSeconds"/> is read by its own hits, the dead's share out (<see cref="HostileWatch.KindRead"/>), over the seconds since its first attack in this attack (never diluted
    /// over a window it was not in); a kind whose attacks began under <see cref="NewKindSeconds"/> ago, or with fewer than <see cref="NewKindHits"/> attacks heard, is at least its derived rate (the ones of it on her, <see cref="OnHer"/>, at least one; its mean hit with criticals on
    /// her armour and live resistance against its damage type, at its own hits a second and with its priced spells (M6 14 attack timing: <see cref="Times"/>, ACE's attack loop over the dats): <see cref="CreatureFacts"/>, the world
    /// database's attacks).
    /// <para>M6 14 attack timing (the owner, 2026-11-15: every fight number computed, never guessed; live 06:17:07.285 UTC: the Infected Assailers' 4.2 s and five hits had ended their "new" read, so the guard took their measured 9.3 a second, the first hits of
    /// Assailers arriving one by one, and drained on at 122 while 3 to 4 of them and two Reapers took her to 0): a kind whose hits are measured over less than the hits' window (<see cref="RecentHits.AverageSeconds"/>) is still at least the
    /// derived rate of the ones of it on her (<see cref="OnHer"/>: alive, within <see cref="HostileReach.DefaultMetres"/> where the screen draws them, the kill side's picture); the measure lifts it when it reads more.</para>
    /// <para>M6 14 the losing race (live ladder45 2026-11-16 23:07:43 and 23:15:02 UTC, the Crater Pathway's Zefirs and Dark Wisps: recalled at 129 and 116 of 218 on 16.7 and 18.8 a second read against about 2.6 and 9 measured): once a
    /// kind's attacks span the whole window, its measured hits take over from the derived rate, which is not added to them: every hit of its creatures on her in that window is in the measure, its misses, evades and resisted spells
    /// heard as attacks that took nothing (<see cref="HostileWatch.AttacksOf"/>). The derived rate only fills the part of the window a kind was not yet heard in. So with every kind heard over the whole window the incoming is at
    /// most the window's plain mean of the hits, the dead's share out (<see cref="HostileWatch.AverageOfLiving"/>): the living's incoming never reads over the measured total. That mean, not the recent hits' burst read (the fight
    /// emergency's next-seconds read: 50 in 2 s from three kinds read 16.7 a second), is the floor for hits no kind is named for: the race compares rates sustained over seconds, and a burst's largest hit is in the way out's need.</para>
    /// <para>M6 14 the losing race (live ladder52 2026-11-17 04:54:27.874 UTC, the Crater Caves: a Nubilous Golem's first hit, 42, read as 42 a second, its 42 over a span of 0 s, against its derived 9.9 and the window's mean of 4.2, and she
    /// recalled out of the mote hunt at 137 of 199 with her health level): a kind heard under the window is never its damage over its short span. Its hits count over the window and its derived rate prices the part of the window it was
    /// not yet heard in (the walk's line, <see cref="FightLines.DeriveAttackers"/>, the same rule), never under the derived rate of the ones of it on her (the 06:17:07 protection above): the one 42 adds 4.2 a second to the golems'
    /// 9.9. A kind with no derived rate read is its hits over its span, the span never under <see cref="NewKindSeconds"/>.</para>
    /// <para>M6 14 the losing race (live ladder55 2026-11-17 07:51:27.657 UTC, the Crater Caves mote hunt: a new golem attack priced at the derived rate of the 5 Nubilous Golems alive, 8.6 a second, while 2 were on her, as the kill side
    /// counted; 12.5 against her heal's 8.6, and she recalled at 167 of 219): the derived rate, new or measuring, counts only the ones of the kind on her (<see cref="OnHer"/>: alive, not killed, within
    /// <see cref="HostileReach.DefaultMetres"/> where the screen draws them, the kill side's picture), at least one, never every one the object table holds alive.</para>
    /// </summary>
    private double IncomingNow(IGameSessionView session, double clock, out string why)
    {
        var state = session.State;
        var sum = 0.0;
        _detail.Clear();
        foreach (var name in _watch.HeardWithin(clock, HostileWatch.AttackQuietSeconds))
        {
            if (!_watch.KindRead(name, clock, out var damage, out var hits, out var since, out var living, out var everHeld))
            {
                continue;
            }

            if (everHeld && living == 0)
            {
                continue; // every one of the kind is dead: its hits are over
            }

            var attacks = _watch.AttacksOf(name, clock, out var span, out var heard) ? heard : hits;
            since = Math.Max(since, span);
            var window = RecentHits.AverageSeconds;
            var heardShare = Math.Clamp(since / window, 0, 1);
            var whole = KindIncoming.Whole(since, attacks, out var isNew); // its attacks span the hits' window: its measure is the whole of it
            var derived = 0.0;
            var derivedRead = false;
            AttackTiming? timing = null;
            var count = whole ? 0 : Math.Max(1, OnHer(session, name)); // the ones of it on her (the kill side's attackers), never every one alive: it was heard attacking, so at least one
            if (count > 0 && WcidOf(state, name) is { } wcid && CreatureFacts.All.Find(wcid) is { } fact)
            {
                timing = Times.Of(wcid, name);
                var factor = _line.FactorFor(state, fact.DamageTypes, clock, out _);
                _line.SpellFactorFor(state, 0); // her registry as now, for the spells' resistance
                derived = count * timing.PerSecondOn(FightLines.MeanHit(fact.Hitter), factor, _line.SpellResistance, DefenceRead.Of(state.Self.Qualities)); // M6 14 the derived incoming: ACE's evade and resist rolls on her defences
                derivedRead = true;
            }

            // M6 14 tactics: the kind's rate is KindIncoming's, the one read the fight's target switch also makes: its hits over the window, the derived rate for the part of it the kind was not yet heard in, at least the derived; no
            // derived rate read: its hits over its span, the span never under the new read's seconds
            var rate = KindIncoming.Rate(damage, since, attacks, derived, derivedRead, out var blend);

            sum += rate;
            var how = whole ? string.Create(CultureInfo.InvariantCulture, $", measured over the whole {window:0} s: its {attacks} attack(s) since {since:0.#} s")
                : derived > 0 ? string.Create(CultureInfo.InvariantCulture, $", {attacks} attack(s) over {since:0.#} s, derived {derived:0.#}/s {(isNew ? "while new" : "on her while measured under " + window.ToString("0", CultureInfo.InvariantCulture) + " s")} ({count} x {timing!.Short()}); its {damage:0} over the {window:0} s and the derived rate for the {(1 - heardShare) * window:0.#} s it was not heard give {blend:0.#}/s, at least the derived")
                : string.Create(CultureInfo.InvariantCulture, $", {attacks} attack(s) over {since:0.#} s, no derived rate read: its hits over {Math.Max(NewKindSeconds, since):0.#} s");
            _detail.Append(_detail.Length == 0 ? string.Empty : ", ").Append(string.Create(CultureInfo.InvariantCulture, $"'{name}' {rate:0.#}/s ({hits} hit(s) for {damage:0} over {Math.Min(RecentHits.AverageSeconds, since):0.#} s{how}{(everHeld ? $", {living} alive" : string.Empty)})"));
        }

        var mean = _watch.AverageOfLiving(clock);
        why = _detail.Length == 0 ? "no kind heard" : _detail.ToString();
        return Math.Max(sum, mean);
    }

    /// <summary>
    /// M6 14 attack timing: the living creatures named <paramref name="name"/> on her: within <see cref="HostileReach.DefaultMetres"/> of her where the screen draws them (the kill side's picture, <see cref="AttackersOnHer"/>); 0 when her
    /// position is not read. Allocation-free.
    /// </summary>
    private int OnHer(IGameSessionView session, string name)
    {
        var state = session.State;
        if (state.Self.Position is not { } at)
        {
            return 0;
        }

        var self = at.ToGlobal();
        var count = 0;
        var watch = _watch;
        foreach (var o in state.Objects.All)
        {
            if ((o.ItemType & CreatureItemType) != 0 && string.Equals(o.Name, name, StringComparison.Ordinal) && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord } && !watch.IsKilled(o.Guid)
                && session.Live.At(o) is { } p && p.ToGlobal().DistanceTo(self) <= HostileReach.DefaultMetres)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>M6 14 the losing race: a kind heard under this many seconds ago reads at least its derived rate (<see cref="KindIncoming.NewKindSeconds"/>).</summary>
    public const double NewKindSeconds = KindIncoming.NewKindSeconds;

    /// <summary>M6 14 the losing race: a kind with fewer hits measured than this reads at least its derived rate (<see cref="KindIncoming.NewKindHits"/>).</summary>
    public const int NewKindHits = KindIncoming.NewKindHits;

    /// <summary>The weenie class of a living creature named <paramref name="name"/> in the object table, or null.</summary>
    private static uint? WcidOf(GameState state, string name)
    {
        foreach (var o in state.Objects.All)
        {
            if ((o.ItemType & CreatureItemType) != 0 && string.Equals(o.Name, name, StringComparison.Ordinal) && o.Health is not <= 0f)
            {
                return o.Weenie.WeenieClassId;
            }
        }

        return null;
    }

    /// <summary>
    /// M6 14 the losing race, her side: one read for the race, each frame the autopilot runs (and at each read of the guard): her health for its trend (<see cref="HealthTrend"/>), the Action API's casts, her damage lines and the resists
    /// of her bolts (<see cref="KillRate"/>; a resist of the guard's own drain in flight is the drain's). Allocation-free.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    public void Sample(IGameSessionView session, double clock)
    {
        ArgumentNullException.ThrowIfNull(session);
        _trend.Sample(session.State, clock);
        PictureOf(session); // M6 16 E2d, Q5: the session's one health picture hears from the guard's first sample
        _kills.Read(session.ActionLog, session.State.Chat, _drainOwns);
        ReadKills(session, clock);
        _lastWalk = WalkTarget?.Invoke();
    }

    private long _killsSeen = -1;
    private KnownTargetHealth? _lastWalk;

    /// <summary>
    /// M6 14 the losing race, the killing blow (live ladder45 2026-11-16 23:07:24 to 23:07:43 UTC: four Shock Wave VI casts each killed a Zefir outright, so no damage line came, her bolts read "not measured" and the kill side was dropped):
    /// each killer notice since the last read (GameEvent 0x01AD, <see cref="HostileWatch.KillNotice"/>: ACE's death message to the killer, which names the victim) is tied to her latest cast before it when that was War Magic
    /// (<see cref="KillRate.TryKillingBolt"/>) and the notice names that cast's target, still in the object table. Its health before the blow is the one picture of it (<see cref="KnownTargetHealth"/>): the fight-back's target (this frame's
    /// or the last frame's: the journey may have moved on to the next), or the guard's own tracking of it when this bolt was her only cast at it in the window (nothing of hers was taken off it before); otherwise the blow is not counted
    /// (a lower bound only), and the line says so. Counted, it is a landed bolt of that health (<see cref="KillRate.NoteKill"/>) and the creature is dead for the incoming from its death line (<see cref="HostileWatch.NoteKilled"/>).
    /// Allocates only for a notice.
    /// </summary>
    private void ReadKills(IGameSessionView session, double clock)
    {
        if (_killsSeen < 0)
        {
            _killsSeen = _watch.KillNotices;
            return;
        }

        var state = session.State;
        for (; _killsSeen < _watch.KillNotices; _killsSeen++)
        {
            if (!_watch.KillNotice(_killsSeen, out var at, out var text))
            {
                continue;
            }

            if (!_kills.TryKillingBolt(at, out var spell, out var target) || target == 0)
            {
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {StateWord}: killing blow \"{text}\": not counted as a bolt: her latest cast before it was not a War Magic cast at a target (M6 14 the losing race, the kill side)"));
                continue;
            }

            var spellName = _spells.TryGetValue(spell, out var info) ? info.Name : spell.ToString(CultureInfo.InvariantCulture);
            if (!state.Objects.TryGet(target, out var o) || o.Name is not { Length: > 0 } name || !text.Contains(name, StringComparison.Ordinal))
            {
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {StateWord}: killing blow \"{text}\": not counted as a bolt: her {spellName}'s target 0x{target:X8} is not the creature it names (M6 14 the losing race, the kill side)"));
                continue;
            }

            var walk = WalkTarget?.Invoke() is { } w && w.Guid == target ? w : _lastWalk is { } l && l.Guid == target ? l : null;
            var known = walk ?? (_kills.CastsAt(at, target) == 1 ? Track(o, Listed(o.Weenie.WeenieClassId), state.Chat.Total) : null);
            if (known is null)
            {
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {StateWord}: killing blow \"{text}\" by her {spellName} at '{name}' 0x{target:X8}: not counted as a bolt: its health before the blow is not known ({_kills.CastsAt(at, target)} cast(s) of hers at it in the last {KillRate.WindowSeconds:0} s and it is not the fight-back's target) (M6 14 the losing race, the kill side)"));
                continue;
            }

            var before = HealthOf(session, known, clock, out var read); // M6 16 Q5: the one picture (its health just before its death)
            _kills.NoteKill(at, before, name);
            _watch.NoteKilled(target);
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {StateWord}: killing blow \"{text}\" by her {spellName} at '{name}' 0x{target:X8}: counted as a landed bolt of at least {before} ({PictureHealth.Words(known, before, in read, state)}); it is dead for the incoming from this line (M6 14 the losing race, the kill side)"));
        }
    }

    private string StateWord => _state.Length == 0 ? "sample" : _state;

    private bool DrainOwns(string kind) => _drain.Cast is { IsRunning: true } && !_drain.IsHeal && string.Equals(kind, _drainName, StringComparison.Ordinal);

    /// <summary>
    /// M6 14 the losing race, the kill side: the attackers on her (living creatures heard attacking her within <see cref="HostileWatch.AttackQuietSeconds"/>, the fight-back's target among them, standing within
    /// <see cref="HostileReach.DefaultMetres"/> of her where the screen draws them), their known remaining health together (<see cref="KnownTargetHealth"/>, the same picture the drain pick reads) and the seconds her bolts need to kill them
    /// one after another: each one's health over her damage a second on its kind (<see cref="KillRate.TryAgainst"/>: her mean landed bolt, the chance it lands, ACE's resist roll of her buffed War Magic against its Magic Defense from the
    /// world database with her measured resists on the kind taking over, over her mean cast). <paramref name="unknown"/> names why the kill side cannot be counted on (a health known nowhere, her bolts not measured).
    /// </summary>
    private int AttackersOnHer(IGameSessionView session, double clock, out double remaining, out double killSeconds, out string? unknown, out string detail)
    {
        (remaining, killSeconds, unknown, detail) = (0, 0, null, string.Empty);
        var state = session.State;
        if (state.Self.Position is not { } at)
        {
            return 0;
        }

        var war = GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var current, out _) ? current : 0u;
        var self = at.ToGlobal();
        var live = session.Live;
        var heard = _watch.HeardWithin(clock, HostileWatch.AttackQuietSeconds);
        var walk = WalkTarget?.Invoke();
        var count = 0;
        _detail.Clear();
        foreach (var o in state.Objects.All)
        {
            if ((o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || _watch.IsKilled(o.Guid)
                || ((o.Name is not { } n || !heard.Contains(n)) && (walk is null || walk.Guid != o.Guid)) || live.At(o) is not { } p || p.ToGlobal().DistanceTo(self) > HostileReach.DefaultMetres)
            {
                continue;
            }

            count++;
            var listed = Listed(o.Weenie.WeenieClassId);
            var known = walk is not null && walk.Guid == o.Guid ? walk : Track(o, listed, state.Chat.Total);
            if (known is null)
            {
                unknown ??= $"'{o.Name}' 0x{o.Guid:X8}'s maximum health is known nowhere";
                continue;
            }

            var health = HealthOf(session, known, clock, out _); // M6 16 Q5: the one picture
            remaining += health;
            var defence = (uint)Math.Max(0, known.MagicDefence);
            var chance = KillRate.LandChance(war, defence);
            if (!_kills.TryAgainst(clock, known.Name, chance, out var dps))
            {
                unknown ??= _kills.Unmeasured(clock);
                continue;
            }

            killSeconds += dps > 0 ? health / dps : double.PositiveInfinity;
            var (landed, resisted) = _kills.Of(known.Name);
            _detail.Append(_detail.Length == 0 ? string.Empty : ", ").Append(string.Create(CultureInfo.InvariantCulture, $"'{known.Name}' 0x{o.Guid:X8} {health} left, her bolts land {_kills.LandShare(known.Name, chance):0.##} (ACE's {chance:0.##} at War Magic {war} against Magic Defense {defence}, {landed} landed and {resisted} resisted measured): {(dps > 0 ? (health / dps).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "never")}"));
        }

        detail = _detail.ToString();
        return count;
    }

    private const int NotRead = -3;

    /// <summary>The heal pick now (no cast): <see cref="HealPick.HealSelf"/>, a drain's index, <see cref="HealPick.Nothing"/>, or <see cref="NotRead"/>; its candidates and rates are kept for the caller.</summary>
    private int Assess(IGameSessionView session, double clock, uint health, uint max)
    {
        var self = session.State.Self;
        var q = self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out _) || !q.TryGetSkill(MageTemplate.LifeMagic, out var lifeSkill))
        {
            return NotRead;
        }

        _drain.Fill(q, self.Spellbook, LevelFloor.HeldForAccount(session.State, MagicSchool.LifeMagic, _accountName), _castChance, _payable);
        var initPlusRanks = lifeSkill.Init + lifeSkill.Ranks;
        var life = GearCasts.TryCurrentSkill(q, MageTemplate.LifeMagic, out var current, out _) ? current : initPlusRanks;
        var missing = max > health ? max - health : 0u;
        var target = KnownFullest(session, clock, out var targetHealth, out var defence, out var distance, out var name, out var resist);
        var count = 0;
        if (target != 0)
        {
            var levels = _drain.Levels;
            var situation = new DrainSituation(health, max, mana, life, initPlusRanks, targetHealth, defence, distance, resist); // M6 14 drain resistance: the kind's share of the take
            for (var i = 0; i < levels.Length && count < _candidates.Length; i++)
            {
                if (mana < GearCasts.ManaCeiling(levels[i].Info, 0) || distance > FightScenario.CastRange(levels[i].Info, initPlusRanks))
                {
                    continue;
                }

                _candidates[count] = FightHealPick.DrainCandidate(in levels[i], in situation, target, _heals, out _formulas[count]);
                _levelIndex[count++] = i;
            }
        }

        var canSelf = FightHealPick.SelfCandidate(_drain, _heals, double.NaN, missing, mana, life, out var healSelf);

        // M6 14 tactics (live ladder56 08:33:08.306 UTC: Heal Self VI 9.4 against Drain Health Other I 20.8, each charged the seed 0.09 s a mana point on its spell's ceiling, Heal Self on the drain's seed seconds): the pick is by the
        // health a landed cast gives her a second now (HealPick.ChooseNow), each candidate's chance the measured share of its casts that landed on that kind where there is one (the race's own read), Heal Self on its own seconds.
        for (var i = 0; i < count; i++)
        {
            _nowCandidates[i] = _candidates[i] with { Chance = _heals.LandShare(_candidates[i].Name, name, _candidates[i].Chance) };
        }

        var nowSelf = healSelf with { Chance = _heals.LandShare(healSelf.Name, null, healSelf.Chance) };
        var choice = HealPick.ChooseNow(in nowSelf, canSelf, _nowCandidates.AsSpan(0, count), out var selfRate, out var best, out var bestRate);
        (_aMana, _aTarget, _aTargetHealth, _aDistance, _aName, _aCount, _aCanSelf) = (mana, target, targetHealth, distance, name, count, canSelf);
        (_aSelf, _aSelfRate, _aBest, _aBestRate) = (healSelf, selfRate, best, bestRate);

        // The retreat rule's heal read (AssessHeal) keeps its own measure of the chosen heal, the fight's score (HealPick.PerSecond: the mana's time and the damage credit in it), as before this pick changed.
        _aChosenScore = choice == HealPick.HealSelf ? HealPick.PerSecond(in healSelf, FightRates.SeedSecondsPerMana, _damageWeight) : choice >= 0 ? HealPick.PerSecond(in _candidates[choice], FightRates.SeedSecondsPerMana, _damageWeight) : 0;
        return choice;
    }

    /// <summary>One frame of the guard in <paramref name="state"/>. True while it holds the state (the caller does not step the state's station).</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="state">The state's token, for the log.</param>
    public bool Step(IGameSessionView session, double clock, string state)
    {
        _state = state;
        ReadChat(session.State.Chat); // M6 14 the losing race: the heals ACE gave and the takes of her drains, measured
        Sample(session, clock);
        if (_drain.Cast is { IsRunning: true } running)
        {
            var end = running.Step(clock);
            if (end == CastEnd.Running)
            {
                return true;
            }

            CastEnded(session, end, clock);
        }

        if (!_emergency.Enabled || !session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || health == 0 || max == 0)
        {
            return Release(null);
        }

        if (session.State.Self.Travel != SelfTravel.Present || session.State.Self.Position is null)
        {
            // M5 7 F30 (soak round 7: three Heal Self casts at the login were refused 0x001D, ACE's YoureTooBusy while the player is still in portal space): the guard casts nothing until the portal space is over and the player is placed.
            if (!_settleLogged)
            {
                _settleLogged = true;
                _log.Info(AutopilotScenario.LogCategory, $"guard: {state}: the login is still settling (travel {session.State.Self.Travel}, placed {(session.State.Self.Position is null ? "no" : "yes")}): no cast until the portal space is over and the player is placed");
            }

            return Release(null);
        }

        _settleLogged = false;

        if (clock < _fightOnUntil)
        {
            Holding = false; // M6 14 the losing race: she kills first: the fight goes on, and the race is read again in a second
            return false;
        }

        if (clock < _holdOffUntil)
        {
            // M6 14 the losing race: the leave by Lifestone Recall is under way: no heal starts before the recall is cast and ACE's delay has run (a heal here would take the lanes the recall needs)
            if (!_holdOffLogged)
            {
                _holdOffLogged = true;
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {state}: holds off {_holdOffUntil - clock:0.#} s more at health {health}/{max}: the losing race's leave is under way and no heal starts while Lifestone Recall is cast (M6 14 the losing race)"));
            }

            Holding = false;
            return false;
        }

        if (_holdOffLogged)
        {
            _holdOffLogged = false;
            if (RaceLost)
            {
                RaceLost = false; // nobody took the leave it asked for: the guard is back
                _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {state}: the losing race's leave was not taken within {HoldOffSeconds:0.##} s (health {health}/{max}): the guard heals again"));
            }
        }

        if (!_emergency.Wanted(health, max, _watch.IncomingPerSecond(clock), clock))
        {
            return Release(string.Create(CultureInfo.InvariantCulture, $"health {health}/{max} at the safe line"));
        }

        if (session.Actions is ActionApi api && (api.QueuedCount > 0 || api.InFlightCount > 0))
        {
            if (!_waitLogged)
            {
                _waitLogged = true;
                _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {state}: health {health}/{max} under the line {_emergency.LineHealth:0}: the state's action in flight ends first (the busy rules), then the heal pick"));
            }

            Holding = true;
            return true;
        }

        _waitLogged = false;
        Holding = true;
        if (clock < _retryAt)
        {
            // M5 7 F24: a refused cast backs off, at most one attempt a second; the state's station stays held meanwhile, as for any heal. M6 14 the losing race (live 06:17:10.9 UTC: a resisted drain at 76, the next read at 50): the race is
            // read at once, not after the wait: the resist that made it lost is the reason to leave now
            return !RaceDuringWait(session, clock, health, max);
        }

        return Pick(session, clock, health, max);
    }

    private bool Pick(IGameSessionView session, double clock, uint health, uint max)
    {
        // M6 0 F8 (the first climb probe: Winnie's Training Wand sat in the pack, the hands held nothing, and the guard asked for the magic mode every second for 90 s until the known-state watchdog fired; the audit's wield-caster step never ran
        // because the guard held the state): check, no caster in the hands (and no unreadable weapon). Act, none: a cast cannot start, and the mode change is refused by name (NoCasterHeld). The state goes on, and its caster step
        // (the audit's wield-caster fix, the recovery table's) wields one; the next frame checks again. With no caster in the packs either there is nothing to wield: the guard asks as before and the refusal (NoCasterHeld) goes to the recovery table.
        if (CombatModeGuard.Read(session.State) is { Caster: null, Unknown: 0 } && PackHoldsCaster(session.State))
        {
            if (!_noCasterLogged)
            {
                _noCasterLogged = true;
                _log.Warn(AutopilotScenario.LogCategory, $"guard: {_state}: health {health}/{max} under the line {_emergency.LineHealth:0}, but the hands hold no caster (the packs hold one), so no Heal Self can be cast (the magic mode is refused): the state goes on and its caster step wields one first (M6 0 F8)");
            }

            Holding = false;
            return false;
        }

        _noCasterLogged = false;
        var choice = Assess(session, clock, health, max);
        if (choice == NotRead)
        {
            return Release("no mana or Life Magic read");
        }

        var q = session.State.Self.Qualities;
        var (mana, target, targetHealth, distance, name, count) = (_aMana, _aTarget, _aTargetHealth, _aDistance, _aName, _aCount);
        var (healSelf, selfRate, best, bestRate) = (_aSelf, _aSelfRate, _aBest, _aBestRate);
        if (choice == HealPick.Nothing)
        {
            if (!_nothingLogged)
            {
                _nothingLogged = true;
                _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: health {health}/{max} under the line {_emergency.LineHealth:0}, but no Heal Self and no drain is paid for (mana {mana}): the retreat rule is asked (M6 6 F2, f23-guard-nothing-paid's answer: on the ground the retreat takes it home; elsewhere the state goes on)"));
            }

            NothingPaid = true;
            Holding = false;
            return false;
        }

        _nothingLogged = false;
        if (Reserve?.Invoke() is { } reserve)
        {
            // M6 14 retreat attackers (rule 4; live 2026-11-14: this guard's heals spent the fight-back's mana from 268 to 49, under Lifestone Recall's 50, while the leg's own checks waited on them): a travel leg's fight-back holds
            // the way out's reserve, and a heal may spend into it only while the recall stays paid (ManaWayOut.Spend, the heal's arm, the fight's own rule), or when the next hit kills. Refused: nothing is paid here, the hold is
            // released, and the leg's next check leaves by the recall it can still pay.
            var cost = choice == HealPick.HealSelf ? (uint)Math.Ceiling(healSelf.Mana) : GearCasts.ManaCeiling(_drain.Levels[_levelIndex[choice]].Info, 0);
            var nextHitKills = _watch.Read(clock).NextHitKills(health);
            var answer = ManaWayOut.Spend(new WayOutSpend(WayOutSpendKind.Drain, mana, cost, reserve, Attackers: true, NextHitKills: nextHitKills));
            if (!answer.Yes)
            {
                if (!_reserveLogged)
                {
                    _reserveLogged = true;
                    _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: health {health}/{max} under the line {_emergency.LineHealth:0}, but a heal of {cost} would take the mana {mana} under Lifestone Recall's {reserve.RecallCost} (the fight-back's reserve, line {reserve.Line}): refused ({answer.Why}); the hold is released and the leg leaves by the recall it can still pay (M6 14 retreat attackers)"));
                }

                NothingPaid = true;
                Holding = false;
                return false;
            }
        }

        _reserveLogged = false;
        NothingPaid = false;
        if (RaceHandedOver(session, clock, health, max, choice))
        {
            return false; // M6 14 the losing race: the leave is asked, or she kills first and the fight goes on: the hold is released
        }

        // M6 14 tactics: the pick itself reads the measured landing share of each heal on the kind (the reason for the losing race's switch to "the heal that holds" here, live 06:17:07 UTC) and the health a
        // cast gives her a second now (HealPick.ChooseNow); the switch compared the race's sustained rates, which charge each cast its ceiling mana at the seed rate, and would put the drain back over Heal Self on the mana's time alone.
        if (q.CombatMode != CombatModes.Magic)
        {
            // M5 7 F24 (the corpse trip: the caster refused "not in the magic combat mode" dozens of times in one frame): the mode first, as the fight and the keeper do; the next frame casts once the change has landed.
            if (clock - _modeAskedAt >= RetrySeconds)
            {
                _modeAskedAt = clock;
                _log.Info(AutopilotScenario.LogCategory, $"guard: {_state}: entering the magic mode for {(choice == HealPick.HealSelf ? _drain.HealName : _drain.Levels[_levelIndex[choice]].Info.Name)} (the caster cannot cast outside it)");
                session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
            }

            return true;
        }

        var line = HealPick.Line(choice, in healSelf, selfRate, _candidates.AsSpan(0, count), best, bestRate);
        _castAt = clock;
        Casts++;
        if (choice == HealPick.HealSelf)
        {
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {line}; {_drain.HealName} at health {health}/{max} (line {_emergency.LineHealth:0}), mana {mana}{(target == 0 ? ", no target of known health in reach" : string.Empty)}"));
            _drainTarget = 0;
            _drain.StartHeal(session, clock);
            return true;
        }

        var level = _drain.Levels[_levelIndex[choice]];
        _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {line}; {level.Info.Name} on {name} 0x{target:X8} at health {health}/{max} (line {_emergency.LineHealth:0}, target health {targetHealth}, {distance:0.#} m)"));
        (_drainTarget, _drainSpell, _drainName, _drainRow, _drainFormula) = (target, level.Info.Name, name, level.Row, _formulas[choice]); // M6 14 the losing race: its gain line is measured against this cast
        _drain.Start(session, _levelIndex[choice], target, clock, attack: true);
        return true;
    }

    /// <summary>
    /// M6 14 the losing race: the race at this pick (<see cref="LosingRace"/>, both sides), said with its numbers whenever an attack is measured (each new verdict, and a repeat at most every <see cref="RaceLogSeconds"/>). Winning (her
    /// health rises or holds) or holding (the heal holds the incoming of the living attackers): the heal goes. Losing but she kills what is on her first (<see cref="LosingRaceAnswer.FightsOn"/>): the hold is released for
    /// <see cref="FightOnSeconds"/>, so the fight-back's bolts go on, and true. Losing and not killing, but one more heal still leaves the way out paid: the heal goes. Lost, with a leave open (<see cref="LeaveRoad"/>): the hold is released,
    /// <see cref="RaceLost"/> is up for the autopilot, no heal starts for <see cref="HoldOffSeconds"/>, and true. Lost with no leave open: said once for each reason, and the heal goes (it slows the loss).
    /// </summary>
    private bool RaceHandedOver(IGameSessionView session, double clock, uint health, uint max, int choice)
    {
        var input = RaceInput(session, clock, health, choice);
        var answer = LosingRace.Decide(in input);
        if (!(input.IncomingAllPerSecond > 0))
        {
            _raceLogged = string.Empty;
            return false; // no attack measured: nothing to race
        }

        var words = LosingRace.Words(in input, in answer);
        var verdict = LosingRace.Verdict(in answer);
        if (!answer.Lost && (!string.Equals(verdict, _raceVerdictLogged, StringComparison.Ordinal) || clock - _raceLoggedAt >= RaceLogSeconds))
        {
            (_raceVerdictLogged, _raceLoggedAt) = (verdict, clock);
            var then = answer.FightsOn ? $"she kills first: the hold is released for {FightOnSeconds:0.#} s and the fight-back's bolts go on" : answer.Losing ? "one more heal still leaves the way out paid, so it goes" : "the heal goes";
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: race at health {health}/{max}: {words}: {then} (M6 14 the losing race)"));
        }

        if (answer.FightsOn)
        {
            (_fightOnUntil, Holding) = (clock + FightOnSeconds, false);
            return true;
        }

        if (!answer.Lost)
        {
            _raceLogged = string.Empty;
            return false;
        }

        var (open, road) = LeaveRoad?.Invoke() ?? (false, "no leave is wired for this run");
        if (!open)
        {
            if (!string.Equals(_raceLogged, road, StringComparison.Ordinal))
            {
                _raceLogged = road;
                _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: the race is lost at health {health}/{max}: {words}; but no leave from here ({road}): she heals on, which only slows the loss (M6 14 the losing race)"));
            }

            return false;
        }

        _raceLogged = string.Empty;
        (RaceLost, RaceWhy, Holding) = (true, words, false);
        _holdOffUntil = clock + HoldOffSeconds;
        _holdOffLogged = true;
        _log.Warn(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: the race is lost at health {health}/{max}: {words}: the hold is released and the leave is asked: {road}; no heal starts for {HoldOffSeconds:0.##} s (M6 14 the losing race)"));
        return true;
    }

    /// <summary>
    /// M6 14 the losing race: the chat lines since the last read: ACE's Heal Self lines ("You cast Heal Self VI and restore 98 points of your Health.") measure Heal Self; the gain line of the guard's own drain in flight ("You gain 60
    /// points of health due to casting Drain Health Other I on Assailer") measures the drain's ratio to the formula it was cast on and its take from the target, the gain over 1 minus the spell's LossPercent cut to its TransferCap
    /// (ACE <c>HandleCastSpell_Transfer</c>, <c>WorldObject_Magic.cs:721-747</c> @95ef426d), which the target's health is told (<see cref="KnownTargetHealth.NoteTake"/>). The lines before the guard's first read are not its. Allocates only
    /// for a line it measures.
    /// </summary>
    private void ReadChat(ChatLog chat)
    {
        if (_chatScanned < 0)
        {
            _chatScanned = chat.Total;
            return;
        }

        var fresh = (int)Math.Min(chat.Count, chat.Total - _chatScanned);
        _chatScanned = chat.Total;
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind != ChatKind.System || line.ChatType != FightTally.MagicChatType || !line.Text.StartsWith("You ", StringComparison.Ordinal))
            {
                continue;
            }

            if (MeasuredHeals.TryParseSelfHeal(line.Text, out var restored, out var healName))
            {
                _heals.Note(healName, null, restored);
                continue;
            }

            if (_drainTarget == 0 || !MeasuredHeals.TryParseDrain(line.Text, out var gain, out var spell, out var on) || !string.Equals(spell, _drainSpell, StringComparison.Ordinal) || !string.Equals(on, _drainName, StringComparison.Ordinal))
            {
                continue;
            }

            var take = DrainTake(gain, in _drainRow);
            _heals.Note(spell, on, gain);
            if (_drainFormula > 0)
            {
                _heals.NoteRatio(spell, gain, _drainFormula);
            }

            var known = _known.TryGetValue(_drainTarget, out var k) ? k : null;
            known?.NoteTake(take);
            HealthPictureTook(_drainTarget, take); // M6 16 E2d, Q5: the one picture is told the drain's take
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: measured: {spell} on {on} 0x{_drainTarget:X8} gave {gain} (the formula said {_drainFormula:0.#}): it took {take:0.#} from it (the gain over 1 - its loss {_drainRow.LossPercent:0.##}, cut to its cap {_drainRow.TransferCap}){(known is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"; it is told {known.Dealt:0} dealt"))} (M6 14 the losing race)"));
            _drainTarget = 0; // one gain line a cast
        }
    }

    /// <summary>What a drain took from its target for a gain of <paramref name="gain"/>: ACE's destination change is the source change times 1 minus the loss, both cut to the cap, so the source change is the gain over 1 minus the loss.</summary>
    /// <param name="gain">Her gain.</param>
    /// <param name="row">The drain's transfer row.</param>
    internal static double DrainTake(double gain, in TransferRow row)
    {
        var take = gain / Math.Max(0.25, 1.0 - row.LossPercent);
        return row.TransferCap > 0 ? Math.Min(take, row.TransferCap) : take;
    }

    /// <summary>The race during a retry's wait (no cast): true when it released the hold (the leave asked, or the fight goes on). Read once a wait.</summary>
    private bool RaceDuringWait(IGameSessionView session, double clock, uint health, uint max)
    {
        if (_raceReadInWait == _retryAt)
        {
            return false;
        }

        _raceReadInWait = _retryAt;
        var choice = Assess(session, clock, health, max);
        return choice != NotRead && choice != HealPick.Nothing && RaceHandedOver(session, clock, health, max, choice);
    }

    private double _raceReadInWait = double.NaN;

    /// <summary>True when a pack holds a caster (a wand, a staff, an orb) the caster step can wield.</summary>
    private static bool PackHoldsCaster(GameState state)
    {
        foreach (var item in PackLayout.Items(state))
        {
            if ((item.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private void CastEnded(IGameSessionView session, CastEnd end, double clock)
    {
        session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max);
        var cast = _drain.Cast!;
        var seconds = clock - _castAt;
        if (end.Succeeded())
        {
            Landed++;
            if (seconds > 0)
            {
                _heals.NoteSeconds(_drain.LastName, seconds); // M6 14 the losing race: Heal Self's seconds too (the race reads them; the pick keeps its level's seed for Heal Self)
            }
        }
        else if (!_drain.IsHeal)
        {
            _drainTarget = 0; // no gain line comes for a drain that did not land
        }

        if (end != CastEnd.Busy)
        {
            _heals.NoteTry(_drain.LastName, _drain.IsHeal ? null : _drainName, end.Succeeded()); // M6 14 the losing race: the share of its casts that land, a drain's on the kind it was cast at (a resist, a fizzle and a refusal gave nothing)
        }

        if (_emergency.Ended(end.Succeeded(), end == CastEnd.Resisted, seconds, clock))
        {
            _asideUntil = clock + FightEmergency.AsideSeconds; // M6 6 F2: the retreat rule's aside=
        }
        if (end.Succeeded())
        {
            _refusalLogged = string.Empty;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} landed in {seconds:0.0} s; health {health}/{max}"));
            return;
        }

        if (end == CastEnd.Busy)
        {
            // M5 7 F30: ACE's YoureTooBusy (0x001D) past the cast rules' own busy waits is a wait, not a failure: the guard tries again after a second.
            _retryAt = clock + RetrySeconds;
            Busy++;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} met WeenieError 0x001D (YoureTooBusy) in {seconds:0.0} s: a wait, not a failure; the next try in {RetrySeconds:0.#} s; health {health}/{max}"));
            return;
        }

        _retryAt = clock + RetrySeconds; // M5 7 F24: a refused or failed cast is tried again after a second at the soonest, and logged once per reason
        var reason = end.Name() + ": " + (cast.Report.Why ?? "?");
        if (!string.Equals(reason, _refusalLogged, StringComparison.Ordinal))
        {
            _refusalLogged = reason;
            _log.Info(AutopilotScenario.LogCategory, string.Create(CultureInfo.InvariantCulture, $"guard: {_state}: {_drain.LastName} did not land ({reason}) in {seconds:0.0} s; health {health}/{max}; the next try in {RetrySeconds:0.#} s at the soonest"));
        }
    }

    private bool Release(string? why)
    {
        if (Holding && why is not null)
        {
            _log.Info(AutopilotScenario.LogCategory, $"guard: {_state}: {why}: the state goes on");
        }

        (Holding, _waitLogged, NothingPaid, RaceLost) = (false, false, false, false);
        return false;
    }

    /// <summary>
    /// The living creature in the watch's reach a drain can take the most from, or 0. The candidates: a target of the ground (its profile's facts), the travel leg's fight-back target (<see cref="WalkTarget"/>) and, M6 14 the losing race,
    /// every creature heard attacking her within <see cref="HostileWatch.AttackQuietSeconds"/> (live 2026-11-15: three full Tuskie Gunners hit her while every drain went to an Assailer drained to a few points). Each is read from the one
    /// picture of its health (<see cref="KnownTargetHealth"/>: the fight-back's own target, or the guard's tracking of it by its facts), its distance from the body the screen draws (<see cref="IGameSessionView.Live"/>, else the server's
    /// report). The fullest is the most a drain takes from: its health times its kind's drain resistance (<see cref="DrainResist.Of"/>; a kind that lets none through, an Armored Tusker, is passed over). D92: a creature the server never gave
    /// the health of is taken as full only for the ladder's character (the 2026-11-11 owner rule, <see cref="CharacterPolicy.ManaBySum"/>) or as the fight-back's target; for any other mage it is passed over.
    /// </summary>
    private uint KnownFullest(IGameSessionView session, double clock, out uint targetHealth, out uint defence, out float distance, out string name, out double resist)
    {
        (targetHealth, defence, distance, name, resist) = (0, 0, 0, string.Empty, 1.0);
        if (session.State.Self.Position is not { } at)
        {
            return 0;
        }

        var state = session.State;
        var walk = WalkTarget?.Invoke();
        Adopt(walk);
        Prune(state.Objects);
        var self = at.ToGlobal();
        var live = session.Live;
        var assume = CharacterPolicy.For(state.Self.Name).ManaBySum;
        var heard = _watch.HeardWithin(clock, HostileWatch.AttackQuietSeconds);
        var best = 0u;
        var pick = new Threat.FullestPick(Threat.FullestRule.GuardDrain); // M6 16 E2d: the one comparison, by this pick's rule (the most a drain takes, the first met among equals)
        foreach (var o in state.Objects.All)
        {
            if ((o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
            {
                continue;
            }

            var wcid = o.Weenie.WeenieClassId;
            var listed = Listed(wcid);
            var isWalk = walk is not null && walk.Guid == o.Guid;
            if (listed is null && !isWalk && (o.Name is not { } n || !heard.Contains(n)))
            {
                continue;
            }

            if (live.At(o) is not { } p || (float)p.ToGlobal().DistanceTo(self) is var d && d > _watch.ReachMetres)
            {
                continue;
            }

            var kindResist = DrainResist.Of(wcid);
            if (kindResist <= 0 || (!isWalk && !assume && o.Health is null))
            {
                continue;
            }

            if ((isWalk ? walk : Track(o, listed, state.Chat.Total)) is not { } known)
            {
                continue;
            }

            var health = HealthOf(session, known, clock, out _); // M6 16 Q5: the one picture
            var take = health * kindResist;
            if (!pick.Offer(-1, o.Guid, take, d))
            {
                continue;
            }

            (best, targetHealth, distance, name, defence, resist) = (o.Guid, health, d, o.Name ?? "creature", (uint)Math.Max(0, known.MagicDefence), kindResist);
        }

        return best;
    }

    /// <summary>The ground's target of weenie class <paramref name="wcid"/> (<see cref="FightTargets.Of"/>), or null.</summary>
    private GroundTarget? Listed(uint wcid)
    {
        foreach (var t in _targets ??= FightTargets.Of(_ground))
        {
            if (t.Wcid == wcid)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// The fight-back's published target joins the guard's picture of the creatures (<see cref="_known"/>): a tracking the guard held for the same creature carries into it (<see cref="KnownTargetHealth.CarryFrom"/>), and every other tracking
    /// stops counting the lines that name its kind (they are the new target's).
    /// </summary>
    private void Adopt(KnownTargetHealth? walk)
    {
        if (walk is not null && (!_known.TryGetValue(walk.Guid, out var had) || !ReferenceEquals(had, walk)))
        {
            if (had is not null)
            {
                walk.CarryFrom(had);
            }

            _known[walk.Guid] = walk;
        }

        foreach (var k in _known.Values)
        {
            if (k.CountsLines && !ReferenceEquals(k, walk))
            {
                k.CountsLines = false;
            }
        }
    }

    /// <summary>The guard's tracking of <paramref name="o"/> (made on first sight from its facts: the ground's target's, else the profiles' and the world database's table), or null when its maximum health is known nowhere (named once).</summary>
    private KnownTargetHealth? Track(WorldObject o, GroundTarget? listed, long chatTotal)
    {
        if (_known.TryGetValue(o.Guid, out var known))
        {
            return known;
        }

        var wcid = o.Weenie.WeenieClassId;
        int max;
        string source;
        if (listed is { MaxHealth: > 0 })
        {
            (max, source) = (listed.MaxHealth, "the ground's target max_health");
        }
        else if (CreatureFacts.All.MaxHealthOf(wcid, out source) is { } facts)
        {
            max = facts;
        }
        else
        {
            if (_unknownNoted.Add(wcid))
            {
                _log.Warn(AutopilotScenario.LogCategory, $"guard: {_state}: '{o.Name}' 0x{o.Guid:X8} (weenie {wcid}) is offered no drain: its maximum health is known nowhere ({source})");
            }

            return null;
        }

        var defence = listed is { MagicDefence: > 0 } ? listed.MagicDefence : Bestiary.Find(wcid)?.Defence.MagicDefence ?? 0; // M6 16 E1b: a row that gives no magic_defence (the den's avoided kinds) is read on in the bestiary, as the fight's read does (FightScenario.TargetStatsOf)
        known = new KnownTargetHealth(o.Guid, wcid, o.Name ?? "creature", max, defence, string.Create(CultureInfo.InvariantCulture, $"weenie {wcid}, {source}, Magic Defense {defence}; the guard's tracking"), chatTotal) { CountsLines = false };
        _known[o.Guid] = known;
        return known;
    }

    /// <summary>Forgets the trackings of the creatures the object table no longer holds (allocation-free after the first prune).</summary>
    private void Prune(WorldObjectTable objects)
    {
        if (_known.Count == 0)
        {
            return;
        }

        _gone.Clear();
        foreach (var guid in _known.Keys)
        {
            if (!objects.TryGet(guid, out _))
            {
                _gone.Add(guid);
            }
        }

        foreach (var guid in _gone)
        {
            _known.Remove(guid);
        }
    }
}
