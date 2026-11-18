using System.Globalization;
using Lifestone.Client.Session.Creatures;

namespace Lifestone.Client.Session.Grounds;

/// <summary>What <see cref="GroundWants.Check"/> found: one warn line for each wanted spell the book lacks, and the failure when a target of the ground can be hurt by no line she casts, else null.</summary>
/// <param name="Lines">The loud lines, one a missing spell; empty when the book holds them all.</param>
/// <param name="Failure">The named stop's words, or null.</param>
public sealed record GroundWantsVerdict(IReadOnlyList<string> Lines, string? Failure);

/// <summary>
/// M6 8 B1: the ground's <c>wants_known</c> against the book, pure (the Crater Caves: fire does nothing to three of its four creatures). Every wanted spell the book lacks is one warn line. M6 14 the element line (closes
/// <c>TODO(m6-fight-element-line)</c>): the fight casts, at each target, the line of her book that hits it hardest (<see cref="Mage.BoltElement"/>), so a plan naming another line than the Flame Bolt is no longer a stop. The stop is
/// what that rule cannot answer: a hunted target of the ground whose hardest line does nothing (every element she can cast resisted whole), named with its resistances; a silent bolt that does no damage is the failure this rule exists for.
/// </summary>
public static class GroundWants
{
    /// <summary>The verdict for <paramref name="ground"/> at <paramref name="level"/>.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="level">The character's level.</param>
    /// <param name="knows">True when the book holds the spell.</param>
    /// <param name="hardest">
    /// The mean plain hit of the line that hits a creature (by weenie class) hardest from her book (<see cref="Mage.BoltElement.Best"/>; 0 when every castable line is resisted whole), or null when no line is castable (the fight's own start names
    /// that); null for the function: the targets are not judged.
    /// </param>
    public static GroundWantsVerdict Check(HuntingGround ground, int level, Func<uint, bool> knows, Func<uint, double?>? hardest = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(knows);
        var lines = ground.WantsKnown.Where(w => !knows(w.SpellId)).Select(w => $"ground wants: {ground.Key} wants {w.Name} (spell {w.SpellId}) and the book lacks it: {w.Why}").ToList();
        string? failure = null;
        if (hardest is not null)
        {
            var immune = ground.Targets.Where(t => hardest(t.Wcid) is <= 0).ToList();
            if (immune.Count > 0)
            {
                failure = string.Create(CultureInfo.InvariantCulture, $"TODO(m6-element-immune): no bolt line her book casts hurts {string.Join(", ", immune.Select(t => $"{t.Name} ({t.Wcid}; {Bestiary.ResistsText(t.Wcid)})"))} on {ground.Key} at level {level}: the learn plan has no line for it");
            }
        }

        return new GroundWantsVerdict(lines, failure);
    }
}
