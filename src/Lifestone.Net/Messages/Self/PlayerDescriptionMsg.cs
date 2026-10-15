using System.Collections.Immutable;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// GameEvent 0x0013 PlayerDescription (owner decision D16: vitals, attributes, skills and the properties behind
/// character info and the run rate), decoded whole by <c>PlayerDescriptionCodec</c> (N10). Retail reads it in
/// <c>CPlayerSystem::Handle_PlayerDescription</c>: the player's qualities (<c>CACQualities::UnPack</c>), then the player
/// module (<c>PlayerModule::UnPack</c>), then the inventory and the equipped objects. Words stay raw; ids are the server's.
/// </summary>
/// <remarks>
/// The members from <see cref="PropertyFlags"/> on were added by N10 (additive; the N0 members keep their meaning).
/// </remarks>
public sealed record PlayerDescriptionMsg
{
    /// <summary>The weenie type word that follows the property flags.</summary>
    public uint WeenieType { get; init; }

    /// <summary>PropertyInt values by property id.</summary>
    public ImmutableDictionary<uint, int> IntProperties { get; init; } = ImmutableDictionary<uint, int>.Empty;

    /// <summary>PropertyInt64 values by property id.</summary>
    public ImmutableDictionary<uint, long> Int64Properties { get; init; } = ImmutableDictionary<uint, long>.Empty;

    /// <summary>PropertyBool values by property id.</summary>
    public ImmutableDictionary<uint, bool> BoolProperties { get; init; } = ImmutableDictionary<uint, bool>.Empty;

    /// <summary>PropertyFloat values by property id.</summary>
    public ImmutableDictionary<uint, double> FloatProperties { get; init; } = ImmutableDictionary<uint, double>.Empty;

    /// <summary>PropertyString values by property id.</summary>
    public ImmutableDictionary<uint, string> StringProperties { get; init; } = ImmutableDictionary<uint, string>.Empty;

    /// <summary>PropertyDataId values by property id.</summary>
    public ImmutableDictionary<uint, uint> DataIdProperties { get; init; } = ImmutableDictionary<uint, uint>.Empty;

    /// <summary>PropertyInstanceId values by property id.</summary>
    public ImmutableDictionary<uint, uint> InstanceIdProperties { get; init; } = ImmutableDictionary<uint, uint>.Empty;

    /// <summary>The primary attributes present (1 Strength … 6 Self), in id order.</summary>
    public ImmutableArray<AttributeDesc> Attributes { get; init; } = [];

    /// <summary>
    /// The vitals present, in the order health, stamina, mana. The attribute cache carries them as its entries 7, 8 and
    /// 9; <see cref="VitalDesc.Id"/> holds the id PrivateUpdateVital uses for the same vital (1 MaxHealth, 3 MaxStamina,
    /// 5 MaxMana).
    /// </summary>
    public ImmutableArray<VitalDesc> Vitals { get; init; } = [];

    /// <summary>The skills, in wire order.</summary>
    public ImmutableArray<SkillDesc> Skills { get; init; } = [];

    /// <summary>
    /// The spellbook: spell id to its casting likelihood, decoded as retail's <c>SpellBookPage::UnPack</c> decodes it (a
    /// first f32 of 2 or more is the likelihood plus 2, the form ACE writes; a smaller one starts the old 12-byte form,
    /// whose third word is the likelihood).
    /// </summary>
    public ImmutableDictionary<uint, float> Spells { get; init; } = ImmutableDictionary<uint, float>.Empty;

    /// <summary>The property flags word, raw (which property tables follow; bits retail does not know are kept and ignored).</summary>
    public uint PropertyFlags { get; init; }

    /// <summary>Position properties by position type (ACE sends only LastOutsideDeath, 0x0E, when the player has one).</summary>
    public ImmutableDictionary<uint, AcPosition> PositionProperties { get; init; } = ImmutableDictionary<uint, AcPosition>.Empty;

    /// <summary>The vector flags word, raw: attributes 0x1, skills 0x2, spells 0x100, enchantments 0x200.</summary>
    public uint VectorFlags { get; init; }

    /// <summary>The has-health word (ACE writes 1 for a player).</summary>
    public bool HasHealth { get; init; }

    /// <summary>The attribute cache's flags word, raw (ACE writes 0x1FF: the six attributes and the three vitals).</summary>
    public uint AttributeFlags { get; init; }

    /// <summary>The enchantment registry, in wire order: multiplicative, additive, cooldown, then vitae.</summary>
    public ImmutableArray<EnchantmentDesc> Enchantments { get; init; } = [];

    /// <summary>The enchantment registry's mask word, raw (0 when the vector flags carry no registry).</summary>
    public uint EnchantmentMask { get; init; }

