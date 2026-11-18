using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Threat;

/// <summary>Which attacks on her a query counts inside its quiet window.</summary>
public enum ThreatHeard : byte
{
    /// <summary>Only an attack that landed: a damage notice, a spell's damage, health a drain took (what the outer loop's watch calls attacked).</summary>
    Hits,

    /// <summary>Every attack heard, landed or not: also an evaded swing, a resisted spell, a drain of stamina or mana, a hostile spell cast on her (what the travel legs' watch hears).</summary>
    Attacks,
}

/// <summary>Which creatures a query counts for standing within its reach.</summary>
public enum ThreatStanding : byte
{
    /// <summary>None: only attacks count.</summary>
    None,

    /// <summary>The living creatures of the ground's target list, whether they hit her or not.</summary>
    Targets,

    /// <summary>Only the living creatures that may be attacked and whose name was heard attacking her inside the window.</summary>
    Attackers,

    /// <summary>Any living creature that may be attacked (no player), whatever it did.</summary>
    Any,
}

/// <summary>
/// One question to the threat board (<see cref="ThreatBoard.Ask"/>). Its parameters ARE the differences between today's detectors, so each detector is one query with its own numbers.
/// </summary>
/// <param name="ReachMetres">The reach, centre to centre, to the body the client draws (the asking session's <see cref="IGameSessionView.Live"/>); 0: no creature counts for standing near.</param>
/// <param name="QuietSeconds">The quiet window: an attack older than this does not count; 0: no attack counts.</param>
/// <param name="Heard">Which attacks count inside the window.</param>
/// <param name="Standing">Which creatures count for standing within the reach.</param>
public readonly record struct ThreatQuery(double ReachMetres, double QuietSeconds, ThreatHeard Heard, ThreatStanding Standing)
{
    /// <summary>The ground's target list (weenie classes) for <see cref="ThreatStanding.Targets"/>.</summary>
    public IReadOnlyList<uint>? Targets { get; init; }

    /// <summary>
    /// True when an attack inside the window answers by itself, wherever its creature stands or whether it stands at all (the outer loop's watch: "attacked 3 s ago"); it counts an attack younger than the window. False: an attack
    /// counts only through a creature of its name standing within the reach (<see cref="ThreatStanding.Attackers"/>, which counts an attack no older than the window: the two detectors' own comparisons).
    /// </summary>
    public bool AttackAlone { get; init; }

    /// <summary>Attacks heard before this session time do not count (a detector's own start: its watch hears from its construction); 0, the default: every attack the board heard.</summary>
    public double Since { get; init; }

    /// <summary>Where she stands as the asker reads her (a travel leg: her body as the client draws it), or null: her server position.</summary>
    public CellFrame? Here { get; init; }

    /// <summary>The creatures the asker has set aside, by guid, or null: none.</summary>
    public Func<uint, bool>? Skip { get; init; }

    /// <summary>
    /// M6 16 Q1: of two creatures at one distance the answer names the one met LATER in the table (the old scans took one at the distance with "at most", so the last of equals won); false, the default: the first met.
    /// The three detectors that moved onto the board (the keeper's and the fight's awake reads, the heal station's pre-check) keep the creature they named.
    /// </summary>
    public bool TiesToLast { get; init; }

    /// <summary>The outer loop's question (<c>HostileWatch.InReach</c>): a hit inside <paramref name="quietSeconds"/>, or a living creature of <paramref name="targets"/> within <paramref name="reachMetres"/>.</summary>
    /// <param name="reachMetres">The aggro reach.</param>
    /// <param name="quietSeconds">The attack quiet.</param>
    /// <param name="targets">The ground's target list.</param>
    public static ThreatQuery InReach(double reachMetres, double quietSeconds, IReadOnlyList<uint> targets) =>
        new(reachMetres, quietSeconds, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = targets, AttackAlone = true };

    /// <summary>The travel legs' question (<c>AttackWatch.HeardWithin</c> and <c>HostileReach</c>): the living creatures of a name heard attacking her inside <paramref name="quietSeconds"/> that stand within <paramref name="reachMetres"/>.</summary>
    /// <param name="reachMetres">The reach.</param>
    /// <param name="quietSeconds">How recent the attack.</param>
    /// <param name="here">Where she stands as the asker reads her, or null.</param>
    public static ThreatQuery OnHer(double reachMetres, double quietSeconds, CellFrame? here = null) =>
        new(reachMetres, quietSeconds, ThreatHeard.Attacks, ThreatStanding.Attackers) { Here = here };

    /// <summary>The rests' question (<c>AwakeNear</c>, <c>VitalsKeeper.CreatureAwakeNear</c>): any living creature that may be attacked within <paramref name="reachMetres"/>.</summary>
    /// <param name="reachMetres">The reach.</param>
    public static ThreatQuery AwakeNear(double reachMetres) => new(reachMetres, 0, ThreatHeard.Hits, ThreatStanding.Any);
}

