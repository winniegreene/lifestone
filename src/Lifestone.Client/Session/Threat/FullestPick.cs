namespace Lifestone.Client.Session.Threat;

/// <summary>Which vital a fullest pick compares, and in what unit (<see cref="FullestRule"/>).</summary>
public enum FullestVital : byte
{
    /// <summary>A creature's health as a share of its maximum, 0 to 1 (the fight's model).</summary>
    HealthShare,

    /// <summary>What a drain can take from a creature: its health in points times its kind's drain resistance (the guard's picture).</summary>
    HealthTake,

    /// <summary>A creature's stamina in points (its appraisal's, or its kind's as read).</summary>
    Stamina,
}

/// <summary>Which of two creatures that hold the same wins a fullest pick (<see cref="FullestRule"/>).</summary>
public enum FullestTie : byte
{
    /// <summary>The one met first.</summary>
    FirstMet,

    /// <summary>The nearer one.</summary>
    Nearest,

    /// <summary>The one already picked (<see cref="FullestPick"/>'s kept guid), else the one met first.</summary>
    Kept,
}

/// <summary>
/// M6 16 E2d: what one pick of "the fullest creature" compares by. Its parameters ARE the differences between today's picks, so each is one rule with its own numbers and the comparison itself is one function
/// (<see cref="FullestPick.Beats"/>).
/// </summary>
/// <param name="Vital">Which vital the values are, and their unit.</param>
/// <param name="Band">How far under the best a value may lie and still count as holding the same (0: only an equal value does). Applied in single precision: the shares it is used on are floats.</param>
/// <param name="Tie">Which of two that hold the same wins.</param>
/// <param name="Above">The value the first creature must exceed to be picked at all (negative infinity: any value is).</param>
/// <param name="Unknown">What a creature whose vital is not known (offered as NaN) counts as, or NaN: it is passed over.</param>
public readonly record struct FullestRule(FullestVital Vital, float Band, FullestTie Tie, double Above, double Unknown)
{
    /// <summary>
    /// The fight's emergency and its drain rule (<c>FightScenario.EmergencyTarget</c>, <c>FullestDrainTarget</c>: one pick with two reaches): the health share, the nearest among those within a hundredth of the
    /// most; a creature never told and never hit counts as full (<c>HealthEstimate</c> answers 1 for it).
    /// </summary>
    public static FullestRule EmergencyDrain { get; } = new(FullestVital.HealthShare, 0.01f, FullestTie.Nearest, double.NegativeInfinity, 1.0);

    /// <summary>The ring's anchor (<c>FightScenario.ChooseRingAnchor</c>): the health share, the picked target among equals; a creature never told and never hit counts as full (<c>LiveHealthOrFull</c> answers 1 for it).</summary>
    public static FullestRule RingAnchor { get; } = new(FullestVital.HealthShare, 0f, FullestTie.Kept, double.NegativeInfinity, 1.0);

    /// <summary>
    /// The mana loop's stamina source (<c>FightScenario.FullestStamina</c>): the stamina in points, more than none, the first met among equals; a creature whose stamina no appraisal told and whose kind was not read
    /// is passed over.
    /// </summary>
    public static FullestRule StaminaDrain { get; } = new(FullestVital.Stamina, 0f, FullestTie.FirstMet, 0, double.NaN);

    /// <summary>
    /// The vitals guard's drain target (<c>AutopilotVitalsGuard.KnownFullest</c>): the health a drain can take, the first met among equals; a creature the server gave no health of is passed over unless the guard
    /// takes it as full by its facts (the ladder's character, or the fight-back's target), which the guard resolves before it offers.
    /// </summary>
    public static FullestRule GuardDrain { get; } = new(FullestVital.HealthTake, 0f, FullestTie.FirstMet, -1, double.NaN);
}

