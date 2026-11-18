using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One way to heal the mage, scored in health a second: Heal Self, or one Drain Health Other level on one target.</summary>
/// <param name="Name">The spell's name.</param>
/// <param name="Target">The target's guid (0 for Heal Self).</param>
/// <param name="Heal">The health a landed cast gives, already cut to the mage's missing health.</param>
/// <param name="Damage">The health the cast takes from the target (0 for Heal Self); it earns the damage weight.</param>
/// <param name="Chance">The chance the cast lands: the cast chance, times the chance the target does not resist for a drain (a self cast never resists).</param>
/// <param name="Mana">The mana the cast costs at most.</param>
/// <param name="CastSeconds">The seconds the cast takes (the measured per-level seconds, else the gesture's seed).</param>
public readonly record struct HealCandidate(string Name, uint Target, double Heal, double Damage, double Chance, double Mana, double CastSeconds);

/// <summary>
/// M5 7 F21 (D92, the owner: a drain is cast only when it would help more than Heal Self; otherwise Heal Self): the one comparison every place that chooses a heal makes (the emergency under its line, D88's drain floor and the
/// picker's drain as an attack). Heal Self and each drain candidate are scored in health a second: the chance it lands times (the health it gives plus <see cref="DefaultDamageWeight"/> of the health it takes from the target, the small
/// credit for the damage a drain does), over the cast seconds plus the mana's time at the dance's seconds a mana point (F10's measured rate). The most wins; a tie goes to Heal Self, and a drain the model scores under Heal Self is never
/// cast. Engine-free and allocation-free.
/// </summary>
public static class HealPick
{
    /// <summary>
    /// The share of the damage a drain does to its target that counts as health a second (Lifestone's own, registered as <c>--fight-heal-damage-weight</c>): a target brought nearer its death is worth a little, never as much as
    /// health the mage gains.
    /// </summary>
    public const double DefaultDamageWeight = 0.25;

    /// <summary>The value of <see cref="Choose"/> when Heal Self wins.</summary>
    public const int HealSelf = -1;

    /// <summary>The value of <see cref="Choose"/> when nothing can be cast (no Heal Self and no drain).</summary>
    public const int Nothing = -2;

    /// <summary>The health a second of one candidate: chance times (heal plus the damage credit) over (cast seconds plus mana times the dance's seconds a mana point).</summary>
    /// <param name="c">The candidate.</param>
    /// <param name="secondsPerMana">The dance's seconds a mana point.</param>
    /// <param name="damageWeight">The share of the damage that counts.</param>
    public static double PerSecond(in HealCandidate c, double secondsPerMana, double damageWeight)
    {
        var seconds = Math.Max(0.1, c.CastSeconds) + (Math.Max(0, c.Mana) * Math.Max(0, secondsPerMana));
        return Math.Clamp(c.Chance, 0, 1) * (c.Heal + (Math.Max(0, damageWeight) * c.Damage)) / seconds;
    }

    /// <summary>
    /// M6 14 the losing race: the health a second a candidate keeps giving her while it is cast again and again: its landing chance times its heal over its cast seconds. No damage credit and no mana time: what she gains, not what the
    /// pick scores (<see cref="PerSecond"/>).
    /// </summary>
    /// <param name="c">The candidate.</param>
    public static double Sustained(in HealCandidate c) => Sustained(in c, 0);

    /// <summary>
    /// M6 14 the losing race (live 06:17:07 UTC: Heal Self VI and Drain Health Other I both read as holding an Infected Assailer while the mana each cast cost was not counted): the sustained heal with the mana's time in it, the seconds
    /// the dance takes to win the mana back (<paramref name="secondsPerMana"/>, the pick's own rate): a heal that costs 70 mana is not cast again and again for free.
    /// </summary>
    /// <param name="c">The candidate.</param>
    /// <param name="secondsPerMana">The dance's seconds a mana point.</param>
    public static double Sustained(in HealCandidate c, double secondsPerMana) => Math.Clamp(c.Chance, 0, 1) * Math.Max(0, c.Heal) / (Math.Max(0.1, c.CastSeconds) + (Math.Max(0, c.Mana) * Math.Max(0, secondsPerMana)));

