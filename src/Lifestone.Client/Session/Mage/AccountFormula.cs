using DatReaderWriter.Enums;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 item 1 g (closes <c>m6-account-formula-plan</c> and <c>m6-outskirts-account-formula</c>): the components a spell line burns for THIS character, pure. ACE casts a school on the foci formula when the character has the school's focus
/// (<c>Player.HasFoci</c>, Player_Spells.cs:458-488 @95ef426d: the school's infusion augmentation above 0, or the focus item in the inventory, <c>FociWCIDs</c> 451-457); otherwise it casts the account's full formula
/// (<c>SpellFormula.GetPlayerFormula</c>, SpellFormula.cs:224, over <c>SpellTable.GetSpellFormula</c>: the base formula with its taper slots rewritten from the hash of the account name, <see cref="SpellFormulaRules.CustomizeForAccount"/>).
/// The starter foci exist only for the schools trained at creation (War 15271 and Life 15270, docs/session.md section 64), so a school trained later from the credits (Creature and Item Enchantment, M6 item 1) has no focus and its casts
/// burn the account's scarab, herbs, powders, potions and talisman. This is the mage's counterpart of the fighter's pattern (<see cref="Fighter.FighterComponents"/>, which plans on the account formula alone).
/// The infusion augmentations are not read (no test character has one; a character with one holds the focus formula without the item: <c>TODO(m6-foci-augmentation)</c>, the answer would be a wrong "short of" for that school).
/// </summary>
internal static class AccountFormula
{
    /// <summary>
    /// What a cast of <paramref name="info"/> burns, by component id (the slots of each; ACE needs as many of a component in the pack as the formula has slots of it, <c>HasComponentsForSpell</c>, Player_Magic.cs:1227-1248):
    /// the foci formula (<see cref="GearKit.FociFormulaCounts"/>) when <paramref name="holdsFocus"/>, else the account's full formula.
    /// </summary>
    /// <param name="info">The spell (its base formula and formula version).</param>
    /// <param name="holdsFocus">True when the character holds the spell's school's focus (see <see cref="HoldsFocus"/>).</param>
    /// <param name="accountName">The account's name as the server stores it (ACE creates it in lower case, AuthenticationHandler.cs:83); lower-cased here, hashed and never stored or logged. Read only without the focus.</param>
    /// <exception cref="ArgumentException">There is no focus and no account name: the full formula cannot be named (a loud stop, never a guess).</exception>
    public static Dictionary<uint, int> Counts(SpellInfo info, bool holdsFocus, string? accountName)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (holdsFocus)
        {
            return GearKit.FociFormulaCounts(info.Formula);
        }

        if (string.IsNullOrEmpty(accountName))
        {
            throw new ArgumentException($"spell {info.Id} ({info.Name}) is cast on the account's full formula (no {info.School} focus is held) and no account name was given", nameof(accountName));
        }

        var counts = new Dictionary<uint, int>();
        foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, accountName.ToLowerInvariant()))
        {
            if (id != 0)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return counts;
    }

    /// <summary><see cref="Counts(SpellInfo, bool, string)"/> for a pack: the focus is held when the pack holds the school's focus item (<see cref="LevelFloor.FocusOf"/>).</summary>
    /// <param name="info">The spell.</param>
    /// <param name="held">What the pack holds, by weenie class.</param>
    /// <param name="accountName">The account's name (read only without the focus).</param>
    public static Dictionary<uint, int> Counts(SpellInfo info, IReadOnlyDictionary<uint, int> held, string? accountName)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(held);
        return Counts(info, HoldsFocus(held, info.School), accountName);
    }

    /// <summary>True when <paramref name="held"/> holds the focus item of <paramref name="school"/> (a school with no focus item, or an unknown one, has none).</summary>
    /// <param name="held">What the pack holds, by weenie class.</param>
    /// <param name="school">The spell's school.</param>
    public static bool HoldsFocus(IReadOnlyDictionary<uint, int> held, MagicSchool school)
    {
        ArgumentNullException.ThrowIfNull(held);
        var focus = LevelFloor.FocusOf(school);
        return focus != 0 && held.GetValueOrDefault(focus) > 0;
    }
}
