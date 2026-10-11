// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/SpellFlags.cs (AGPL-3.0) [with changes: only the
// SpellFlags enum (names and values) is from ACE, with doc comments added; SpellInfo itself follows retail
// CSpellBase::UnPack].
using DatReaderWriter.Enums;

namespace Lifestone.Data.Magic;

/// <summary>
/// <c>CSpellBase::_bitfield</c> flags. Names and values from ACE <c>Source/ACE.Entity/Enum/SpellFlags.cs</c>
/// (AGPL-3.0, @95ef426d); the retail client stores the same 32-bit field.
/// </summary>
[Flags]
public enum SpellFlags : uint
{
    None = 0,
    Resistable = 0x1,
    PKSensitive = 0x2,
    Beneficial = 0x4,
    SelfTargeted = 0x8,
    Reversed = 0x10,
    NotIndoor = 0x20,
    NotOutdoor = 0x40,
    NotResearchable = 0x80,
    Projectile = 0x100,
    CreatureSpell = 0x200,
    ExcludedFromItemDescriptions = 0x400,
    IgnoresManaConversion = 0x800,
    NonTrackingProjectile = 0x1000,
    FellowshipSpell = 0x2000,

    /// <summary>No wind-up gestures: ACE <c>Player.DoWindupGestures</c> returns early for these spells.</summary>
    FastCast = 0x4000,
    IndoorLongRange = 0x8000,
    DamageOverTime = 0x10000,
}

/// <summary>
/// One spell of the SpellTable (0x0E00000E), as retail <c>CSpellBase::UnPack</c> reads it (acclient 11.4186
/// AC/accmagic/CSpellBase.c). Strings are de-obfuscated (nibble-swapped) and decoded as Windows-1252.
/// </summary>
public sealed record SpellInfo
{
    public required uint Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required MagicSchool School { get; init; }

    public required uint IconId { get; init; }

    /// <summary>Spell category (stacking family; ACE <c>SpellCategory</c>).</summary>
    public required uint Category { get; init; }

    public required SpellFlags Flags { get; init; }

    public required uint BaseMana { get; init; }

    public required float BaseRangeConstant { get; init; }

    public required float BaseRangeMod { get; init; }

    /// <summary>Spell difficulty/power (used for resistance and stacking).</summary>
    public required uint Power { get; init; }

    public required float SpellEconomyMod { get; init; }

    /// <summary>Which account-name randomisation applies to the formula (0 = none, 1–3 = <c>SpellFormula::RandomizeVersionN</c>).</summary>
    public required uint FormulaVersion { get; init; }

    public required float ComponentLoss { get; init; }

    public required SpellType MetaSpellType { get; init; }

    public required uint MetaSpellId { get; init; }

    /// <summary>Enchantment duration in seconds (Enchantment / FellowEnchantment only).</summary>
    public double Duration { get; init; }

    public float DegradeModifier { get; init; }

    public float DegradeLimit { get; init; }

    /// <summary>Summoned portal lifetime (PortalSummon only).</summary>
    public double PortalLifetime { get; init; }

    /// <summary>The eight formula words exactly as stored (encrypted with the name/description key).</summary>
    public required IReadOnlyList<uint> EncryptedFormula { get; init; }

    /// <summary>
    /// The base formula: eight component ids decrypted like retail <c>CSpellBase::InqSpellFormula</c> (0 = empty slot).
    /// This is the formula before the per-account taper randomisation (<see cref="SpellFormulaRules.CustomizeForAccount"/>).
    /// </summary>
    public required IReadOnlyList<uint> Formula { get; init; }

    /// <summary>Effect played on the caster when the spell lands (resolved through the caster's PhysicsScriptTable).</summary>
    public required PlayScript CasterEffect { get; init; }

    /// <summary>Effect played on the target when the spell lands.</summary>
    public required PlayScript TargetEffect { get; init; }

    /// <summary>Stored fizzle effect; 0 for every spell at end of retail (ACE plays <c>PlayScript.Fizzle</c> at 0.5 instead).</summary>
    public required PlayScript FizzleEffect { get; init; }

    public required double RecoveryInterval { get; init; }

    public required float RecoveryAmount { get; init; }

    public required uint DisplayOrder { get; init; }

    public required uint NonComponentTargetType { get; init; }

    public required uint ManaMod { get; init; }

    /// <summary>Non-zero formula components in order.</summary>
    public IEnumerable<uint> Components => Formula.Where(c => c != 0);
}
