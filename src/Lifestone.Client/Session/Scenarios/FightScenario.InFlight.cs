using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 D124 (S1b, the owner: the error gone at its source): the mage's own projectiles in flight, and the picker's rule that no new cast starts at a creature they are predicted to kill. Soak round 12 cast 44 times at a creature
/// that died during the wind-up (ACE then answers 0x042C, TargetNotAcquired, Player_Magic.cs:137-140, 173-179, 197-203, 751-757 @95ef426d). Every one was killed by the mage's own earlier cast, confirmed 37-322 ms before the new one began:
/// in 24 a bolt, streak, volley or ring aimed at that same creature, in 20 a ring aimed at a neighbour whose projectiles reached it. The hit wait had gone back to the pick at once, on a damage line of an earlier projectile.
/// <para>A confirmed war cast is entered here with its mean hit (<see cref="WarSpellPicker.MeanHit"/>), the player's position (a ring's centre) and its time; it stays until its projectiles' flight over the target's distance (or the
/// ring's reach) at the spell's speed and <see cref="InFlightMarginSeconds"/> have passed. Each damage line of that spell on a creature takes one expected hit off it. A creature whose expected in-flight damage (the picker's hits
/// at its distance times the mean hit) reaches its remaining health in the live model (F20: the share times the ground profile's maximum) is doomed: the pick leaves it out (logged once and counted, <c>doomed_skips</c>), and with no
/// other target in reach the fight holds until the projectiles settle instead of walking. A creature with no maximum health in the profile (the avoided Virindi) cannot be predicted and is never left out.</para>
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The seconds a projectile is still counted in flight after its modelled arrival (Lifestone's own: the bolt's measured hit waits are a few tenths over the flight).</summary>
    public const double InFlightMarginSeconds = 0.5;

    private const int InFlightCap = 4;
    private const int InFlightHitCap = 8;

    private readonly InFlightCast[] _inFlight = [new(), new(), new(), new()];
    private readonly HashSet<uint> _doomedNoted = [];
    private int _doomedThisPick;
    private static readonly WarSpellPicker InFlightModel = new(new WarPickSettings());

    /// <summary>The creatures left out of a pick because a projectile in flight was predicted to kill them (each once per prediction; the verdict's <c>doomed_skips</c>).</summary>
    public int DoomedSkips { get; private set; }

    /// <summary>A confirmed war cast whose projectiles may still be flying.</summary>
    private sealed class InFlightCast
    {
        public bool Active;
        public uint Target;
        public WarOption Option = null!;
        public double MeanHit;
        public double Until;
        public GlobalPosition From;
        public string LineEnd = string.Empty;
        public readonly uint[] HitGuids = new uint[InFlightHitCap];
        public readonly int[] HitCounts = new int[InFlightHitCap];
        public int HitN;

        public int Seen(uint guid)
        {
            for (var i = 0; i < HitN; i++)
            {
                if (HitGuids[i] == guid)
                {
                    return HitCounts[i];
                }
            }

            return 0;
        }

        public void NoteHit(uint guid)
        {
            for (var i = 0; i < HitN; i++)
            {
                if (HitGuids[i] == guid)
                {
                    HitCounts[i]++;
                    return;
                }
            }

            if (HitN < InFlightHitCap)
            {
                (HitGuids[HitN], HitCounts[HitN]) = (guid, 1);
                HitN++;
            }
        }
    }

    /// <summary>The war option of <paramref name="spellId"/>: the built one when the picks hold it, else one made from the spell table and the projectile rows; null for a spell with no projectile row (a drain, a debuff).</summary>
    private WarOption? InFlightOptionOf(uint spellId)
    {
        if (_warPicks is { } picks)
        {
            foreach (var option in picks.Options)
            {
                if (option.Row.Id == spellId)
                {
                    return option;
                }
            }
        }

        return _spells.TryGetValue(spellId, out var info) && WarSpellData.TryGet(spellId, out var row) ? WarOptionOf(info, info.Name, row) : null;
    }

    private WarSpellPicker InFlightPicker => _warPicks?.Picker ?? InFlightModel;

    /// <summary>A war cast at <paramref name="target"/> was confirmed (its projectile exists): it is entered as in flight.</summary>
    internal void NoteInFlight(IGameSessionView session, double clock, uint spellId, uint target, double sentAt)
    {
        if (InFlightOptionOf(spellId) is not { } option || session.State.Self.Position is not { } here)
        {
            return;
        }

        GearCasts.TryCurrentSkill(session.State.Self.Qualities, MageTemplate.WarMagic, out var war, out _);
        var from = here.ToGlobal();
        var reach = option.Row.Kind == WarKind.Ring ? option.ReachMetres : session.State.Objects.TryGet(target, out var t) && StandsAt(session, t) is { } at ? at.ToGlobal().DistanceTo(from) : option.ReachMetres;
        var slot = _inFlight[0];
        foreach (var cast in _inFlight)
        {
            if (!cast.Active || cast.Until < slot.Until)
            {
                slot = cast;
                if (!cast.Active)
                {
                    break;
                }
            }
        }

        slot.Active = true;
        slot.Target = target;
        slot.Option = option;
        slot.MeanHit = InFlightPicker.MeanHit(option, war);
        slot.Until = clock + (option.Row.SpeedMetresPerSecond > 0 ? reach / option.Row.SpeedMetresPerSecond : 0) + InFlightMarginSeconds;
        slot.From = from;
        slot.LineEnd = " with " + option.Name + ".";
        slot.HitN = 0;
        ClaimEarlyHits(slot, sentAt);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"in flight: {option.Name} at 0x{target:X8}, mean hit {slot.MeanHit:0}, counted until {slot.Until - clock:0.0#} s from now ({option.Row.Kind.ToString().ToLowerInvariant()}, {reach:0.#} m at {option.Row.SpeedMetresPerSecond:0.#} m/s)"));
    }

    /// <summary>A damage line of a spell in flight landed on <paramref name="guid"/>: one expected hit fewer on it, taken from the oldest cast of that spell that still expects a hit there (the first sent lands first).</summary>
    internal void NoteInFlightHit(uint guid, string text)
    {
        InFlightCast? oldest = null;
        foreach (var cast in _inFlight)
        {
            if (cast.Active && text.EndsWith(cast.LineEnd, StringComparison.Ordinal) && (oldest is null || cast.Until < oldest.Until)
                && cast.Seen(guid) < Math.Max(1.0, InFlightPicker.RawHits(cast.Option, 0, castTarget: guid == cast.Target)))
            {
                oldest = cast;
            }
        }

        if (oldest is not null)
        {
            oldest.NoteHit(guid);
            return;
        }

        // A hit no cast in flight claims: kept a moment, for a cast whose projectile and hit came in the same update, before the fight entered it (NoteInFlight claims it).
        _unclaimed[_unclaimedNext] = (guid, text, _lastClock);
        _unclaimedNext = (_unclaimedNext + 1) % _unclaimed.Length;
    }

    /// <summary>
    /// M6 7 AA: "<paramref name="victim"/> resists your spell": the oldest cast still expecting a hit on a creature of that name loses one (a resisted projectile is in flight no more: left in, the pick would hold for a kill that cannot come, and the finishing bolt would count it).
    /// The line names no guid: the cast's own target is the one meant, as two tuskers share a name. When the current target has every projectile of its cast answered, the hit wait has its answer (<c>ResistEndsWait</c>).
    /// </summary>
    private void NoteInFlightResist(IGameSessionView session, string victim)
    {
        var objects = session.State.Objects;
        InFlightCast? oldest = null;
        foreach (var cast in _inFlight)
        {
            if (cast.Active && (oldest is null || cast.Until < oldest.Until) && cast.Seen(cast.Target) < ProjectilesAt(cast) && objects.TryGet(cast.Target, out var t) && t.Name == victim)
            {
                oldest = cast;
            }
        }

        if (oldest is null)
        {
            if (objects.TryGet(_target, out var target) && target.Name == victim)
            {
                _boltResists++; // its projectile and its resist came before the fight entered it
            }

            return;
        }

        oldest.NoteHit(oldest.Target);
        if (oldest.Target == _target && oldest.Seen(_target) >= ProjectilesAt(oldest))
        {
            _boltResists++;
        }
    }

    /// <summary>The projectiles of <paramref name="cast"/> aimed at its target, at least one.</summary>
    private double ProjectilesAt(InFlightCast cast) => Math.Max(1.0, InFlightPicker.RawHits(cast.Option, 0, castTarget: true));

    /// <summary>
    /// M6 7 AA: true when the projectiles in flight are expected to kill <paramref name="guid"/> (the sum <see cref="ExcludeDoomed"/> reads: its live health; a target with no maximum health never); <paramref name="seconds"/> is what is left of the last one's flight on it.
    /// </summary>
    private bool InFlightFinishes(IGameSessionView session, uint guid, double clock, out double seconds)
    {
        seconds = 0;
        if (!session.State.Objects.TryGet(guid, out var o) || StandsAt(session, o) is not { } at)
        {
            return false;
        }

        var max = TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
        var expected = max > 0 ? InFlightDamageOn(guid, at.ToGlobal(), clock) : 0;
        if (expected <= 0 || expected < LiveHealthOrFull(o) * max)
        {
            return false;
        }

        foreach (var cast in _inFlight)
        {
            if (cast.Active && cast.Target == guid)
            {
                seconds = Math.Max(seconds, cast.Until - InFlightMarginSeconds - clock);
            }
        }

        return true;
    }

    private readonly (uint Guid, string? Text, double At)[] _unclaimed = new (uint, string?, double)[4];
    private int _unclaimedNext;

    /// <summary>The hits no cast claimed that landed since <paramref name="sentAt"/> on <paramref name="cast"/>'s creatures with its spell: they are that cast's own (its projectile and its hit came before the fight entered it).</summary>
    private void ClaimEarlyHits(InFlightCast cast, double sentAt)
    {
        for (var i = 0; i < _unclaimed.Length; i++)
        {
            if (_unclaimed[i] is { Text: { } text } hit && hit.At >= sentAt && text.EndsWith(cast.LineEnd, StringComparison.Ordinal))
            {
                cast.NoteHit(hit.Guid);
                _unclaimed[i] = default;
            }
        }
    }

    /// <summary>The damage the projectiles in flight are expected to deal <paramref name="guid"/> standing at <paramref name="at"/> (the model's hits at its distance from each cast's origin, less the hits seen, times the mean hit).</summary>
    internal double InFlightDamageOn(uint guid, GlobalPosition at, double clock)
    {
        var sum = 0.0;
        foreach (var cast in _inFlight)
        {
            if (!cast.Active)
            {
                continue;
            }

            if (clock > cast.Until)
            {
                cast.Active = false;
                continue;
            }

            var hits = InFlightPicker.Hits(cast.Option, at.DistanceTo(cast.From), castTarget: guid == cast.Target) - cast.Seen(guid);
            if (hits > 0)
            {
                sum += hits * cast.MeanHit;
            }
        }

        return sum;
    }

    /// <summary>
    /// Before a pick: every creature that the projectiles in flight are predicted to kill goes into the pick's exclusions (logged once a prediction and counted). True when any was left out. Allocation-free while nothing is in flight.
    /// </summary>
    private bool ExcludeDoomed(IGameSessionView session, double clock)
    {
        _doomedThisPick = 0;
        var any = false;
        foreach (var cast in _inFlight)
        {
            any |= cast.Active && clock <= cast.Until;
        }

        if (!any)
        {
            _doomedNoted.Clear();
            return false;
        }

        foreach (var o in session.State.Objects.All)
        {
            if ((o.ItemType & WarCreatureItemType) == 0 || StandsAt(session, o) is not { } p || o.Health is <= 0f || _killed.Contains(o.Guid))
            {
                continue;
            }

            var max = TargetStatsOf(o.Weenie.WeenieClassId).MaxHealth;
            if (max <= 0)
            {
                continue; // no maximum health in the ground profile: the damage cannot be set against it
            }

            var expected = InFlightDamageOn(o.Guid, p.ToGlobal(), clock);
            if (expected <= 0)
            {
                continue;
            }

            var remaining = LiveHealthOrFull(o) * max;
            if (expected < remaining)
            {
                continue;
            }

            _doomedThisPick++;
            _pickExcluded.Add(o.Guid);
            if (_doomedNoted.Add(o.Guid))
            {
                DoomedSkips++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pick: 0x{o.Guid:X8} ({o.Name}) is left out: the projectiles in flight are expected to deal {expected:0} against its {remaining:0} health left (live model); a new cast would find it dead at the release (0x042C) (doomed_skips {DoomedSkips})"));
            }
        }

        return _doomedThisPick > 0;
    }
}