/// <summary>What answered a <see cref="ThreatQuery"/>.</summary>
public enum ThreatAnswerKind : byte
{
    /// <summary>Nothing: no attack inside the window and no creature that counts within the reach.</summary>
    None,

    /// <summary>An attack inside the window (<see cref="ThreatQuery.AttackAlone"/>).</summary>
    Attacked,

    /// <summary>A creature that counts stands within the reach.</summary>
    Standing,
}

/// <summary>The board's answer to a <see cref="ThreatQuery"/>. A value: no allocation.</summary>
/// <param name="Kind">What answered.</param>
/// <param name="Guid">The nearest creature that counts within the reach, or 0 (also 0 for <see cref="ThreatAnswerKind.Attacked"/>: the notices name no guid).</param>
/// <param name="Name">The attacker's name (<see cref="ThreatAnswerKind.Attacked"/>) or the nearest creature's, or empty.</param>
/// <param name="Metres">The nearest creature's distance, or NaN.</param>
/// <param name="At">The session time of the attack that answers, or of the last attack heard from the nearest creature's name; negative infinity when none was heard.</param>
/// <param name="Count">How many creatures that count stand within the reach (0 for <see cref="ThreatAnswerKind.Attacked"/>: the bodies are not read then).</param>
public readonly record struct ThreatAnswer(ThreatAnswerKind Kind, uint Guid, string Name, double Metres, double At, int Count)
{
    /// <summary>True when anything answered.</summary>
    public bool Any => Kind != ThreatAnswerKind.None;
}

/// <summary>M6 16 E3.0: one creature that counts for a <see cref="ThreatQuery"/> within its reach (<see cref="ThreatBoard.Gather"/>). A value: no allocation.</summary>
/// <param name="Guid">Its guid.</param>
/// <param name="Name">Its name.</param>
/// <param name="Wcid">Its weenie class.</param>
/// <param name="Metres">How far it stands from her, to where the asking session draws it.</param>
/// <param name="HeardAt">The session time of the last attack heard from its name inside the query's window, or negative infinity (a query that does not ask who was heard).</param>
public readonly record struct ThreatStander(uint Guid, string Name, uint Wcid, double Metres, double HeardAt);

/// <summary>The board's answer to "is she in a lull" (<see cref="ThreatBoard.Lull"/>).</summary>
/// <param name="Quiet">True when no attack that counts came from anyone inside the window.</param>
/// <param name="LastBy">Who attacked last (as the notice named it), or empty when none was heard this session.</param>
/// <param name="LastAt">The session time of that attack, or negative infinity.</param>
public readonly record struct ThreatLull(bool Quiet, string LastBy, double LastAt);

/// <summary>The hits one name (or everyone) landed inside a window (<see cref="ThreatBoard.Window"/>).</summary>
/// <param name="Hits">How many.</param>
/// <param name="Damage">Their damage.</param>
/// <param name="Biggest">The largest.</param>
public readonly record struct ThreatWindow(int Hits, double Damage, double Biggest);

/// <summary>
/// One creature kind that attacked her, as the notices name it (a name: the damage and evasion notices and the spell lines carry no guid). Kept by the <see cref="ThreatBoard"/> for the session.
/// </summary>
public sealed class ThreatAttacker
{
    internal ThreatAttacker(string name, double now)
    {
        Name = name;
        FirstAt = now;
    }

    /// <summary>Its name, as the notices name it.</summary>
    public string Name { get; }

