using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 16 Q4 (closes <c>TODO(m6-bolt-element-caster-mod)</c>): the one factor ACE multiplies her war bolt of an element by for a caster in her hands, against a plain caster she has mastered. The caster score's war term
/// (<see cref="GearScore.Caster"/>) and the fight's line for a target (<see cref="BoltElement.Lines"/> through <see cref="CastElement.BestOn"/>) both read it:
/// <list type="bullet">
/// <item>the elemental bonus: the caster's own ElementalDamageMod plus the enchantments on it and the wielder's Spirit Drinker, only when its DamageType is the spell's element (<c>GetCasterElementalDamageModifier</c>,
/// WorldObject_Weapon.cs:382-401 @95ef426d);</item>
/// <item>a quest weapon's resistance cleaving on its ResistanceModifierType: the bolt strikes as if the target had a vulnerability of 1 + ResistanceModifier (an absent modifier is ACE's 1), a factor on the target's own
/// resistance (<c>GetWeaponResistanceModifier</c>, WorldObject_Weapon.cs:461-486; <c>Creature.GetResistanceMod</c>, Creature_Properties.cs:132-135, 185-205);</item>
/// <item>the mastery bonus x1.05 on every spell, which a caster that is tough to master lacks (SpellProjectile.cs:735, WorldObject_Weapon.cs:1012-1023): the same on every line, so it moves no choice.</item>
/// </list>
/// A plain caster (no element, no cleaving, masterable) is x1 on every line: the fight's choice with one in hand is the choice of before.
/// </summary>
/// <param name="OnElement">The caster's element is the spell's: its elemental bonus counts.</param>
/// <param name="Elemental">The caster's own ElementalDamageMod (1 when absent).</param>
/// <param name="ElementalAdd">What is added to it: the enchantments on the caster and the wielder's aura.</param>
/// <param name="Cleaves">The caster cleaves the spell's element.</param>
/// <param name="Cleave">That cleaving's factor (1 when it does not cleave).</param>
/// <param name="Tough">The caster is tough to master: no mastery bonus.</param>
public readonly record struct WarFactor(bool OnElement, double Elemental, double ElementalAdd, bool Cleaves, double Cleave, bool Tough)
{
    /// <summary>The whole factor against a plain caster she has mastered.</summary>
    public double Value => (OnElement ? 1.0 + (Elemental - 1.0 + ElementalAdd) : 1.0) * Cleave * (Tough ? 1.0 / GearScore.MasteryBonus : 1.0);

    /// <summary>The factor of the caster appraised as <paramref name="a"/> on a bolt of <paramref name="element"/> (ACE DamageType bits; 0: no element, so no bonus and no cleaving count).</summary>
    /// <param name="a">The caster's appraisal.</param>
    /// <param name="element">The bolt's element bits.</param>
    /// <param name="ownElemental">The caster's own ElementalDamageMod, unfolded of enchantments (<see cref="CasterIntrinsic"/>), or null when absent.</param>
    /// <param name="elementalAdd">The enchantments on it and her aura, added to it on its element.</param>
    public static WarFactor Of(Appraisal a, uint element, double? ownElemental, double elementalAdd)
    {
        ArgumentNullException.ThrowIfNull(a);
        var type = a.Int(SelfEquipJudge.DamageTypeInt);
        var onElement = element != 0 && type is { } t && (uint)t == element;
        var cleaves = element != 0 && a.Int(AppraisalIds.ResistanceModifierType) is { } cleaveType && (uint)cleaveType == element;
        var cleave = cleaves ? 1.0 + (a.Float(AppraisalIds.ResistanceModifier) ?? 1.0) : 1.0;
        var tough = a.LongDescription?.Contains(GearScore.ToughToMaster, StringComparison.OrdinalIgnoreCase) == true;
        return new WarFactor(onElement, ownElemental ?? 1.0, elementalAdd, cleaves, cleave, tough);
    }
}

/// <summary>
/// M6 16 Q4: the caster in her hands now, as the fight reads it for its line (<see cref="WarFactor"/> on each element). Her live auras and the enchantments on the caster are read as ACE adds them when she casts: the
/// caster's appraisal unfolded to its own numbers (<see cref="CasterIntrinsic.Of"/>), then her Spirit Drinker on her (<see cref="CasterFold"/>) and the Spirit Thirst on it (its item enchantments) added back. No caster in her
/// hands, or one not yet appraised, is x1 on every line (the choice of before), and <see cref="Note"/> says which.
/// </summary>
public sealed class CasterWar
{
    private readonly double[] _factors;

