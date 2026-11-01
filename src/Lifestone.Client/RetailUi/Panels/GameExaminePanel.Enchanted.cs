using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

// M5 7 D142: the item window draws a line an enchantment raised in the raised font and one it lowered in the lowered font, as retail does (ItemExamineUI's constructor 0x004ABB70 makes index 1 the raised font and 2 the lowered, the
// attributes and skills tabs' indices; Appraisal_ShowWeaponAndArmorData 0x004B1780, Appraisal_ShowCasterData 0x004B21B0, Appraisal_ShowDefenseModData 0x004B2490 and Appraisal_ShowArmorMods 0x004B2600 pass the font to AddItemInfo
// 0x004AE020 with the whole line, label and value, so the whole line takes the colour). Whether a value was raised is read from the appraisal's three masks (AppraisalProfile::InqIntEnchantmentMod 0x005B3CC0 and
// InqFloatEnchantmentMod 0x005B3D20: the highlight half says an enchantment changed the value, the colour half that it raised it). ACE fills the masks in ArmorMaskHelper, WeaponMaskHelper and ResistMaskHelper.

/// <summary>The item window's lines and the font colour index of each (<see cref="ExamineColor"/>; M5 7 D142).</summary>
/// <param name="Lines">The lines, one a row (<see cref="GameExaminePanel.ItemLines"/>).</param>
/// <param name="Colors">The colour index of each line, as many as <paramref name="Lines"/>.</param>
public sealed record ExamineItemPage(IReadOnlyList<string> Lines, IReadOnlyList<int> Colors)
{
    /// <summary>The colour index of line <paramref name="index"/> (<see cref="ExamineColor.Normal"/> past the end of <see cref="Colors"/>).</summary>
    public int ColorOf(int index) => index >= 0 && index < Colors.Count ? Colors[index] : ExamineColor.Normal;
}

public sealed partial class GameExaminePanel
{
    /// <summary>ACE <c>ArmorMask.ArmorLevel</c> (<c>Network/Enum/ArmorMask.cs:8</c>): the armour level (retail's PropertyInt 28 in <c>InqIntEnchantmentMod</c>).</summary>
    internal const uint ArmorLevelBit = 0x1;

    /// <summary>ACE <c>WeaponMask.MeleeDefense</c> (<c>Network/Enum/WeaponMask.cs:13</c>): the defence bonus (PropertyFloat 29), the one weapon bit ACE sets for a caster.</summary>
    internal const uint WeaponMeleeDefenseBit = 0x2;

    /// <summary>ACE <c>WeaponMask.Speed</c> (<c>WeaponMask.cs:14</c>): the weapon's time (PropertyInt 49); raised means faster (ACE's colour bit is set for a time enchantment below 0, <c>WeaponMask.cs:68</c>).</summary>
    internal const uint WeaponSpeedBit = 0x4;

    /// <summary>ACE <c>WeaponMask.Damage</c> (<c>WeaponMask.cs:15</c>): the greatest damage (PropertyInt 44).</summary>
    internal const uint WeaponDamageBit = 0x8;

    /// <summary>ACE <c>WeaponMask.DamageVariance</c> (<c>WeaponMask.cs:16</c>): the damage variance (PropertyFloat 22); raised means a smaller variance (<c>WeaponMask.cs:72</c>).</summary>
    internal const uint WeaponVarianceBit = 0x10;

    /// <summary>ACE <c>ResistMask.ManaConversionMod</c> (<c>Network/Enum/ResistMask.cs:22</c>): a caster's mana conversion bonus (PropertyFloat 144; Hermetic Link raises it).</summary>
    internal const uint ResistManaConversionBit = 0x1000;

    /// <summary>ACE <c>ResistMask.ElementalDamageMod</c> (<c>ResistMask.cs:23</c>): a caster's elemental damage bonus (PropertyFloat 152; Spirit Drinker raises it).</summary>
    internal const uint ResistElementalDamageBit = 0x2000;

    /// <summary>
    /// The armour window's protections in retail's order (<c>Appraisal_ShowArmorMods</c>: slashing, piercing, bludgeoning, fire, cold, acid, electric, nether), each with its damage type, its armour-mask bit (ACE <c>ArmorMask</c>,
    /// <c>ArmorMask.cs:9-15</c>; retail's nether bit 0x100, which ACE never sets) and its label (<c>AppraisalSystem::DamageResistanceToString</c>, 11.6096 0x005B6540).
    /// </summary>
    private static readonly (uint DamageType, uint Bit, string Label)[] Protections =
    [
        (0x1, 0x2, "Slashing"), (0x2, 0x4, "Piercing"), (0x4, 0x8, "Bludgeoning"), (0x10, 0x20, "Fire"), (0x8, 0x10, "Cold"), (0x20, 0x40, "Acid"), (0x40, 0x80, "Electric"), (0x400, 0x100, "Nether"),
    ];

