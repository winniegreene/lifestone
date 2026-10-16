using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

// The magic game events (M3 1.2 s0). Each is the payload of an F7B0 envelope; MagicCodecs holds the parsers and the
// layouts. Lane s1 applies them to PlayerQualities.Enchantments and the spellbook.

/// <summary>A spell and its layer (ACE <c>LayeredSpell</c>; retail's layered spell id: the spell in the low half, the layer in the high half).</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="Layer">The layer.</param>
public readonly record struct LayeredSpellDesc(ushort SpellId, ushort Layer);

/// <summary>GameEvent 0x02C1 MagicUpdateSpell: a spell added to the spellbook ("You learn the X spell." is a separate chat line).</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="Layer">The layer ACE writes (always 0, "unused?" in ACE).</param>
public readonly record struct MagicUpdateSpellEvent(ushort SpellId, ushort Layer);

/// <summary>GameEvent 0x02C3 MagicRemoveEnchantment: an enchantment ended or was removed, with its chat line and sound.</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="Layer">The layer.</param>
public readonly record struct MagicRemoveEnchantmentEvent(ushort SpellId, ushort Layer);

/// <summary>GameEvent 0x02C7 MagicDispelEnchantment: an enchantment removed silently (no chat line).</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="Layer">The layer.</param>
public readonly record struct MagicDispelEnchantmentEvent(ushort SpellId, ushort Layer);

/// <summary>GameEvent 0x02C6 MagicPurgeEnchantments: every enchantment removed silently (death). It has no payload.</summary>
public readonly record struct MagicPurgeEnchantmentsEvent;

/// <summary>GameEvent 0x0312 MagicPurgeBadEnchantments: every harmful enchantment removed silently. It has no payload.</summary>
public readonly record struct MagicPurgeBadEnchantmentsEvent;

/// <summary>GameEvent 0x02C5 MagicRemoveMultipleEnchantments (ACE never sends it): several enchantments removed.</summary>
/// <param name="Spells">The spells and layers, in wire order.</param>
public readonly record struct MagicRemoveMultipleEnchantmentsEvent(ImmutableArray<LayeredSpellDesc> Spells);

/// <summary>GameEvent 0x02C8 MagicDispelMultipleEnchantments: several enchantments removed silently.</summary>
/// <param name="Spells">The spells and layers, in wire order.</param>
public readonly record struct MagicDispelMultipleEnchantmentsEvent(ImmutableArray<LayeredSpellDesc> Spells);
