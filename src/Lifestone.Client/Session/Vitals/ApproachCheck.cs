using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One approach hostile as the check judged it (<see cref="ApproachCheck"/>).</summary>
/// <param name="Hostile">The hostile and its facts.</param>
/// <param name="DamageType">The worst of its damage types for her.</param>
/// <param name="Factor">What reaches her of its unarmoured hit of that type: her armour times her live resistance.</param>
/// <param name="Loss">The health one camp of it takes while she leaves (<see cref="FightLines.LossWhileLeaving"/>).</param>
/// <param name="Share">That loss as a percentage of her maximum health.</param>
public readonly record struct ApproachHostileRead(GroundApproachHostile Hostile, uint DamageType, double Factor, double Loss, double Share)
{
    /// <summary>M6 14 attack timing: the hostile's attack timing the loss was read at (null: the named fallback).</summary>
    public AttackTiming? Timing { get; init; }
}

/// <summary>The approach check's answer (<see cref="ApproachCheck.Judge"/>).</summary>
/// <param name="Survivable">True when every camp of the approach can be left alive from a full bar.</param>
/// <param name="Ground">The ground whose approach it is.</param>
/// <param name="Why">The verdict in words, with the worst camp's numbers.</param>
/// <param name="Worst">The camp that takes the most, or null when no hostile carries facts.</param>
/// <param name="Unknown">The hostiles with no facts (not judged; named).</param>
public sealed record ApproachVerdict(bool Survivable, string Ground, string Why, ApproachHostileRead? Worst, IReadOnlyList<string> Unknown)
{
    /// <summary>M6 14 approach cost (rule 1): the crossing's cost on her live state (<see cref="ApproachCost.Judge"/>), or null when not judged.</summary>
    public CrossingCost? Cost { get; init; }

    /// <summary>M6 14 approach cost (rule 2): why the run leg gave this approach's crossing up earlier in the run (<see cref="ApproachCost.Measured"/>), or null: never; such a crossing is not tried again in the same run.</summary>
    public string? Abandoned { get; init; }

    /// <summary>True when the approach holds her off: she cannot survive it, she gave its crossing up this run, or its crossing costs more than it pays.</summary>
    public bool Holds => !Survivable || Abandoned is not null || Cost is { Costly: true };

    /// <summary>The hold's token: <c>approach-held</c> (not survivable), <c>approach-abandoned</c>, <c>approach-costly</c>, or <c>approach-ok</c> when it does not hold.</summary>
    public string Kind => !Survivable ? "approach-held" : Abandoned is not null ? ApproachCost.AbandonedPrefix : Cost is { Costly: true } ? ApproachCost.CostlyPrefix : "approach-ok";

    /// <summary>The verdict's token for a held rung, trial or corpse trip: <c>approach: &lt;why&gt;</c>, or the crossing's (<c>approach abandoned: </c>, <c>approach costly: </c>).</summary>
    public string Held => Kind switch
    {
        ApproachCost.AbandonedPrefix => "approach abandoned: " + Abandoned,
        ApproachCost.CostlyPrefix => $"approach costly ({Cost!.Reason}): {Cost.Why}{(Cost.Change is { } change ? "; " + change : string.Empty)}",
        _ => "approach: " + Why,
    };
}

