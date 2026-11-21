using System.Collections.Concurrent;
using System.Collections.Frozen;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 drain resistance (live 2026-11-14 19:42 UTC, the Tusker Armory: under several Armored Tuskers at about 8 health a second the climber's emergency heal picked Drain Health Other I, III and IV on three of them, each gave 0
/// ("You gain 0 points of health due to casting ..."), and she died 12 s later; Heal Self VI was worth about 9.4 health a second). The share of a Drain Health Other's take, and of a Harm Other's, that a creature kind lets through:
/// ACE's <c>HandleCastSpell_Transfer</c> takes <c>round(current health x Proportion x drainMod)</c> before the TransferCap (<c>WorldObject_Magic.cs</c> 719-726 @95ef426d), and <c>HandleCastSpell_Boost</c> scales a Harm Other's roll by
/// the same mod (474-491); the mod is the target's <c>GetResistanceMod(HealthDrain)</c>, its <c>ResistHealthDrain</c> (float property 125) times a natural resistance of 1 and its life-resist rating, which no creature of the profiles has
/// (<c>Creature_Properties.cs</c> 154-158, 207-208). Closes <c>TODO(m5-drain-resistance-mods)</c>.
/// </summary>
/// <remarks>
/// <para><b>The fact.</b> Every ground profile's creature entries carry their kind's value where the world copy says something other than 1 (<c>resist_health_drain</c>: a target's, an approach hostile's, an outdoor creature's;
/// <see cref="GroundTarget.ResistHealthDrain"/>); a kind no profile names reads as 1, ACE's default. A kind recorded twice keeps the lower value (the safe side: a heal pick that expects less from a drain never stays on it too long).</para>
/// <para><b>The measured override.</b> A landed drain that gave 0 where the model expected at least <see cref="TargetHealthModel.MinDrainYield"/> (the target's health, cut to her missing health: only the creature can have
/// taken it to 0) sets that kind to 0 for the rest of the process (<see cref="NoteMeasuredZero"/>; the caller logs it once by name). Process-wide, as the catalog is: a ConcurrentDictionary, as <c>LadderLoader</c>'s cache.</para>
/// </remarks>
public static class DrainResist
{
    private static readonly Lazy<FrozenDictionary<uint, double>> Catalog = new(() => Recorded(GroundCatalog.All));
    private static readonly ConcurrentDictionary<uint, string> MeasuredZero = new();

    /// <summary>
    /// The health-drain resistance of weenie class <paramref name="wcid"/> now: 0 once a drain measured it so (<see cref="NoteMeasuredZero"/>), else the profiles' value, else 1. Allocates nothing.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static double Of(uint wcid) => MeasuredZero.ContainsKey(wcid) ? 0.0 : RecordedOf(wcid);

    /// <summary>
    /// The profiles' value for <paramref name="wcid"/>, else (M6 14 walk drains: the server's database holds every creature) the world database's (<see cref="Grounds.CreatureTable"/>), else 1, ACE's default (the measured
    /// override aside).
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static double RecordedOf(uint wcid) => Catalog.Value.TryGetValue(wcid, out var resist) ? resist : Grounds.CreatureTable.Find(wcid)?.ResistHealthDrain ?? 1.0;

    /// <summary>
    /// M6 undrainable fills: the stamina-drain resistance of weenie class <paramref name="wcid"/>, its <c>ResistStaminaDrain</c> (float property 72) as the bestiary records it from the world database
    /// (<see cref="Creatures.CreatureDefence.StaminaDrainResist"/>, the one reader of the creature tables, stage E1 of docs/automation-engine.md; ACE's <c>GetResistanceMod(StaminaDrain)</c> is it times a creature's natural resistance,
    /// which is 1: Creature_Properties.cs 154-158, 211-212 @95ef426d), else 1, ACE's default. An Armored Tusker's is 0: a Drain Stamina Other on it takes nothing. The first call loads the bestiary; after that it allocates nothing.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static double StaminaOf(uint wcid) => Creatures.Bestiary.Find(wcid)?.Defence.StaminaDrainResist ?? 1.0;

    /// <summary>True when a landed drain measured kind <paramref name="wcid"/> at 0 in this process.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsMeasuredZero(uint wcid) => MeasuredZero.ContainsKey(wcid);

    /// <summary>
    /// A landed drain on a creature of kind <paramref name="wcid"/> gave 0 where the model expected a real heal: the kind's resistance is 0 for the rest of the process. True the first time for that kind (the caller logs it once).
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="name">The creature's name (kept for the log).</param>
    public static bool NoteMeasuredZero(uint wcid, string name) => MeasuredZero.TryAdd(wcid, name);

    /// <summary>Forgets the measured overrides (tests).</summary>
    internal static void Clear() => MeasuredZero.Clear();

    /// <summary>Forgets one kind's measured override (tests that run beside others).</summary>
    /// <param name="wcid">The weenie class id.</param>
    internal static void Forget(uint wcid) => MeasuredZero.TryRemove(wcid, out _);

    /// <summary>The values other than 1 that <paramref name="grounds"/> record, by weenie class: targets, approach hostiles and outdoor creatures; a kind recorded twice keeps the lower.</summary>
    /// <param name="grounds">The ground profiles.</param>
    public static FrozenDictionary<uint, double> Recorded(IEnumerable<HuntingGround> grounds)
    {
        ArgumentNullException.ThrowIfNull(grounds);
        var found = new Dictionary<uint, double>();
        foreach (var g in grounds)
        {
            foreach (var t in g.Targets)
            {
                Keep(found, t.Wcid, t.ResistHealthDrain);
            }

            foreach (var h in g.Approach?.Hostiles ?? [])
            {
                Keep(found, h.Wcid, h.ResistHealthDrain);
            }

            foreach (var c in g.Outdoor?.Creatures ?? [])
            {
                Keep(found, c.Wcid, c.ResistHealthDrain);
            }
        }

        return found.ToFrozenDictionary();
    }

    private static void Keep(Dictionary<uint, double> found, uint wcid, double resist)
    {
        if (resist == 1.0)
        {
            return;
        }

        found[wcid] = found.TryGetValue(wcid, out var had) ? Math.Min(had, resist) : resist;
    }
}
