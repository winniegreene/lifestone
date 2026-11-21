using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 one target at a time (the owner's ruling of 2026-11-21: she kills one target at a time, drains apart; docs/session.md section 124). Run 10's live log, the Tusker Armory, 07:31:00 to 07:34:59 UTC: 18 of 21 target changes
/// read "self-defence: it attacked the player"; 8 followed a kill, 6 left a living target right after the in-flight ledger predicted the bolt just confirmed would kill it, and 4 were the weighing taking that hurt one back.
/// The cause was in the pick, not in the attack notices: the prediction (<see cref="ExcludeDoomed"/>: the bolt's mean hit against the live model's health) left her target out of the pick, the weighing of the current against
/// the attacker (the old <c>KeepsCurrent</c>) read a target left out as one it could not keep, and the nearest attacker was taken; 8 of the 9 predictions of 07:31:30 to 07:35:59 left the creature alive (each was cast at again).
/// The rule now, in <c>Choose</c>, whenever an attacker can be cast at (<see cref="DefenceMove.Cast"/>):
/// <list type="number">
/// <item>Her target is held while it is alive, not dropped by the shot rule nor waiting out a wall block, within the cast range with a line (an attacker: the self-defence pick's own read, the struck proof included), and
/// engaged: attacking her, or a creature she has hit (ACE wakes a monster a player's spell strikes and sets the player its attack target, <c>Player_Monster.cs:36-53</c> <c>OnAttackMonster</c>, from
/// <c>SpellProjectile.cs:364</c> @95ef426d). A held target is kept: a new attacker does not take it (one line a pair, with the kill order's own weighing beside it, report-only).</item>
/// <item>A held target attacking her that the bolts in flight are expected to finish is held for: no new cast at another creature until they land (a kill, a damage line, a resist, or the ledger's own end, the flight and
/// <see cref="InFlightMarginSeconds"/>); the in-flight ledger logs and counts it as before (<c>doomed_skips</c>).</item>
/// <item>Otherwise (no target, dead or gone, out of range or line, dropped, a passive target she has not hit, or one not attacking her that the bolts in flight will finish) the next target is the kill order's first among the
/// attackers she can hit: <see cref="TargetSwitch.KillOrder"/>, the rule the fight simulator folds (<c>tools/fight-sim/InFightRules.cs</c> ChooseTarget), here folded nearest first.</item>
/// </list>
/// M5 D88 stands where it was meant: a passive target is left for an attacker at once, and an attacker she cannot cast at from here keeps the defence step (the walk, the sidestep, the life spell; docs/session.md section 120).
/// The drains' and heals' source picks are their own (the emergency's fullest, <see cref="FightEmergency.Fullest"/>; the drain rule's; the stamina source's) and are not read here.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>What the one-target rule did with the pick.</summary>
    private enum OneTargetMove
    {
        /// <summary>No attacker is left to pick after the exclusions: the self-defence pick's own answer stands.</summary>
        None,

        /// <summary>Her target is held: it stays the target.</summary>
        Keep,

        /// <summary>Her target attacks her and the bolts in flight are expected to finish it: nothing new is cast until they land.</summary>
        Hold,

        /// <summary>The kill order's first among the attackers she can hit.</summary>
        Order,
    }

    private readonly List<(uint Guid, double Distance)> _castableAttackers = [];
    private readonly List<(uint Guid, double Distance)> _orderCandidates = [];
    private readonly List<SwitchSide> _orderSides = [];
    private readonly List<uint> _hitByHer = [];
    private (uint Kept, uint Over) _keepLogged;
    private uint _holdLogged;

    /// <summary>The creature she attacks (the bolt's, the war option's or the life spell's target); the emergency drain's source and the ring's anchor move the selected target, not this.</summary>
    private uint _boltTarget;

    /// <summary>This pick's reason for the target line when the one-target rule chose (empty: the line's own reasons).</summary>
    private string _pickWhy = string.Empty;

    /// <summary>The attackers her held target was kept over, one a pair (report-only).</summary>
    public int OneTargetKeeps { get; private set; }

    /// <summary>The holds for the bolts in flight on her target, one each (report-only).</summary>
    public int OneTargetHolds { get; private set; }

    /// <summary>The targets the kill order chose that were not her target (report-only).</summary>
    public int KillOrderPicks { get; private set; }

    /// <summary>The hit wait read a damage line of her cast at <paramref name="guid"/>: she has hit it (the engagement the rule keeps).</summary>
    private void NoteHitByHer(uint guid)
    {
        if (guid != 0 && !_hitByHer.Contains(guid))
        {
            _hitByHer.Add(guid);
        }
    }

    /// <summary>Forgets the creatures she hit that left the object table or died (the list stays as long as the creatures in view).</summary>
    private void PruneHitByHer(WorldObjectTable objects)
    {
        for (var i = _hitByHer.Count - 1; i >= 0; i--)
        {
            if (!objects.TryGet(_hitByHer[i], out _) || _killed.Contains(_hitByHer[i]))
            {
                _hitByHer.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The rule, with an attacker castable (<paramref name="guid"/>: the self-defence pick's nearest, in; the target, out). Check before: her target's state from the fight's own reads; act: keep, hold or the kill order;
    /// each decision logged (a keep once a pair, a hold once a flight, a switch on the target line through <see cref="_pickWhy"/>).
    /// </summary>
    private OneTargetMove OneTargetStep(IGameSessionView session, double clock, in CellFrame here, ref uint guid)
    {
        var current = _boltTarget;
        var attacking = current != 0 && IsAttacker(current);
        var notHeld = CurrentNotHeld(session, clock, in here, current, attacking, out var distance);
        if (notHeld is null)
        {
            if (attacking || _hitByHer.Contains(current))
            {
                if (!InFlightFinishes(session, current, clock, out var seconds))
                {
                    _holdLogged = 0;
                    NoteKeep(session, clock, current, attacking, distance);
                    if (current != _target)
                    {
                        // The emergency's drain (its fullest source) or the ring's anchor moved the selected target: the bolts go back to the one she is on.
                        _pickWhy = string.Create(CultureInfo.InvariantCulture, $"one target: back on the target she is on, {(attacking ? "attacking her" : "a creature she has hit")}, after 0x{_target:X8} (a drain's or the ring's own pick; docs/session.md section 124)");
                    }

                    guid = current;
                    return OneTargetMove.Keep;
                }

                if (attacking)
                {
                    NoteHold(session, clock, current, seconds);
                    return OneTargetMove.Hold;
                }

                notHeld = string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} is not attacking her and the bolts in flight are expected to finish it");
            }
            else
            {
                notHeld = string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} is not attacking her and she has not hit it (M5 D88: an attacker is fought back at once)");
            }
        }

        _holdLogged = 0;
        var pick = KillOrderPick(session, clock, current, out var count, out var last);
        if (pick == 0)
        {
            return OneTargetMove.None; // every attacker she could cast at is dropped, blocked or about to die to the bolts in flight: the pick's own rules (the shot drop's move, the hold for the bolts) decide
        }

        guid = pick;
        if (pick != current)
        {
            KillOrderPicks++;
            var weighing = count > 1 ? "; the last weighing: " + last.Line : string.Empty;
            _pickWhy = string.Create(CultureInfo.InvariantCulture, $"self-defence: it attacked the player; {notHeld}: the kill order's first of the {count} attacker(s) on her she can hit (TargetSwitch.Decide folded nearest first, as the fight simulator folds it; docs/session.md section 124){weighing}");
        }

        return OneTargetMove.Order;
    }

    /// <summary>Null when her target is held by the fight's reads (alive, not dropped, not blocked, in range with a line); else why not, in words.</summary>
    private string? CurrentNotHeld(IGameSessionView session, double clock, in CellFrame here, uint current, bool attacking, out double distance)
    {
        distance = double.NaN;
        var objects = session.State.Objects;
        if (current == 0)
        {
            return "she has no target";
        }

        if (_killed.Contains(current) || TargetGone(objects, current))
        {
            return string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} is dead or gone");
        }

        if (_wallBlockedUntil.ContainsKey(current))
        {
            return string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} waits out a wall block (FightScenario.WallStay.cs)");
        }

        if (ShotDropHolds(session, current, clock))
        {
            return string.Create(CultureInfo.InvariantCulture, $"the shot rule dropped her target 0x{current:X8}");
        }

        if (!objects.TryGet(current, out var o) || StandsAt(session, o) is not { } at)
        {
            return string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} has no position");
        }

        distance = at.ToGlobal().DistanceTo(here.ToGlobal());
        if (distance > _range)
        {
            return string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} is out of range ({distance:0.#} m, the cast range {_range:0.#} m)");
        }

        if (attacking ? !CastableAttacker(current) : !(_options.IgnoreLine || LineIsClear(session, in here, current)))
        {
            return string.Create(CultureInfo.InvariantCulture, $"her target 0x{current:X8} has no clear line from here");
        }

        return null;
    }

    /// <summary>True when the self-defence pick of this frame found <paramref name="guid"/> in range with a line (or the struck proof).</summary>
    private bool CastableAttacker(uint guid)
    {
        foreach (var (g, _) in _castableAttackers)
        {
            if (g == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The kill order's first (<see cref="TargetSwitch.KillOrder"/>) among the attackers the self-defence pick can cast at, nearest first, less her target, the killed, the dropped, the blocked and the ones the bolts in flight
    /// are expected to finish. 0 when none is left. Reads the sides only with two or more.
    /// </summary>
    private uint KillOrderPick(IGameSessionView session, double clock, uint current, out int count, out SwitchAnswer last)
    {
        last = default;
        _orderCandidates.Clear();
        foreach (var (g, d) in _castableAttackers)
        {
            if (g != current && !_pickExcluded.Contains(g) && !ShotDropHolds(session, g, clock) && !InFlightFinishes(session, g, clock, out _))
            {
                _orderCandidates.Add((g, d));
            }
        }

        count = _orderCandidates.Count;
        if (count == 0)
        {
            return 0;
        }

        _orderCandidates.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Guid.CompareTo(b.Guid));
        if (count == 1)
        {
            return _orderCandidates[0].Guid;
        }

        _orderSides.Clear();
        foreach (var (g, _) in _orderCandidates)
        {
            if (session.State.Objects.TryGet(g, out var o))
            {
                _orderSides.Add(SideOf(session, clock, o));
            }
        }

        var best = TargetSwitch.KillOrder(_orderSides, out last);
        return best < 0 ? _orderCandidates[0].Guid : _orderSides[best].Guid;
    }

    /// <summary>Her held target is kept; one line a pair with the nearest other attacker she can hit, and the kill order's weighing of the two beside it (report-only, the counters of M6 14 tactics).</summary>
    private void NoteKeep(IGameSessionView session, double clock, uint current, bool attacking, double distance)
    {
        var over = 0u;
        var overDistance = double.MaxValue;
        foreach (var (g, d) in _castableAttackers)
        {
            if (g != current && d < overDistance)
            {
                (over, overDistance) = (g, d);
            }
        }

        if (over == 0 || _keepLogged == (current, over) || !session.State.Objects.TryGet(current, out var c) || !session.State.Objects.TryGet(over, out var a))
        {
            return;
        }

        _keepLogged = (current, over);
        OneTargetKeeps++;
        var currentSide = SideOf(session, clock, c);
        var attackerSide = SideOf(session, clock, a);
        var answer = TargetSwitch.Decide(in currentSide, in attackerSide);
        SwitchesWeighed++;
        SwitchesKept += answer.Keep ? 1 : 0;
        var how = attacking ? "attacking her" : "a creature she has hit, which ACE sets to attack her (Player_Monster.cs OnAttackMonster)";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"one target: keep '{c.Name}' 0x{current:X8} ({LiveHealthOrFull(c) * 100:0}% by the live model, {how}, {distance:0.#} m, in range with a line) over the attacker '{a.Name}' 0x{over:X8} ({overDistance:0.#} m): she finishes the target she is on (the owner, 2026-11-21; docs/session.md section 124); the kill order, report-only: {answer.Line} (M6 14 tactics)"));
    }

    /// <summary>Her target attacks her and the bolts in flight are expected to finish it: the hold, logged once a flight.</summary>
    private void NoteHold(IGameSessionView session, double clock, uint current, double seconds)
    {
        ExcludeDoomed(session, clock); // the ledger's own line and count (doomed_skips), as when the pick leaves a doomed creature out
        if (_holdLogged == current)
        {
            return;
        }

        _holdLogged = current;
        OneTargetHolds++;
        var objects = session.State.Objects;
        var name = objects.TryGet(current, out var o) ? o.Name : null;
        var max = o is null ? 0 : TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
        var expected = o is not null && StandsAt(session, o) is { } at ? InFlightDamageOn(current, at.ToGlobal(), clock) : 0;
        var left = o is null ? 0 : LiveHealthOrFull(o) * max;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"one target: hold for '{name ?? "creature"}' 0x{current:X8}, which is attacking her: the bolts in flight are expected to deal {expected:0} against its {left:0} health left (live model), the last lands within {Math.Max(0, seconds):0.##} s; no new cast at another creature until they land (a kill, a damage line, a resist, or the ledger's end; docs/session.md section 124)"));
    }
}
