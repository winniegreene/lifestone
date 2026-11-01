namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Contract readers for the parts of a creature appraisal the <see cref="Appraisal"/> class does not name (M5 7, D137): the six attributes, the enchantment masks and the nine armour levels, as plain values so the retail UI
/// reads them without naming the wire records (the retail UI reaches the session only through this contract). Each is null when the server did not send it: ACE sends the attributes and the masks only on a successful roll
/// (<c>CreatureProfile.cs:47-74</c>) and the armour levels only on a success, for a player or an unattackable creature (<c>AppraiseInfo.cs:574-575</c>).
/// </summary>
public static class AppraisalCreatureReads
{
    /// <summary>
    /// A creature's attribute by retail's attribute id (1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self; <c>CreatureAppraisalProfile::InqAttribute</c>, 11.6096 0x005B6E30), or null without the attributes
    /// or for another id.
    /// </summary>
    public static uint? CreatureAttribute(this Appraisal appraisal, int attribute)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        var p = appraisal.Creature;
        return p is null ? null : attribute switch
        {
            1 => p.Strength,
            2 => p.Endurance,
            3 => p.Quickness,
            4 => p.Coordination,
            5 => p.Focus,
            6 => p.Self,
            _ => null,
        };
    }

    /// <summary>
    /// The creature profile's enchantment masks (ACE <c>AttributeMask</c>: 0x1 Strength to 0x20 Self, 0x40 Health, 0x80 Stamina, 0x100 Mana): the highlight word (set: enchanted) and the colour word (set: raised), or null when the
    /// profile carries none (no enchantment, or a failed roll).
    /// </summary>
    public static (ushort Highlights, ushort Colors)? CreatureEnchantmentMasks(this Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.Creature is { AttributeHighlights: { } high } p ? (high, p.AttributeColors ?? 0) : null;
    }

    /// <summary>The nine armour levels in the wire's order (head, chest, abdomen, upper arm, lower arm, hand, upper leg, lower leg, foot), or null when they were not sent.</summary>
    public static uint[]? ArmorLevelValues(this Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.ArmorLevels is { } al ? [al.Head, al.Chest, al.Abdomen, al.UpperArm, al.LowerArm, al.Hand, al.UpperLeg, al.LowerLeg, al.Foot] : null;
    }
}
