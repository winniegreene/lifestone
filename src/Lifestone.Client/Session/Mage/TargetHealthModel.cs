using System.Globalization;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where a creature's health in <see cref="TargetHealthModel"/> came from (the decide line names it).</summary>
public enum HealthSource
{
    /// <summary>Never told, and nothing dealt to it: unknown, never taken as full.</summary>
    Unknown,

    /// <summary>The server pushed it (UpdateHealth, GameEvent 0x01C0): ACE pushes the selected target's health on every change and once per player heartbeat (Creature.OnHealthUpdate, Creature.cs 369-378; Player.HandleTargetVitals, Player_Vitals.cs 164-174 @95ef426d).</summary>
    Pushed,

    /// <summary>The answer to our own QueryHealth (GameAction 0x01BF), which also makes that creature the selected target (Player.cs 393-413).</summary>
    Queried,

    /// <summary>Worked out from the last value and what our own spells took from it (a drain's source change, a war spell's damage line), or from the damage dealt to a creature never asked.</summary>
    Modelled,
}

/// <summary>
/// M5 7 F20: one live health model per creature for the whole fight (the owner: track the tuskers' health from the network traffic). Engine-free. The server's pushes and answers set a creature's health fraction;
/// what the mage takes from it afterwards (a drain's "You gain N" over (1 - loss), a war spell's "for N points") reduces it, so every provider reads one value that falls with our own casts; the next push or answer
/// replaces it. A creature never told is unknown (<see cref="HealthSource.Unknown"/>), not full. Also keeps the measured drain yield per creature: a drain that gave little sets drains on that creature off until a fresher
/// health says otherwise (F10's picker chose Drain Health Other VI seven times on a Slave whose 43 % was a stale value: it gave 51, 4, 3, 2, 1, 1, 0).
/// </summary>
public sealed class TargetHealthModel
{
    /// <summary>A health this many seconds old is fresh for a drain's choice (Lifestone's own, the brief's); a selected target's pushed value stays fresh for <see cref="SelectedHeartbeatSeconds"/>.</summary>
    public const double FreshSeconds = 2.0;

    /// <summary>The longest an unchanged value of the selected target stays fresh: the server pushes on every change and on its heartbeat, so a quiet selected target is still right (Lifestone's own, over ACE's heartbeat).</summary>
    public const double SelectedHeartbeatSeconds = 6.0;

    /// <summary>The longest a QueryHealth is awaited before the choice goes on the model (the brief).</summary>
    public const double QueryWaitSeconds = 1.0;

    /// <summary>A drain that gave under this many points sets drains on that creature off (the owner's 20; <c>MeasuredHeals.MinDrainHeal</c> is the same figure).</summary>
    public const double MinDrainYield = 20;

    /// <summary>A drain that gave under this share of the model's expectation sets drains on that creature off.</summary>
    public const double MinYieldShare = 0.5;

    /// <summary>A pushed or queried health this far over the one that set drains off lifts it (the creature healed, or the model was wrong the other way).</summary>
    public const float LiftMargin = 0.05f;

    private sealed class Entry
    {
        public float Fraction;
        public bool Known;
        public HealthSource Source;
        public double At = double.NegativeInfinity;
        public double Dealt;
        public bool DrainOff;
        public float OffFraction;
        public double LastGave;
        public double AskedAt = double.NegativeInfinity;
        public string Why = string.Empty;
        public bool Pending;
    }

    private readonly Dictionary<uint, Entry> _entries = [];

    /// <summary>The creature the server holds as the player's selected target (the last one asked about; 0: none).</summary>
    public uint Selected { get; private set; }

    private Entry EntryOf(uint guid)
    {
        if (!_entries.TryGetValue(guid, out var e))
        {
            _entries[guid] = e = new Entry();
        }

        return e;
    }

    /// <summary>A QueryHealth was sent for <paramref name="guid"/> at <paramref name="clock"/>: it is the selected target now, and its answer is the truth when it comes.</summary>
    public void Asked(uint guid, double clock)
    {
        Selected = guid;
        var e = EntryOf(guid);
        (e.AskedAt, e.Pending) = (clock, true);
    }

    /// <summary>The server's answer or push (UpdateHealth) for <paramref name="guid"/>: the fraction is the truth, whatever the model held.</summary>
    public void Updated(uint guid, float fraction, double clock)
    {
        var e = EntryOf(guid);
        e.Source = e.Pending ? HealthSource.Queried : HealthSource.Pushed;
        e.Pending = false;
        if (e.DrainOff && fraction > e.OffFraction + LiftMargin)
        {
            e.DrainOff = false; // a fresher health that says otherwise
        }

        (e.Fraction, e.Known, e.At, e.Dealt) = (Math.Clamp(fraction, 0f, 1f), true, clock, 0);
    }

    /// <summary>A health the object table already held (before the fight listened): kept as the last value, not fresh.</summary>
    public void Seed(uint guid, float fraction)
    {
        var e = EntryOf(guid);
        if (!e.Known)
        {
            (e.Fraction, e.Known, e.Source, e.At) = (Math.Clamp(fraction, 0f, 1f), true, HealthSource.Pushed, double.NegativeInfinity);
        }
    }

