using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The player's spellbook (<see cref="SelfState.Spellbook"/>): the spells the character knows, the favourite-spell bars, the spellbook's filters and the desired components, as GameEvent 0x0013
/// PlayerDescription sets them (retail's <c>CSpellBook</c>, <c>PlayerModule::m_favorite_spells</c>, <c>PlayerModule::m_SpellbookFilters</c> and the desired-components table) and GameEvent 0x02C1
/// MagicUpdateSpell adds to (<c>ClientMagicSystem::Handle_Magic__UpdateSpell</c>, 11.4186 0x005677F0, 11.6096 0x00568590, which asks <c>CACQualities::AddSpell</c>, 0x0058F220 / 0x00590050).
/// Empty until the description arrives; <see cref="SpellbookChanged"/> follows every change.
/// </summary>
/// <remarks>
/// Read by the spellbook and favourites panels and by the spell-bar and cast trackers. Mutated only by the reducer. The collections are immutable snapshots: a reader may keep one.
/// <c>TODO(m3-spell-removal)</c>: retail also takes a GameEvent that removes a spell (<c>Handle_Magic__RemoveSpell</c>, 0x00567890 / 0x00568630); net does not decode it and ACE sends it only
/// for a spell the player deletes through the spellbook.
/// </remarks>
public sealed class Spellbook
{
    /// <summary>An empty spellbook. Only the session creates one.</summary>
    internal Spellbook()
    {
    }

    /// <summary>True once a PlayerDescription arrived.</summary>
    public bool IsKnown { get; internal set; }

    /// <summary>
    /// The known spells: spell id to the value the description gave (retail's <c>SpellBookPage::_casting_likelihood</c>; 0 for a spell learned since, which retail adds with 0:
    /// <c>CACQualities::AddSpell</c>).
    /// </summary>
    public ImmutableDictionary<uint, float> Spells { get; private set; } = ImmutableDictionary<uint, float>.Empty;

    /// <summary>How many spells the character knows.</summary>
    public int Count => Spells.Count;

    /// <summary>True when the character knows <paramref name="spellId"/>.</summary>
    /// <param name="spellId">The spell id.</param>
    public bool Knows(uint spellId) => Spells.ContainsKey(spellId);

    /// <summary>The value of a known spell (the description's number; 0 when it was learned since).</summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="value">The value.</param>
    public bool TryGetValue(uint spellId, out float value) => Spells.TryGetValue(spellId, out value);

    /// <summary>
    /// The favourite-spell bars in the order the description carries them (ACE sends eight: bar 0 is the first spell tab's bar; the lists hold spell ids in slot order). Empty until the description
    /// arrives. Nothing sent by the client changes it yet (<c>AddSpellFavorite</c> 0x01E3 and <c>RemoveSpellFavorite</c> 0x01E4 are not sent).
    /// </summary>
    public ImmutableArray<ImmutableArray<uint>> SpellBars { get; private set; } = [];

    /// <summary>One favourite bar's spell ids in slot order, or an empty array when the bar does not exist.</summary>
    /// <param name="bar">The bar's index.</param>
    public ImmutableArray<uint> Bar(int bar) => bar >= 0 && bar < SpellBars.Length && !SpellBars[bar].IsDefault ? SpellBars[bar] : [];

    /// <summary>The spellbook's filter word (retail's default 0x3FFF when the description omits it; 0 until the description arrives).</summary>
    public uint Filters { get; private set; }

    /// <summary>The components the character wants to rebuy: component id to amount, in the description's order. Empty until the description arrives.</summary>
    public ImmutableArray<DesiredComponentDesc> DesiredComponents { get; private set; } = [];

    /// <summary>Replaces everything with the description's spellbook, bars, filters and desired components.</summary>
    internal void ApplyDescription(PlayerDescriptionMsg message)
    {
        Spells = message.Spells ?? ImmutableDictionary<uint, float>.Empty;
        SpellBars = message.SpellBars.IsDefault ? [] : message.SpellBars;
        Filters = message.SpellbookFilters;
        DesiredComponents = message.DesiredComponents.IsDefault ? [] : message.DesiredComponents;
        IsKnown = true;
    }

    /// <summary>Adds a learned spell (0x02C1) with the value 0 as retail does; a spell the book already holds keeps its value. Returns true when the spell was new.</summary>
    internal bool Learn(uint spellId)
    {
        if (spellId == 0 || Spells.ContainsKey(spellId))
        {
            return false;
        }

        Spells = Spells.Add(spellId, 0f);
        return true;
    }

    /// <summary>Forgets everything (the player left the world, or the session was reset).</summary>
    internal void Clear()
    {
        IsKnown = false;
        Spells = ImmutableDictionary<uint, float>.Empty;
        SpellBars = [];
        Filters = 0;
        DesiredComponents = [];
    }
}