/// <summary>
/// M6 16 E2d (docs/automation-engine.md stage E2, "one fullest-creature pick"; m6-plan.md D139): a running pick of the fullest creature. Today the fight's emergency and drain rule, the ring's anchor, the mana loop's
/// stamina source and the vitals guard's drain target each carried the comparison "is this one fuller than the best so far" in a form of its own; it is this one function now, and the forms' differences are the
/// rule's parameters (<see cref="FullestRule"/>: the vital and its unit, the band, the tie, the floor, what an unknown counts as) and the kept guid. A site offers its candidates in the order it meets them and keeps
/// gathering them itself: who is a candidate, within which reach, and when a line is tested gate that site's own side effects (asks of the server, trackings made, counters) and stay where they are.
/// A value: no allocation.
/// </summary>
public struct FullestPick
{
    private readonly FullestRule _rule;
    private readonly uint _kept;

    /// <summary>A pick by <paramref name="rule"/>.</summary>
    /// <param name="rule">What to compare by.</param>
    /// <param name="kept">The guid that wins a tie under <see cref="FullestTie.Kept"/> (the target already picked), or 0.</param>
    public FullestPick(in FullestRule rule, uint kept = 0)
    {
        (_rule, _kept) = (rule, kept);
        (Any, Index, Guid, Value, Metres) = (false, -1, 0, double.NaN, double.NaN);
    }

    /// <summary>True once a creature was taken.</summary>
    public bool Any { get; private set; }

    /// <summary>The index the best was offered with, or -1.</summary>
    public int Index { get; private set; }

    /// <summary>The best creature's guid, or 0.</summary>
    public uint Guid { get; private set; }

    /// <summary>The best creature's value (an unknown one's as the rule counts it), or NaN.</summary>
    public double Value { get; private set; }

    /// <summary>The best creature's distance as offered, or NaN.</summary>
    public double Metres { get; private set; }

    /// <summary>
    /// True when a creature of <paramref name="value"/> at <paramref name="metres"/> would be the best so far: the first must exceed the rule's floor; a later one must exceed the best by more than the band, or hold
    /// the same (within the band) and win the tie. An unknown value (NaN) counts as the rule says, or never beats.
    /// </summary>
    /// <param name="guid">The creature.</param>
    /// <param name="value">Its vital in the rule's unit, or NaN when it is not known.</param>
    /// <param name="metres">Its distance (read by <see cref="FullestTie.Nearest"/> alone).</param>
    public readonly bool Beats(uint guid, double value, double metres = 0)
    {
        if (double.IsNaN(value))
        {
            value = _rule.Unknown;
            if (double.IsNaN(value))
            {
                return false;
            }
        }

        if (!Any)
        {
            return value > _rule.Above;
        }

        var tie = _rule.Tie switch
        {
            FullestTie.Nearest => metres < Metres,
            FullestTie.Kept => guid == _kept,
            _ => false,
        };
        if (_rule.Band > 0f)
        {
            var (v, best) = ((float)value, (float)Value); // the band's arithmetic in single precision, as the fight's shares are compared
            return v > best + _rule.Band || (v >= best - _rule.Band && tie);
        }

        return value > Value || (value == Value && tie);
    }

    /// <summary>Takes a creature as the best so far (after <see cref="Beats"/>, and whatever else the site tests of it: a clear line).</summary>
    /// <param name="index">The site's index of it, or -1.</param>
    /// <param name="guid">The creature.</param>
    /// <param name="value">Its vital, or NaN when it is not known.</param>
    /// <param name="metres">Its distance.</param>
    public void Take(int index, uint guid, double value, double metres = 0)
    {
        (Any, Index, Guid, Value, Metres) = (true, index, guid, double.IsNaN(value) ? _rule.Unknown : value, metres);
    }

    /// <summary>Takes the creature when it beats the best so far; true when it did.</summary>
    /// <param name="index">The site's index of it, or -1.</param>
    /// <param name="guid">The creature.</param>
    /// <param name="value">Its vital, or NaN when it is not known.</param>
    /// <param name="metres">Its distance.</param>
    public bool Offer(int index, uint guid, double value, double metres = 0)
    {
        if (!Beats(guid, value, metres))
        {
            return false;
        }

        Take(index, guid, value, metres);
        return true;
    }
}