    /// <summary>Its weenie class, or 0 while unread: a living creature of the name seen in the object table when it attacked, else the bestiary's one kind of that name (<see cref="ThreatBoard.WcidOf"/>).</summary>
    public uint Wcid { get; internal set; }

    /// <summary>Where <see cref="Wcid"/> comes from: <c>seen</c> (the object table), <c>bestiary</c> (the one kind of the name), <c>unread</c>, or why it is unknown.</summary>
    public string WcidSource { get; internal set; } = "unread";

    /// <summary>
    /// The guid of the last creature of this name an event named: the server's swing order aimed at her (ACE <c>Monster_Melee.cs</c> <c>DoSwingMotion</c>: a motion with StickToObject and her guid as its target). 0 when no event
    /// named one, or when that creature died or left the object table since.
    /// </summary>
    public uint Guid { get; internal set; }

    /// <summary>The session time <see cref="Guid"/> was named at, or negative infinity.</summary>
    public double GuidAt { get; internal set; } = double.NegativeInfinity;

    /// <summary>The session time it was first heard at this session.</summary>
    public double FirstAt { get; }

    /// <summary>The session time of its last hit (health taken), or negative infinity.</summary>
    public double LastHitAt { get; internal set; } = double.NegativeInfinity;

    /// <summary>The session time of its last attack, landed or not, or negative infinity (a kind known only by a swing order or an enchantment).</summary>
    public double LastAttackAt { get; internal set; } = double.NegativeInfinity;

    /// <summary>How its last attack was heard (<c>damage notice</c>, <c>evasion notice</c>, <c>spell damage</c>, ...), or empty.</summary>
    public string LastHow { get; internal set; } = string.Empty;

    /// <summary>Its hits this session.</summary>
    public int Hits { get; internal set; }

    /// <summary>Its attacks this session, hits included.</summary>
    public int Attacks { get; internal set; }

    /// <summary>The health its hits took this session.</summary>
    public double Damage { get; internal set; }

    /// <summary>Its largest hit this session.</summary>
    public double BiggestHit { get; internal set; }

    /// <summary>The swing orders of its creatures that named her.</summary>
    public int Swings { get; internal set; }

    /// <summary>The enchantments it cast on her (no attack by themselves).</summary>
    public int Enchants { get; internal set; }
}

/// <summary>
/// M6 16 E2a (docs/automation-engine.md section 2.2, m6-plan.md D139): the threat board, one picture a session of who attacks her and who is near. Today that question is answered by eight detectors, each with its own reach, its own
/// quiet window, its own attacker list and its own reading of the damage lines, and most of them die with the scenario that made them (a scenario is rebuilt at every rung move). The board hangs off the session's state
/// (<see cref="For"/>, the pattern of <c>PackRoomMemory</c>), hears the event bus once, and answers queries whose parameters are the detectors' differences (<see cref="ThreatQuery"/>). In this slice it decides nothing: each detector
/// decides as before, and where it is asked the board is asked the same question beside it (<see cref="ThreatShadowLane"/>).
/// </summary>
/// <remarks>
/// <para><b>The feed</b> (one subscription each, made once): the defence notices (GameEvent 0x01B2 and 0x01B4, a <see cref="CombatEvent"/>), the damage lines (<see cref="ChatReceived"/>, read by <see cref="ThreatLines"/>), the
/// swing orders (<see cref="ObjectMotionChanged"/> with her guid as the object stuck to: the one event that names an attacker's guid), a creature's Dead motion and <see cref="ObjectDeleted"/> (a named guid is forgotten), her death
/// (<see cref="PlayerDied"/>) and her kills (<see cref="PlayerKilledSomething"/>). It adds a handler to each channel and changes nothing for the handlers already there (the bus dispatches in subscription order and catches a handler
/// that throws).</para>
/// <para><b>What it holds.</b> For each name that attacked her (<see cref="ThreatAttacker"/>): its weenie class, the guid a swing order named, the times of its last hit and last attack, its hits, attacks and damage; a ring of the last
/// <see cref="HitRing"/> hits for windows (<see cref="Window"/>); the last hit and the last attack from anyone; her last death and kill. A swing order and an enchantment are kept apart from the attacks: no detector hears them as
/// attacks today, so no query counts them.</para>
/// <para><b>Positions</b> are the one world picture: a creature stands where the asking session's <see cref="IGameSessionView.Live"/> says (the body the client draws when it can be trusted, else the server's report).</para>
/// <para><b>Allocation.</b> Game thread only. A query allocates nothing. The feed allocates nothing for a defence notice of a name already held, a swing order, a removal, a death; a new name allocates its entry once; a damage line
/// allocates what its classification allocates (<see cref="ThreatLines"/>).</para>
/// <para><b>The creatures</b> (M6 16 Eperf). A query that reads bodies walked the whole object table, and a frame of a fight asks up to thirteen (the outer loop's lane and twelve more): 80 microseconds a frame in a room of
/// 237 objects with 30 creatures (EngineFrameCostTests). The board keeps the table's creatures (every object whose description has the creature bit), in the table's own order, and draws the list again when the table says
/// its objects changed (<see cref="WorldObjectTable.Version"/>: an object added, replaced, removed or described anew), so no query is answered from a list the table has left behind; every query still reads each creature's
/// place, health, motion and name from the object itself, as before.</para>
/// </remarks>
public sealed class ThreatBoard
{
    /// <summary>The hits the ring keeps for <see cref="Window"/>.</summary>
    public const int HitRing = 256;

