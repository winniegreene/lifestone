using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 tactics (live ladder56 2026-11-17 08:33:02.349 UTC): the self-defence switch weighs the target she is on against the creature that attacked her (<see cref="TargetSwitch"/>), by each one's health left over her damage
/// a second on its kind and each one's damage a second on her, the losing race's own per-kind read (<see cref="KindIncoming"/> over a <see cref="HostileWatch"/> of the fight's own, the derived rate from the world database's
/// attacks at ACE's attack timing, <see cref="AttackTimes"/>). M6 one target at a time (the owner, 2026-11-21; <c>FightScenario.OneTarget.cs</c>, docs/session.md section 124): the weighing no longer takes her off a target
/// she holds; it orders the attackers when she has none to hold (<see cref="TargetSwitch.KillOrder"/>, the fight simulator's own fold), and beside a keep it is logged report-only.
/// </summary>
public sealed partial class FightScenario
{
    private HostileWatch? _threatWatch;
    private LiveFightLine? _threatLine;

    /// <summary>
    /// M6 14 tactics: the creatures' attack timings the target switch's derived rates read (<see cref="CreatureAttackTimes.For"/> of the session's dats, as the vitals guard's); <see cref="CreatureAttackTimes.None"/> (the default)
    /// gives each the named fallback.
    /// </summary>
    public CreatureAttackTimes AttackTimes { get; init; } = CreatureAttackTimes.None;

    /// <summary>The weighings logged beside a keep of the one-target rule, one a pair (report-only).</summary>
    public int SwitchesWeighed { get; private set; }

    /// <summary>The weighings that would have kept the current target too (report-only).</summary>
    public int SwitchesKept { get; private set; }

    private void SubscribeThreats(IGameSessionView session) => _threatWatch ??= new HostileWatch(session, _ground, 0);

    private void DisposeThreats()
    {
        _threatWatch?.Dispose();
        _threatWatch = null;
    }

    /// <summary>One creature for the rule: its health left (the live model), her damage a second on it (her hardest bolt line on its kind, at the chance her War Magic lands against its Magic Defense, over the bolt's seconds), its threat.</summary>
    private SwitchSide SideOf(IGameSessionView session, double clock, WorldObject o)
    {
        var state = session.State;
        var wcid = o.Weenie.WeenieClassId;
        var (max, defence) = TargetStatsOf(wcid);
        var health = LiveHealthOrFull(o) * (max > 0 ? max : 0);
        var war = GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var skill, out _) ? skill : 0u;
        var hit = BestBoltOn(state, wcid)?.Expected ?? 0;
        var seconds = WarSpellData.EffectSeconds(WarKind.Bolt) + InFlightPicker.Settings.CastOverheadSeconds;
        var dps = max > 0 && seconds > 0 ? hit * KillRate.LandChance(war, (uint)Math.Max(0, defence)) / seconds : 0;
        var threat = ThreatOf(session, clock, o, out var why);
        return new SwitchSide(o.Guid, o.Name ?? "creature", health, dps, threat, why);
    }

    /// <summary>
    /// The creature's damage a second on her: its kind's rate (<see cref="KindIncoming.Rate"/>: its measured hits on her and the derived rate of the ones of it on her) shared among the ones of it on her; a kind not heard yet
    /// (a creature she is bolting fights back) is one creature's derived rate; 0 with neither read.
    /// </summary>
    private double ThreatOf(IGameSessionView session, double clock, WorldObject o, out string why)
    {
        var state = session.State;
        var name = o.Name ?? string.Empty;
        var onHer = Math.Max(1, OnHerNamed(session, name));
        var one = DerivedOne(state, clock, o, out var derivedRead);
        if (_threatWatch is { } watch && watch.KindRead(name, clock, out var damage, out var hits, out var since, out _, out _))
        {
            var attacks = watch.AttacksOf(name, clock, out var span, out var heard) ? heard : hits;
            since = Math.Max(since, span);
            var whole = KindIncoming.Whole(since, attacks, out _);
            var kind = KindIncoming.Rate(damage, since, attacks, whole ? 0 : onHer * one, derivedRead && !whole, out _);
            why = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"its kind {kind:0.#}/s ({hits} hit(s) for {damage:0}, {attacks} attack(s) over {since:0.#} s{(derivedRead && !whole ? $", derived {one:0.#}/s each" : string.Empty)}) over the {onHer} of it on her");
            return kind / onHer;
        }

        why = derivedRead ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"not heard yet: its derived {one:0.#}/s") : "not heard and no derived rate read";
        return derivedRead ? one : 0;
    }

    /// <summary>One creature's derived damage a second on her (the vitals guard's derivation: the world database's attacks at ACE's attack timing, on her armour and live resistances).</summary>
    private double DerivedOne(GameState state, double clock, WorldObject o, out bool read)
    {
        read = false;
        if (CreatureFacts.All.Find(o.Weenie.WeenieClassId) is not { } fact)
        {
            return 0;
        }

        var line = _threatLine ??= new LiveFightLine { Times = AttackTimes };
        var timing = AttackTimes.Of(o.Weenie.WeenieClassId, o.Name ?? string.Empty);
        var factor = line.FactorFor(state, fact.DamageTypes, clock, out _);
        line.SpellFactorFor(state, 0); // her registry as now, for the spells' resistance
        read = true;
        return timing.PerSecondOn(FightLines.MeanHit(fact.Hitter), factor, line.SpellResistance, DefenceRead.Of(state.Self.Qualities)); // M6 14 the derived incoming: ACE's evade and resist rolls on her defences
    }

    /// <summary>The living creatures named <paramref name="name"/> within <see cref="HostileReach.DefaultMetres"/> of her where the session draws them (the vitals guard's "on her").</summary>
    private static int OnHerNamed(IGameSessionView session, string name)
    {
        var state = session.State;
        if (state.Self.Position is not { } at)
        {
            return 0;
        }

        var self = at.ToGlobal();
        var count = 0;
        foreach (var o in state.Objects.All)
        {
            if ((o.ItemType & HostileReach.CreatureItemType) != 0 && string.Equals(o.Name, name, StringComparison.Ordinal) && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord }
                && session.Live.At(o) is { } p && p.ToGlobal().DistanceTo(self) <= HostileReach.DefaultMetres)
            {
                count++;
            }
        }

        return count;
    }
}
