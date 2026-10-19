using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>What an <see cref="EnchantmentChanged"/> did to the registry.</summary>
public enum EnchantmentChange
{
    /// <summary>New enchantments joined the registry (GameEvent 0x02C2 or 0x02C4).</summary>
    Added,

    /// <summary>Enchantments overwrote existing ones: a recast or a refresh (0x02C2 or 0x02C4).</summary>
    Replaced,

    /// <summary>Enchantments ended or were removed, with the chat line and sound of an expiry (0x02C3, 0x02C5).</summary>
    Removed,

    /// <summary>Enchantments were removed silently (0x02C7, 0x02C8).</summary>
    Dispelled,

    /// <summary>Every enchantment but the item spells, the cooldowns and the vitae was removed (0x02C6, death).</summary>
    Purged,

    /// <summary>Every harmful enchantment was removed (0x0312, death with an augmentation).</summary>
    PurgedBad,
}

/// <summary>
/// The player's enchantment registry changed (<see cref="PlayerQualities.Enchantments"/>, <see cref="PlayerQualities.ActiveEnchantments"/>): GameEvent 0x02C2 and 0x02C4 (added, replaced), 0x02C3 and
/// 0x02C5 (removed), 0x02C7 and 0x02C8 (dispelled), 0x02C6 (purged) and 0x0312 (purged bad), applied as retail's <c>ClientMagicSystem::Handle_Magic__*</c> do (11.4186 0x00567930 to 0x00568B20). The
/// vitals' maximums change with it: a panel that shows them re-reads <see cref="PlayerQualities.TryGetVitalLevels"/>. A 0x02C4 that adds some enchantments and replaces others publishes two events.
/// </summary>
/// <param name="Change">What happened.</param>
/// <param name="Spells">The spell ids and layers the change touched, in message order (for a purge, the enchantments that went).</param>
/// <param name="Count">How many enchantments the registry holds now.</param>
/// <param name="Notify">True when retail prints "&lt;spell&gt; has expired." for each spell (<c>ClientMagicSystem::NotifyOfEnchantmentRemoval</c>, 0x005686C0 / 0x00569460: 0x02C3 and 0x02C5 only, spells below 0x8000, " penalty" added for the vitae).</param>
/// <param name="VitaeChanged">True when the vitae was among the entries (retail sends its vitae notice then).</param>
public readonly record struct EnchantmentChanged(EnchantmentChange Change, ImmutableArray<LayeredSpellDesc> Spells, int Count, bool Notify, bool VitaeChanged) : IGameEvent;

/// <summary>What a <see cref="SpellbookChanged"/> did.</summary>
public enum SpellbookChange
{
    /// <summary>A whole PlayerDescription replaced the spellbook, the bars, the filters and the desired components.</summary>
    Description,

    /// <summary>A spell was learned (GameEvent 0x02C1).</summary>
    Learned,

    /// <summary>The client put a spell on a bar or took one off (it sent AddSpellFavorite or RemoveSpellFavorite; M3 4.2 p1). The next description replaces the bars.</summary>
    Favorites,
}

/// <summary>
/// The player's spellbook changed (<see cref="SelfState.Spellbook"/>): the description set it, or GameEvent 0x02C1 MagicUpdateSpell added a spell (<c>ClientMagicSystem::Handle_Magic__UpdateSpell</c>).
/// ACE sends "You learn the X spell." as its own chat message, so nothing composes it here.
/// </summary>
/// <param name="Change">What happened.</param>
/// <param name="SpellId">The spell learned; 0 for <see cref="SpellbookChange.Description"/>.</param>
/// <param name="Count">How many spells the book holds now.</param>
/// <param name="IsNew">For <see cref="SpellbookChange.Learned"/>: false when the book already held the spell (nothing changed); always true for a description.</param>
public readonly record struct SpellbookChanged(SpellbookChange Change, uint SpellId, int Count, bool IsNew) : IGameEvent;
