using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 item 7 F4, self-defence: the fight fights back, as a player does. A living creature that attacks the player becomes the fight's target at once, before the profile's targets, whatever the profile says of it (avoided,
/// no-go, outside the level band), until it is dead, gone from the object table, or has not attacked for <see cref="FightOptions.DefendForgetSeconds"/>; then the profile's targets resume. A rule of the fight for any ground
/// (D46): nothing here names a dungeon, a creature or a cell; the no-go cells are the ground profile's.
/// </summary>
/// <remarks>
/// <para><b>How the fight hears an attack.</b> ACE tells the defender three ways, all on the session bus: GameEvent 0x01B2 DefenderNotification and 0x01B4 EvasionDefenderNotification (published as a <see cref="CombatEvent"/>
/// with <see cref="CombatEventKind.DefenseHit"/> or <see cref="CombatEventKind.DefenseEvaded"/>, naming the attacker; <c>Creature_Combat.cs</c> and <c>Player_Combat.cs</c> of ACE @95ef426d), and the Magic-typed chat lines a
/// creature's spell writes to its target (<c>SpellProjectile.cs</c> 810-845: "&lt;source&gt; &lt;verb&gt; you for &lt;n&gt; points with &lt;spell&gt;." and "&lt;source&gt; casts &lt;spell&gt; and drains ...";
/// <c>WorldObject_Magic.cs</c> 199: "You resist the spell cast by &lt;source&gt;"; 1429: "&lt;source&gt; casts &lt;spell&gt; on you"), published as <see cref="ChatReceived"/>. ACE also sets the player's CurrentAttacker
/// (a private update carrying the attacker's guid, not decoded: <c>TODO(m3-autotarget-attacker-id)</c>), so every notice names the attacker, never gives its guid: the attacker is the nearest living creature of that name in
/// the object table, as the auto-target resolves it (<see cref="WorldObjectTable.NearestLivingCreatureNamed"/>).</para>
/// <para><b>Walking.</b> The fight walks only when no attacker can be cast at from where the player stands (out of range or no clear line, the same line rule as the pick), by the walk it already has. M6 6 B2
/// (<c>m5-defend-walk-to</c>): the walk goes toward the nearest such attacker: a leg (<see cref="IFightWalk.BeginLegTo"/>) to the spot nearest the mage on the straight line to it (<see cref="IFightWalk.CellsOnLine"/>, the floor's
/// room middles in those cells, <see cref="IFightWalk.FloorSpots"/>) that is within the cast range of it with a clear line, at most <see cref="MaxDefenceWalksTo"/> legs an attacker. A walk that cannot trace the line or offers no
/// spots (a named refusal, <c>no-planner:&lt;walk&gt;</c>), no such spot, a leg that cannot be planned, or the bound spent fall back to today's walk, the tour's next leg, each named. Either walk is dropped the moment an attacker
/// is in range with a clear line. An attacker in a no-go cell is cast at from where the player stands when a line exists, else the fight holds (nothing is walked into a no-go cell) and logs it once.</para>
/// <para><b>M5 7 P7s (D148, D88 meets the no-go map; <c>m5-8-no-go-attacker</c>).</b> The long run's 19:42 UTC: a Virindi Servant shot from inside the den's no-go cell 0x01050244 and the fight held still with no line. Now, before
/// it holds, the fight steps: a short leg (<see cref="IFightWalk.BeginLegTo"/>, over the walkable cells only) to the nearest room middle of the floor (<see cref="IFightWalk.FloorSpots"/>) within <see cref="DefendStepMaxMetres"/>
/// that has a clear line to the attacker within the cast range, else to the nearest one out of its reach. At most <see cref="MaxDefenceSteps"/> steps an attacker, each decision logged; past them, or with no such spot, the hold
/// and its bound stand as before.</para>
/// <para><b>M6 14 (<c>m6-life-through</c>, the owner's rule of 2026-11-12).</b> Before the tour's walk, the step and the hold: an attacker no bolt reaches from here that stands within the life spells' range is cast at where
/// she stands, with Drain Health Other or Harm Other (<see cref="DefenceMove.Life"/>; <c>FightScenario.LifeThrough.cs</c>): ACE asks no line of a spell with no projectile.</para>
/// <para><b>M6 fight targeting (run 9, 2026-11-20 19:51 to 2026-11-21 04:58 UTC; docs/session.md section 120).</b> Who struck: a damage or evasion notice with no swing order is pinned only on a creature that can have
/// struck her (<see cref="BlowCanBeFrom"/>, <c>m6-blow-unpinned</c>), never on a far one by its name. What first: an attacker with no shot from here is moved to before any life spell, a sidestep and then a walk to a spot
/// with a line (<see cref="DefenceLineSpot"/>, <c>m6-line-first</c>), at most <see cref="MaxDefenceWalksTo"/> searches; the life spell is cast when neither finds a spot, the leg to it cannot be planned, or the searches
/// are spent (the owner's rule of 2026-11-16: a creature no move reaches is fought with Life Magic).</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>What the pick does about the creatures that attacked the player.</summary>
    private enum DefenceMove
    {
        /// <summary>No live attacker: the profile's targets.</summary>
        None,

        /// <summary>An attacker is in range with a clear line: it is the target.</summary>
        Cast,

        /// <summary>Attackers stand, but none can be cast at from here and the nearest is not in a no-go cell: the fight walks.</summary>
        Walk,

        /// <summary>The nearest attacker that cannot be cast at stands in a no-go cell: the player stays where it is.</summary>
        Hold,

        /// <summary>M5 7 P7s (D148): that attacker stands in a no-go cell with no line from here: the player steps to a spot with a line to it, or out of its reach.</summary>
        Step,

        /// <summary>
        /// M6 14 (<c>m6-life-through</c>): no attacker can be cast at with a bolt from here, and the nearest stands within the life spells' range with a spell to cast at it: it is fought through the obstacle, in place
        /// of the walk, the step and the hold (<c>FightScenario.LifeThrough.cs</c>).
        /// </summary>
        Life,
    }

    /// <summary>M5 7 P7s (D148): the steps the fight takes for one attacker in a no-go cell before it holds (Lifestone's own bound: a step that found no line, and one more from there).</summary>
    public const int MaxDefenceSteps = 2;

    /// <summary>M5 7 P7s (D148): the farthest, straight, a step's spot may lie from the mage (Lifestone's own: a step, not a tour leg; about 4 s at the den's 10.4 m/s).</summary>
    public const double DefendStepMaxMetres = 40;

    /// <summary>M5 7 P7s (D148): the most spots whose line to the attacker one step decision sweeps, nearest first (Lifestone's own bound on the sweeps a decision costs).</summary>
    public const int DefendStepLineTests = 24;

    /// <summary>
    /// M6 6 B2 (<c>m5-defend-walk-to</c>): the legs the fight walks toward one attacker out of range or line before it falls back to the tour's next leg (Lifestone's own bound, as <see cref="MaxDefenceSteps"/>: a leg that did not
    /// bring a line, and one more from there).
    /// </summary>
    public const int MaxDefenceWalksTo = 2;

    /// <summary>How a melee or missile blow that landed is heard (<see cref="Attacker.Heard"/>).</summary>
    private const string HeardDamage = "damage notice";

    /// <summary>How a melee or missile blow that was evaded is heard (<see cref="Attacker.Heard"/>).</summary>
    private const string HeardEvasion = "evasion notice";

    /// <summary>
    /// M6 14 (the struck proof): how long after a creature's blow it is taken to stand at the mage whatever the line test says (Lifestone's own: a Tusker Crimsonback's blows came 4 s apart in the Cave's log of 2026-11-12, and
    /// one blow missed in between keeps the proof).
    /// </summary>
    public const double StruckProofSeconds = 6;

    /// <summary>
    /// M6 14 (the struck proof): the farthest its last known position may lie from the mage when a blow first proves a creature is at her (Lifestone's own: a melee blow lands from the two bodies' radii plus ACE's 0.75 m,
    /// <c>Monster_Navigation.cs:28,189</c>, and the known position is as old as her last steps; an archer's arrow from farther off proves nothing about where it stands).
    /// </summary>
    public const double StruckProofMetres = 8;

    /// <summary>A creature that attacked the player, and when it last did.</summary>
    private sealed class Attacker(uint guid, string name, double lastAt, string heard)
    {
        /// <summary>M6 14: the session clock of its last melee or missile blow (landed or evaded); NaN: none yet.</summary>
        public double StruckAt { get; set; } = double.NaN;

        /// <summary>M6 14: true while its blows prove it stands at the mage: a blow came with its known position within <see cref="StruckProofMetres"/>, and the blows since came no more than <see cref="StruckProofSeconds"/> apart.</summary>
        public bool StruckClose { get; set; }

        /// <summary>M6 14: true once the struck proof was logged for this attacker.</summary>
        public bool StruckLogged { get; set; }

        /// <summary>M6 14 (the swing order): the session clock of the last swing order of this creature's that named the mage; NaN: none yet.</summary>
        public double SwingAt { get; set; } = double.NaN;

        /// <summary>M6 14 (<c>m6-live-position</c>): the session clock of the last far-blow line for this attacker (the measure's rate).</summary>
        public double FarBlowLoggedAt { get; set; } = double.NegativeInfinity;

        /// <summary>M6 14: true once the proof's standing down for the shot rule's drop was logged for this attacker.</summary>
        public bool StruckVoidLogged { get; set; }

        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public double LastAt { get; set; } = lastAt;

        /// <summary>M6 6 B2 (<c>m5-attacker-reach</c>): how the last attack was heard (a damage or evasion notice: melee or missile; a spell line: magic), the input a reach read would key on.</summary>
        public string Heard { get; set; } = heard;

        /// <summary>
        /// M6 6 B2 (<c>m5-attacker-reach</c>): how far the creature attacks from, in metres; NaN while unread, and <see cref="ReachOf"/> stands the mage's cast range in for it. ACE works it out per attack on the server
        /// (<c>Monster_Combat.cs:165-215</c> @95ef426d, <c>GetMaxRange</c>: a spell's <c>BaseRangeConstant + skill * BaseRangeMod</c> capped at the outdoor radar range, <c>Monster_Magic.cs:113-124</c>; a missile weapon's
        /// <c>MaximumVelocity^2 * 0.102</c> capped at 77.7 m, <c>Creature_Missile.cs:406-425</c>; melee <c>MaxMeleeRange</c> 0.75 m, <c>Monster_Navigation.cs:28</c>); the client sees neither the creature's spell nor its skill nor its
        /// launcher, so nothing sets it yet (<c>TODO(m5-attacker-reach)</c>).
        /// </summary>
        public double ReachMetres { get; set; } = double.NaN;

        /// <summary>M6 6 B2 (<c>m5-defend-walk-to</c>): the legs walked (or tried) toward this attacker (at most <see cref="MaxDefenceWalksTo"/>).</summary>
        public int WalksTo { get; set; }

        /// <summary>M6 6 B2: true once the fall back to the tour's leg after the bound was logged for this attacker.</summary>
        public bool WalkToBoundLogged { get; set; }

        /// <summary>M6 7 D: true once the owed crossing's leg was logged taking the place of a walk toward this attacker.</summary>
        public bool CrossingLogged { get; set; }

        public bool HoldLogged { get; set; }

        /// <summary>M5 7 F15 (7-F5): the session clock the hold for this attacker began (NaN: not holding).</summary>
        public double HoldSince { get; set; } = double.NaN;

        /// <summary>M5 7 P7s (D148): the steps taken (or tried) for this attacker in a no-go cell (at most <see cref="MaxDefenceSteps"/>).</summary>
        public int Steps { get; set; }
    }

    private readonly List<(double Me, double It, int Index)> _stepSpots = [];
    private CellFrame _defenceSpot;
    private string _defenceSpotNote = string.Empty;
    private bool _stepSpotsTodoLogged;
    private bool _reachTodoLogged;
    private readonly List<uint> _walkToCells = [];
    private CellFrame _walkToSpot;
    private string _walkToNote = string.Empty;

    private readonly List<Attacker> _attackers = [];
    private readonly List<uint> _defenceLogged = [];
    private readonly List<uint> _defenceDropped = []; // M5 7 F15 (7-F5): attackers dropped after the hold bound; not fought back at while they stay in the table
    private readonly List<string> _defenceUnseen = [];
    private IDisposable? _defenceCombatSubscription;
    private IDisposable? _defenceChatSubscription;
    private IDisposable? _defenceMotionSubscription;
    private uint _defendingGuid;
    private int _defended;

    /// <summary>The kills of creatures the fight fought as self-defence (<c>defended=</c> on the verdict line, report-only): a kill notice that named the target while the target was an attacker.</summary>
    public int Defended => _defended;

    /// <summary>The creatures that attacked the player and are still fought or waited for (a test reads it).</summary>
    internal int AttackersNow => _attackers.Count;

    private void SubscribeDefence(IGameSessionView session)
    {
        if (_options.DefendForgetSeconds <= 0 || _defenceCombatSubscription is not null)
        {
            return; // --fight-defend-forget-s 0: self-defence off
        }

        SubscribeThreats(session); // M6 14 tactics: the attackers' measured rates for the target switch (FightScenario.TargetSwitch.cs)
        _defenceCombatSubscription = session.Events.Subscribe<CombatEvent>(OnDefenceCombat);
        _defenceChatSubscription = session.Events.Subscribe<ChatReceived>(OnDefenceChat);
        _defenceMotionSubscription = session.Events.Subscribe<ObjectMotionChanged>(OnDefenceMotion);
    }

    private void DisposeDefence()
    {
        DisposeThreats();
        _defenceCombatSubscription?.Dispose();
        _defenceChatSubscription?.Dispose();
        _defenceMotionSubscription?.Dispose();
        _defenceMotionSubscription = null;
        _defenceCombatSubscription = null;
        _defenceChatSubscription = null;
    }

    private void OnDefenceCombat(in CombatEvent e)
    {
        switch (e.Kind)
        {
            case CombatEventKind.DefenseHit:
                NoteAttacker(e.Who, HeardDamage);
                break;
            case CombatEventKind.DefenseEvaded:
                NoteAttacker(e.Who, HeardEvasion);
                break;
        }
    }

    private void OnDefenceChat(in ChatReceived e)
    {
        if (e.Line.ChatType == FightTally.MagicChatType && e.Line.Kind == ChatKind.System && HostileSpellSource(e.Line.Text, out var source, out var how))
        {
            NoteAttacker(source, how);
        }
    }

    /// <summary>
    /// The source of a hostile spell line the server wrote to the player, and what it was: "&lt;source&gt; &lt;verb&gt; you for &lt;n&gt; points with &lt;spell&gt;." (spell damage), "&lt;source&gt; casts &lt;spell&gt; and drains &lt;n&gt;
    /// points of your &lt;vital&gt;." (a drain), "You resist the spell cast by &lt;source&gt;" (a resist) and "&lt;source&gt; casts &lt;spell&gt; on you" (a spell cast on the player). The player's own lines ("You ...", "X resists your
    /// spell") are none of them.
    /// </summary>
    /// <param name="text">The Magic-typed line.</param>
    /// <param name="source">The creature's name.</param>
    /// <param name="how">What the line was, for the log.</param>
    /// <returns>True when the line is a creature's hostile spell at the player.</returns>
    internal static bool HostileSpellSource(string text, out string source, out string how)
    {
        source = string.Empty;
        how = string.Empty;
        var span = text.AsSpan();
        foreach (var prefix in Prefixes)
        {
            while (span.StartsWith(prefix, StringComparison.Ordinal))
            {
                span = span[prefix.Length..];
            }
        }

        const string resist = "You resist the spell cast by ";
        if (span.StartsWith(resist, StringComparison.Ordinal))
        {
            source = span[resist.Length..].TrimEnd('.').ToString();
            how = "spell resisted";
            return source.Length > 0;
        }

        if (span.StartsWith("You ", StringComparison.Ordinal))
        {
            return false;
        }

        var casts = span.IndexOf(" casts ", StringComparison.Ordinal);
        if (casts > 0)
        {
            if (FightTally.Classify(text, out _, out _) == MagicLineKind.DrainTaken)
            {
                source = span[..casts].ToString();
                how = "spell drain";
                return true;
            }

            if (span.IndexOf(" on you", StringComparison.Ordinal) > casts)
            {
                source = span[..casts].ToString();
                how = "spell cast on the player";
                return true;
            }

            return false;
        }

        const string damaged = " you for ";
        if (FightTally.Classify(text, out _, out _) == MagicLineKind.DamageTaken && span.IndexOf(damaged, StringComparison.Ordinal) is var at and > 0)
        {
            var head = span[..at];
            var verb = head.LastIndexOf(' ');
            if (verb > 0)
            {
                source = head[..verb].ToString();
                how = "spell damage";
                return true;
            }
        }

        return false;
    }

    private static readonly string[] Prefixes = ["Critical hit! ", "Overpower! ", "Sneak Attack! "];

    /// <summary>How a melee swing the server's order names is heard (<see cref="Attacker.Heard"/>).</summary>
    private const string HeardSwing = "swing order";

    /// <summary>M6 14 (the swing order): how long after a creature's swing order a blow notice of its name is taken for its blow (Lifestone's own: a swing's strikes land within its animation).</summary>
    public const double SwingBlowSeconds = 3;

    /// <summary>The swing orders that named the mage (report-only count).</summary>
    public int SwingOrders { get; private set; }

    /// <summary>The blow notices resolved to the creature whose swing order had just come, in place of the nearest of that name (report-only count).</summary>
    public int SwingBlows { get; private set; }

    /// <summary>
    /// M6 14 (the swing order; the owner, 2026-11-12: use the information the server gives; the wire trace of the same day: 106 of 106 melee swings in five and a half minutes carried it). Each melee swing a
    /// creature makes is broadcast as a motion with the StickToObject flag and the guid of what it swings at (ACE <c>Monster_Melee.cs</c> <c>DoSwingMotion</c>: <c>motion.MotionFlags |= MotionFlags.StickToObject;
    /// motion.TargetGuid = target.Guid</c>, and <c>PhysicsObj.stick_to_object</c> at line 71). So the server says, swing by swing, WHICH creature attacks her (the message's own guid) and that it is stuck to
    /// her (within the two bodies' reach). The damage and evasion notices name the attacker by name alone, and the fight took "the nearest living creature of that name", a guess among tuskers of one name.
    /// A swing order that names the mage makes its creature an attacker by guid, proven close (<see cref="Attacker.StruckClose"/>); a blow notice within <see cref="SwingBlowSeconds"/> of it is its blow.
    /// </summary>
    private void OnDefenceMotion(in ObjectMotionChanged e)
    {
        if (e.IsSelf || _view is null || Stage != "fight" || e.Motion.StickyGuid is not { } stuck || stuck != _view.State.Self.Guid || stuck == 0)
        {
            return;
        }

        var guid = e.Guid;
        if (_killed.Contains(guid) || _defenceDropped.Contains(guid) || !_view.State.Objects.TryGet(guid, out var o) || (o.ItemType & WarCreatureItemType) == 0)
        {
            return;
        }

        var now = _view.Clock.Now;
        SwingOrders++;
        Attacker? swinger = null;
        foreach (var known in _attackers)
        {
            if (known.Guid == guid)
            {
                swinger = known;
                break;
            }
        }

        if (swinger is null)
        {
            swinger = new Attacker(guid, o.Name ?? "creature", now, HeardSwing);
            _attackers.Add(swinger);
            if (!_defenceLogged.Contains(guid))
            {
                _defenceLogged.Add(guid);
                _log.Info(LogCategory, $"self-defence: {swinger.Name} 0x{guid:X8} swings at the mage (the server's swing order names her as the object it is stuck to; Monster_Melee.cs DoSwingMotion): it is an attacker by its own guid, at her; fighting it before the targets (m6-swing-order)");
            }
        }

        (swinger.LastAt, swinger.Heard, swinger.SwingAt, swinger.StruckAt, swinger.StruckClose) = (now, HeardSwing, now, now, true);
    }

    /// <summary>The attacker of <paramref name="name"/> whose swing order came last, within <see cref="SwingBlowSeconds"/>; null when none did.</summary>
    private Attacker? SwingerNamed(string name, double now)
    {
        Attacker? best = null;
        foreach (var a in _attackers)
        {
            if (now - a.SwingAt <= SwingBlowSeconds && string.Equals(a.Name, name, StringComparison.Ordinal) && (best is null || a.SwingAt > best.SwingAt))
            {
                best = a;
            }
        }

        return best;
    }

    /// <summary>True when <paramref name="guid"/> is one of the creatures that attacked the player and are still fought.</summary>
    private bool IsAttacker(uint guid)
    {
        foreach (var a in _attackers)
        {
            if (a.Guid == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A creature of <paramref name="name"/> attacked the player: it becomes (or stays) an attacker the fight fights before its targets.</summary>
    private void NoteAttacker(string name, string how)
    {
        if (_view is null || Stage != "fight" || string.IsNullOrEmpty(name) || _view.State.Self.Position is not { } at)
        {
            return;
        }

        if (how is HeardDamage or HeardEvasion && SwingerNamed(name, _view.Clock.Now) is { } swinger)
        {
            // M6 14: the server's swing order named the creature that swung; the notice's name is matched to it, not to the nearest of that name.
            SwingBlows++;
            swinger.LastAt = _view.Clock.Now;
            NoteStruck(swinger, how, swinger.LastAt, in at);
            swinger.StruckClose = true; // stuck to her by the server's own order
            return;
        }

        var objects = _view.State.Objects;
        if (objects.NearestLivingCreatureNamed(name, at, _view.State.Self.Guid, out var dropped, _view.Live) && _defenceDropped.Contains(dropped))
        {
            return; // dropped after its hold bound (7-F5): the profile's targets stay
        }

        if (!objects.NearestLivingCreatureNamed(name, at, _view.State.Self.Guid, out var guid, _view.Live) || _killed.Contains(guid))
        {
            if (!_defenceUnseen.Contains(name))
            {
                _defenceUnseen.Add(name);
                _log.Warn(LogCategory, $"self-defence: {name} attacked ({how}) but no living creature of that name is in the object table; nothing to fight back at (TODO(m5-defend-attacker-unseen))");
            }

            return;
        }

        if (how is HeardDamage or HeardEvasion && !BlowCanBeFrom(guid, name, how, in at))
        {
            return; // M6 fight targeting (m6-blow-unpinned): the nearest of that name cannot have struck her; the hitter is not known, and nothing far is taken by its name
        }

        var now = _view.Clock.Now;
        foreach (var known in _attackers)
        {
            if (known.Guid == guid)
            {
                known.LastAt = now;
                known.Heard = how;
                NoteStruck(known, how, now, in at);
                return;
            }
        }

        var added = new Attacker(guid, name, now, how);
        NoteStruck(added, how, now, in at);
        _attackers.Add(added);
        if (!_defenceLogged.Contains(guid))
        {
            _defenceLogged.Add(guid);
            _log.Info(LogCategory, $"self-defence: {name} 0x{guid:X8} attacked ({how}); fighting it before the targets");
        }
    }

    /// <summary>
    /// ACE's <c>MissileRangeCap</c>, the farthest a creature's missile flies: 85 yards over ACE's <c>MetersToYards</c> 1.094 (<c>Creature_Missile.cs:402-403</c>, applied in <c>GetMaxMissileRange</c> :406-416 @95ef426d), 77.7 m.
    /// </summary>
    public const double AceMissileRangeCapMetres = 85.0 / 1.094;

    /// <summary>The blow notices with no swing order whose nearest creature of the name stood past where its blow can come from (report-only count, <c>m6-blow-unpinned</c>).</summary>
    public int BlowsUnpinned { get; private set; }

    /// <summary>Which rule says how far a creature's kind can strike from (<see cref="BlowCanBeFrom"/>).</summary>
    private enum BlowReach : byte
    {
        /// <summary>A melee blow: the far-blow measure's reach, <see cref="LivePositionBlowMetres"/>.</summary>
        Melee,

        /// <summary>A thrown weapon whose range a ground profile records (<see cref="Grounds.GroundRanged.RangeMetres"/>, through the bestiary).</summary>
        RecordedThrow,

        /// <summary>A thrown weapon or a launcher by the world database's creature table, its range not recorded: ACE's cap, <see cref="AceMissileRangeCapMetres"/>.</summary>
        MissileCap,
    }

    /// <summary>The farthest a blow of <paramref name="o"/>'s kind can come from, in metres, and the rule (<see cref="BlowReach"/>). One read of the bestiary (a dictionary read once it is loaded).</summary>
    private static double BlowReachOf(WorldObject o, out BlowReach rule)
    {
        var kind = Bestiary.Find(o.Weenie.WeenieClassId);
        if (kind?.Offence.Ranged is { Spell: false, RangeMetres: > 0 } thrown)
        {
            rule = BlowReach.RecordedThrow;
            return Math.Max(LivePositionBlowMetres, thrown.RangeMetres);
        }

        if (kind?.Timing is { } timing && (timing.Thrown || timing.Launcher))
        {
            rule = BlowReach.MissileCap;
            return AceMissileRangeCapMetres;
        }

        rule = BlowReach.Melee;
        return LivePositionBlowMetres;
    }

    /// <summary>
    /// M6 fight targeting (<c>m6-blow-unpinned</c>; run 9, 2026-11-20 19:51 to 2026-11-21 04:58 UTC, the Tusker Holding): a damage or evasion notice names its attacker by name alone, and with no swing order of that name in the last
    /// <see cref="SwingBlowSeconds"/> the fight took the nearest living creature of the name. In run 9 that was 32 Tusker Slaves 9.6 to 32.7 m off (stuck on ledges and lower floors), and 253 Harm Other and drain decisions went at
    /// them while the Slaves at her went on striking; no swing order named her from any of them in the 10 s before any of those casts. A blow is now pinned on that nearest creature only when it can have struck her: it stands
    /// within its kind's reach by the fight's record (<see cref="BlowReachOf"/>: a melee blow's <see cref="LivePositionBlowMetres"/>, the far-blow measure's own; a thrower's recorded range; a launcher's or an unrecorded
    /// thrower's ACE cap). Else the hitter is not known: nothing is pinned, the fight keeps its target and fights what is near, and one line says so with the numbers.
    /// </summary>
    private bool BlowCanBeFrom(uint guid, string name, string how, in CellFrame mage)
    {
        if (!_view!.State.Objects.TryGet(guid, out var o))
        {
            return false;
        }

        var reach = BlowReachOf(o, out var rule);
        var away = StandsAt(_view, o) is { } it ? it.ToGlobal().DistanceTo(mage.ToGlobal()) : double.PositiveInfinity;
        if (away <= reach)
        {
            return true;
        }

        if (_view.State.Objects.LivingCreaturesNamed(name, _view.State.Self.Guid, _view.Live) == 1)
        {
            // The only living creature of the name: the server's notice names its attacker, so the name is its identity, wherever its record has it (a sticky creature's stale record, TODO(m6-sticky-walk-back);
            // a missile). No other creature can have struck her. Logged, so a record this far off is seen.
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: a blow of {name} ({how}) with no swing order: 0x{guid:X8}, {(double.IsPositiveInfinity(away) ? "with no position" : string.Create(CultureInfo.InvariantCulture, $"{away:0.0} m off by the fight's record"))}, past the {reach:0.#} m a blow of its kind comes from, is the only living {name} in the table: the notice is its (m6-blow-unpinned: the name is an identity)"));
            return true;
        }

        BlowsUnpinned++;
        var reachText = rule switch
        {
            BlowReach.RecordedThrow => string.Create(CultureInfo.InvariantCulture, $"its recorded throw's {reach:0.#} m (the bestiary's ranged attack)"),
            BlowReach.MissileCap => string.Create(CultureInfo.InvariantCulture, $"ACE's missile cap {reach:0.#} m (it throws or shoots by the world database's creature table; Creature_Missile.cs:403)"),
            _ => string.Create(CultureInfo.InvariantCulture, $"a melee blow's reach {reach:0.#} m (the far-blow measure's, FightScenario.LivePositionBlowMetres; ACE's MaxMeleeRange 0.75 m and the two bodies, Monster_Navigation.cs:28)"),
        };
        var place = double.IsPositiveInfinity(away) ? "has no position the fight can read" : string.Create(CultureInfo.InvariantCulture, $"stands {away:0.0} m off by the fight's record");
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: a blow of {name} ({how}) came with no swing order of that name in the last {SwingBlowSeconds:0} s, and the nearest {name} 0x{guid:X8} {place}, past {reachText}: it cannot have struck her, so the hitter is not known; nothing is pinned by the name, the fight keeps its target 0x{_target:X8} and fights what is near (m6-blow-unpinned, {BlowsUnpinned} this fight)"));
        return false;
    }

    /// <summary>The attackers the struck proof let the fight cast at with no line by the test (<see cref="StruckProof"/>; report-only count).</summary>
    public int StruckProofs { get; private set; }

    /// <summary>The attackers whose struck proof stood down because the shot rule had dropped them (report-only count).</summary>
    public int StruckProofsVoid { get; private set; }

    /// <summary>
    /// M6 14 (the struck proof), at each blow: a melee or missile blow (landed or evaded) whose attacker's known position is within <see cref="StruckProofMetres"/> of the mage proves it stands at her; later blows keep the
    /// proof while they come no more than <see cref="StruckProofSeconds"/> apart, wherever its known position has been left behind (see <see cref="StruckProof"/>). A spell line proves nothing about where its caster stands.
    /// </summary>
    private void NoteStruck(Attacker a, string how, double now, in CellFrame mage)
    {
        if (how is not (HeardDamage or HeardEvasion))
        {
            return;
        }

        if (double.IsNaN(a.StruckAt) || now - a.StruckAt > StruckProofSeconds)
        {
            a.StruckClose = false; // a gap: the next blow proves it again, by where it is known to stand
        }

        a.StruckAt = now;
        LivePositionBlow(a, now, in mage); // M6 14 (m6-live-position): report-only
        if (!a.StruckClose && _view!.State.Objects.TryGet(a.Guid, out var o) && StandsAt(_view, o) is { } it && it.ToGlobal().DistanceTo(mage.ToGlobal()) <= StruckProofMetres)
        {
            a.StruckClose = true;
        }
    }

    /// <summary>
    /// M6 14 (the struck proof; the Cave's stalls of 2026-11-12: 85 times in two and a half hours an attacker "had no line", 40 of them in her own cell, and she walked a tour leg of up to 17 s while it went on hitting her).
    /// The position the client knows for a creature is the server's last UpdatePosition, and a monster in melee that follows her sends none: ACE moves a sticky monster with the broadcast off
    /// (<c>Monster_Tick.cs:118-119</c>, <c>UpdatePosition(false)</c>; one more at its first swing, <c>Monster_Melee.cs:342-346</c>), where retail's client moves it itself. So after her own steps (a corpse's walk, a leg) the
    /// tusker on her is known where it stood before, often round the tunnel's bend, and the line test, right about that spot, is wrong about the creature. A blow is the better witness: an attacker whose blow came within
    /// <see cref="StruckProofSeconds"/> and that <see cref="NoteStruck"/> found close is cast at with no line by the test. A bolt that does meet a wall is bounded as any other (three misses and the block, the shot rule).
    /// <c>TODO(m6-sticky-position)</c>, answered by <c>m6-live-position</c> (<see cref="StandsAt"/>): the client does move a sticky creature's body with her as retail does, and the fight now reads that body where the
    /// session draws bodies, so the line test judges the creature where it is drawn. The proof stays for what the body cannot answer: a host that draws no bodies (the headless runner), a creature whose body the seam
    /// refuses (<see cref="LivePositions"/>' fall backs), and the seconds in which the body itself has walked back to the server's stale position (when a stick ends with that position still queued, retail's
    /// interpolation returns the body to it until the next swing: <c>TODO(m6-sticky-walk-back)</c>, measured offline in <c>AvatarStickyFollowTests</c>). At a blow the body is in its stick, beside her, so
    /// <see cref="NoteStruck"/> reads it close and the proof spans that gap.
    /// </summary>
    private bool StruckProof(Attacker a, double clock, double distance, in CellFrame here, in CellFrame it)
    {
        if (!a.StruckClose || double.IsNaN(a.StruckAt) || clock - a.StruckAt > StruckProofSeconds)
        {
            return false;
        }

        if (ShotDropHoldsAt(a.Guid, clock, in here, in it)) // M6 fight targeting: the drop from where it was measured (m6-shot-drop-place)
        {
            // Live 2026-11-12 13:00 to 13:07 UTC, the Cave: the proof named a tusker 2.5 m off by its record whose bolts came to nothing (a ledge between them, or another tusker of the same name was the one striking);
            // the shot rule dropped it after two wasted casts, the close-attacker rule took the drop back at once, and she cast at it for minutes with three kills in seven. The shot rule's drop outranks the proof:
            // while it stands the proof does not, and the line test decides as before.
            if (!a.StruckVoidLogged)
            {
                a.StruckVoidLogged = true;
                StruckProofsVoid++;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: the struck proof of {a.Name} 0x{a.Guid:X8} stands down: the shot rule dropped it (its casts came to nothing), so a blow no longer proves a bolt reaches it; the line test decides until the drop ends (m6-struck-proof-void)"));
            }

            return false;
        }

        if (!a.StruckLogged)
        {
            a.StruckLogged = true;
            StruckProofs++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: {a.Name} 0x{a.Guid:X8} struck the mage {clock - a.StruckAt:0.0} s ago ({a.Heard}): it stands at her, whatever the line test says of its last known position (0x{it.CellId:X8} [{it.Origin.X:0.#} {it.Origin.Y:0.#} {it.Origin.Z:0.#}], {distance:0.#} m from her at 0x{here.CellId:X8} [{here.Origin.X:0.#} {here.Origin.Y:0.#} {here.Origin.Z:0.#}]; a monster in melee follows her with no position sent, Monster_Tick.cs:118-119); it is cast at (m6-struck-proof, TODO(m6-sticky-position))"));
        }

        return true;
    }

    /// <summary>
    /// What the pick does about the attackers (the hook in <c>Choose</c> and in the walk phase). Drops the dead, the gone and the quiet ones (logged); of those left, the nearest that is in range with a clear line is the target
    /// (<see cref="DefenceMove.Cast"/>); with none, the nearest decides: in a no-go cell the fight holds (logged once), else it walks.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="here">Where the player stands.</param>
    /// <param name="announce">True from the pick, which logs a hold; false from the walk phase's check.</param>
    /// <param name="guid">The attacker to cast at, or the one the move is about; 0 for none.</param>
    private DefenceMove DefenceStep(IGameSessionView session, double clock, in CellFrame here, bool announce, out uint guid)
    {
        guid = 0;
        if (announce)
        {
            (_walkToReady, _walkToSearched) = (0, 0); // this pick's search for a spot with a line (DefenceLineSpot) starts clean
            _castableAttackers.Clear(); // M6 one target (FightScenario.OneTarget.cs): this pick's attackers in range with a line, for the kill order
        }

        if (_attackers.Count == 0)
        {
            return DefenceMove.None;
        }

        var objects = session.State.Objects;
        var origin = here.ToGlobal();
        Attacker? cast = null;
        Attacker? other = null;
        uint otherCell = 0;
        CellFrame otherAt = default;
        var castDistance = double.MaxValue;
        var otherDistance = double.MaxValue;
        for (var i = _attackers.Count - 1; i >= 0; i--)
        {
            var a = _attackers[i];
            if (_killed.Contains(a.Guid) || TargetGone(objects, a.Guid))
            {
                _attackers.RemoveAt(i);
                _log.Info(LogCategory, $"self-defence: {a.Name} 0x{a.Guid:X8} is dead or gone; the targets resume");
                continue;
            }

            if (clock - a.LastAt > _options.DefendForgetSeconds)
            {
                _attackers.RemoveAt(i);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: {a.Name} 0x{a.Guid:X8} has not attacked for {_options.DefendForgetSeconds:0.#} s; the targets resume"));
                continue;
            }

            if (!objects.TryGet(a.Guid, out var o) || StandsAt(session, o) is not { } p)
            {
                continue; // no position to judge by yet
            }

            var distance = p.ToGlobal().DistanceTo(origin);
            if (distance <= _range && (_options.IgnoreLine || LineIsClear(session, in here, a.Guid) || StruckProof(a, clock, distance, in here, in p)))
            {
                a.HoldSince = double.NaN;
                if (announce)
                {
                    _castableAttackers.Add((a.Guid, distance));
                }

                if (distance < castDistance)
                {
                    (cast, castDistance) = (a, distance);
                }
            }
            else if (distance < otherDistance)
            {
                (other, otherDistance, otherCell, otherAt) = (a, distance, p.CellId, p);
            }
        }

        if (cast is not null)
        {
            guid = cast.Guid;
            return DefenceMove.Cast;
        }

        if (other is null)
        {
            return DefenceMove.None;
        }

        guid = other.Guid;
        if (announce && DefenceLineSpot(session, clock, other, in here, in otherAt))
        {
            other.HoldSince = double.NaN;
            return DefenceMove.Walk; // M6 fight targeting (m6-line-first; the owner's rule after run 9: she moves to the creature for a line before she harms it through the obstacle): a sidestep or a walk to a spot with a line, before a life spell
        }

        if (announce && LifeThroughPlans(session, clock, in here, other.Guid))
        {
            other.HoldSince = double.NaN;
            return DefenceMove.Life; // M6 14 (m6-life-through): no spot with a line (or the bound spent): before the tour's walk, the step and the hold: a life spell needs no line
        }

        if (_options.ArenaHere || !_ground.IsNoGo(otherCell))
        {
            other.HoldSince = double.NaN;
            return DefenceMove.Walk;
        }

        if (announce && other.Steps < MaxDefenceSteps && _walk is not null)
        {
            // M5 7 P7s (D148, D88): never hold still while it shoots: a short step to a spot with a line to it, or out of its reach, before any hold (at most MaxDefenceSteps an attacker).
            other.Steps++;
            if (DefenceSpot(in here, in otherAt, other, otherCell, out _defenceSpot, out _defenceSpotNote))
            {
                other.HoldSince = double.NaN;
                return DefenceMove.Step;
            }
        }

        if (announce)
        {
            if (double.IsNaN(other.HoldSince))
            {
                other.HoldSince = clock;
            }
            else if (_options.DefendHoldSeconds > 0 && clock - other.HoldSince > _options.DefendHoldSeconds)
            {
                // M5 7 F15 (7-F5): an attacker the fight cannot cast at and will not walk to (a no-go cell, no line) is held for at most this long; then the profile's targets resume and it is not fought back at again.
                _attackers.Remove(other);
                _defenceDropped.Add(other.Guid);
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: {other.Name} 0x{other.Guid:X8} could not be cast at for {_options.DefendHoldSeconds:0.#} s (no-go cell 0x{otherCell:X8}, no line); dropped, the targets resume"));
                guid = 0;
                return DefenceMove.None;
            }
        }

        if (announce && !other.HoldLogged)
        {
            other.HoldLogged = true;
            _log.Info(LogCategory, $"self-defence: {other.Name} 0x{other.Guid:X8} stands in the no-go cell 0x{otherCell:X8} with no line from here; holding, nothing is walked into a no-go cell");
        }

        return DefenceMove.Hold;
    }

    /// <summary>
    /// M5 7 P7s (D148): the spot a step for <paramref name="who"/> (in the no-go cell <paramref name="cell"/>, at <paramref name="attacker"/>) goes to: of the floor's room middles within <see cref="DefendStepMaxMetres"/> of the mage, the
    /// nearest with a clear line to it within the cast range (at most <see cref="DefendStepLineTests"/> lines swept), else the nearest farther from it than its reach. Its reach is not read: the mage's own cast range stands in
    /// (<c>TODO(m5-attacker-reach)</c>). Every decision is logged. True with the spot and the leg's note.
    /// </summary>
    private bool DefenceSpot(in CellFrame here, in CellFrame attacker, Attacker who, uint cell, out CellFrame spot, out string note)
    {
        spot = default;
        note = string.Empty;
        var spots = _walk!.FloorSpots;
        if (spots.Count == 0)
        {
            if (!_stepSpotsTodoLogged)
            {
                _stepSpotsTodoLogged = true;
                _log.Warn(LogCategory, "self-defence: TODO(m5-defend-step-spots): this walk offers no floor spots to step to; an attacker in a no-go cell with no line is held as before");
            }

            return false;
        }

        var origin = here.ToGlobal();
        var it = attacker.ToGlobal();
        _stepSpots.Clear();
        for (var i = 0; i < spots.Count; i++)
        {
            var s = spots[i];
            var at = s.ToGlobal();
            var me = at.DistanceTo(origin);
            if (me <= DefendStepMaxMetres && _ground.IsWalkable(s.CellId))
            {
                _stepSpots.Add((me, at.DistanceTo(it), i));
            }
        }

        _stepSpots.Sort(static (a, b) => a.Me.CompareTo(b.Me));
        var swept = 0;
        foreach (var (me, away, i) in _stepSpots)
        {
            if (away > _range)
            {
                continue;
            }

            if (swept >= DefendStepLineTests)
            {
                break;
            }

            swept++;
            var s = spots[i];
            if (_options.IgnoreLine || LineIsClearTo(in s, in attacker))
            {
                spot = s;
                note = string.Create(CultureInfo.InvariantCulture, $"a step to a spot with a line to {who.Name} 0x{who.Guid:X8} in the no-go cell 0x{cell:X8}: 0x{s.CellId:X8} [{s.Origin.X:0.#} {s.Origin.Y:0.#} {s.Origin.Z:0.#}], {me:0.#} m off, {away:0.#} m from it (the cast range {_range:0.#} m; step {who.Steps} of {MaxDefenceSteps})");
                _log.Info(LogCategory, $"self-defence: {who.Name} 0x{who.Guid:X8} stands in the no-go cell 0x{cell:X8} with no line from here; {note}");
                return true;
            }
        }

        var reach = ReachOf(who, out var reachNote);
        foreach (var (me, away, i) in _stepSpots)
        {
            if (away > reach)
            {
                var s = spots[i];
                spot = s;
                note = string.Create(CultureInfo.InvariantCulture, $"a step out of the reach of {who.Name} 0x{who.Guid:X8} in the no-go cell 0x{cell:X8}: 0x{s.CellId:X8} [{s.Origin.X:0.#} {s.Origin.Y:0.#} {s.Origin.Z:0.#}], {me:0.#} m off, {away:0.#} m from it, past its reach ({reachNote}; none of the {swept} spot(s) swept within {DefendStepMaxMetres:0} m has a line to it; step {who.Steps} of {MaxDefenceSteps})");
                _log.Info(LogCategory, $"self-defence: {who.Name} 0x{who.Guid:X8} stands in the no-go cell 0x{cell:X8} with no line from here; {note}");
                return true;
            }
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: {who.Name} 0x{who.Guid:X8} stands in the no-go cell 0x{cell:X8} with no line from here; no spot within {DefendStepMaxMetres:0} m has a line to it ({swept} swept of {_stepSpots.Count}) or lies out of its reach (step {who.Steps} of {MaxDefenceSteps})"));
        return false;
    }

    /// <summary>
    /// M5 7 P7s (D148): begins the step <see cref="DefenceStep"/> chose (purpose <c>self-defence</c>, named): a leg to the spot over the walkable cells. The walk phase drops it the moment an attacker is in range with a clear line
    /// (<see cref="DefenceDropsWalk"/>); its end hands the pick back. A leg that cannot be planned is logged and the next pick decides again (the step is counted).
    /// </summary>
    private ScenarioStatus StartDefenceStep(IGameSessionView session, double clock, uint guid)
    {
        if (_walk is null || !_walk.BeginLegTo(session, clock, in _defenceSpot, out var leg))
        {
            _log.Info(LogCategory, $"self-defence: the step for 0x{guid:X8} could not be planned ({(_walk is null ? "the fight has no walk" : _walk.Failure ?? "no leg")}); the next pick decides again ({_defenceSpotNote})");
            return ScenarioStatus.Running;
        }

        _walks++;
        NoteLegFrom(session); // M5 7 P7i
        _missesInARow = 0;
        _target = 0;
        _holdingLogged = false;
        (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, _defenceSpotNote, 0);
        _walkBackCorpse = 0;
        _lootWalkCorpse = 0;
        _log.Info(LogCategory, $"walking: purpose=self-defence ({_defenceSpotNote}); D88: never hold still while it shoots; {leg}");
        _phase = Phase.Walk;
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// M6 6 B2 (<c>m5-attacker-reach</c>): how far <paramref name="who"/> attacks from: its <see cref="Attacker.ReachMetres"/> once read, else the mage's own cast range stands in (the source is not read: the TODO is logged once
    /// a fight, with how the attack was heard). <paramref name="note"/> says which, for the line that uses it.
    /// </summary>
    private double ReachOf(Attacker who, out string note)
    {
        if (!double.IsNaN(who.ReachMetres))
        {
            note = string.Create(CultureInfo.InvariantCulture, $"its reach {who.ReachMetres:0.#} m");
            return who.ReachMetres;
        }

        note = string.Create(CultureInfo.InvariantCulture, $"the mage's cast range {_range:0.#} m stands in, TODO(m5-attacker-reach)");
        if (!_reachTodoLogged)
        {
            _reachTodoLogged = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: TODO(m5-attacker-reach): the reach of {who.Name} 0x{who.Guid:X8} (heard by {who.Heard}) is not read (ACE works it out per attack on the server, Monster_Combat.cs:165-215 @95ef426d: the spell's range, the launcher's, or melee's 0.75 m); the mage's cast range {_range:0.#} m stands in"));
        }

        return _range;
    }

    /// <summary>The attacker <see cref="DefenceLineSpot"/> chose a spot for in this pick (0: none): <see cref="StartDefenceWalk"/> walks to <see cref="_walkToSpot"/> for it.</summary>
    private uint _walkToReady;

    /// <summary>The attacker <see cref="DefenceLineSpot"/> searched for in this pick and found no spot for (0: none), and why (<see cref="_walkToNone"/>).</summary>
    private uint _walkToSearched;

    private string _walkToNone = string.Empty;

    /// <summary>The attacker the self-defence leg under way walks for a line to (0: none), for the check after it (<see cref="DefenceLegArrived"/>).</summary>
    private uint _defenceLegFor;

    /// <summary>The searches for a spot with a line that found one (report-only count, <c>m6-line-first</c>).</summary>
    public int LineSpotsFound { get; private set; }

    /// <summary>The searches for a spot with a line that found none, so a life spell was asked (report-only count, <c>m6-line-first</c>).</summary>
    public int LineSpotsNone { get; private set; }

    /// <summary>
    /// M6 fight targeting (<c>m6-line-first</c>; run 9, 2026-11-20 19:51 to 2026-11-21 04:58 UTC: 355 Harm Other and drain decisions at Tusker Slaves no bolt was sent at, and the move for a line ran 0 times, because the life rule
    /// came first whenever Harm Other was known, in range and paid for). An attacker that cannot be cast at from here is first moved to: a sidestep for a line (<see cref="SidestepSpot"/>, an attacker within
    /// <see cref="SidestepReachMetres"/>), else the walk to the spot on the line to it with range and a line (<see cref="WalkToSpot"/>). Each search counts as one of its <see cref="MaxDefenceWalksTo"/> legs, so a spot
    /// that brought no line is not walked to for ever. Before: the crossing the tour owes keeps its ground (<see cref="CrossingOwed"/>), and the bound spent ends the search; neither is logged here
    /// (<see cref="StartDefenceWalk"/> says it when the walk is reached). True with the spot in <see cref="_walkToSpot"/> and <see cref="_walkToReady"/> set; false with why in <see cref="_walkToNone"/>, logged, and the life
    /// rule is asked next (<see cref="LifeThroughPlans"/>: no spot found, or the creature cannot be reached at all).
    /// </summary>
    private bool DefenceLineSpot(IGameSessionView session, double clock, Attacker who, in CellFrame here, in CellFrame it)
    {
        (_walkToReady, _walkToSearched, _walkToNone) = (0, 0, string.Empty);
        if (_walk is null || CrossingOwed(session, clock, out _) || who.WalksTo >= MaxDefenceWalksTo)
        {
            return false; // no walk, the crossing's ground, or the bound spent: the life rule, then StartDefenceWalk names which
        }

        who.WalksTo++;
        var side = SidestepSpot(who, in here, in it);
        var line = side is null ? null : WalkToSpot(who, in here, in it);
        if (side is null || line is null)
        {
            LineSpotsFound++;
            _walkToReady = who.Guid;
            return true;
        }

        LineSpotsNone++;
        (_walkToSearched, _walkToNone) = (who.Guid, side + "; " + line);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: no spot with a line to {who.Name} 0x{who.Guid:X8} ({it.ToGlobal().DistanceTo(here.ToGlobal()):0.#} m off, no shot from here; search {who.WalksTo} of {MaxDefenceWalksTo}): {_walkToNone}; a life spell is asked next (m6-line-first)"));
        return false;
    }

    /// <summary>
    /// M6 fight targeting: the shot rule's drop stands on <paramref name="guid"/>, an attacker the line test calls castable, from where she stands: a spot with a line is searched for it as for any attacker no bolt reaches
    /// (<see cref="DefenceLineSpot"/>). The line test cannot tell such a spot from here (it called this place clear), so the move is what re-measures: once she has moved, the drop is hers to take back
    /// (<see cref="ShotDropHolds"/>) and the shot check is asked again before any bolt.
    /// </summary>
    private bool DropLineSpot(IGameSessionView session, double clock, in CellFrame here, uint guid)
    {
        foreach (var a in _attackers)
        {
            if (a.Guid == guid)
            {
                return session.State.Objects.TryGet(guid, out var o) && StandsAt(session, o) is { } it && DefenceLineSpot(session, clock, a, in here, in it);
            }
        }

        return false;
    }

    /// <summary>
    /// M6 6 B2 (<c>m5-defend-walk-to</c>, D88): the pick's walk arm. No attacker can be cast at from here and the nearest (<paramref name="guid"/>) is not in a no-go cell, or a spot with a line was found for it
    /// (<see cref="DefenceLineSpot"/>): a leg to the spot that search chose (purpose <c>self-defence</c>, named), dropped the moment an attacker is in range with a clear line (<see cref="DefenceDropsWalk"/>), and checked when it
    /// arrives (<see cref="DefenceLegArrived"/>). A leg that cannot be planned is no walkable path: the life rule is asked where she stands (<c>m6-line-first</c>), else the tour's next leg. No spot, the crossing owed, or
    /// <see cref="MaxDefenceWalksTo"/> searches spent on this attacker fall back to today's walk, the tour's next leg, named and logged.
    /// </summary>
    private ScenarioStatus StartDefenceWalk(IGameSessionView session, double clock, uint guid)
    {
        Attacker? who = null;
        foreach (var a in _attackers)
        {
            if (a.Guid == guid)
            {
                who = a;
                break;
            }
        }

        if (who is not null && _walkToReady == guid && _walk is not null)
        {
            _walkToReady = 0;
            if (_walk.BeginLegTo(session, clock, in _walkToSpot, out var leg))
            {
                _walks++;
                NoteLegFrom(session); // M5 7 P7i
                _missesInARow = 0;
                _target = 0;
                _holdingLogged = false;
                (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, _walkToNote, 0);
                _walkBackCorpse = 0;
                _lootWalkCorpse = 0;
                _defenceLegFor = guid;
                _log.Info(LogCategory, $"walking: purpose=self-defence ({_walkToNote}); D88: an attacker out of range or line is walked toward; {leg}");
                _phase = Phase.Walk;
                return ScenarioStatus.Running;
            }

            // No walkable path to the spot: the creature cannot be reached from here, so the life spell is the answer (the owner's rule of 2026-11-16 for a creature no move reaches), else the tour's leg.
            _log.Info(LogCategory, $"self-defence: the leg to the spot with a line to {who.Name} 0x{guid:X8} could not be planned (no walkable path: {leg}); a life spell is asked where she stands (m6-line-first)");
            if (session.State.Self.Position is { } stand && LifeThroughPlans(session, clock, in stand, guid))
            {
                return LifeThroughBegin(session, clock, in stand);
            }

            _log.Info(LogCategory, $"self-defence: the walk toward {who.Name} 0x{guid:X8} falls back to the tour's next leg (as before m5-defend-walk-to): the leg could not be planned: {leg}");
            return StartWalk(session, clock, SelfDefenceNoLine);
        }

        if (who is not null && CrossingOwed(session, clock, out var storeyOwed))
        {
            // M6 7 D (m6-gate-den-storey-race): a leg back toward an attacker behind the mage would give up the crossing's ground; the tour's leg goes on, and the walk phase drops it the moment this attacker has a line (D88 holds).
            if (!who.CrossingLogged)
            {
                who.CrossingLogged = true;
                _log.Info(LogCategory, $"self-defence: no walk toward {who.Name} 0x{guid:X8} (no shot from here): the tour owes storey '{_ground.Storeys[storeyOwed].Name}', its leg keeps the ground the crossing has made; the attacker is cast at as soon as it has a line");
            }

            return StartWalk(session, clock, SelfDefenceNoLine);
        }

        string why;
        if (who is null || _walk is null)
        {
            why = _walk is null ? "the fight has no walk" : "the attacker or the mage has no position";
        }
        else if (_walkToSearched == guid)
        {
            why = _walkToNone; // DefenceLineSpot searched in this pick and logged it; the life rule had nothing to cast
        }
        else if (who.WalksTo >= MaxDefenceWalksTo)
        {
            if (who.WalkToBoundLogged)
            {
                return StartWalk(session, clock, SelfDefenceNoLine);
            }

            who.WalkToBoundLogged = true;
            why = string.Create(CultureInfo.InvariantCulture, $"{who.WalksTo} leg(s) toward it brought no shot (the bound {MaxDefenceWalksTo}, FightScenario.MaxDefenceWalksTo)");
        }
        else
        {
            why = "TODO(m6-line-first-unsearched): the pick reached the walk with no search for a spot with a line to it in this pick";
            _log.Warn(LogCategory, $"self-defence: {why} ({who.Name} 0x{guid:X8})");
        }

        _walkToSearched = 0;
        var name = who is null ? string.Create(CultureInfo.InvariantCulture, $"0x{guid:X8}") : string.Create(CultureInfo.InvariantCulture, $"{who.Name} 0x{guid:X8}");
        _log.Info(LogCategory, $"self-defence: the walk toward {name} falls back to the tour's next leg (as before m5-defend-walk-to): {why}");
        return StartWalk(session, clock, SelfDefenceNoLine);
    }

    /// <summary>
    /// M6 fight targeting (<c>m6-line-first</c>), the check after the act: a self-defence leg for a line to <see cref="_defenceLegFor"/> has arrived. One line says whether the line test now finds it clear from where she
    /// stands, with the distance; the next pick decides from there (a clear line: the bolt, after the shot check; none: the second search or the life rule). Nothing else changes here.
    /// </summary>
    private void DefenceLegArrived(IGameSessionView session)
    {
        var guid = _defenceLegFor;
        _defenceLegFor = 0;
        if (guid == 0 || session.State.Self.Position is not { } here)
        {
            return;
        }

        if (!session.State.Objects.TryGet(guid, out var o) || StandsAt(session, o) is not { } it || _killed.Contains(guid))
        {
            _log.Info(LogCategory, $"self-defence: the leg for a line to 0x{guid:X8} arrived: it is dead or gone (m6-line-first)");
            return;
        }

        var clear = _options.IgnoreLine || LineIsClear(session, in here, guid);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"self-defence: the leg for a line to {o.Name} 0x{guid:X8} arrived at 0x{here.CellId:X8} [{here.Origin.X:0.#} {here.Origin.Y:0.#} {here.Origin.Z:0.#}], {it.ToGlobal().DistanceTo(here.ToGlobal()):0.#} m from it: the line test finds it {(clear ? "clear: the bolt next, after the shot check" : "still blocked: the next search or the life rule")} (m6-line-first)"));
    }

    /// <summary>
    /// M6 6 B2 (<c>m5-defend-walk-to</c>): the spot a walk toward <paramref name="who"/> (at <paramref name="it"/>) goes to. The cells of the straight line from the mage to it (<see cref="IFightWalk.CellsOnLine"/>) are taken in order
    /// from the mage, and in each the floor's room middles (<see cref="IFightWalk.FloorSpots"/>) on a walkable cell off the no-go cells; the first within the cast range of it whose line to it is clear (at most
    /// <see cref="DefendStepLineTests"/> lines swept) is the last spot, coming from the attacker, with range and a line: the shortest walk that lets the bolt fly. Null with <see cref="_walkToSpot"/> and its note set; else why none.
    /// </summary>
    private string? WalkToSpot(Attacker who, in CellFrame here, in CellFrame it)
    {
        var trace = _walk!.CellsOnLine(in here, in it, _walkToCells);
        if (trace == LineTrace.NotTraced)
        {
            return "this walk cannot trace the line to it (CellsOnLine: not traced)";
        }

        var spots = _walk.FloorSpots;
        if (spots.Count == 0)
        {
            return "this walk offers no floor spots (FloorSpots: none; TODO(m5-defend-step-spots))";
        }

        var origin = here.ToGlobal();
        var target = it.ToGlobal();
        var swept = 0;
        foreach (var cell in _walkToCells)
        {
            if (!_ground.IsWalkable(cell) || (!_options.ArenaHere && _ground.IsNoGo(cell)))
            {
                continue;
            }

            for (var i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (s.CellId != cell)
                {
                    continue;
                }

                var away = s.ToGlobal().DistanceTo(target);
                if (away > _range)
                {
                    continue;
                }

                if (swept >= DefendStepLineTests)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"no spot on the line had a clear line to it within the {DefendStepLineTests} lines swept (FightScenario.DefendStepLineTests)");
                }

                swept++;
                if (!_options.IgnoreLine && !LineIsClearTo(in s, in it))
                {
                    continue;
                }

                var reach = ReachOf(who, out var reachNote);
                var me = s.ToGlobal().DistanceTo(origin);
                _walkToSpot = s;
                _walkToNote = string.Create(CultureInfo.InvariantCulture, $"a walk toward {who.Name} 0x{who.Guid:X8} at 0x{it.CellId:X8}, {target.DistanceTo(origin):0.#} m off with no shot from here: to 0x{s.CellId:X8} [{s.Origin.X:0.#} {s.Origin.Y:0.#} {s.Origin.Z:0.#}], {me:0.#} m off on the line to it, {away:0.#} m from it with a clear line (the cast range {_range:0.#} m; {(away > reach ? "out of" : "within")} its reach, {reachNote}; leg {who.WalksTo} of {MaxDefenceWalksTo})");
                _log.Info(LogCategory, $"self-defence: {_walkToNote}");
                return null;
            }
        }

        var stopped = trace == LineTrace.Stopped ? string.Create(CultureInfo.InvariantCulture, $" (the sweep stopped at 0x{(_walkToCells.Count > 0 ? _walkToCells[^1] : here.CellId):X8}: the cells past it are not known)") : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"no floor spot in the {_walkToCells.Count} cell(s) on the line to it is within the cast range {_range:0.#} m with a clear line ({swept} line(s) swept){stopped}");
    }

    /// <summary>M6 14 (the sidestep): the farthest an attacker may stand, by the position the fight takes it at, for a sidestep to be tried (Lifestone's own: melee's reach and a body's width; farther off, the walk toward it is the answer).</summary>
    public const double SidestepReachMetres = 6;

    /// <summary>M6 14 (the sidestep): the directions round the mage a sidestep probes at each radius (Lifestone's own: every 45 degrees).</summary>
    public const int SidestepDirections = 8;

    private static readonly float[] SidestepRadii = [2f, 3.5f, 5f];

    /// <summary>The sidesteps the fight began (report-only count).</summary>
    public int Sidesteps { get; private set; }

    /// <summary>
    /// M6 14 (the sidestep; live 2026-11-12 13:00 UTC, the Cave, as the owner saw it: a tusker stood on the other side of a torch; its blows reached her, ACE's melee asks no line, and every bolt of hers met the torch).
    /// The walk toward an attacker knows the rooms' middles alone (<see cref="WalkToSpot"/>), and with none of them giving a line the fight took the tour's next leg, tens of metres off, while it went on hitting her.
    /// A player takes two steps to the side. With the attacker within <see cref="SidestepReachMetres"/>, the points round her at 2, 3.5 and 5 m, <see cref="SidestepDirections"/> to a ring, nearest ring first, are
    /// probed: the straight step to the point must be clear and its cells known (<see cref="IFightWalk.CellsOnLine"/>, traced whole), the point's cell walkable and off the no-go cells, and the line from the point
    /// to the attacker clear and in the cast range. The first such point is the leg's goal (the walk phase drops the leg the moment the attacker has a line, <see cref="DefenceDropsWalk"/>). It counts as one of the
    /// attacker's <see cref="MaxDefenceWalksTo"/> legs. Null with <see cref="_walkToSpot"/> and its note set; else why none.
    /// </summary>
    private string? SidestepSpot(Attacker who, in CellFrame here, in CellFrame it)
    {
        var origin = here.ToGlobal();
        var target = it.ToGlobal();
        var off = target.DistanceTo(origin);
        if (off > SidestepReachMetres)
        {
            return string.Create(CultureInfo.InvariantCulture, $"no sidestep: it stands {off:0.#} m off, past the sidestep's reach {SidestepReachMetres:0.#} m");
        }

        var probed = 0;
        var lines = 0;
        foreach (var radius in SidestepRadii)
        {
            for (var k = 0; k < SidestepDirections; k++)
            {
                var angle = k * 2 * Math.PI / SidestepDirections;
                var at = here.Frame.Origin + new System.Numerics.Vector3((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius), 0f);
                var spot = new CellFrame(here.CellId, here.Frame with { Origin = at });
                probed++;
                if (_walk!.CellsOnLine(in here, in spot, _walkToCells) != LineTrace.Traced)
                {
                    continue; // something stands in the step's own way, or its cells are not known
                }

                var cell = _walkToCells.Count > 0 ? _walkToCells[^1] : here.CellId;
                if (!_ground.IsWalkable(cell) || (!_options.ArenaHere && _ground.IsNoGo(cell)))
                {
                    continue;
                }

                spot = new CellFrame(cell, spot.Frame);
                var away = spot.ToGlobal().DistanceTo(target);
                if (away > _range)
                {
                    continue;
                }

                lines++;
                if (!_options.IgnoreLine && !LineIsClearTo(in spot, in it))
                {
                    continue;
                }

                Sidesteps++;
                _walkToSpot = spot;
                _walkToNote = string.Create(CultureInfo.InvariantCulture, $"a sidestep for a line to {who.Name} 0x{who.Guid:X8}, {off:0.#} m off with something between them: to 0x{cell:X8} [{at.X:0.#} {at.Y:0.#} {at.Z:0.#}], {radius:0.#} m to the side, {away:0.#} m from it with a clear line ({probed} point(s) probed, {lines} line(s) swept; leg {who.WalksTo} of {MaxDefenceWalksTo}; m6-sidestep)");
                _log.Info(LogCategory, $"self-defence: {_walkToNote}");
                return null;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"no sidestep: none of the {probed} point(s) within {SidestepRadii[^1]:0.#} m round her has a clear step to it and a clear line from it ({lines} line(s) swept; m6-sidestep)");
    }

    /// <summary>The walk phase's check: an attacker now in range with a clear line drops the walk, so the next frame's pick casts at it.</summary>
    private bool DefenceDropsWalk(IGameSessionView session, double clock)
    {
        if (_attackers.Count == 0 || session.State.Self.Position is not { } here || DefenceStep(session, clock, in here, announce: false, out var guid) != DefenceMove.Cast)
        {
            return false;
        }

        _walk?.Abandon();
        _defenceLegFor = 0; // the check after a leg for a line: the line came during it (m6-line-first)
        _log.Info(LogCategory, $"self-defence: the walk is dropped; 0x{guid:X8} is in range with a clear line");
        _phase = Phase.Choose;
        return true;
    }

    /// <summary>
    /// M5 7 loot retry: the creature attacking the player now, or null: the most recent attacker that is alive in the object table (not killed, not gone) and attacked within <see cref="FightOptions.DefendForgetSeconds"/>. A read only; the pick prunes the list.
    /// </summary>
    private Attacker? AttackerNow(IGameSessionView session, double clock)
    {
        Attacker? found = null;
        foreach (var a in _attackers)
        {
            if (!_killed.Contains(a.Guid) && !TargetGone(session.State.Objects, a.Guid) && clock - a.LastAt <= _options.DefendForgetSeconds && (found is null || a.LastAt > found.LastAt))
            {
                found = a;
            }
        }

        return found;
    }

    /// <summary>The kill notice named the target: when the target was an attacker, the kill is a defended one (<c>defended=</c>).</summary>
    private void NoteDefenceKill(uint target)
    {
        var guid = target == 0 ? _seenTarget : target;
        if (guid != 0 && guid == _defendingGuid)
        {
            _defended++;
        }
    }

    /// <summary>Forgets the logged attackers whose object has left the table (the list stays as long as the creatures in view; the hours-long fight does not grow it).</summary>
    private void PruneDefence(WorldObjectTable objects)
    {
        PruneLifeThrough(objects); // M6 14
        PruneHitByHer(objects); // M6 one target
        for (var i = _defenceDropped.Count - 1; i >= 0; i--)
        {
            if (!objects.TryGet(_defenceDropped[i], out _))
            {
                _defenceDropped.RemoveAt(i);
            }
        }

        for (var i = _defenceLogged.Count - 1; i >= 0; i--)
        {
            if (!objects.TryGet(_defenceLogged[i], out _))
            {
                _defenceLogged.RemoveAt(i);
            }
        }
    }
}