    /// <summary>The names the board keeps at most; past it the one heard longest ago is forgotten.</summary>
    public const int MaxAttackers = 128;

    private const uint CreatureItemType = 0x10; // ACE ItemType.Creature
    private const uint AttackableFlag = 0x10; // ObjectDescriptionFlag.Attackable

    private static readonly ConditionalWeakTable<GameState, ThreatBoard> PerSession = new();

    private readonly IGameSessionView _session;
    private readonly Dictionary<string, ThreatAttacker> _byName = new(StringComparer.Ordinal);
    private readonly List<ThreatAttacker> _attackers = [];
    private readonly Dictionary<string, ThreatShadowLane> _lanes = new(StringComparer.Ordinal);
    private readonly double[] _hitAt = new double[HitRing];
    private readonly ThreatAttacker?[] _hitBy = new ThreatAttacker?[HitRing];
    private readonly double[] _hitDamage = new double[HitRing];
    private int _hitNext;
    private readonly List<WorldObject> _creatures = new(64);
    private WorldObjectTable? _creaturesOf;
    private int _creaturesVersion;

    private ThreatBoard(IGameSessionView session)
    {
        _session = session;
        HearingSince = session.Clock.Now;
        Health = HealthPicture.For(session); // M6 16 E2d: the one picture of a target's health begins hearing with the board
        Array.Fill(_hitAt, double.NegativeInfinity);
        var bus = session.Events; // the subscriptions live as long as the session: the board is the session's
        bus.Subscribe<CombatEvent>(OnCombat);
        bus.Subscribe<ChatReceived>(OnChat);
        bus.Subscribe<ObjectMotionChanged>(OnMotion);
        bus.Subscribe<ObjectDeleted>(OnDeleted);
        bus.Subscribe<PlayerDied>(OnDied);
        bus.Subscribe<PlayerKilledSomething>(OnKill);
    }

    /// <summary>The board of a session, made (and its feed begun) on first use.</summary>
    /// <param name="session">The session.</param>
    public static ThreatBoard For(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        if (!PerSession.TryGetValue(state, out var board))
        {
            board = new ThreatBoard(session);
            PerSession.Add(state, board);
        }

        return board;
    }

    /// <summary>The session time the board began hearing at: an attack before it is not on the board.</summary>
    public double HearingSince { get; }

    /// <summary>M6 16 E2d: the session's one picture of a target's health (<see cref="HealthPicture"/>), made with the board. M6 16 Q5: the vitals guard and the travel legs read it; the fight's readers stand beside it in shadow.</summary>
    public HealthPicture Health { get; }

    /// <summary>The session time of the last hit from anyone (health taken, or a damage notice), or negative infinity.</summary>
    public double LastHitAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Who hit her last, as the notice named it, or empty.</summary>
    public string LastHitBy { get; private set; } = string.Empty;

