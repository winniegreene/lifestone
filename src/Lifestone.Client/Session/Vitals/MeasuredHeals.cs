using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M5 7 F19 (D92, the owner: in an emergency do the thing that WORKS): the heals the server actually gave, read from ACE's own chat lines, kept as a live measured heal per Drain Health Other level and target kind and per Heal Self level.
/// ACE writes "You gain N points of health due to casting &lt;spell&gt; on &lt;target&gt;" for a drain (<c>WorldObject_Magic.cs</c> 853 @95ef426d) and "You cast &lt;spell&gt; and restore N points of your Health." for a self boost
/// (<c>WorldObject_Magic.cs</c> 550-554). The rehearsal's level-80 mage died casting Drain Health Other I five times at a Tusker Guard the client showed at 100%: ACE answered 0, 0, 2, 0, 0 because the guard's real health was a few points
/// (each drain had taken from it and nothing had asked the server since). The expected heal of an option is the measured mean, never more than the formula with the target's real health; with no measure it is the formula.
/// Engine-free; a note allocates only a new key's slot.
/// </summary>
public sealed class MeasuredHeals
{
    /// <summary>The weight of a new measure in the running mean (Lifestone's own: recent results count most, so a target drained low shows at once).</summary>
    public const double Weight = 0.4;

    /// <summary>
    /// Heal Self's mean heal by level I to VII before one is measured: ACE's boost plus half its variance from the pinned world SQL v0.9.296 table <c>spell</c> (ids 6, 1157-1161, 2073: boost 10/15/20/30/45/55/80, variance
    /// 15/20/25/30/45/65/70; ACE rolls <c>Next(min(boost, maxBoost), max(boost, maxBoost))</c> and a self cast takes the target's boost-resistance mod, 1 for a mage with none, <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 481-491 @95ef426d; the vital then cuts it to the missing health, <c>UpdateVitalDelta</c>, 542-545). Level VI is 55 to 120: mean 87.5.
    /// </summary>
    public static readonly double[] SelfHealSeed = [17.5, 25, 32.5, 45, 67.5, 87.5, 115];

    private const string DrainPrefix = "You gain ";
    private const string DrainMiddle = " points of health due to casting ";
    private const string SelfPrefix = "You cast ";
    private const string SelfMiddle = " and restore ";
    private const string SelfEnd = " points of your Health.";

    private readonly Dictionary<string, Mean> _means = new(StringComparer.Ordinal);

    /// <summary>The measures taken (every parsed line).</summary>
    public int Measures { get; private set; }

    /// <summary>The last measure's line for the log, or empty.</summary>
    public string LastNote { get; private set; } = string.Empty;

