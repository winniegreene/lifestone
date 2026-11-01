namespace Lifestone.Client.Session.Contracts;

/// <summary>Which of an item appraisal's three enchantment masks (M5 7 D142; ACE <c>AppraiseInfo</c> ArmorHighlight/ArmorColor, WeaponHighlight/WeaponColor, ResistHighlight/ResistColor).</summary>
public enum AppraisalMaskKind
{
    /// <summary>The armour mask (ACE <c>ArmorMask</c>: 0x1 the armour level, 0x2 to 0x80 the protections slashing, piercing, bludgeoning, cold, fire, acid, electric).</summary>
    Armor,

    /// <summary>The weapon mask (ACE <c>WeaponMask</c>: 0x1 attack, 0x2 melee defence, 0x4 speed, 0x8 damage, 0x10 variance, 0x20 damage modifier).</summary>
    Weapon,

    /// <summary>The resist mask (ACE <c>ResistMask</c>: 0x1000 mana conversion, 0x2000 elemental damage, and the wielder's resistances).</summary>
    Resist,
}

/// <summary>
/// Contract readers for the parts of an item appraisal the retail UI's item window colours and shows (M5 7 D142): the three enchantment masks and the armour's protections, as plain values so the retail UI reads them without naming the
/// wire records (as <see cref="AppraisalCreatureReads"/> does for the creature window). Each is null when the server did not send it.
/// </summary>
public static class AppraisalItemReads
{
    /// <summary>The mask <paramref name="kind"/>: the highlight word (set: an enchantment changed the value) and the colour word (set: it raised it), or null when the answer carries none.</summary>
    public static (ushort Highlights, ushort Colors)? EnchantmentMask(this Appraisal appraisal, AppraisalMaskKind kind)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        var mask = kind switch
        {
            AppraisalMaskKind.Armor => appraisal.ArmorEnchantments,
            AppraisalMaskKind.Weapon => appraisal.WeaponEnchantments,
            _ => appraisal.ResistEnchantments,
        };
        return mask is { } m ? (m.Highlight, m.Color) : null;
    }

    /// <summary>
    /// The armour profile's eight protections in the order retail's <c>Appraisal_ShowArmorMods</c> (11.6096 0x004B2600) shows them: slashing, piercing, bludgeoning, fire, cold, acid, electric, nether; null without a profile (ACE sends one
    /// for clothing, armour and shields on a success).
    /// </summary>
    public static float[]? ArmorProtections(this Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.Armor is { } p ? [p.Slashing, p.Piercing, p.Bludgeoning, p.Fire, p.Cold, p.Acid, p.Lightning, p.Nether] : null;
    }
}