    /// <summary>
    /// The colour of a value a mask (its <paramref name="highlight"/> and <paramref name="color"/> halves, null when the answer has no such mask) covers with <paramref name="bit"/> (<c>AppraisalProfile::InqIntEnchantmentMod</c> 11.6096 0x005B3CC0, <c>InqFloatEnchantmentMod</c> 0x005B3D20): normal when the mask is absent or
    /// its highlight half lacks the bit, else raised when the colour half has it and lowered when not. <see cref="ExamineCreatureLines"/>' attribute colours read the creature's mask the same way.
    /// </summary>
    internal static int EnchantmentColor(ushort? highlight, ushort? color, uint bit)
    {
        if (highlight is not { } changed || (changed & bit) == 0)
        {
            return ExamineColor.Normal;
        }

        return ((color ?? 0) & bit) != 0 ? ExamineColor.Raised : ExamineColor.Lowered;
    }

    /// <summary><see cref="EnchantmentColor"/> of the armour mask (<see cref="AppraisalMaskKind.Armor"/>).</summary>
    internal static int ArmorColor(Appraisal a, uint bit) => a.EnchantmentMask(AppraisalMaskKind.Armor) is { } m ? EnchantmentColor(m.Highlights, m.Colors, bit) : ExamineColor.Normal;

    /// <summary><see cref="EnchantmentColor"/> of the weapon mask (<see cref="AppraisalMaskKind.Weapon"/>).</summary>
    internal static int WeaponColor(Appraisal a, uint bit) => a.EnchantmentMask(AppraisalMaskKind.Weapon) is { } m ? EnchantmentColor(m.Highlights, m.Colors, bit) : ExamineColor.Normal;

    /// <summary><see cref="EnchantmentColor"/> of the resist mask (<see cref="AppraisalMaskKind.Resist"/>).</summary>
    internal static int ResistColor(Appraisal a, uint bit) => a.EnchantmentMask(AppraisalMaskKind.Resist) is { } m ? EnchantmentColor(m.Highlights, m.Colors, bit) : ExamineColor.Normal;

    /// <summary>Adds <paramref name="text"/> and, when it is not <see cref="ExamineColor.Normal"/>, its colour.</summary>
    private static void AddColored(List<string> lines, Dictionary<int, int> colors, string text, int color)
    {
        if (color != ExamineColor.Normal)
        {
            colors[lines.Count] = color;
        }

        lines.Add(text);
    }

    /// <summary>
    /// <c>Appraisal_ShowDefenseModData</c> (11.6096 0x004B2490): a bonus to Melee Defense (PropertyFloat 29, in the colour the weapon mask's 0x2 gives: Defender on the wielder raises a caster's), to Missile Defense (149) and to Magic Defense
    /// (150), each only when it is not 1, as a percent to a tenth (<see cref="SmallModifierText"/>).
    /// </summary>
    private static void DefenseModLines(Appraisal a, List<string> lines, Dictionary<int, int> colors)
    {
        if (a.Float(29) is { } melee && melee != 1.0)
        {
            AddColored(lines, colors, $"Bonus to Melee Defense: {SmallModifierText(melee)}.", WeaponColor(a, WeaponMeleeDefenseBit));
        }

        if (a.Float(149) is { } missile && missile != 1.0)
        {
            lines.Add($"Bonus to Missile Defense: {SmallModifierText(missile)}.");
        }

        if (a.Float(150) is { } magic && magic != 1.0)
        {
            lines.Add($"Bonus to Magic Defense: {SmallModifierText(magic)}.");
        }
    }