    /// <summary>Parses ACE's drain line "You gain N points of health due to casting &lt;spell&gt; on &lt;target&gt;".</summary>
    /// <param name="text">The chat text.</param>
    /// <param name="gain">The health gained.</param>
    /// <param name="spell">The spell's name.</param>
    /// <param name="target">The target's name.</param>
    public static bool TryParseDrain(string text, out int gain, out string spell, out string target)
    {
        (gain, spell, target) = (0, string.Empty, string.Empty);
        if (text is null || !text.StartsWith(DrainPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var middle = text.IndexOf(DrainMiddle, StringComparison.Ordinal);
        if (middle < 0 || !int.TryParse(text.AsSpan(DrainPrefix.Length, middle - DrainPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out gain))
        {
            return false;
        }

        var rest = text[(middle + DrainMiddle.Length)..];
        var on = rest.LastIndexOf(" on ", StringComparison.Ordinal);
        if (on <= 0)
        {
            return false;
        }

        (spell, target) = (rest[..on], rest[(on + 4)..].TrimEnd('.', ' '));
        return true;
    }

    /// <summary>Parses ACE's self boost line "You cast &lt;spell&gt; and restore N points of your Health.".</summary>
    /// <param name="text">The chat text.</param>
    /// <param name="gain">The health restored.</param>
    /// <param name="spell">The spell's name.</param>
    public static bool TryParseSelfHeal(string text, out int gain, out string spell)
    {
        (gain, spell) = (0, string.Empty);
        if (text is null || !text.StartsWith(SelfPrefix, StringComparison.Ordinal) || !text.EndsWith(SelfEnd, StringComparison.Ordinal))
        {
            return false;
        }

        var middle = text.IndexOf(SelfMiddle, StringComparison.Ordinal);
        if (middle < 0)
        {
            return false;
        }

        var number = text.AsSpan(middle + SelfMiddle.Length, text.Length - SelfEnd.Length - middle - SelfMiddle.Length);
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out gain))
        {
            return false;
        }

        spell = text[SelfPrefix.Length..middle];
        return true;
    }

    /// <summary>Reads one chat line: a drain or a self heal is measured (true); anything else is left (false).</summary>
    /// <param name="text">The chat text.</param>
    public bool Read(string text)
    {
        if (TryParseDrain(text, out var gain, out var spell, out var target))
        {
            Note(spell, target, gain);
            return true;
        }

        if (TryParseSelfHeal(text, out gain, out spell))
        {
            Note(spell, null, gain);
            return true;
        }

        return false;
    }

    /// <summary>Notes one measured heal.</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="target">The target kind's name, or null for a self heal.</param>
    /// <param name="gain">The health the server gave.</param>
    public void Note(string spell, string? target, int gain)
    {
        var key = Key(spell, target);
        var mean = _means.TryGetValue(key, out var m) ? m : new Mean();
        mean.Value = mean.Count == 0 ? gain : mean.Value + (Weight * (gain - mean.Value));
        mean.Count++;
        _means[key] = mean;
        Measures++;
        LastNote = string.Create(CultureInfo.InvariantCulture, $"measured: {spell}{(target is null ? string.Empty : " on " + target)} gave {gain}; the measured heal is {mean.Value:0.#} over {mean.Count}");
    }

    /// <summary>The measured mean of <paramref name="spell"/> on <paramref name="target"/> (null: a self heal), or false with none measured.</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="target">The target kind's name, or null.</param>
    /// <param name="mean">The mean.</param>
    /// <param name="count">The measures behind it.</param>
    public bool TryMeasured(string spell, string? target, out double mean, out int count)
    {
        if (_means.TryGetValue(Key(spell, target), out var m) && m.Count > 0)
        {
            (mean, count) = (m.Value, m.Count);
            return true;
        }

        (mean, count) = (0, 0);
        return false;
    }

    /// <summary>The expected heal: the formula (with the target's real health) when nothing is measured, else the measured mean, never more than the formula.</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="target">The target kind's name, or null for a self heal.</param>
    /// <param name="formula">The formula's heal now.</param>
    public double Expected(string spell, string? target, double formula) =>
        TryMeasured(spell, target, out var mean, out _) ? Math.Min(mean, formula) : formula;

    /// <summary>
    /// The drain's cast seconds by level I to VII before one is measured (Lifestone's own, <c>TODO(f19-drain-wind-up-seconds)</c>): level I is one gesture, measured live at 1.57 to 1.58 s (the rehearsal of 2026-10-28); each higher pair of
    /// levels adds a wind-up. The measured mean replaces the seed once a cast of the level has landed.
    /// </summary>
    public static readonly double[] DrainSecondsSeed = [1.6, 1.6, 2.4, 2.4, 3.0, 3.0, 3.6];

    private readonly Dictionary<string, Mean> _ratios = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mean> _seconds = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes a landed drain's measured gain against ACE's formula for that cast (the coordinator's rule: the heal is attributed to the TARGET'S health, not the level): the ratio gain over formula is what the model missed (a resist mod,
    /// a stale health); it is kept per spell.
    /// </summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="gain">The health the server gave.</param>
    /// <param name="formula">The formula's heal for the cast, with the target's health at the cast (over 0).</param>
    public void NoteRatio(string spell, int gain, double formula)
    {
        if (formula <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(gain / formula, 0, 1.5);
        var mean = _ratios.TryGetValue(spell, out var m) ? m : new Mean();
        mean.Value = mean.Count == 0 ? ratio : mean.Value + (Weight * (ratio - mean.Value));
        mean.Count++;
        _ratios[spell] = mean;
        Measures++;
        LastNote = string.Create(CultureInfo.InvariantCulture, $"measured: {spell} gave {gain} against the formula's {formula:0.#} (ratio {ratio:0.##}; the measured ratio is {mean.Value:0.##} over {mean.Count})");
    }

    /// <summary>The formula's heal scaled by the spell's measured ratio (the model confirmed or corrected by the measures; the formula alone with none).</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="formula">The formula's heal now, with the target's real (or estimated) health.</param>
    public double ExpectedFromRatio(string spell, double formula) => _ratios.TryGetValue(spell, out var m) && m.Count > 0 ? formula * m.Value : formula;

    /// <summary>Notes a landed cast's seconds for its spell.</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="seconds">The seconds from the cast's start to its landing.</param>
    public void NoteSeconds(string spell, double seconds)
    {
        if (seconds <= 0 || seconds >= 30)
        {
            return;
        }

        var mean = _seconds.TryGetValue(spell, out var m) ? m : new Mean();
        mean.Value = mean.Count == 0 ? seconds : mean.Value + (Weight * (seconds - mean.Value));
        mean.Count++;
        _seconds[spell] = mean;
    }

    /// <summary>The spell's measured cast seconds, else <paramref name="seed"/>.</summary>
    /// <param name="spell">The spell's name.</param>
    /// <param name="seed">The seed.</param>
    public double Seconds(string spell, double seed) => _seconds.TryGetValue(spell, out var m) && m.Count > 0 ? m.Value : seed;

    /// <summary>The seed seconds of a drain level (1 to 7) from <see cref="DrainSecondsSeed"/>.</summary>
    /// <param name="level">The level.</param>
    public static double DrainSeed(int level) => DrainSecondsSeed[Math.Clamp(level, 1, DrainSecondsSeed.Length) - 1];

    /// <summary>The seed of a Heal Self level (1 to 7) before a measure, from <see cref="SelfHealSeed"/>; 0 outside.</summary>
    /// <param name="level">The level, 1 to 7.</param>
    public static double SelfSeed(int level) => level >= 1 && level <= SelfHealSeed.Length ? SelfHealSeed[level - 1] : 0;

    private static string Key(string spell, string? target) => target is null ? spell : spell + "\u0001" + target;

    private sealed class Mean
    {
        public double Value;
        public int Count;
    }
}
