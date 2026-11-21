using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// A piece of armour's own numbers, read out of an appraisal that may have been taken with enchantments on the piece (M6, the owner's go-ahead of 2026-11-19: the armour score counted her Impenetrability twice).
/// <para><b>What ACE sends.</b> An appraisal's armour level is the piece's own plus the enchantments on it (<c>AppraiseInfo.AddPropertyEnchantments</c>, Network/Structure/AppraiseInfo.cs:407-408 @95ef426d:
/// <c>ArmorLevel += EnchantmentManager.GetArmorMod()</c>), and each protection of its armour profile is the piece's own plus the banes and lures on it, clamped to -2..2 (<c>ArmorProfile.GetArmorMod</c>,
/// Network/Structure/ArmorProfile.cs:40-59). The enchantments themselves are listed in the same answer (<c>AddEnchantments</c>, AppraiseInfo.cs:493-501: every Item Enchantment on the piece, the 0x80000000 entries of the
/// spell book: <see cref="Appraisal.ItemEnchantments"/>). The live case, run 6 of 2026-11-19 (cache/m6/sprint/run6/one-window.log): the Pathwarden Robe, armour level 50 in the world database, read 100 with its own
/// Impenetrability II on (21:43 UTC) and 150 after her Impenetrability IV (22:09:59 UTC, read at 23:40:07).</para>
/// <para><b>Why it matters.</b> Every reader of a piece's armour adds her lines to it: the wear pick (<see cref="WearScore.DamageFactor"/>), the gear score (<see cref="GearScore.Armour"/>), the armour report and the fight's own
/// armour line (<see cref="ArmorReport.Read(IReadOnlyList{WornPiece}, uint, out int)"/>). On the appraised numbers a piece she wears and has cast on counted its Impenetrability and its bane twice, and a piece in the pack once, so
/// the worn piece won every comparison (the robe put back on over the leather set a quarter of a second after each swap, ten times in runs 5 and 6), and the fight read her armour higher than ACE does.</para>
/// <para><b>The rule.</b> The piece's own number is the appraised one less what the listed enchantments add: of each spell family on the piece the strongest row (ACE applies the top layer of a category,
/// <c>GetEnchantments_TopLayer</c>, WorldObjects/Managers/EnchantmentManager.cs:123-129, the layers by <c>Add</c>, :134-175), the families summed (<c>GetAdditiveMod</c>, :814-823; <c>GetArmorModVsType</c>, :1089-1101). The rows
/// are <see cref="ArmourSpellRows"/>. An appraisal taken before a cast lists nothing and is its own numbers already.</para>
/// </summary>
public static class ArmourBase
{
    /// <summary>The armour highlight's bit for the armour level (ACE <c>ArmorMask.ArmorLevel</c>): set when an enchantment changed it.</summary>
    private const ushort LevelHighlight = 0x1;

    /// <summary>What the enchantments listed in <paramref name="a"/> add to the stat <paramref name="key"/> of the piece: the strongest row of each family, the families summed; 0 when none is listed.</summary>
    /// <param name="a">The piece's appraisal.</param>
    /// <param name="key">28 the armour level, 13 to 19 a protection (<see cref="ArmourSpellRows.KeyOf"/>).</param>
    public static double Mod(Appraisal a, uint key)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (key == 0 || a.ItemEnchantments.IsDefaultOrEmpty)
        {
            return 0;
        }

        Dictionary<uint, double>? top = null;
        foreach (var id in a.ItemEnchantments)
        {
            if (!ArmourSpellRows.ById.TryGetValue(id, out var row) || row.Key != key)
            {
                continue;
            }

            top ??= [];
            if (!top.TryGetValue(row.Category, out var held) || Math.Abs(row.Value) > Math.Abs(held))
            {
                top[row.Category] = row.Value;
            }
        }

        return top is null ? 0 : top.Values.Sum();
    }

    /// <summary>
    /// The piece's own armour level: the appraised one less the Impenetrability (and Brittlemail) on it. 0 when the appraisal carries none. When ACE marks the level as enchanted and no listed enchantment explains it, the appraised
    /// level is returned and <paramref name="note"/> names <c>TODO(m6-armour-base-unread)</c>.
    /// </summary>
    /// <param name="a">The piece's appraisal.</param>
    /// <param name="note">How it was read, for a log line.</param>
    public static int Level(Appraisal a, out string note)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a.ArmorLevel is not { } appraised)
        {
            note = "no armour level";
            return 0;
        }

        var mod = (int)Math.Round(Mod(a, ArmourSpellRows.ArmorLevelKey));
        if (mod == 0)
        {
            note = a.ArmorEnchantments is { } mask && (mask.Highlight & LevelHighlight) != 0
                ?"TODO(m6-armour-base-unread): ACE marks the armour level as enchanted and the appraisal lists no enchantment that changes it: taken as appraised"
                : "as appraised: no enchantment on it changes the level";
            return appraised;
        }

        note = string.Create(CultureInfo.InvariantCulture, $"its own {appraised - mod}: appraised {appraised} less {mod} of the enchantments on it (AppraiseInfo.cs:407-408)");
        return appraised - mod;
    }

    /// <summary>The piece's own armour level (<see cref="Level(Appraisal, out string)"/>).</summary>
    /// <param name="a">The piece's appraisal.</param>
    public static int Level(Appraisal a) => Level(a, out _);

    /// <summary>
    /// A protection of the armour profile without the banes and lures on the piece: <paramref name="appraised"/> less what the listed enchantments add against <paramref name="damageType"/>. ACE clamps the profile's value to -2..2
    /// (ArmorProfile.cs:57), so at the clamp the own value is at least this far from it: the difference is still returned, and the reader clamps the sum with her line again as ACE does.
    /// </summary>
    /// <param name="a">The piece's appraisal.</param>
    /// <param name="damageType">One DamageType bit.</param>
    /// <param name="appraised">The profile's protection against it.</param>
    public static double Protection(Appraisal a, uint damageType, double appraised) => appraised - Mod(a, ArmourSpellRows.KeyOf(damageType));
}
