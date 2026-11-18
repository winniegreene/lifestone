using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AY: what the big pack rule remembers of a ground, kept in the autopilot's checkpoint (<c>AutopilotCheckpoint.PackHold</c>) and read back at every start, whatever the run's tag: a restart does not forget it, a re-created character does (its guid is another).
/// </summary>
/// <param name="Ground">The ground's key.</param>
/// <param name="Deaths">The deaths to a big pack on it since the last lift.</param>
/// <param name="Held">True while she is dropped back to the first slots.</param>
/// <param name="ManaAtDrop">Her base mana when she was dropped back; 0 before.</param>
public sealed record PackHold(string Ground, int Deaths, bool Held, uint ManaAtDrop)
{
    /// <summary>Nothing remembered.</summary>
    public static PackHold None { get; } = new(string.Empty, 0, false, 0);
}

/// <summary>
/// M6 7 AY (the owner's decision of 2026-11-06): big packs stay allowed on a ground with a drop-back (<see cref="GroundHunt.HasDropBack"/>, the Lodge). A death with <see cref="LadderBigPack.Awake"/> or more creatures awake around her counts; <see cref="LadderBigPack.Deaths"/> of them drop her back to the
/// ground's first slots (<see cref="GroundHunt.DropBack"/> held out of the hunt), by a logged line, until her base mana has grown by <see cref="LadderBigPack.ManaGrows"/>. Nothing here touches the rung: the tusk job goes ahead meanwhile, and the Burrow's own keep-out is its own. Pure.
/// </summary>
public static class BigPackRule
{
    /// <summary>Her base mana: Max Mana's start and ranks plus base Self, no buff: a buff lapses, so it never lifts the hold (the retail formula of <see cref="PlayerQualities.TryGetVitalLevels"/> without enchantments).</summary>
    /// <param name="q">The player's qualities.</param>
    /// <returns>0 when not read.</returns>
    public static uint BaseMana(PlayerQualities q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return q.TryGetVital(MageTemplate.MaxMana, out var vital) && q.TryGetAttribute(MageTemplate.Self, out var self) ? vital.Start + vital.Ranks + self.Start + self.Ranks : 0;
    }

    /// <summary>The state after a death.</summary>
    /// <param name="hold">The state before.</param>
    /// <param name="rule">The ladder's values.</param>
    /// <param name="ground">The ground she died on, or null when it has no drop-back or she died off it.</param>
    /// <param name="awake">The creatures awake around her at the death (<see cref="LadderBigPack.ReachMetres"/>).</param>
    /// <param name="baseMana">Her base mana.</param>
    /// <param name="line">The logged line, or null on a ground with no drop-back.</param>
    public static PackHold OnDeath(PackHold hold, LadderBigPack rule, HuntingGround? ground, int awake, uint baseMana, out string? line)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(rule);
        line = null;
        if (ground?.Hunt is not { HasDropBack: true })
        {
            return hold;
        }

        var c = CultureInfo.InvariantCulture;
        var same = string.Equals(hold.Ground, ground.Key, StringComparison.Ordinal) ? hold : PackHold.None with { Ground = ground.Key };
        if (awake < rule.Awake)
        {
            line = string.Create(c, $"pack: death on {ground.Key}: {awake} awake within {rule.ReachMetres:0.#} m, under {rule.Awake}: no big pack; {same.Deaths} of {rule.Deaths} counted");
            return same;
        }

        var deaths = same.Deaths + 1;
        if (same.Held)
        {
            line = string.Create(c, $"pack: death on {ground.Key}: {awake} awake within {rule.ReachMetres:0.#} m: a big pack, she is already dropped back (base mana at the drop {same.ManaAtDrop})");
            return same with { Deaths = deaths };
        }

        if (deaths < rule.Deaths)
        {
            line = string.Create(c, $"pack: death on {ground.Key}: {awake} awake within {rule.ReachMetres:0.#} m: big pack death {deaths} of {rule.Deaths}; big packs stay allowed");
            return same with { Deaths = deaths };
        }

        line = string.Create(c, $"pack: drop-back: {deaths} deaths to big packs on {ground.Key} ({awake} awake at the last): she holds to the first slots, {ground.Hunt.DropBack.Cells.Count} cell(s) out of the hunt, until her base mana grows by {rule.ManaGrows} from {baseMana} to {baseMana + (uint)rule.ManaGrows}; the tusk job goes ahead meanwhile");
        return same with { Deaths = deaths, Held = true, ManaAtDrop = baseMana };
    }

    /// <summary>The state after reading her mana: the hold lifts when it has grown enough, and the count starts again.</summary>
    /// <param name="hold">The state before.</param>
    /// <param name="rule">The ladder's values.</param>
    /// <param name="baseMana">Her base mana; 0 is not read and lifts nothing.</param>
    /// <param name="line">The logged line when it lifted, else null.</param>
    public static PackHold Lift(PackHold hold, LadderBigPack rule, uint baseMana, out string? line)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(rule);
        line = null;
        if (!hold.Held || baseMana == 0 || baseMana < hold.ManaAtDrop + (uint)rule.ManaGrows)
        {
            return hold;
        }

        line = string.Create(CultureInfo.InvariantCulture, $"pack: drop-back lifted on {hold.Ground}: base mana {hold.ManaAtDrop} -> {baseMana}, grown by {rule.ManaGrows} or more: big packs are allowed again");
        return hold with { Deaths = 0, Held = false, ManaAtDrop = 0 };
    }
}
