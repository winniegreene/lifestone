using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 16 Q5 (docs/automation-engine.md section 6, <c>TODO(engine-health-flip)</c>, its first half): the vitals guard's and the travel legs' read of a creature's health, from the session's one picture
/// (<see cref="HealthPicture"/>) in place of their own model (<see cref="KnownTargetHealth"/>). What they gain: a creature hurt earlier (by a fight, an earlier leg, before it became the target) stays hurt, a report of the
/// server is the base from the moment it came, and a creature mends at its own rate. What stays the model's: the facts it carries (its maximum for a creature the picture holds none for, its Magic Defense, its name) and,
/// where the picture does not know the creature, its answer as before (named in the words). Each reader keeps its own rounding: the guard's and the fight-back's to the nearest point, at least 1 while it stands, as the model
/// rounded; so where the picture and the model hold the same base, the same damage and no regeneration, the answer is the model's to the point. Pure over its arguments; allocates only for the words.
/// </summary>
internal static class PictureHealth
{
    /// <summary>The health of <paramref name="facts"/>' creature now, in points (at least 1): the picture's (its read, or for the creature it forgot last its health just before), else the model's.</summary>
    /// <param name="picture">The session's picture.</param>
    /// <param name="facts">The guard's or the fight-back's facts of the creature.</param>
    /// <param name="state">The game state (the model's read where the picture does not know the creature).</param>
    /// <param name="now">The session clock.</param>
    /// <param name="read">The picture's read (<see cref="HealthRead.Known"/> false: the model answered).</param>
    public static uint Points(HealthPicture picture, KnownTargetHealth facts, GameState state, double now, out HealthRead read)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(facts);
        read = picture.ReadOrLast(facts.Guid, now);
        return read.Known ? (uint)Math.Max(1, Math.Round(read.Health)) : facts.Health(state);
    }

    /// <summary>The health in words, with its numbers and source (the guard's killing blow and the fight-back's target line).</summary>
    /// <param name="facts">The facts of the creature.</param>
    /// <param name="health">The health <see cref="Points"/> answered.</param>
    /// <param name="read">The picture's read <see cref="Points"/> gave.</param>
    /// <param name="state">The game state.</param>
    public static string Words(KnownTargetHealth facts, uint health, in HealthRead read, GameState state)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!read.Known)
        {
            return "the one picture holds no maximum for it: " + facts.Words(state);
        }

        var c = CultureInfo.InvariantCulture;
        var basis = read.Basis switch
        {
            HealthBasis.Reported => string.Create(c, $"the server's health update of {read.Base:0} {read.BaseAge:0.#} s ago"),
            HealthBasis.Appraised => string.Create(c, $"its appraisal of {read.Base:0} {read.BaseAge:0.#} s ago"),
            HealthBasis.Counted => string.Create(c, $"its maximum {read.Max}"),
            _ => string.Create(c, $"its maximum {read.Max}, never told and never hit"),
        };
        var max = read.MaxSource == HealthMaxSource.Appraisal ? "its own appraisal" : "the bestiary";
        return string.Create(c, $"'{facts.Name}' 0x{facts.Guid:X8} health {health} of {read.Max} (the one picture: {basis}, less the {read.Dealt:0} she dealt it since, plus {read.Regen:0.#} regenerated at {read.PerSecond:0.###} a second; the maximum from {max}; {facts.Source})");
    }
}
