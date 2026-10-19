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
    /// arrives. Nothing sent by the client changes it: <c>AddSpellFavorite</c> 0x01E3 and <c>RemoveSpellFavorite</c> 0x01E4 show at the next description.
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

    /// <summary>
    /// Puts a spell on a bar as the client does when it sends AddSpellFavorite (M3 4.2 p1): retail's <c>SpellCastSubMenu::AddSpellToPlayerModule</c> (11.6096 0x004C70B0) asks
    /// <c>PlayerModule::AddSpellFavorite</c> (0x005D5460) before <c>CM_Character::Event_AddSpellFavorite</c> (0x006A1D90) goes out, so the client's own list changes at once. The rules are ACE's
    /// (<c>HandleActionAddSpellFavorite</c>, <c>AddSpellToBar</c>): a bar above 7 or a spell the book lacks changes nothing, a spell already on the bar stays where it is, the index is clamped to the bar's length.
    /// The next PlayerDescription replaces it. Returns true when the bar changed.
    /// </summary>
    internal bool AddFavorite(uint spellId, int index, int bar)
    {
        if (bar is < 0 or > 7 || spellId == 0 || !Knows(spellId))
        {
            return false;
        }

        var bars = GrowBars(bar);
        var list = bars[bar].IsDefault ? [] : bars[bar];
        if (list.Contains(spellId))
        {
            return false;
        }

        bars[bar] = list.Insert(Math.Clamp(index, 0, list.Length), spellId);
        SpellBars = bars.ToImmutable();
        return true;
    }

    /// <summary>
    /// Takes a spell off a bar as the client does when it sends RemoveSpellFavorite: <c>SpellCastSubMenu::RemoveSpellFromPlayerModule</c> (11.6096 0x004C7000) asks <c>PlayerModule::RemoveSpellFavorite</c>
    /// (0x005D5A60) before <c>CM_Character::Event_RemoveSpellFavorite</c> (0x006A26B0) goes out; the bar closes up. Returns true when the spell was on the bar.
    /// </summary>
    internal bool RemoveFavorite(uint spellId, int bar)
    {
        if (bar is < 0 or > 7 || bar >= SpellBars.Length || SpellBars[bar].IsDefault || !SpellBars[bar].Contains(spellId))
        {
            return false;
        }

        var bars = GrowBars(bar);
        bars[bar] = bars[bar].Remove(spellId);
        SpellBars = bars.ToImmutable();
        return true;
    }

    private ImmutableArray<ImmutableArray<uint>>.Builder GrowBars(int bar)
    {
        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>(Math.Max(SpellBars.Length, bar + 1));
        bars.AddRange(SpellBars);
        while (bars.Count <= bar)
        {
            bars.Add([]);
        }

        return bars;
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
