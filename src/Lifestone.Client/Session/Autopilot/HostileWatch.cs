using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 F23 (soak round 2's death): whether a hostile is on the mage, read the same way in every autopilot state. A hostile is in reach when a living target creature of the ground (the profile's targets: any ground, D46) stands within
/// the aggro reach (<see cref="DeathOptions.TripAggroMetres"/>, F19's registered distance, 20 m by default), or when a creature hit the player (F4's attack notice, GameEvent 0x01B2 DefenseHit) within <see cref="AttackQuietSeconds"/>.
/// It also keeps the recent hits (<see cref="RecentHits"/>, M6 7 Y) for the vitals guard's emergency line and the retreat rule. Allocation-free after construction.
/// </summary>
public sealed class HostileWatch : IDisposable
{
    /// <summary>The seconds after an attack notice during which the mage counts as attacked (as the re-buff driver's <see cref="RebuffDriver.FightQuietSeconds"/>).</summary>
    public const double AttackQuietSeconds = RebuffDriver.FightQuietSeconds;

    private const uint CreatureItemType = 0x10; // ACE ItemType.Creature

    private readonly IGameSessionView _session;
    private readonly IReadOnlyList<uint> _targets;
    private readonly RecentHits _hits = new();
    private readonly IDisposable _subscription;
    private readonly IDisposable _chatSubscription;
    private readonly IDisposable _killSubscription;
    private readonly ThreatBoard _board;
    private readonly ThreatQuery _inReach;