    /// <summary>The session time of the last attack from anyone, landed or not, or negative infinity.</summary>
    public double LastAttackAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Who attacked her last, or empty.</summary>
    public string LastAttackBy { get; private set; } = string.Empty;

    /// <summary>The hits heard this session.</summary>
    public long Hits { get; private set; }

    /// <summary>The attacks heard this session, hits included.</summary>
    public long Attacks { get; private set; }

    /// <summary>Her deaths heard this session.</summary>
    public int Deaths { get; private set; }

    /// <summary>The session time of her last death, or negative infinity.</summary>
    public double LastDeathAt { get; private set; } = double.NegativeInfinity;

    /// <summary>The guid her last death's notice named as the killer, or 0.</summary>
    public uint LastKiller { get; private set; }

    /// <summary>Her kills heard this session (GameEvent 0x01AD).</summary>
    public long Kills { get; private set; }

    /// <summary>The session time of her last kill, or negative infinity.</summary>
    public double LastKillAt { get; private set; } = double.NegativeInfinity;

    /// <summary>The text of her last kill's notice, or empty.</summary>
    public string LastKillText { get; private set; } = string.Empty;

    /// <summary>The names held.</summary>
    public int AttackerCount => _attackers.Count;

    /// <summary>The name of number <paramref name="index"/> (in the order first heard).</summary>
    /// <param name="index">Its number.</param>
    public ThreatAttacker AttackerAt(int index) => _attackers[index];

    /// <summary>The kind named <paramref name="name"/>, or null when it was never heard.</summary>
    /// <param name="name">The name, as the notices name it.</param>
    public ThreatAttacker? Find(string name) => name is not null && _byName.TryGetValue(name, out var found) ? found : null;

    /// <summary>
    /// The weenie class of the kind named <paramref name="name"/>: the one read when it attacked (a living creature of the name in the object table), else one in the table now, else the bestiary's name read
    /// (<see cref="Bestiary.WcidNamed"/>: the one kind of that name). 0 when the name was never heard or no source knows it; <see cref="ThreatAttacker.WcidSource"/> says which.
    /// </summary>
    /// <param name="name">The name.</param>
    public uint WcidOf(string name)
    {
        if (Find(name) is not { } who)
        {
            return 0;
        }

        if (who.Wcid == 0 && !Seen(who))
        {
            if (Bestiary.WcidNamed(name, out var kinds) is { } wcid)
            {
                (who.Wcid, who.WcidSource) = (wcid, "bestiary");
            }
            else
            {
                who.WcidSource = kinds == 0 ? "unknown: no creature of the name is in view and the bestiary holds none" : "unknown: no creature of the name is in view and the bestiary holds several kinds of it";
            }
        }

        return who.Wcid;
    }

    /// <summary>The hits of <paramref name="name"/> (null: of anyone) inside the last <paramref name="seconds"/>, from the ring. Allocation-free.</summary>
    /// <param name="name">The name, or null for everyone.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="seconds">The window.</param>
    public ThreatWindow Window(string? name, double now, double seconds)
    {
        ThreatAttacker? who = null;
        if (name is not null && !_byName.TryGetValue(name, out who))
        {
            return default;
        }

        var (hits, damage, biggest) = (0, 0.0, 0.0);
        for (var i = 0; i < HitRing; i++)
        {
            if (now - _hitAt[i] > seconds || (who is not null && !ReferenceEquals(_hitBy[i], who)))
            {
                continue;
            }

            hits++;
            damage += _hitDamage[i];
            biggest = Math.Max(biggest, _hitDamage[i]);
        }

        return new ThreatWindow(hits, damage, biggest);
    }

    /// <summary>
    /// The one definition of a lull the plan records (the owner, 2026-11-15): no hit from anyone inside the window, the last one named so the claim can be checked. <paramref name="heard"/> says what breaks it:
    /// <see cref="ThreatHeard.Hits"/> (the default: health taken or a damage notice) or <see cref="ThreatHeard.Attacks"/> (also a swing she evaded, a spell she resisted). Where she stands and who stands near her are not read: a
    /// lull is about hits. Allocation-free.
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <param name="quietSeconds">The window.</param>
    /// <param name="heard">Which attacks break the lull.</param>
    public ThreatLull Lull(double now, double quietSeconds, ThreatHeard heard = ThreatHeard.Hits)
    {
        var (at, by) = heard == ThreatHeard.Hits ? (LastHitAt, LastHitBy) : (LastAttackAt, LastAttackBy);
        return new ThreatLull(!(now - at < quietSeconds), by, at);
    }