    /// <summary>The player module's option flags word, raw (which option sections follow; ACE's <c>CharacterOptionDataFlag</c>).</summary>
    public uint OptionFlags { get; init; }

    /// <summary>The first character options word (retail <c>PlayerModule.options_</c>).</summary>
    public uint Options1 { get; init; }

    /// <summary>The second character options word; retail's default 0x948700 when the flags omit it.</summary>
    public uint Options2 { get; init; }

    /// <summary>The spellbook filters; retail's default 0x3FFF when the flags omit them.</summary>
    public uint SpellbookFilters { get; init; }

    /// <summary>The shortcut bar entries (option flag 0x1).</summary>
    public ImmutableArray<ShortcutDesc> Shortcuts { get; init; } = [];

    /// <summary>The favourite-spell bars: one, then four, six or seven more as the option flags say (ACE sends eight).</summary>
    public ImmutableArray<ImmutableArray<uint>> SpellBars { get; init; } = [];

    /// <summary>The desired components to rebuy (option flag 0x8): component id to amount, in wire order.</summary>
    public ImmutableArray<DesiredComponentDesc> DesiredComponents { get; init; } = [];

    /// <summary>The timestamp format string (option flag 0x80); null when absent. ACE never sends it.</summary>
    public string? TimestampFormat { get; init; }

    /// <summary>The generic player options data (option flag 0x100); null when absent. ACE never sends it.</summary>
    public GenericQualitiesDesc? OptionsData { get; init; }

    /// <summary>
    /// The gameplay options (option flag 0x200), undecoded, and everything after them: retail's
    /// <c>PlayerModule::m_colGameplayOptions</c> is a property collection serialised through a versioned archive, which
    /// ACE stores and returns as the bytes the client last sent, with no length in front, so where it ends (and the
    /// inventory begins) is not known without decoding it. Empty when the flag is absent. See
    /// <see cref="InventoryUndecoded"/>.
    /// </summary>
    public ImmutableArray<byte> GameplayOptions { get; init; } = [];

    /// <summary>
    /// True when the option flags carry the gameplay options (0x200): then <see cref="GameplayOptions"/> holds every byte
    /// from them to the end of the message, and <see cref="Inventory"/> and <see cref="Equipped"/> stay empty because
    /// their start cannot be found. TODO(net): decode retail's archive (<c>PackObjPropertyCollection::Serialize</c>,
    /// 11.4186 0x005D4F80, 11.6096 0x005D60D0) when an options screen or the inventory needs it. ACE sets the flag only
    /// for a character a client sent options for; Lifestone sends none in M2. The session logs a warning for it.
    /// </summary>
    public bool InventoryUndecoded { get; init; }

    /// <summary>The objects in the player's main pack, in wire order (retail's <c>PackableList&lt;ContentProfile&gt;</c>).</summary>
    public ImmutableArray<ContentDesc> Inventory { get; init; } = [];

    /// <summary>The objects the player wields or wears, in wire order (retail's <c>PackableList&lt;InventoryPlacement&gt;</c>).</summary>
    public ImmutableArray<EquippedDesc> Equipped { get; init; } = [];
}

/// <summary>A primary attribute (PlayerDescription, PrivateUpdateAttribute 0x02E3).</summary>
/// <param name="Id">The attribute id (1 Strength … 6 Self).</param>
/// <param name="Ranks">Ranks raised.</param>
/// <param name="Start">The starting (innate) value.</param>
/// <param name="Experience">Experience spent.</param>
public readonly record struct AttributeDesc(uint Id, uint Ranks, uint Start, uint Experience);

/// <summary>A vital (PlayerDescription, PrivateUpdateVital 0x02E7).</summary>
/// <param name="Id">The vital id (1 MaxHealth, 3 MaxStamina, 5 MaxMana).</param>
/// <param name="Ranks">Ranks raised.</param>
/// <param name="Start">The starting value.</param>
/// <param name="Experience">Experience spent.</param>
/// <param name="Current">The current value.</param>
public readonly record struct VitalDesc(uint Id, uint Ranks, uint Start, uint Experience, uint Current);

/// <summary>A skill (PlayerDescription, PrivateUpdateSkill 0x02DD).</summary>
/// <param name="Id">The skill id.</param>
/// <param name="Ranks">Ranks raised (u16).</param>
/// <param name="AdjustPp">The u16 ACE writes as 1 after the ranks.</param>
/// <param name="AdvancementClass">The SkillAdvancementClass (untrained, trained, specialised).</param>
/// <param name="Experience">Experience spent.</param>
/// <param name="Init">The starting level (bonus points).</param>
/// <param name="ResistanceAtLastCheck">The resistance at the last check.</param>
/// <param name="LastUsed">When the skill was last used (f64).</param>
public readonly record struct SkillDesc(
    uint Id,
    ushort Ranks,
    ushort AdjustPp,
    uint AdvancementClass,
    uint Experience,
    uint Init,
    uint ResistanceAtLastCheck,
    double LastUsed);

