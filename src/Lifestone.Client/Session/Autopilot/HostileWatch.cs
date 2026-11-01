using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 F23 (soak round 2's death): whether a hostile is on the mage, read the same way in every autopilot state. A hostile is in reach when a living target creature of the ground (the profile's targets: any ground, D46) stands within
/// the aggro reach (<see cref="DeathOptions.TripAggroMetres"/>, F19's registered distance, 20 m by default), or when a creature hit the player (F4's attack notice, GameEvent 0x01B2 DefenseHit) within <see cref="AttackQuietSeconds"/>.
/// It also keeps the damage taken over the last <see cref="RateWindowSeconds"/> (F16's incoming rate) for the vitals guard's emergency line. Allocation-free after construction.
/// </summary>
public sealed class HostileWatch : IDisposable
{
    /// <summary>The seconds after an attack notice during which the mage counts as attacked (as the re-buff driver's <see cref="RebuffDriver.FightQuietSeconds"/>).</summary>
    public const double AttackQuietSeconds = RebuffDriver.FightQuietSeconds;

    /// <summary>The window of the incoming damage rate, in seconds (Lifestone's own; the fight's rate is its own measure, F16).</summary>
    public const double RateWindowSeconds = 10;

    private const int Ring = 64;
    private const uint CreatureItemType = 0x10; // ACE ItemType.Creature

    private readonly IGameSessionView _session;
    private readonly IReadOnlyList<uint> _targets;
    private readonly double[] _hitAt = new double[Ring];
    private readonly uint[] _hitDamage = new uint[Ring];
    private readonly IDisposable _subscription;
    private readonly IDisposable _chatSubscription;
    private int _next;

    /// <summary>The watch over a session on a ground.</summary>
    /// <param name="session">The session (its bus gives the attack notices; its read model the creatures).</param>
    /// <param name="ground">The ground (its target creatures).</param>
    /// <param name="reachMetres">The aggro reach in metres (0: only the attack notices count).</param>
    public HostileWatch(IGameSessionView session, HuntingGround ground, double reachMetres)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ground);
        _session = session;
        _targets = ground.TargetWcids();
        ReachMetres = Math.Max(0, reachMetres);
        Array.Fill(_hitAt, double.NegativeInfinity);
        _subscription = session.Events.Subscribe<CombatEvent>(OnCombat);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>The aggro reach in metres.</summary>
    public double ReachMetres { get; }

    /// <summary>The session time of the last attack notice, or negative infinity.</summary>
    public double LastAttackAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Who hit the mage last (the notice's attacker), or empty.</summary>
    public string LastAttacker { get; private set; } = string.Empty;

    /// <summary>The damage taken a second over the last <see cref="RateWindowSeconds"/> at <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    public double IncomingPerSecond(double now)
    {
        var sum = 0u;
        for (var i = 0; i < Ring; i++)
        {
            if (now - _hitAt[i] <= RateWindowSeconds)
            {
                sum += _hitDamage[i];
            }
        }

        return sum / RateWindowSeconds;
    }

    /// <summary>True when a hostile is in reach at <paramref name="now"/>: an attack notice within <see cref="AttackQuietSeconds"/>, or a living target creature within <see cref="ReachMetres"/>. <paramref name="what"/> names it (allocates only then).</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="what">What holds: the attacker or the creature and its distance.</param>
    public bool InReach(double now, out string what)
    {
        if (now - LastAttackAt < AttackQuietSeconds)
        {
            what = string.Create(CultureInfo.InvariantCulture, $"{LastAttacker} attacked {now - LastAttackAt:0.#} s ago");
            return true;
        }

        if (Nearest(out var guid, out var name, out var metres))
        {
            what = string.Create(CultureInfo.InvariantCulture, $"{name} 0x{guid:X8} at {metres:0.#} m (reach {ReachMetres:0.#} m)");
            return true;
        }

        what = string.Empty;
        return false;
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
        foreach (var o in state.Objects.All)
        {
            if (o.Position is not { } p || (o.ItemType & CreatureItemType) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || !Listed(o.Weenie.WeenieClassId))
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
        if (e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System
            || FightTally.Classify(e.Line.Text, out var amount, out _) != MagicLineKind.DamageTaken
            || !FightScenario.HostileSpellSource(e.Line.Text, out var source, out _))
        {
            return;
        }

        NoteHit(source, (uint)Math.Clamp(amount, 0, uint.MaxValue));
    }

    private void NoteHit(string who, uint damage)
    {
        var now = _session.Clock.Now;
        (LastAttackAt, LastAttacker) = (now, who);
        (_hitAt[_next], _hitDamage[_next]) = (now, damage);
        _next = (_next + 1) % Ring;
    }
}