    private CasterWar(uint guid, string? name, double[] factors, bool read, string note)
    {
        (Guid, Name, _factors, Read, Note) = (guid, name, factors, read, note);
        Plain = Array.TrueForAll(factors, f => f == 1.0);
    }

    /// <summary>No caster read: x1 on every line.</summary>
    public static CasterWar None { get; } = new(0, null, Ones(), false, "no caster in her hands: every line x1");

    /// <summary>The caster, 0 when none.</summary>
    public uint Guid { get; }

    /// <summary>Its name, or null.</summary>
    public string? Name { get; }

    /// <summary>True when its appraisal was read.</summary>
    public bool Read { get; }

    /// <summary>True when it is x1 on every line: the fight's choice is the plain caster's.</summary>
    public bool Plain { get; }

    /// <summary>What was read, for a log line.</summary>
    public string Note { get; }

    /// <summary>The factor on a bolt of <paramref name="element"/> (a key of <see cref="SelfEquipJudge.ElementBits"/>); 1 for a name that is no element.</summary>
    /// <param name="element">The element's name.</param>
    public double On(string element) => Index(element) is var i and >= 0 ? _factors[i] : 1.0;

    /// <summary>The caster in her hands now (see the class).</summary>
    /// <param name="state">The read model.</param>
    public static CasterWar Held(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var fold = CasterFold.Of(state);
        if (fold.WieldedCaster == 0)
        {
            return None;
        }

        var name = state.Objects.TryGet(fold.WieldedCaster, out var o) ? o.Name : null;
        if (!state.Appraisals.TryGet(fold.WieldedCaster, out var a) || a.IsNotFound)
        {
            return new CasterWar(fold.WieldedCaster, name, Ones(), false, $"'{name}' 0x{fold.WieldedCaster:X8} is not appraised yet: every line x1 (TODO(m6-bolt-element-caster-unread))");
        }

        return Of(fold.WieldedCaster, name, a, fold);
    }

    /// <summary>The caster <paramref name="guid"/> appraised as <paramref name="a"/> in her hands, with her fold <paramref name="fold"/>.</summary>
    /// <param name="guid">The caster.</param>
    /// <param name="name">Its name, or null.</param>
    /// <param name="a">Its appraisal.</param>
    /// <param name="fold">Her live auras and the caster she wields.</param>
    public static CasterWar Of(uint guid, string? name, Appraisal a, CasterFold fold)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(fold);
        var intrinsic = CasterIntrinsic.Of(guid, a, fold);
        var ench = a.ItemEnchantments.IsDefaultOrEmpty ? [] : a.ItemEnchantments.ToArray();
        var add = CasterSpells.Top(ench, CasterFold.ElementalKey, aura: true, 0) + CasterSpells.Top(ench, CasterFold.ElementalKey, aura: false, 0) + (guid == fold.WieldedCaster ? fold.Elemental : 0);
        var factors = new double[Elements.Length];
        var parts = new List<string>();
        for (var i = 0; i < Elements.Length; i++)
        {
            factors[i] = WarFactor.Of(a, SelfEquipJudge.ElementBits[Elements[i]], intrinsic.Elemental, add).Value;
            if (factors[i] != 1.0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Elements[i]} x{factors[i]:0.###}"));
            }
        }

        var note = parts.Count == 0
            ? $"'{name}' 0x{guid:X8}: x1 on every line (no element, no cleaving, masterable)"
            : string.Create(CultureInfo.InvariantCulture, $"'{name}' 0x{guid:X8}: {string.Join(", ", parts)}{(parts.Count < Elements.Length ? ", every other line x1" : string.Empty)} (its elemental x{intrinsic.Elemental ?? 1.0:0.###} + {add:0.###} on its element, cleaving, mastery)");
        return new CasterWar(guid, name, factors, true, note);
    }

    private static readonly string[] Elements = ["slash", "pierce", "bludgeon", "cold", "fire", "acid", "electric"];

    private static int Index(string element)
    {
        for (var i = 0; i < Elements.Length; i++)
        {
            if (string.Equals(Elements[i], element, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static double[] Ones() => [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0];
}