    /// <summary>
    /// Answers <paramref name="query"/> at <paramref name="now"/>: an attack inside the window when the query lets one answer alone, else the nearest creature that counts within the reach, measured from where she stands to where
    /// <paramref name="session"/> draws it. Allocation-free.
    /// </summary>
    /// <param name="session">The asking session (its objects, its live positions, her position).</param>
    /// <param name="query">The question.</param>
    /// <param name="now">The session clock.</param>
    public ThreatAnswer Ask(IGameSessionView session, in ThreatQuery query, double now) => Scan(session, in query, now, null);

    /// <summary>
    /// M6 16 E3.0: <see cref="Ask"/>, with every creature that counts within the reach written to <paramref name="into"/> (cleared first), in the object table's order: the board's one list of who stands on her for a
    /// question, where each caller walked the objects itself (docs/automation-engine.md, <c>TODO(engine-fullest-gather)</c>). The answer is <see cref="Ask"/>'s for the same query; a query an attack answers alone
    /// (<see cref="ThreatQuery.AttackAlone"/>) reads no body and leaves the list empty. Allocation-free once the list has its capacity.
    /// </summary>
    /// <param name="session">The asking session.</param>
    /// <param name="query">The question.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="into">Where the creatures go.</param>
    public ThreatAnswer Gather(IGameSessionView session, in ThreatQuery query, double now, List<ThreatStander> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        return Scan(session, in query, now, into);
    }

    private ThreatAnswer Scan(IGameSessionView session, in ThreatQuery query, double now, List<ThreatStander>? into)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (query.AttackAlone && query.QuietSeconds > 0)
        {
            var (at, by) = query.Heard == ThreatHeard.Hits ? (LastHitAt, LastHitBy) : (LastAttackAt, LastAttackBy);
            if (at >= query.Since && now - at < query.QuietSeconds)
            {
                return new ThreatAnswer(ThreatAnswerKind.Attacked, 0, by, double.NaN, at, 0);
            }
        }

        var none = new ThreatAnswer(ThreatAnswerKind.None, 0, string.Empty, double.NaN, double.NegativeInfinity, 0);
        var state = session.State;
        if (query.Standing == ThreatStanding.None || query.ReachMetres <= 0 || (query.Here ?? state.Self.Position) is not { } here)
        {
            return none;
        }

        if (query.Standing == ThreatStanding.Attackers && !AnyHeard(in query, now))
        {
            return none; // nobody was heard inside the window: no body is read
        }

        var origin = here.ToGlobal();
        var live = session.Live;
        var self = state.Self.Guid;
        WorldObject? nearest = null;
        var (metres, heardAt, count) = (double.MaxValue, double.NegativeInfinity, 0);
        var creatures = Creatures(state.Objects); // M6 16 Eperf: the table's creatures in the table's order, not every object of it
        for (var i = 0; i < creatures.Count; i++)
        {
            var o = creatures[i];
            if (o.Position is null || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
            {
                continue; // not a living creature in the world
            }

            var at = double.NegativeInfinity;
            if (query.Standing == ThreatStanding.Targets)
            {
                if (!Listed(query.Targets, o.Weenie.WeenieClassId))
                {
                    continue;
                }
            }
            else if (o.IsSelf || o.Guid == self || (o.DescriptionFlags & AttackableFlag) == 0 || (o.DescriptionFlags & WorldObjectTable.PlayerFlag) != 0
                || (query.Standing == ThreatStanding.Attackers && !HeardInside(in query, o.Name, now, out at)))
            {
                continue; // not one that may be attacked, or not heard attacking inside the window
            }

            if ((query.Skip is { } skip && skip(o.Guid)) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d > query.ReachMetres)
            {
                continue;
            }

            count++;
            into?.Add(new ThreatStander(o.Guid, o.Name ?? "creature", o.Weenie.WeenieClassId, d, at));
            if (d < metres || (query.TiesToLast && d == metres))
            {
                (nearest, metres, heardAt) = (o, d, at);
            }
        }

        return nearest is null ? none : new ThreatAnswer(ThreatAnswerKind.Standing, nearest.Guid, nearest.Name ?? "creature", metres, heardAt, count);
    }