    /// <summary>Our own spell took <paramref name="points"/> from <paramref name="guid"/>: a known health falls by that share of <paramref name="maxHealth"/>; an unknown one collects the damage for the estimate.</summary>
    /// <param name="guid">The creature.</param>
    /// <param name="points">What the spell took.</param>
    /// <param name="maxHealth">Its maximum health (the profile's; 0: unknown).</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="since">When the cast began: a push or answer after it already holds what the spell took (ACE pushes the change as it happens, before the chat line), so nothing is taken twice.</param>
    public void Took(uint guid, double points, double maxHealth, double clock, double since = double.PositiveInfinity)
    {
        var e = EntryOf(guid);
        if (e.Known && e.At >= since && e.Source is HealthSource.Pushed or HealthSource.Queried)
        {
            return;
        }

        points = Math.Max(0, points);
        e.Dealt += points;
        e.At = clock;
        if (e.Known && maxHealth > 0)
        {
            e.Fraction = (float)Math.Clamp(e.Fraction - (points / maxHealth), 0, 1);
            e.Source = HealthSource.Modelled;
        }
        else if (maxHealth > 0)
        {
            e.Source = HealthSource.Modelled;
        }
    }

    /// <summary>
    /// The health share of <paramref name="guid"/> and where it came from. A creature never told and with nothing dealt is <see cref="HealthSource.Unknown"/> (false); one never told but hurt by us is estimated from the
    /// profile's <paramref name="maxHealth"/> less the damage dealt (<see cref="HealthSource.Modelled"/>).
    /// </summary>
    public bool TryGet(uint guid, double maxHealth, out float fraction, out HealthSource source)
    {
        if (_entries.TryGetValue(guid, out var e))
        {
            if (e.Known)
            {
                (fraction, source) = (e.Fraction, e.Source);
                return true;
            }

            if (e.Dealt > 0 && maxHealth > 0)
            {
                (fraction, source) = ((float)Math.Clamp(1 - (e.Dealt / maxHealth), 0, 1), HealthSource.Modelled);
                return true;
            }
        }

        (fraction, source) = (0f, HealthSource.Unknown);
        return false;
    }

    /// <summary>The damage the mage has dealt <paramref name="guid"/> since the server last told its health.</summary>
    public double DealtSince(uint guid) => _entries.TryGetValue(guid, out var e) ? e.Dealt : 0;

    /// <summary>The seconds since the value of <paramref name="guid"/> was last set by the server or by our own cast (infinity: never).</summary>
    public double AgeOf(uint guid, double clock) => _entries.TryGetValue(guid, out var e) ? clock - e.At : double.PositiveInfinity;

    /// <summary>
    /// True when a drain may be chosen on <paramref name="guid"/>: its health is known and either set within <see cref="FreshSeconds"/> (by the server or by our last cast on it) or the creature is the selected target and its
    /// value was told within <see cref="SelectedHeartbeatSeconds"/> (the server pushes every change).
    /// </summary>
    public bool Fresh(uint guid, double clock)
    {
        if (!_entries.TryGetValue(guid, out var e) || (!e.Known && e.Dealt <= 0))
        {
            return false;
        }

        var age = clock - e.At;
        return age <= FreshSeconds || (guid == Selected && e.Known && e.Source != HealthSource.Modelled && age <= SelectedHeartbeatSeconds);
    }

    /// <summary>True while a QueryHealth for <paramref name="guid"/> is awaited (asked within <see cref="QueryWaitSeconds"/>, not yet answered).</summary>
    public bool Waiting(uint guid, double clock) => _entries.TryGetValue(guid, out var e) && e.Pending && clock - e.AskedAt < QueryWaitSeconds;

    /// <summary>True when a QueryHealth for <paramref name="guid"/> went unanswered for over <see cref="QueryWaitSeconds"/> and is not to be sent again yet (five times the wait).</summary>
    public bool AskedRecently(uint guid, double clock) => _entries.TryGetValue(guid, out var e) && e.Pending && clock - e.AskedAt < QueryWaitSeconds * 5;

    /// <summary>
    /// A landed drain gave the mage <paramref name="gain"/> health where <paramref name="expected"/> was expected (the model's health, cut to the mage's missing health). Under <see cref="MinDrainYield"/> (or the mage's whole
    /// missing health, when that was less) or under <see cref="MinYieldShare"/> of the expectation, drains on the creature are off. True when this call turned them off.
    /// </summary>
    public bool NoteYield(uint guid, double gain, double expected, double missingBefore)
    {
        var e = EntryOf(guid);
        e.LastGave = gain;
        var floor = Math.Min(MinDrainYield, missingBefore);
        if (gain >= floor && gain >= expected * MinYieldShare)
        {
            return false;
        }

        var wasOff = e.DrainOff;
        (e.DrainOff, e.OffFraction) = (true, e.Known ? e.Fraction : 0f);
        e.Why = string.Create(CultureInfo.InvariantCulture, $"drain off on 0x{guid:X8}: last gave {gain:0.#}");
        return !wasOff;
    }

    /// <summary>True, with the reason, when drains on <paramref name="guid"/> are off.</summary>
    public bool DrainOff(uint guid, out string why)
    {
        if (_entries.TryGetValue(guid, out var e) && e.DrainOff)
        {
            why = e.Why;
            return true;
        }

        why = string.Empty;
        return false;
    }

    /// <summary>The name of a source for the log.</summary>
    public static string Name(HealthSource source) => source switch
    {
        HealthSource.Pushed => "pushed",
        HealthSource.Queried => "queried",
        HealthSource.Modelled => "modelled",
        _ => "unknown",
    };

    /// <summary>Forgets one creature that left the table (one at a time, allocation-free).</summary>
    public void Forget(uint guid) => _entries.Remove(guid);

    /// <summary>The guids held (the caller prunes the ones the table lost).</summary>
    public IEnumerable<uint> Guids => _entries.Keys;
}