    /// <summary>The watch over a session on a ground.</summary>
    /// <param name="session">The session (its bus gives the attack notices; its read model the creatures).</param>
    /// <param name="ground">The ground (its target creatures).</param>
    /// <param name="reachMetres">The aggro reach in metres (0: only the attack notices count).</param>
    public HostileWatch(IGameSessionView session, HuntingGround ground, double reachMetres)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ground);
        _session = session;
        _targets = FightTargets.Wcids(ground); // M6 0 a5: an outdoor ground's outdoor.creatures within its band (a dungeon's profile targets as before)
        ReachMetres = Math.Max(0, reachMetres);
        _board = ThreatBoard.For(session); // M6 16 Q2: the board hears from this watch's start at the latest (it is the session's: it may have heard long before)
        _inReach = ThreatQuery.InReach(ReachMetres, AttackQuietSeconds, _targets);
        _subscription = session.Events.Subscribe<CombatEvent>(OnCombat);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat);
        _killSubscription = session.Events.Subscribe<PlayerKilledSomething>(OnKill);
    }

    /// <summary>The aggro reach in metres.</summary>
    public double ReachMetres { get; }

    /// <summary>The session time of the last attack notice, or negative infinity.</summary>
    public double LastAttackAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Who hit the mage last (the notice's attacker), or empty.</summary>
    public string LastAttacker { get; private set; } = string.Empty;

    /// <summary>
    /// M6 16 Q2: the session time of the last hit on her as the threat board heard it (hits only, the board's whole hearing: a hit from before this watch was built included), or negative infinity: the hit <see cref="InReach"/>
    /// answers "attacked" by. The outer loop's readers of "was she attacked, and when" read this, so they agree with <see cref="InReach"/>; <see cref="LastAttackAt"/> stays the watch's own hearing for its rings.
    /// </summary>
    public double LastHitAt => _board.LastHitAt;

    /// <summary>M6 16 Q2: who landed that hit (<see cref="LastHitAt"/>), as the notice named it, or empty.</summary>
    public string LastHitBy => _board.LastHitBy;

    /// <summary>M6 7 Y: what the recent hits say at <paramref name="now"/>: the read the fight's emergency answers from its own ring of the same notices (<see cref="RecentHits"/>).</summary>
    /// <param name="now">The session clock.</param>
    public IncomingRead Read(double now) => _hits.Read(now);

    /// <summary>The damage taken a second at <paramref name="now"/> (<see cref="Read"/>).</summary>
    /// <param name="now">The session clock.</param>
    public double IncomingPerSecond(double now) => Read(now).PerSecond;

    /// <summary>
    /// True when a hostile is in reach at <paramref name="now"/>: an attack that landed on her within <see cref="AttackQuietSeconds"/>, or a living target creature of the ground within <see cref="ReachMetres"/> where the
    /// session draws it. <paramref name="what"/> names it (allocates only then). M6 16 Q2 (docs/automation-engine.md E2, <c>TODO(engine-threat-flip)</c>): the session's threat board answers, asked the watch's own question
    /// (<see cref="ThreatQuery.InReach"/>: this watch's reach, its quiet, a hit as what counts, the ground's targets); the one change: a hit the board heard before this watch was built (a rung move rebuilds the scenario and its
    /// watch) is known for its quiet, where the watch's own hearing began empty. The watch keeps its own hearing for its other reads (<see cref="LastAttackAt"/>, <see cref="Read"/>, <see cref="HeardWithin"/>, ...).
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <param name="what">What holds: the attacker or the creature and its distance.</param>
    public bool InReach(double now, out string what)
    {
        var answer = _board.Ask(_session, in _inReach, now);
        switch (answer.Kind)
        {
            case ThreatAnswerKind.Attacked:
                what = string.Create(CultureInfo.InvariantCulture, $"{answer.Name} attacked {now - answer.At:0.#} s ago");
                return true;
            case ThreatAnswerKind.Standing:
                what = string.Create(CultureInfo.InvariantCulture, $"{answer.Name} 0x{answer.Guid:X8} at {answer.Metres:0.#} m (reach {ReachMetres:0.#} m)");
                return true;
            default:
                what = string.Empty;
                return false;
        }
    }

    /// <summary>The nearest living target creature of the ground within <see cref="ReachMetres"/> of the mage, if any.</summary>
    /// <param name="guid">Its guid.</param>
    /// <param name="name">Its name.</param>
    /// <param name="metres">Its distance.</param>
    public bool Nearest(out uint guid, out string name, out double metres)
    {
        (guid, name, metres) = (0, string.Empty, double.MaxValue);
        var state = _session.State;
        if (ReachMetres <= 0 || state.Self.Position is not { } at)
        {
            return false;
        }

        var self = at.ToGlobal();
        var live = _session.Live; // M6 14 (m6-live-position): a creature stands where its body is drawn when the session draws bodies, else where the server last reported it
        foreach (var o in state.Objects.All)
        {
            if (o.Position is null || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || !Listed(o.Weenie.WeenieClassId) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(self);
            if (d <= ReachMetres && d < metres)
            {
                (guid, name, metres) = (o.Guid, o.Name ?? "creature", d);
            }
        }

        return guid != 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _subscription.Dispose();
        _chatSubscription.Dispose();
        _killSubscription.Dispose();
    }

    private bool Listed(uint wcid)
    {
        for (var i = 0; i < _targets.Count; i++)
        {
            if (_targets[i] == wcid)
            {
                return true;
            }
        }

        return false;
    }

    private void OnCombat(in CombatEvent e)
    {
        if (e.Kind == CombatEventKind.DefenseEvaded)
        {
            NoteAttack(e.Who); // M6 14 the losing race (live ladder45 23:07:43 UTC): an evaded swing is an attack heard, no hit
            return;
        }

        if (e.Kind != CombatEventKind.DefenseHit)
        {
            return;
        }

        NoteHit(e.Who, e.Damage);
    }

    /// <summary>
    /// M5 7 G2 (edge audit item 10): a creature's spell at the player counts as an attack like a melee hit, so a Ravener or a Virindi bolting from range outside the fight is heard (the guard's incoming rate and the trip's, the death's
    /// and the stop's hostile checks). The line is the server's Magic-typed damage line the fight's tally reads ("&lt;source&gt; &lt;verb&gt; you for &lt;n&gt; points with &lt;spell&gt;."), its source taken by the self-defence parser.
    /// </summary>
    private void OnChat(in ChatReceived e)
    {
        if (e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System)
        {
            return;
        }

        if (TryHealthLoss(e.Line.Text, out var lossSource, out var loss))
        {
            NoteHitOrAttack(lossSource, loss);
            return;
        }

        if (!FightScenario.HostileSpellSource(e.Line.Text, out var source, out _))
        {
            return;
        }

        switch (FightTally.Classify(e.Line.Text, out var amount, out _))
        {
            case MagicLineKind.DamageTaken:
                NoteHit(source, (uint)Math.Clamp(amount, 0, uint.MaxValue));
                break;
            case MagicLineKind.DrainTaken when e.Line.Text.Contains(HealthDrainTail, StringComparison.Ordinal):
                NoteHitOrAttack(source, amount); // M6 14 the losing race (live ladder45 23:15:02.114 UTC: "Dark Wisp casts Harm Other II and drains 11 points of your health."): health a spell took is a hit
                break;
            default:
                NoteAttack(source); // a resisted spell, a drain of stamina or mana, a spell cast on her: an attack heard, no hit
                break;
        }
    }

    private const string HealthDrainTail = " points of your health.";

    private void NoteHitOrAttack(string who, long amount)
    {
        if (amount > 0)
        {
            NoteHit(who, (uint)Math.Min(amount, uint.MaxValue));
        }
        else
        {
            NoteAttack(who);
        }
    }

    /// <summary>M6 14 the losing race: a health drain cast on her by another, from her side (<see cref="FightTally.TryHealthLoss"/>).</summary>
    /// <param name="text">The Magic-typed line.</param>
    /// <param name="source">The caster's name.</param>
    /// <param name="amount">The health lost.</param>
    internal static bool TryHealthLoss(string text, out string source, out long amount) => FightTally.TryHealthLoss(text, out source, out amount);

    private double _attackBiggest;

    /// <summary>
    /// M6 14 the losing race (live 2026-11-15: a Tuskie Gunner's critical of 29 at 04:12:49 UTC had left the ten seconds of <see cref="RecentHits.AverageSeconds"/> when the guard held at 04:13:09): the largest hit since this attack began
    /// (hits with no quiet gap of <see cref="AttackQuietSeconds"/> between them), 0 when no attack goes on: a critical that came once can come again in the way out.
    /// </summary>
    /// <param name="now">The session clock.</param>
    public double LargestOfAttack(double now) => now - LastAttackAt < AttackQuietSeconds ? _attackBiggest : 0;

    private const int HitRing = 64;
    private readonly double[] _hitAt = [.. Enumerable.Repeat(double.NegativeInfinity, HitRing)];
    private readonly string?[] _hitWho = new string?[HitRing];
    private readonly double[] _hitDamage = new double[HitRing];
    private readonly int[] _hitLiving = new int[HitRing];
    private int _hitNext;

    /// <summary>
    /// M6 14 the losing race (the lead, 2026-11-15, live ladder27 06:03:56 UTC: a White Phyntos Wasp died and its frost bolts went on counting in the incoming rate): the living creatures named <paramref name="name"/> the session holds (in the
    /// object table, not dead). Allocation-free.
    /// </summary>
    /// <param name="name">The creature's name, as the hit notices name it.</param>
    public int Living(string name)
    {
        var count = 0;
        foreach (var o in _session.State.Objects.All)
        {
            if ((o.ItemType & CreatureItemType) != 0 && string.Equals(o.Name, name, StringComparison.Ordinal) && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord } && !_killed.Contains(o.Guid))
            {
                count++;
            }
        }

        return count;
    }

    private readonly HashSet<uint> _killed = [];
    private readonly List<uint> _killedGone = [];

    /// <summary>
    /// M6 14 the losing race: her killing blow's death line named <paramref name="guid"/> (GameEvent 0x01AD, ACE's <c>GetDeathMessage</c> to the killer, tied to her bolt's target by the vitals guard): it is dead from that line on, before
    /// its dead motion or its health's report, for <see cref="Living"/> and <see cref="IsKilled"/>. The guids the object table no longer holds are forgotten. Allocation-free after the first.
    /// </summary>
    /// <param name="guid">The creature's guid.</param>
    public void NoteKilled(uint guid)
    {
        _killedGone.Clear();
        foreach (var g in _killed)
        {
            if (!_session.State.Objects.TryGet(g, out _))
            {
                _killedGone.Add(g);
            }
        }

        foreach (var g in _killedGone)
        {
            _killed.Remove(g);
        }

        _killed.Add(guid);
    }

    /// <summary>M6 14 the losing race: true when her death line named <paramref name="guid"/> (<see cref="NoteKilled"/>).</summary>
    /// <param name="guid">The creature's guid.</param>
    public bool IsKilled(uint guid) => _killed.Contains(guid);

    private const int KillRing = 16;
    private readonly double[] _killAt = new double[KillRing];
    private readonly string[] _killText = new string[KillRing];

    /// <summary>M6 14 the losing race: the killer notices heard (GameEvent 0x01AD KillerNotification: ACE sends it to the killer only), the ring's drops counted.</summary>
    public long KillNotices { get; private set; }

    /// <summary>The killer notice of number <paramref name="number"/> (0 is the first heard): its session time and text; false when the ring has dropped it or it is not heard yet.</summary>
    /// <param name="number">The notice's number.</param>
    /// <param name="at">Its session time.</param>
    /// <param name="text">Its text ("Kilif Zefir is shattered by your assault!").</param>
    public bool KillNotice(long number, out double at, out string text)
    {
        (at, text) = (0, string.Empty);
        if (number < 0 || number >= KillNotices || KillNotices - number > KillRing)
        {
            return false;
        }

        var i = (int)(number % KillRing);
        (at, text) = (_killAt[i], _killText[i]);
        return true;
    }

    private void OnKill(in PlayerKilledSomething e)
    {
        var i = (int)(KillNotices % KillRing);
        (_killAt[i], _killText[i]) = (_session.Clock.Now, e.Text ?? string.Empty);
        KillNotices++;
    }

    /// <summary>
    /// M6 14 the losing race (live ladder45 23:07:43 UTC: a burst of 50 in 2 s read as 16.7 a second): the plain mean of the hits of the last <see cref="RecentHits.AverageSeconds"/>, the dead's share out (each hit weighed as
    /// <see cref="LivingShare"/> weighs it): the measured total of the attackers still alive. Allocation-free.
    /// </summary>
    /// <param name="now">The session clock.</param>
    public double AverageOfLiving(double now)
    {
        var living = 0.0;
        for (var i = 0; i < HitRing; i++)
        {
            if (now - _hitAt[i] > RecentHits.AverageSeconds)
            {
                continue;
            }

            living += _hitWho[i] is not { } who || _hitLiving[i] == 0 ? _hitDamage[i] : _hitDamage[i] * Math.Min(1.0, Living(who) / (double)_hitLiving[i]);
        }

        return living / RecentHits.AverageSeconds;
    }

    /// <summary>M6 14 the losing race: the plain mean of every hit of the last <see cref="RecentHits.AverageSeconds"/>, the dead's included. Allocation-free.</summary>
    /// <param name="now">The session clock.</param>
    public double AverageOfAll(double now)
    {
        var all = 0.0;
        for (var i = 0; i < HitRing; i++)
        {
            if (now - _hitAt[i] <= RecentHits.AverageSeconds)
            {
                all += _hitDamage[i];
            }
        }

        return all / RecentHits.AverageSeconds;
    }

    /// <summary>
    /// M6 14 the losing race: the share of the last <see cref="RecentHits.AverageSeconds"/> of hits that came from attackers still alive. The notices name an attacker by its kind only, so each hit is weighed by its kind's living now
    /// over its kind's living when it landed (two of three wasps alive: each of the three's hits counts two thirds; none alive: nothing; a kind the table did not hold when it hit counts whole, the safe side): the incoming rate times it
    /// (<see cref="IncomingOfLiving"/>) drops a dead attacker's share. 1 with no hit. Allocation-free.
    /// </summary>
    /// <param name="now">The session clock.</param>
    public double LivingShare(double now)
    {
        var (all, living) = (0.0, 0.0);
        for (var i = 0; i < HitRing; i++)
        {
            if (now - _hitAt[i] > RecentHits.AverageSeconds || _hitWho[i] is not { } who)
            {
                continue;
            }

            all += _hitDamage[i];
            living += _hitLiving[i] == 0 ? _hitDamage[i] : _hitDamage[i] * Math.Min(1.0, Living(who) / (double)_hitLiving[i]); // a kind the table never held when it hit cannot be told dead: it counts whole
        }

        return all > 0 ? living / all : 1.0;
    }

    private readonly Dictionary<string, double> _firstHeard = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _attackLast = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _attacks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _everHeld = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 14 the losing race (live ladder45 23:07:43 UTC: the Dark Wisps had cast at her for half a minute, most spells resisted, and read "new" on their two hits of the last ten seconds): one kind's attacks in this attack, the seconds
    /// since its first and how many, hits and attacks that took nothing alike (an evaded swing, a resisted spell, a drain of stamina or mana, a spell cast on her). A quiet of <see cref="AttackQuietSeconds"/> starts it again. False when
    /// not heard within that quiet.
    /// </summary>
    /// <param name="name">The kind's name.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="since">The seconds since its first attack in this attack.</param>
    /// <param name="attacks">Its attacks since.</param>
    public bool AttacksOf(string name, double now, out double since, out int attacks)
    {
        (since, attacks) = (0, 0);
        if (!_firstHeard.TryGetValue(name, out var first) || !_attackLast.TryGetValue(name, out var last) || now - last >= AttackQuietSeconds)
        {
            return false;
        }

        (since, attacks) = (now - first, _attacks.TryGetValue(name, out var n) ? n : 0);
        return true;
    }

    /// <summary>
    /// M6 14 the losing race (the lead, 2026-11-15, live 06:17:03 UTC: an Infected Assailer's first hits read diluted in the ten seconds' mean): one kind's hits in the last <see cref="RecentHits.AverageSeconds"/>, the dead's share out
    /// (each hit weighed by the kind's living now over its living when it landed), how many, the seconds since the kind was first heard in this attack (a quiet of <see cref="AttackQuietSeconds"/> starts it again), its living now, and
    /// whether the object table ever held one of it (a kind it never held cannot be told dead). False when it was not heard. Allocation-free.
    /// </summary>
    /// <param name="name">The kind's name.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="damage">Its hits' damage.</param>
    /// <param name="hits">Its hits.</param>
    /// <param name="since">The seconds since it was first heard in this attack.</param>
    /// <param name="living">Its living now.</param>
    /// <param name="everHeld">True when the table held one of it when it hit.</param>
    public bool KindRead(string name, double now, out double damage, out int hits, out double since, out int living, out bool everHeld)
    {
        (damage, hits, since, living, everHeld) = (0, 0, 0, Living(name), _everHeld.Contains(name));
        if (!_firstHeard.TryGetValue(name, out var first))
        {
            return false;
        }

        since = now - first;
        for (var i = 0; i < HitRing; i++)
        {
            if (now - _hitAt[i] > RecentHits.AverageSeconds || !string.Equals(_hitWho[i], name, StringComparison.Ordinal))
            {
                continue;
            }

            hits++;
            damage += _hitLiving[i] == 0 ? _hitDamage[i] : _hitDamage[i] * Math.Min(1.0, living / (double)_hitLiving[i]);
        }

        return true;
    }

    /// <summary>M6 14 the losing race: the damage a second of the attackers still alive: <see cref="IncomingPerSecond"/> times <see cref="LivingShare"/>.</summary>
    /// <param name="now">The session clock.</param>
    public double IncomingOfLiving(double now) => IncomingPerSecond(now) * LivingShare(now);

    private void NoteHit(string who, uint damage)
    {
        var now = _session.Clock.Now;
        _attackBiggest = now - LastAttackAt >= AttackQuietSeconds ? damage : Math.Max(_attackBiggest, damage);
        var alive = string.IsNullOrEmpty(who) ? 0 : Living(who);
        (_hitAt[_hitNext], _hitWho[_hitNext], _hitDamage[_hitNext], _hitLiving[_hitNext]) = (now, who, damage, alive);
        _hitNext = (_hitNext + 1) % HitRing;
        if (!string.IsNullOrEmpty(who))
        {
            NoteAttack(who);
            if (alive > 0)
            {
                _everHeld.Add(who);
            }
        }
        (LastAttackAt, LastAttacker) = (now, who);
        _hits.Note(now, damage);
        if (!string.IsNullOrEmpty(who))
        {
            _heardAt[who] = now;
        }
    }

    /// <summary>One attack of <paramref name="who"/> heard now, a hit or not: a quiet of <see cref="AttackQuietSeconds"/> since its last attack starts a new attack of the kind.</summary>
    private void NoteAttack(string who)
    {
        if (string.IsNullOrEmpty(who))
        {
            return;
        }

        var now = _session.Clock.Now;
        if (!_attackLast.TryGetValue(who, out var last) || now - last >= AttackQuietSeconds)
        {
            _firstHeard[who] = now; // a new attack of this kind
            _attacks[who] = 0;
        }

        _attackLast[who] = now;
        _attacks[who] = _attacks.TryGetValue(who, out var n) ? n + 1 : 1;
    }

    private readonly Dictionary<string, double> _heardAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _heard = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 14 retreat attackers: the names of the creatures that hit the mage within <paramref name="seconds"/> of <paramref name="now"/> (the set is the watch's own, refilled by each call: read it before the next): the attackers the fight's
    /// line from the creatures present reads (<see cref="LiveFightLine.ReadAttackers"/>).
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <param name="seconds">How recent.</param>
    public IReadOnlySet<string> HeardWithin(double now, double seconds)
    {
        _heard.Clear();
        foreach (var (name, at) in _heardAt)
        {
            if (now - at <= seconds)
            {
                _heard.Add(name);
            }
        }

        return _heard;
    }
}