    /// <summary>The shadow lane of <paramref name="detector"/>: its counts and its last pair, kept for the session so they outlive the scenario that asks (made on first use).</summary>
    /// <param name="detector">The detector's word in the shadow's lines (<c>outer-loop</c>, <c>travel-leg</c>).</param>
    public ThreatShadowLane Lane(string detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        if (!_lanes.TryGetValue(detector, out var lane))
        {
            _lanes[detector] = lane = new ThreatShadowLane(detector);
        }

        return lane;
    }

    /// <summary>
    /// M6 16 Eperf: the creatures of <paramref name="objects"/>: every object whose description has the creature bit, in the table's own order (so the nearest of two at one distance is the one a walk of the table
    /// meets first, as before). Drawn again when the table is another one or says its objects changed (<see cref="WorldObjectTable.Version"/>); allocates only when the list grows past what it held.
    /// </summary>
    private List<WorldObject> Creatures(WorldObjectTable objects)
    {
        if (ReferenceEquals(objects, _creaturesOf) && objects.Version == _creaturesVersion)
        {
            return _creatures;
        }

        _creatures.Clear();
        foreach (var o in objects.All)
        {
            if ((o.ItemType & CreatureItemType) != 0)
            {
                _creatures.Add(o);
            }
        }

        (_creaturesOf, _creaturesVersion) = (objects, objects.Version);
        return _creatures;
    }

    private bool AnyHeard(in ThreatQuery query, double now)
    {
        var at = query.Heard == ThreatHeard.Hits ? LastHitAt : LastAttackAt;
        return at >= query.Since && now - at <= query.QuietSeconds; // the newest of all: when it is outside the window, every name is
    }

    private bool HeardInside(in ThreatQuery query, string? name, double now, out double at)
    {
        at = double.NegativeInfinity;
        if (name is null || !_byName.TryGetValue(name, out var who))
        {
            return false;
        }

        at = query.Heard == ThreatHeard.Hits ? who.LastHitAt : who.LastAttackAt;
        return at >= query.Since && now - at <= query.QuietSeconds;
    }

