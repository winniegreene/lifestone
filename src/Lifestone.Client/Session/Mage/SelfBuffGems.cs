namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 (the owner, 2026-11-15: keep Blackmoor's Favor active): the eternal gems that cast a lasting buff on her when used, as data from the world copy (read-only, 2026-11-15): Blackmoor's Favor, weenie 31000, spell 3811 (its
/// DID 28), usable from the pack (ItemUseable 8), Value 0, never consumed: "Increases Natural Armor by 50 points for 24 hours. Additional spells can be layered over this." She keeps them (never sold or dropped) and uses
/// one at home whenever its spell is not on her or has under <see cref="RefreshUnderSeconds"/> left.
/// </summary>
public static class SelfBuffGems
{
    /// <summary>One gem: its weenie, its name, the spell it casts on her and that spell's duration.</summary>
    /// <param name="Wcid">The gem's weenie class id.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="SpellId">The spell it casts on her.</param>
    /// <param name="DurationSeconds">The spell's duration.</param>
    public sealed record Gem(uint Wcid, string Name, uint SpellId, double DurationSeconds);

    /// <summary>The gems, by weenie.</summary>
    public static IReadOnlyList<Gem> All { get; } = [new(31000, "Blackmoor's Favor", 3811, 24 * 3600)];

    /// <summary>
    /// The time left under which the gem is used again at a home visit: two hours. Her visits home come about hourly (the buff bot's stop), so a line under two hours could lapse before the next one if a visit is missed;
    /// the gem costs nothing to use.
    /// </summary>
    public const double RefreshUnderSeconds = 2 * 3600;

    /// <summary>The gem of <paramref name="wcid"/>, or null.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static Gem? Of(uint wcid)
    {
        foreach (var g in All)
        {
            if (g.Wcid == wcid)
            {
                return g;
            }
        }

        return null;
    }
}