    /// <summary>
    /// <c>Appraisal_ShowArmorMods</c> (11.6096 0x004B2600): an item with an armour profile and an armour level (PropertyInt 28) above 0 shows a blank line, "Armor Level: N" (the armour mask's 0x1 colours it: Impenetrability raises it), then a
    /// line a damage type in retail's order (<see cref="Protections"/>, <see cref="ProtectionLine"/>), each in the colour its mask bit gives (a bane raises it).
    /// </summary>
    private static void ArmorModLines(Appraisal a, List<string> lines, Dictionary<int, int> colors)
    {
        if (a.ArmorProtections() is not { } modifiers || a.ArmorLevel is not { } level || level <= 0)
        {
            return;
        }

        lines.Add(string.Empty); // retail's line is "\nArmor Level: N"
        AddColored(lines, colors, "Armor Level: " + level.ToString(CultureInfo.InvariantCulture), ArmorColor(a, ArmorLevelBit));
        // modifiers: the profile's protections in Protections' order (AppraisalItemReads.ArmorProtections).
        for (var i = 0; i < Protections.Length; i++)
        {
            var (_, bit, label) = Protections[i];
            AddColored(lines, colors, ProtectionLine(label, level, modifiers[i]), ArmorColor(a, bit));
        }
    }

    /// <summary>
    /// <c>AppraisalSystem::DamageResistanceToString</c> (11.6096 0x005B6540): "&lt;label&gt;: &lt;word&gt;" for the protection <paramref name="modifier"/> (none: "No"; up to 0.4 "Poor", 0.8 "Below Average", under 1.2 "Average", under 1.6 "Above
    /// Average", under 2 "Excellent", else "Unparalleled", counted as 2), or for a protection below 0 the armour's warning sentence alone; then two spaces and the armour level times the protection to a whole number in brackets.
    /// </summary>
    internal static string ProtectionLine(string label, int armorLevel, float modifier)
    {
        const double Epsilon = 0.00019999999; // retail compares the float protection with these doubles
        double value = modifier;
        string text;
        if (value < -Epsilon)
        {
            text = label switch
            {
                "Slashing" => "Your armor will rend and slash you if hit.",
                "Piercing" => "Your armor will cave in and pierce you if hit.",
                "Bludgeoning" => "Your armor will shatter and bruise you if hit.",
                "Cold" => "Your armor is unnaturally cold.",
                "Fire" => "Your armor is flammable.",
                "Acid" => "Your armor itches and burns your skin.",
                "Electric" => "Your armor is extremely conductive.",
                _ => "Your armor is infused with shadow.",
            };
        }
        else
        {
            string word;
            if (Math.Abs(value) <= Epsilon)
            {
                word = "No";
            }
            else if (value <= 0.4)
            {
                word = "Poor";
            }
            else if (value <= 0.8)
            {
                word = "Below Average";
            }
            else if (value < 1.2)
            {
                word = "Average";
            }
            else if (value < 1.6)
            {
                word = "Above Average";
            }
            else if (value < 2.0)
            {
                word = "Excellent";
            }
            else
            {
                word = "Unparalleled";
                value = 2.0;
            }

            text = label + ": " + word;
        }

        // "  (%.0f)": the C runtime rounds half to even.
        var protection = Math.Round(armorLevel * value, MidpointRounding.ToEven);
        return text + "  (" + protection.ToString("F0", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One INFO line an item answer's enchantment colours (M5 7 D142; the rehearsal's examine frames are checked against it): the three masks as highlight/colour, and the lines drawn raised and lowered. Logged for every item answer, so
    /// a page with no coloured line says so (raised=[] lowered=[]).
    /// </summary>
    private void LogEnchantedLines(Appraisal a)
    {
        var raised = new List<string>();
        var lowered = new List<string>();
        for (var i = 0; i < Lines.Count; i++)
        {
            var color = i < LineColors.Count ? LineColors[i] : ExamineColor.Normal;
            if (color == ExamineColor.Raised)
            {
                raised.Add(Lines[i]);
            }
            else if (color == ExamineColor.Lowered)
            {
                lowered.Add(Lines[i]);
            }
        }

        _context.Manager.Services.Log.Info(
            "retailui",
            string.Create(CultureInfo.InvariantCulture, $"examine enchanted guid=0x{a.Guid:X8} name=\"{a.Name}\" armor={Mask(a.EnchantmentMask(AppraisalMaskKind.Armor))} weapon={Mask(a.EnchantmentMask(AppraisalMaskKind.Weapon))} resist={Mask(a.EnchantmentMask(AppraisalMaskKind.Resist))} raised=[{string.Join(" | ", raised)}] lowered=[{string.Join(" | ", lowered)}]"));
    }

    private static string Mask((ushort Highlights, ushort Colors)? mask) => mask is { } m ? string.Create(CultureInfo.InvariantCulture, $"0x{m.Highlights:X}/0x{m.Colors:X}") : "none";
}
