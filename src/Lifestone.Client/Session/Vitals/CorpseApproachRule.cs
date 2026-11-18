using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What <see cref="CorpseApproachRule.Decide"/> answers.</summary>
/// <param name="Walk">True when the corpse may be walked to.</param>
/// <param name="Reason">The named reason: <c>no-approach</c>, <c>approach-survivable</c>, <c>approach-unsurvivable</c>, or (M6 14 approach cost) <c>approach-costly</c> or <c>approach-abandoned</c>.</param>
/// <param name="Why">The reason in words, with the rot's numbers when the trip gives up.</param>
public readonly record struct CorpseApproachAnswer(bool Walk, string Reason, string Why);

/// <summary>
/// M6 14 retreat attackers (rule 7; live 2026-11-14 10:59:18 UTC: her corpse lay at 0xEC7D0019, on the Holding's approach where two Infected Assailers had killed her, on no rung's ground, so the corpse run read no gate and was open): the
/// corpse trip uses the same safety as the way out. A corpse in an approach she cannot survive at her state (<see cref="ApproachCheck"/>, read on the buffs that stand after the rebuff) is not walked to. A player corpse with
/// something in it rots <see cref="RotSeconds"/> after the death (ACE <c>Corpse.RecalculateDecayTime</c>, WorldObjects/Corpse.cs:103-110 @95ef426d: an empty one in 15 s); a wait would change nothing the check reads (her health, armour and
/// protections already stand as the rebuff left them), so the trip gives up at once, by name, with the rot's time left. Pure.
/// </summary>
public static class CorpseApproachRule
{
    /// <summary>The reason of a corpse the trip leaves in an approach she cannot survive.</summary>
    public const string Unsurvivable = "approach-unsurvivable";

    /// <summary>ACE's empty player corpse's decay (<c>Corpse.EmptyDecayTime</c>, WorldObjects/Corpse.cs:27 @95ef426d).</summary>
    public const double EmptySeconds = 15;

    /// <summary>The seconds after the death a player corpse that holds something rots: <c>max(3600, level x 300)</c> (ACE @95ef426d, WorldObjects/Corpse.cs:110).</summary>
    /// <param name="level">Her level at the death.</param>
    public static double RotSeconds(int level) => Math.Max(3600.0, Math.Max(1, level) * 300.0);

    /// <summary>Whether the corpse is walked to.</summary>
    /// <param name="verdict">The approach verdict for the corpse's place (the approach that holds its cell, or the approach of the ground it lies in), or null when no approach is recorded there.</param>
    /// <param name="level">Her level.</param>
    /// <param name="secondsSinceDeath">The seconds since the death (NaN: unread, the full rot time is named).</param>
    public static CorpseApproachAnswer Decide(ApproachVerdict? verdict, int level, double secondsSinceDeath)
    {
        if (verdict is null)
        {
            return new CorpseApproachAnswer(true, "no-approach", "no ground records an approach where the corpse lies");
        }

        if (!verdict.Holds)
        {
            return new CorpseApproachAnswer(true, "approach-survivable", verdict.Cost is { } paid ? verdict.Why + "; " + paid.Why : verdict.Why);
        }

        // M6 14 approach cost (rule 3): the corpse trip reads the same verdicts as the way out: a crossing that costs more than it pays, or one given up this run, is not walked either
        var rot = RotSeconds(level);
        var left = double.IsNaN(secondsSinceDeath) ? rot : Math.Max(0, rot - secondsSinceDeath);
        var (reason, what) = verdict.Survivable ? (verdict.Kind, verdict.Held) : (Unsurvivable, verdict.Why);
        return new CorpseApproachAnswer(
            false,
            reason,
            string.Create(CultureInfo.InvariantCulture, $"the corpse lies in {verdict.Ground}'s approach, and {what}: it is not walked to; it rots {rot:0} s after the death (max(3600 s, level {level} x 300 s), ACE Corpse.cs:110), {left:0} s from now, and a wait changes nothing the check reads (her health, armour and protections stand as the rebuff left them), so the trip gives up now"));
    }
}
