using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.PlayerRules;

/// <summary>One kind of drink the automation uses, and the shortcut slot a tidy player keeps it on.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Slot">The slot it goes on when that slot is free (0 is the key 1).</param>
public readonly record struct DrinkKind(uint Wcid, string Name, int Slot);

/// <summary>
/// The player's rule for drinks (M6 item 15, the owner's order): a person drinks a potion from the shortcut bar (<c>gmToolbarUI</c>), so every kind of potion and tincture the automation drinks is on the bar while any is held,
/// and the automation's drink is the use of that shortcut's item. This is the pure part: which slot a kind goes on and which slot holds it. No engine, no session: it reads <see cref="ShortcutBar"/> (what the server sent at
/// login, with the client's own changes since), the inventory and the object table.
/// </summary>
/// <remarks>
/// <para><b>The slots</b> are on the row the player sees (the keys 1 to 9), by vital: the health drinks on 1 to 3 (Health Draught, Potion of Healing, Health Tincture), the mana drinks on 4 to 6 (Mana Potion, Mana Draught,
/// Mana Tincture), the stamina drinks on 7 and 8 (Stamina Potion, Stamina Tincture). A fixed slot for each kind keeps the layout the same from session to session: the server keeps a shortcut only while its item is held
/// (the client removes the row when the item leaves, OpenAC #219), so a kind that ran out and is bought again in a later session goes back to its slot by this table, and within a session by the slot it was last seen on.</para>
/// <para>A slot that something else holds (the person's own shortcut, a kit) is never taken: the kind goes on the first empty slot to its right (<see cref="ShortcutBar.FirstEmptyToTheRightOf"/>), and a full bar is no slot
/// (the drink is then refused by name). A drink of no listed kind (food, a potion the table lacks) goes on the first empty slot after the listed ones.</para>
/// </remarks>
public static class DrinkBarRule
{
    /// <summary>Health Tincture (ACE weenie 27319).</summary>
    public const uint HealthTinctureWcid = 27319;

    /// <summary>Mana Tincture (ACE weenie 27322).</summary>
    public const uint ManaTinctureWcid = 27322;

    /// <summary>Stamina Tincture (ACE weenie 27326).</summary>
    public const uint StaminaTinctureWcid = 27326;

    /// <summary>The kinds the automation drinks, in slot order: the two the fighters' kit holds (377 and the loot's 2457), the mage's mana and stamina potions (379, 378), the loot's Mana Draught (2460) and the three tinctures.</summary>
    public static IReadOnlyList<DrinkKind> Kinds { get; } =
    [
        new(Vitals.VitalsMeans.HealthDraughtWcid, "Health Draught", 0),
        new(Fighter.FighterKit.PotionWcid, "Potion of Healing", 1),
        new(HealthTinctureWcid, "Health Tincture", 2),
        new(Fighter.FighterKit.ManaPotionWcid, "Mana Potion", 3),
        new(Vitals.VitalsMeans.ManaDraughtWcid, "Mana Draught", 4),
        new(ManaTinctureWcid, "Mana Tincture", 5),
        new(Fighter.FighterKit.StaminaPotionWcid, "Stamina Potion", 6),
        new(StaminaTinctureWcid, "Stamina Tincture", 7),
    ];

    /// <summary>The slot kind <paramref name="wcid"/> goes on when it is free, or -1 for a kind the table does not list.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static int PreferredSlot(uint wcid)
    {
        for (var i = 0; i < Kinds.Count; i++)
        {
            if (Kinds[i].Wcid == wcid)
            {
                return Kinds[i].Slot;
            }
        }

        return -1;
    }

    /// <summary>The weenie class of a held item, or 0 when the player does not hold it or the object table lacks it.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="item">The item's guid.</param>
    public static uint KindOf(GameState state, uint item)
    {
        ArgumentNullException.ThrowIfNull(state);
        return item != 0 && state.Self.Inventory.Contains(item) && state.Objects.TryGet(item, out var o) ? o.Weenie.WeenieClassId : 0;
    }

    /// <summary>A drink as the log names it: the object's own name when the table holds it, else the listed kind's, else its class number.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="item">The item's guid.</param>
    public static string NameOf(GameState state, uint item)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Objects.TryGet(item, out var o))
        {
            if (!string.IsNullOrEmpty(o.Weenie.Name))
            {
                return o.Weenie.Name;
            }

            foreach (var kind in Kinds)
            {
                if (kind.Wcid == o.Weenie.WeenieClassId)
                {
                    return kind.Name;
                }
            }

            return "weenie class " + o.Weenie.WeenieClassId.ToString(CultureInfo.InvariantCulture);
        }

        return "item 0x" + item.ToString("X8", CultureInfo.InvariantCulture);
    }

    /// <summary>The first slot whose item the player holds and is of kind <paramref name="wcid"/>, or -1: the shortcut the automation's drink of that kind uses.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="wcid">The weenie class id.</param>
    public static int SlotOfKind(GameState state, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bar = state.Self.Shortcuts;
        if (wcid == 0)
        {
            return -1;
        }

        for (var slot = 0; slot < ShortcutBar.SlotCount; slot++)
        {
            var row = bar[slot];
            if (row.IsItem && KindOf(state, row.ObjectId) == wcid)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// The slot a kind with no shortcut goes on (see the remarks of the class): the slot it was last on in this session when that is free, else its own slot when free, else the first free slot to the right of its own
    /// (after the listed kinds' slots for a kind the table does not list), or -1 when the bar is full. A slot in <paramref name="reserved"/> (an add already asked for and not yet sent) is not free.
    /// </summary>
    /// <param name="bar">The shortcut bar.</param>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="remembered">The slot the kind was last seen on, or -1.</param>
    /// <param name="reserved">Slots other adds wait for, or null.</param>
    public static int SlotFor(ShortcutBar bar, uint wcid, int remembered, IReadOnlySet<int>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(bar);
        bool Free(int slot) => ShortcutBar.InRange(slot) && bar[slot].IsEmpty && (reserved is null || !reserved.Contains(slot));

        if (Free(remembered))
        {
            return remembered;
        }

        var own = PreferredSlot(wcid);
        if (Free(own))
        {
            return own;
        }

        // GetFirstEmptyShortcutToTheRightOf's order (ShortcutBar.FirstEmptyToTheRightOf), with the reserved slots left out.
        var from = own >= 0 ? own : Kinds.Count - 1;
        for (var step = 1; step <= ShortcutBar.SlotCount; step++)
        {
            var slot = (from + step) % ShortcutBar.SlotCount;
            if (Free(slot))
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>The first stack of kind <paramref name="wcid"/> the player holds (the main pack, then the side packs), or 0.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="wcid">The weenie class id.</param>
    public static uint FirstHeld(GameState state, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var pack = inventory.Pack;
        for (var i = 0; i < pack.Count; i++)
        {
            if (state.Objects.TryGet(pack[i].Guid, out var o) && o.Weenie.WeenieClassId == wcid)
            {
                return pack[i].Guid;
            }
        }

        var side = inventory.SidePacks;
        for (var p = 0; p < side.Count; p++)
        {
            var inside = inventory.ItemsIn(side[p].Guid);
            for (var i = 0; i < inside.Count; i++)
            {
                if (state.Objects.TryGet(inside[i].Guid, out var o) && o.Weenie.WeenieClassId == wcid)
                {
                    return inside[i].Guid;
                }
            }
        }

        return 0;
    }
}