    /// <summary>
    /// M6 14 tactics (live ladder56 2026-11-17 08:33:08.306 UTC, the Crater Caves: at 73 of 219 under two Cursed Wisps the guard rated Heal Self VI at 9.4 health a second and Drain Health Other I at 20.8): the emergency's pick, by the
    /// health a landed cast gives her a second now: each candidate's <see cref="Sustained(in HealCandidate)"/> (the chance it lands times its heal over its cast seconds). No mana time: an emergency heal is paid from the mana she
    /// holds (a candidate is offered only when she holds its mana), and the time to win that mana back is not this heal's (the guard charged Heal Self VI 6.3 s for its 70 mana's ceiling at the seed 0.09 s a point, while
    /// ACE takes 12.2 after her Mana Conversion and she held 379). No damage credit: the damage a drain does counts on the kill side, not as health. A tie goes to Heal Self, as in <see cref="Choose"/>.
    /// </summary>
    /// <param name="self">Heal Self; ignored when <paramref name="canSelf"/> is false.</param>
    /// <param name="canSelf">True when Heal Self can be cast now.</param>
    /// <param name="drains">The drain candidates.</param>
    /// <param name="selfRate">Heal Self's health a second (0 when not offered).</param>
    /// <param name="bestDrain">The index of the best drain, or -1 with none.</param>
    /// <param name="bestRate">The best drain's health a second (0 with none).</param>
    public static int ChooseNow(in HealCandidate self, bool canSelf, ReadOnlySpan<HealCandidate> drains, out double selfRate, out int bestDrain, out double bestRate)
    {
        (bestDrain, bestRate) = (-1, 0.0);
        for (var i = 0; i < drains.Length; i++)
        {
            var rate = Sustained(in drains[i]);
            if (bestDrain < 0 || rate > bestRate)
            {
                (bestDrain, bestRate) = (i, rate);
            }
        }

        selfRate = canSelf ? Sustained(in self) : 0;
        if (!canSelf)
        {
            return bestDrain < 0 ? Nothing : bestDrain;
        }

        return bestDrain >= 0 && bestRate > selfRate ? bestDrain : HealSelf;
    }

    /// <summary>
    /// Chooses: <see cref="HealSelf"/> (-1), the index of the best drain candidate, or <see cref="Nothing"/> (-2) when neither Heal Self nor a drain is offered. A drain wins only when it scores more than Heal Self (a tie, or any
    /// drain under it, is Heal Self); with Heal Self not offered the best drain is cast.
    /// </summary>
    /// <param name="self">Heal Self; ignored when <paramref name="canSelf"/> is false.</param>
    /// <param name="canSelf">True when Heal Self can be cast now (known, paid for, the mana held).</param>
    /// <param name="drains">The drain candidates (level and target), already filtered to those in reach and affordable.</param>
    /// <param name="secondsPerMana">The dance's seconds a mana point.</param>
    /// <param name="damageWeight">The share of the damage that counts.</param>
    /// <param name="selfRate">Heal Self's health a second (0 when not offered).</param>
    /// <param name="bestDrain">The index of the best drain candidate by health a second, or -1 with none.</param>
    /// <param name="bestRate">The best drain's health a second (0 with none).</param>
    public static int Choose(in HealCandidate self, bool canSelf, ReadOnlySpan<HealCandidate> drains, double secondsPerMana, double damageWeight, out double selfRate, out int bestDrain, out double bestRate)
    {
        (bestDrain, bestRate) = (-1, 0.0);
        for (var i = 0; i < drains.Length; i++)
        {
            var rate = PerSecond(in drains[i], secondsPerMana, damageWeight);
            if (bestDrain < 0 || rate > bestRate)
            {
                (bestDrain, bestRate) = (i, rate);
            }
        }

        selfRate = canSelf ? PerSecond(in self, secondsPerMana, damageWeight) : 0;
        if (!canSelf)
        {
            return bestDrain < 0 ? Nothing : bestDrain;
        }

        return bestDrain >= 0 && bestRate > selfRate ? bestDrain : HealSelf;
    }

    /// <summary>
    /// The pick line for the log: <c>heal pick: &lt;chosen&gt; h/s=&lt;x&gt; vs Heal Self h/s=&lt;y&gt; (best drain &lt;spell&gt; on 0x... h/s=&lt;z&gt;)</c>; "none offered" in the brackets with no drain candidate.
    /// </summary>
    /// <param name="choice">What <see cref="Choose"/> returned.</param>
    /// <param name="self">Heal Self's candidate.</param>
    /// <param name="selfRate">Heal Self's health a second.</param>
    /// <param name="drains">The drain candidates.</param>
    /// <param name="bestDrain">The best drain's index, or -1.</param>
    /// <param name="bestRate">The best drain's health a second.</param>
    public static string Line(int choice, in HealCandidate self, double selfRate, ReadOnlySpan<HealCandidate> drains, int bestDrain, double bestRate)
    {
        var chosen = choice >= 0 ? drains[choice].Name : self.Name;
        var chosenRate = choice >= 0 ? bestRate : selfRate;
        var best = bestDrain < 0
            ? "no drain offered"
            : string.Create(CultureInfo.InvariantCulture, $"best drain {drains[bestDrain].Name} on 0x{drains[bestDrain].Target:X8} h/s={bestRate:0.###}");
        return string.Create(CultureInfo.InvariantCulture, $"heal pick: {chosen} h/s={chosenRate:0.###} vs Heal Self h/s={selfRate:0.###} ({best})");
    }
}
