namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 14 (<c>m6-caster</c>; the owner, 2026-11-13: a caster like the Fire Baton she saved is wielded and buffed with item magic, more Mana Conversion, melee defence and the like): the weights of the caster pick's score
/// (<see cref="Mage.GearScore.Caster"/>), the ladder's <c>caster_score</c> section, each with its one-line reason in <c>sources.caster_score</c>. One currency: percent of her experience an hour, so a percent of each stat is weighed
/// by what it moves of a kill's seconds. The order is the brief's: the damage on the plan's element first, then the Mana Conversion that makes her faster, then defence.
/// </summary>
/// <param name="Damage">Percent of the hour a percent of bolt damage on the plan's element is worth (the bolts' share of a kill).</param>
/// <param name="Mana">Percent of the hour a percent more bolts a mana bar is worth (the mana model's sweep: the fills a kill that Mana Conversion saves).</param>
/// <param name="MeleeDefence">Percent of the hour a percent of the caster's melee defence modifier is worth.</param>
/// <param name="MagicDefence">Percent of the hour a percent of the caster's magic defence modifier is worth.</param>
/// <param name="SwapMargin">The score a candidate must beat the wielded caster by, in percent of the hour, for a swap.</param>
public sealed record LadderCasterScore(double Damage, double Mana, double MeleeDefence, double MagicDefence, double SwapMargin)
{
    /// <summary>
    /// The weights a ladder without the section reads: the war mage's file's values (a ladder that names none still scores a caster by the same currency). <see cref="LadderLoader.WarMage"/>'s own section wins wherever a ladder is read.
    /// </summary>
    public static LadderCasterScore Default { get; } = new(0.5, 0.1, 0.02, 0.02, 0.1);

    /// <summary>The weights as the log prints them.</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"damage {Damage:0.###}, mana {Mana:0.###}, melee defence {MeleeDefence:0.###}, magic defence {MagicDefence:0.###}, swap margin {SwapMargin:0.###}");
}