/// <summary>
/// One enchantment of the registry (retail <c>Enchantment::UnPack</c>; ACE <c>Network/Structure/Enchantment.cs</c>):
/// 60 bytes, or 64 with a spell set id.
/// </summary>
/// <param name="SpellId">The spell id (the low half of retail's layered spell id).</param>
/// <param name="Layer">The layer (the high half).</param>
/// <param name="SpellCategory">The spell category (the low half of the second word).</param>
/// <param name="HasSpellSetId">The high half of the second word; non-zero when a spell set id follows.</param>
/// <param name="PowerLevel">The power level.</param>
/// <param name="StartTime">The start time, as sent (retail adds its own clock).</param>
/// <param name="Duration">The duration in seconds (-1 for an item's lasting spell).</param>
/// <param name="Caster">The caster's GUID.</param>
/// <param name="DegradeModifier">The degrade modifier.</param>
/// <param name="DegradeLimit">The degrade limit.</param>
/// <param name="LastTimeDegraded">The last time degraded, as sent.</param>
/// <param name="StatModType">The stat mod's type flags.</param>
/// <param name="StatModKey">The stat mod's key.</param>
/// <param name="StatModValue">The stat mod's value.</param>
/// <param name="SpellSetId">The spell set id, 0 when absent.</param>
/// <param name="Bucket">Which list of the registry it came from.</param>
public readonly record struct EnchantmentDesc(
    ushort SpellId,
    ushort Layer,
    ushort SpellCategory,
    ushort HasSpellSetId,
    uint PowerLevel,
    double StartTime,
    double Duration,
    uint Caster,
    float DegradeModifier,
    float DegradeLimit,
    double LastTimeDegraded,
    uint StatModType,
    uint StatModKey,
    float StatModValue,
    uint SpellSetId,
    EnchantmentBucket Bucket);

/// <summary>The lists of the enchantment registry; each value is its bit in the registry's mask.</summary>
public enum EnchantmentBucket : uint
{
    /// <summary>The multiplicative list.</summary>
    Multiplicative = 0x1,

    /// <summary>The additive list.</summary>
    Additive = 0x2,

    /// <summary>The single vitae enchantment.</summary>
    Vitae = 0x4,

    /// <summary>The cooldown list.</summary>
    Cooldown = 0x8,
}

/// <summary>A shortcut bar entry (retail <c>ShortCutData</c>, 12 bytes).</summary>
/// <param name="Index">The slot.</param>
/// <param name="ObjectId">The object's GUID.</param>
/// <param name="SpellId">The layered spell id (spell in the low half, layer in the high half).</param>
public readonly record struct ShortcutDesc(uint Index, uint ObjectId, uint SpellId);

/// <summary>A desired component (retail's <c>PackableHashTable&lt;IDClass, long&gt;</c> entry).</summary>
/// <param name="ComponentId">The spell component id.</param>
/// <param name="Amount">The amount to rebuy.</param>
public readonly record struct DesiredComponentDesc(uint ComponentId, int Amount);

/// <summary>An object in the main pack (retail <c>ContentProfile</c>).</summary>
/// <param name="Guid">The object's GUID.</param>
/// <param name="ContainerType">0 an ordinary object, 1 a container, 2 a foci (ACE <c>ContainerType</c>).</param>
public readonly record struct ContentDesc(uint Guid, uint ContainerType);

/// <summary>An object the player wields or wears (retail <c>InventoryPlacement</c>).</summary>
/// <param name="Guid">The object's GUID.</param>
/// <param name="Location">The equip location mask.</param>
/// <param name="Priority">The clothing priority.</param>
public readonly record struct EquippedDesc(uint Guid, uint Location, uint Priority);

/// <summary>The generic qualities of the player options data (retail <c>GenericQualitiesData::UnPack</c>).</summary>
/// <param name="Ints">Int values by id.</param>
/// <param name="Bools">Bool values by id.</param>
/// <param name="Floats">Float values by id.</param>
/// <param name="Strings">String values by id.</param>
public sealed record GenericQualitiesDesc(
    ImmutableDictionary<uint, int> Ints,
    ImmutableDictionary<uint, bool> Bools,
    ImmutableDictionary<uint, double> Floats,
    ImmutableDictionary<uint, string> Strings);