/// <summary>
/// M6 14 retreat attackers (rule 6; live 2026-11-14: the climber, level 62 and 197 health, died on the Tusker Holding's approach, 1 km of island from the Barracks drop to the entry, among camps of level 115 Infected Assailers): a ground whose
/// approach she cannot survive is not walked to. The approach's facts (<see cref="GroundApproach.Hostiles"/>, each with its attack and the most of it one camp holds) are judged against her live state: her maximum health, her armour and live
/// resistance against each hostile's own damage type (<see cref="LiveFightLine.FactorFor"/>), and the time to recall (<see cref="FightLines.LeaveSeconds"/>). A camp is survivable when its hits while she recalls, and one critical, take
/// less than <see cref="FightLines.MaxPercent"/> of a full bar (<see cref="FightLines.LossWhileLeaving"/>: the derived line's own sum, the camp's line under the top): at or over it, meeting the camp leaves nothing to fight with, so the
/// walk is never begun. The approach is survivable when every camp with facts is. A hostile with no facts is named and not judged (the line's general fallback, under the top, covers it in the fight); an unread health or a book without Lifestone
/// Recall is not survivable (unknown never sends her: the way out is the recall). Pure; the live read is <see cref="Live"/>.
/// </summary>
public static class ApproachCheck
{
    /// <summary>The verdict for <paramref name="approach"/>.</summary>
    /// <param name="ground">The ground's key.</param>
    /// <param name="approach">The approach's facts.</param>
    /// <param name="healthMax">Her maximum health now (0: unread).</param>
    /// <param name="recallReady">True when her book holds Lifestone Recall (the way out the leave time is measured on).</param>
    /// <param name="factorFor">What reaches her of an unarmoured hit of the DamageType bits given (the worst bit) and which bit it is.</param>
    /// <param name="timingOf">M6 14 attack timing: each hostile's attack timing by weenie and name, or null: the named 2.75 s fallback.</param>
    /// <param name="spellFactor">M6 14 attack timing: her resistance to a spell of the DamageType bits given, or null: a hostile's spells are not priced in the camp's loss.</param>
    public static ApproachVerdict Judge(string ground, GroundApproach approach, uint healthMax, bool recallReady, Func<uint, (double Factor, uint Type)> factorFor, Func<uint, string, AttackTiming>? timingOf = null, Func<uint, double>? spellFactor = null)
    {
        ArgumentNullException.ThrowIfNull(approach);
        ArgumentNullException.ThrowIfNull(factorFor);
        var c = CultureInfo.InvariantCulture;
        var unknown = approach.Hostiles.Where(h => h.Hitter is null).Select(h => h.Name).Distinct(StringComparer.Ordinal).ToList();
        ApproachHostileRead? worst = null;
        foreach (var h in approach.Hostiles)
        {
            if (h.Hitter is not { } hitter)
            {
                continue;
            }

            var (factor, type) = factorFor(h.DamageTypes);
            var timing = timingOf?.Invoke(h.Wcid, h.Name) ?? FightLines.TimingOrFallback(hitter, null);
            var loss = FightLines.LossWhileLeaving(hitter, h.Pack, factor, timing, spellFactor is null ? 0 : timing.SpellPerSecond(spellFactor));
            var share = healthMax == 0 ? double.PositiveInfinity : 100.0 * loss / healthMax;
            if (worst is null || share > worst.Value.Share)
            {
                worst = new ApproachHostileRead(h, type, factor, loss, share) { Timing = timing };
            }
        }

        var unnamed = unknown.Count == 0 ? string.Empty : string.Create(c, $"; not judged, no facts: {string.Join(", ", unknown)}");
        if (healthMax == 0)
        {
            return new ApproachVerdict(false, ground, $"{ground}'s approach is not judged: her health is unread (unknown never sends her){unnamed}", worst, unknown);
        }

        if (!recallReady)
        {
            return new ApproachVerdict(false, ground, $"{ground}'s approach cannot be left in {FightLines.LeaveSeconds:0.##} s: her book holds no Lifestone Recall, and the way out is the walk back through it{unnamed}", worst, unknown);
        }

        if (worst is not { } w)
        {
            return new ApproachVerdict(true, ground, $"{ground}'s approach names no hostile's attack facts: nothing to judge{unnamed}", null, unknown);
        }

        var line = FightLines.LineOf(w.Share);
        var survivable = line < FightLines.MaxPercent;
        var hostile = w.Hostile;
        var numbers = string.Create(
            c,
            $"the worst camp is {hostile.Pack} x '{hostile.Name}' (weenie {hostile.Wcid}, level {hostile.Level}, damage 0x{w.DamageType:X}): hit {hostile.Hitter!.Hit} x factor {w.Factor:0.####} at {FightLines.TimingOrFallback(hostile.Hitter, w.Timing).Short()} for the {FightLines.LeaveSeconds:0.##} s of a recall, and a critical of {FightLines.CriticalHit(hostile.Hitter) * w.Factor:0.#}: {w.Loss:0.#} of her {healthMax} health ({w.Share:0.#}%, the line {line}%)");
        return new ApproachVerdict(
            survivable,
            ground,
            survivable
                ? string.Create(c, $"{ground}'s approach is survivable: {numbers}, under the top {FightLines.MaxPercent}%{unnamed}")
                : string.Create(c, $"{ground}'s approach is not survivable at her state: {numbers}: at the top {FightLines.MaxPercent}% meeting it leaves nothing to fight with{unnamed}"),
            w,
            unknown);
    }

    /// <summary>The verdict for <paramref name="ground"/>'s approach on her live state (<paramref name="reader"/>'s armour and resistance), or null when the profile records no approach.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="reader">The run's line reader (its armour reads are kept a second).</param>
    /// <param name="now">The session clock.</param>
    public static ApproachVerdict? Live(GameState state, HuntingGround ground, LiveFightLine reader, double now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(reader);
        if (ground.Approach is not { } approach)
        {
            return null;
        }

        var healthMax = state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max) ? max : 0u;
        var recall = state.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell);
        reader.SpellFactorFor(state, 0); // her registry as now, for the spells' resistance
        return Judge(ground.Key, approach, healthMax, recall, types => (reader.FactorFor(state, types, now, out var type), type), reader.Times.Of, reader.SpellResistance);
    }

    /// <summary>The ground whose approach holds <paramref name="cell"/> (an outdoor cell of its approach's landblocks), the first of <paramref name="grounds"/>, or null.</summary>
    /// <param name="grounds">The ground profiles.</param>
    /// <param name="cell">The cell.</param>
    public static HuntingGround? ApproachOf(IEnumerable<HuntingGround> grounds, uint cell)
    {
        ArgumentNullException.ThrowIfNull(grounds);
        return grounds.FirstOrDefault(g => g.ApproachAt(cell) is not null);
    }
}