    private static bool Listed(IReadOnlyList<uint>? targets, uint wcid)
    {
        if (targets is null)
        {
            return false;
        }

        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i] == wcid)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------------ the feed

    private void OnCombat(in CombatEvent e)
    {
        var notice = e.Kind switch { CombatEventKind.DefenseHit => 1, CombatEventKind.DefenseEvaded => 2, _ => 0 }; // the board's one read of the defence notices (the one line the ThreatDetectors ratchet counts here)
        if (notice == 1)
        {
            NoteHit(e.Who, e.Damage, "damage notice");
        }
        else if (notice == 2)
        {
            NoteAttack(e.Who, "evasion notice"); // an evaded swing is an attack heard, no hit
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (!ThreatLines.TryRead(e.Line, out var line))
        {
            return;
        }

        switch (line.Kind)
        {
            case ThreatLineKind.Hit:
                NoteHit(line.Source, line.Amount, line.How);
                break;
            case ThreatLineKind.Attack:
                NoteAttack(line.Source, line.How);
                break;
            default:
                if (_byName.TryGetValue(line.Source, out var known))
                {
                    known.Enchants++; // an enchantment is no attack: counted for a kind already held, never a new one (ACE writes the line for a buff from a friend too)
                }

                break;
        }
    }

    /// <summary>A swing order that names her: the creature's own guid, the one event that gives one (the notices that follow name it by its name alone).</summary>
    private void OnMotion(in ObjectMotionChanged e)
    {
        if (e.IsSelf || e.Motion is not { } motion)
        {
            return;
        }

        if (motion.Forward == WorldObjectTable.DeadMotionLowWord)
        {
            Forget(e.Guid); // it died: its guid names no attacker any more
            return;
        }

        var state = _session.State;
        if (motion.StickyGuid is not { } stuck || stuck == 0 || stuck != state.Self.Guid || !state.Objects.TryGet(e.Guid, out var o) || (o.ItemType & CreatureItemType) == 0 || string.IsNullOrEmpty(o.Name))
        {
            return;
        }

        var now = _session.Clock.Now;
        var who = Entry(o.Name, now);
        (who.Guid, who.GuidAt) = (e.Guid, now);
        who.Swings++;
        if (who.Wcid == 0)
        {
            (who.Wcid, who.WcidSource) = (o.Weenie.WeenieClassId, "seen");
        }
    }

    private void OnDeleted(in ObjectDeleted e) => Forget(e.Guid);

    private void OnDied(in PlayerDied e)
    {
        Deaths++;
        (LastDeathAt, LastKiller) = (_session.Clock.Now, e.Killer);
    }

    private void OnKill(in PlayerKilledSomething e)
    {
        Kills++;
        (LastKillAt, LastKillText) = (_session.Clock.Now, e.Text ?? string.Empty);
    }

    private void Forget(uint guid)
    {
        if (guid == 0)
        {
            return;
        }

        for (var i = 0; i < _attackers.Count; i++)
        {
            if (_attackers[i].Guid == guid)
            {
                _attackers[i].Guid = 0;
            }
        }
    }

    private void NoteHit(string? by, uint damage, string how)
    {
        var now = _session.Clock.Now;
        var name = by ?? string.Empty;
        Hits++;
        Attacks++;
        (LastHitAt, LastHitBy, LastAttackAt, LastAttackBy) = (now, name, now, name);
        ThreatAttacker? who = null;
        if (name.Length > 0)
        {
            who = Entry(name, now);
            (who.LastHitAt, who.LastAttackAt, who.LastHow) = (now, now, how);
            who.Hits++;
            who.Attacks++;
            who.Damage += damage;
            who.BiggestHit = Math.Max(who.BiggestHit, damage);
        }

        (_hitAt[_hitNext], _hitBy[_hitNext], _hitDamage[_hitNext]) = (now, who, damage);
        _hitNext = (_hitNext + 1) % HitRing;
    }

    private void NoteAttack(string? by, string how)
    {
        if (string.IsNullOrEmpty(by))
        {
            return; // an attack that took nothing from an unnamed attacker: nothing to hold (the detectors drop it too)
        }

        var now = _session.Clock.Now;
        Attacks++;
        (LastAttackAt, LastAttackBy) = (now, by);
        var who = Entry(by, now);
        (who.LastAttackAt, who.LastHow) = (now, how);
        who.Attacks++;
    }

    private ThreatAttacker Entry(string name, double now)
    {
        if (_byName.TryGetValue(name, out var who))
        {
            if (who.Wcid == 0)
            {
                Seen(who);
            }

            return who;
        }

        if (_attackers.Count >= MaxAttackers)
        {
            var oldest = 0;
            for (var i = 1; i < _attackers.Count; i++)
            {
                if (Math.Max(_attackers[i].LastAttackAt, _attackers[i].GuidAt) < Math.Max(_attackers[oldest].LastAttackAt, _attackers[oldest].GuidAt))
                {
                    oldest = i;
                }
            }

            _byName.Remove(_attackers[oldest].Name);
            _attackers.RemoveAt(oldest); // its hits stay in the ring for the windows of everyone
        }

        who = new ThreatAttacker(name, now);
        _byName[name] = who;
        _attackers.Add(who);
        Seen(who);
        return who;
    }

    /// <summary>Reads the weenie class of <paramref name="who"/> from a living creature of its name in the object table; false when none is in view.</summary>
    private bool Seen(ThreatAttacker who)
    {
        var creatures = Creatures(_session.State.Objects);
        for (var i = 0; i < creatures.Count; i++)
        {
            var o = creatures[i];
            if ((o.ItemType & CreatureItemType) != 0 && o.Health is not <= 0f && string.Equals(o.Name, who.Name, StringComparison.Ordinal))
            {
                (who.Wcid, who.WcidSource) = (o.Weenie.WeenieClassId, "seen");
                return true;
            }
        }

        return false;
    }
}
