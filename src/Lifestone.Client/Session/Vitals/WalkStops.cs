using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One stop of a guarded walk by attackers: how far she stood from the walk's goal when it stopped, and what its fight-back did.</summary>
/// <param name="Metres">Her planar distance to the walk's goal when the walk was stopped.</param>
/// <param name="Casts">The fight-back's casts.</param>
/// <param name="Kills">The fight-back's kills.</param>
public readonly record struct WalkStop(double Metres, int Casts, int Kills);

/// <summary>The rule's answer: retreat or fight on, in words with its numbers.</summary>
/// <param name="Retreat">True: the walk retreats (<c>why=cap</c>).</param>
/// <param name="Why">The answer in words.</param>
public readonly record struct WalkStopVerdict(bool Retreat, string Why);

/// <summary>
/// M6 14 the crowded passage (live ladder44, the Crater Pathway 0x01A7, 2026-11-16 22:26:23-22:30:07 UTC: the walk to the Surface portal's front was stopped 8 times by Jibrit, Kilif and Zofrit Zefirs and Dark Wisps; every fight-back killed what it cast
/// at, 24 casts for 24 kills; she stood 457, 285, 268, 266, 255, 182, 165 and 95 m from the front at the stops, gaining each time; the fixed cap of 8 stops recalled her 95 m short of the portal while the guard read her race winning, then holding):
/// a walk's stops by attackers are judged by progress and by the fight, not by their count. Pure. The safety lines (her health under the derived retreat line, her mana at the way out's reserve) stand as before and are read elsewhere.
/// <list type="bullet">
/// <item><b>Stalled</b>: over the last <see cref="StallStops"/> stops she gained under <see cref="RunPast.BlockedGainMetres"/> on the goal (the walk is not getting through): retreat.</item>
/// <item><b>Not winning</b>: the last <see cref="StallStops"/> fight-backs cast and killed nothing (attackers her casts do not kill): retreat.</item>
/// <item>Else she fights on: the walk is gaining ground and the fight-backs kill.</item>
/// </list>
/// The window is <see cref="StallStops"/> stops (Lifestone's own, as the fight's misses before it walks, <c>FightScenario.MissesBeforeWalking</c>); with fewer stops she fights on. A distance not read (no goal, no position) falls back to the count
/// cap the caller names.
/// </summary>
public static class WalkStops
{
    /// <summary>The stops the rule reads back over.</summary>
    public const int StallStops = 3;

    /// <summary>The verdict at a new stop, <paramref name="metresNow"/> from the goal, after <paramref name="stops"/> (the earlier stops, first first).</summary>
    /// <param name="stops">The walk's earlier stops.</param>
    /// <param name="metresNow">Her distance to the goal now.</param>
    public static WalkStopVerdict Judge(IReadOnlyList<WalkStop> stops, double metresNow)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var c = CultureInfo.InvariantCulture;
        if (stops.Count < StallStops)
        {
            return new(false, string.Create(c, $"stop {stops.Count + 1}, {metresNow:0} m from the goal: fewer than {StallStops} stops to judge"));
        }

        var back = stops[^StallStops];
        var gained = back.Metres - metresNow;
        var recent = stops.Skip(stops.Count - StallStops).ToList();
        var (casts, kills) = (recent.Sum(s => s.Casts), recent.Sum(s => s.Kills));
        if (gained < RunPast.BlockedGainMetres)
        {
            return new(true, string.Create(c, $"stalled: {gained:0.#} m gained on the goal over the last {StallStops} stops ({back.Metres:0} m then, {metresNow:0} m now; under {RunPast.BlockedGainMetres:0} m)"));
        }

        if (kills == 0)
        {
            return new(true, string.Create(c, $"not winning: the last {StallStops} fight-backs made {casts} cast(s) and no kill"));
        }

        return new(false, string.Create(c, $"gaining: {gained:0} m on the goal over the last {StallStops} stops ({back.Metres:0} m then, {metresNow:0} m now), their fight-backs {kills} kill(s) in {casts} cast(s): she fights on"));
    }
}
