using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

/// <summary>
/// GameEvent 0x02C2 MagicUpdateEnchantment: one enchantment added to the player or refreshed (a recast replaces the layer).
/// </summary>
/// <param name="Enchantment">
/// The enchantment as ACE's <c>Enchantment</c> writes it (<see cref="EnchantmentDesc"/>: 60 bytes, 64 with the spell set id).
/// Its <see cref="EnchantmentDesc.Bucket"/> is not on the wire: <see cref="MagicCodecs"/> derives it (the cooldown category,
/// the multiplicative stat mod bit, the vitae spell, else additive), and lane s1 confirms the rule against retail's registry
/// (TODO(m3-enchant-bucket)).
/// </param>
public readonly record struct MagicUpdateEnchantmentEvent(EnchantmentDesc Enchantment);

/// <summary>GameEvent 0x02C4 MagicUpdateMultipleEnchantments (ACE never sends it): several enchantments added or refreshed.</summary>
/// <param name="Enchantments">The enchantments, in wire order (buckets derived as for <see cref="MagicUpdateEnchantmentEvent"/>).</param>
public readonly record struct MagicUpdateMultipleEnchantmentsEvent(ImmutableArray<EnchantmentDesc> Enchantments);
