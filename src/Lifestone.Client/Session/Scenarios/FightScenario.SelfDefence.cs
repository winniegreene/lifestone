using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
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
/// <para><b>Walking.</b> The fight walks only when no attacker can be cast at from where the player stands (out of range or no clear line, the same line rule as the pick), by the walk it already has; the walk has no leg toward
/// a given creature (<c>TODO(m5-defend-walk-to)</c>), so that walk is the tour's next leg, dropped the moment an attacker is in range with a clear line. An attacker in a no-go cell is cast at from where the player stands when a
/// line exists, else the fight holds (nothing is walked into a no-go cell) and logs it once.</para>
/// <para><b>M5 7 P7s (D148, D88 meets the no-go map; <c>m5-8-no-go-attacker</c>).</b> The long run's 19:42 UTC: a Virindi Servant shot from inside the den's no-go cell 0x01050244 and the fight held still with no line. Now, before
/// it holds, the fight steps: a short leg (<see cref="IFightWalk.BeginLegTo"/>, over the walkable cells only) to the nearest room middle of the floor (<see cref="IFightWalk.FloorSpots"/>) within <see cref="DefendStepMaxMetres"/>
/// that has a clear line to the attacker within the cast range, else to the nearest one out of its reach. At most <see cref="MaxDefenceSteps"/> steps an attacker, each decision logged; past them, or with no such spot, the hold
/// and its bound stand as before.</para>
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
    }

    /// <summary>M5 7 P7s (D148): the steps the fight takes for one attacker in a no-go cell before it holds (Lifestone's own bound: a step that found no line, and one more from there).</summary>
    public const int MaxDefenceSteps = 2;

    /// <summary>M5 7 P7s (D148): the farthest, straight, a step's spot may lie from the mage (Lifestone's own: a step, not a tour leg; about 4 s at the den's 10.4 m/s).</summary>
    public const double DefendStepMaxMetres = 40;

    /// <summary>M5 7 P7s (D148): the most spots whose line to the attacker one step decision sweeps, nearest first (Lifestone's own bound on the sweeps a decision costs).</summary>
    public const int DefendStepLineTests = 24;

    /// <summary>A creature that attacked the player, and when it last did.</summary>
    private sealed class Attacker(uint guid, string name, double lastAt)
    {
        public uint Guid { get; } = guid;

        public string Name { get; } = name;

        public double LastAt { get; set; } = lastAt;

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

    private readonly List<Attacker> _attackers = [];
    private readonly List<uint> _defenceLogged = [];
    private readonly List<uint> _defenceDropped = []; // M5 7 F15 (7-F5): attackers dropped after the hold bound; not fought back at while they stay in the table
    private readonly List<string> _defenceUnseen = [];
    private IDisposable? _defenceCombatSubscription;
    private IDisposable? _defenceChatSubscription;
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

        _defenceCombatSubscription = session.Events.Subscribe<CombatEvent>(OnDefenceCombat);
        _defenceChatSubscription = session.Events.Subscribe<ChatReceived>(OnDefenceChat);
    }

    private void DisposeDefence()
    {
        _defenceCombatSubscription?.Dispose();
        _defenceChatSubscription?.Dispose();
        _defenceCombatSubscription = null;
        _defenceChatSubscription = null;
    }

    private void OnDefenceCombat(in CombatEvent e)
    {
        switch (e.Kind)
        {
            case CombatEventKind.DefenseHit:
                NoteAttacker(e.Who, "damage notice");
                break;
            case CombatEventKind.DefenseEvaded:
                NoteAttacker(e.Who, "evasion notice");
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

    /// <summary>A creature of <paramref name="name"/> attacked the player: it becomes (or stays) an attacker the fight fights before its targets.</summary>
    private void NoteAttacker(string name, string how)
    {
        if (_view is null || Stage != "fight" || string.IsNullOrEmpty(name) || _view.State.Self.Position is not { } at)
        {
            return;
        }

        var objects = _view.State.Objects;
        if (objects.NearestLivingCreatureNamed(name, at, _view.State.Self.Guid, out var dropped) && _defenceDropped.Contains(dropped))
        {
            return; // dropped after its hold bound (7-F5): the profile's targets stay
        }

        if (!objects.NearestLivingCreatureNamed(name, at, _view.State.Self.Guid, out var guid) || _killed.Contains(guid))
        {
            if (!_defenceUnseen.Contains(name))
            {
                _defenceUnseen.Add(name);
                _log.Warn(LogCategory, $"self-defence: {name} attacked ({how}) but no living creature of that name is in the object table; nothing to fight back at (TODO(m5-defend-attacker-unseen))");
            }

            return;
        }

        var now = _view.Clock.Now;
        foreach (var known in _attackers)
        {
            if (known.Guid == guid)
            {
                known.LastAt = now;
                return;
            }
        }

        _attackers.Add(new Attacker(guid, name, now));
        if (!_defenceLogged.Contains(guid))
        {
            _defenceLogged.Add(guid);
            _log.Info(LogCategory, $"self-defence: {name} 0x{guid:X8} attacked ({how}); fighting it before the targets");
        }
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

            if (!objects.TryGet(a.Guid, out var o) || o.Position is not { } p)
            {
                continue; // no position to judge by yet
            }

            var distance = p.ToGlobal().DistanceTo(origin);
            if (distance <= _range && (_options.IgnoreLine || LineIsClear(session, in here, a.Guid)))
            {
                a.HoldSince = double.NaN;
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

        foreach (var (me, away, i) in _stepSpots)
        {
            if (away > _range)
            {
                var s = spots[i];
                spot = s;
                note = string.Create(CultureInfo.InvariantCulture, $"a step out of the reach of {who.Name} 0x{who.Guid:X8} in the no-go cell 0x{cell:X8}: 0x{s.CellId:X8} [{s.Origin.X:0.#} {s.Origin.Y:0.#} {s.Origin.Z:0.#}], {me:0.#} m off, {away:0.#} m from it, past its reach (the mage's cast range {_range:0.#} m stands in, TODO(m5-attacker-reach); none of the {swept} spot(s) swept within {DefendStepMaxMetres:0} m has a line to it; step {who.Steps} of {MaxDefenceSteps})");
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

    /// <summary>The walk phase's check: an attacker now in range with a clear line drops the walk, so the next frame's pick casts at it.</summary>
    private bool DefenceDropsWalk(IGameSessionView session, double clock)
    {
        if (_attackers.Count == 0 || session.State.Self.Position is not { } here || DefenceStep(session, clock, in here, announce: false, out var guid) != DefenceMove.Cast)
        {
            return false;
        }

        _walk?.Abandon();
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
