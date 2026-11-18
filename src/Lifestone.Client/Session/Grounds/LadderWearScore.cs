namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 14 (<c>m6-armour</c>; the owner, 2026-11-13: she evaluates the armour she finds as possible upgrades for what she wears, especially for minor and major cantrips): the weights of the armour and jewellery pick's score
/// (<see cref="Mage.WearScore"/>), the ladder's <c>wear_score</c> section, each with its one-line reason in <c>sources.wear_score</c>. The currency is the caster score's (<see cref="LadderCasterScore"/>): percent of her experience an
/// hour. War Magic and Mana Conversion are not weighed here: a cantrip that raises them is run through the caster score's own damage and mana weights, by the landing chance and the mana model.
/// </summary>
/// <param name="LifeMagic">Percent of the hour a point of Life Magic is worth.</param>
/// <param name="CreatureEnchantment">Percent of the hour a point of Creature Enchantment is worth.</param>
/// <param name="ItemEnchantment">Percent of the hour a point of Item Enchantment is worth.</param>
/// <param name="ManaPool">Percent of the hour a percent more maximum mana is worth.</param>
/// <param name="ManaRegenASecond">Her mana regeneration a second (the mana a regeneration cantrip multiplies).</param>
/// <param name="ManaPointSeconds">The seconds a point of mana costs her from its cheapest source (a press).</param>
/// <param name="AttacksASecond">The attacks thrown at her a second in a fight.</param>
/// <param name="HealthPointSeconds">The seconds a point of health costs her from its cheapest source (a health drain).</param>
/// <param name="Burden">Percent of the hour a percent of her burden capacity is worth, as a cost.</param>
/// <param name="SwapMargin">The score a piece must beat what it displaces by, in percent of the hour, for a swap.</param>
public sealed record LadderWearScore(double LifeMagic, double CreatureEnchantment, double ItemEnchantment, double ManaPool, double ManaRegenASecond, double ManaPointSeconds, double AttacksASecond, double HealthPointSeconds, double Burden, double SwapMargin)
{
    /// <summary>The weights a ladder without the section reads: the war mage's file's values.</summary>
    public static LadderWearScore Default { get; } = new(0.005, 0.002, 0.002, 0.01, 0.43, 0.023, 0.40, 0.018, 0.01, 0.1);

    /// <summary>The weights as the log prints them.</summary>
    public override string ToString() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"life {LifeMagic:0.###}/pt, creature {CreatureEnchantment:0.###}/pt, item {ItemEnchantment:0.###}/pt, mana pool {ManaPool:0.###}/%, mana regen {ManaRegenASecond:0.##}/s at {ManaPointSeconds:0.###} s/pt, attacks {AttacksASecond:0.##}/s, health {HealthPointSeconds:0.###} s/pt, burden {Burden:0.###}/%, swap margin {SwapMargin:0.###}");
}
