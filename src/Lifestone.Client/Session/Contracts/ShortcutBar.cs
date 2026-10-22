using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>One slot of the shortcut bar: an object, a spell (a shortcut with no object), or nothing.</summary>
/// <param name="ObjectId">The object's guid, or 0.</param>
/// <param name="SpellId">The layered spell word the description carries (the spell in the low 16 bits, the layer in the high 16), or 0. ACE stores no spell with a shortcut (it keeps the object only: <c>Player_Character.cs</c> 245-262), so it is 0 for every row ACE sends.</param>
public readonly record struct ShortcutSlot(uint ObjectId, uint SpellId)
{
    /// <summary>True when the slot holds neither an object nor a spell.</summary>
    public bool IsEmpty => ObjectId == 0 && SpellId == 0;

    /// <summary>True when the slot holds an object.</summary>
    public bool IsItem => ObjectId != 0;

    /// <summary>True when the slot holds a spell and no object.</summary>
    public bool IsSpell => ObjectId == 0 && SpellId != 0;

    /// <summary>The spell id without its layer.</summary>
    public ushort Spell => (ushort)(SpellId & 0xFFFF);
}

/// <summary>What a <see cref="ShortcutsChanged"/> did.</summary>
public enum ShortcutChange
{
    /// <summary>A whole PlayerDescription replaced the bar (login; <c>gmToolbarUI::UpdateFromPlayerDesc</c>, 11.6096 0x004C04A0).</summary>
    Description,

    /// <summary>A slot was filled (the client sent AddShortcut; <c>gmToolbarUI::AddShortcut</c>, 0x004BE630).</summary>
    Added,

    /// <summary>A slot was emptied by the client (it sent RemoveShortcut; <c>gmToolbarUI::RemoveShortcut</c>, 0x004BE0E0).</summary>
    Removed,

    /// <summary>A slot was emptied because its item left the player's holdings (sold, dropped, given, used up: OpenAC #219, <c>gmToolbarUI::RecvNotice_ServerSaysMoveItem</c>, 0x004BFF40); the Action API sends RemoveShortcut for it once.</summary>
    ItemLeft,
}

/// <summary>
/// The shortcut bar changed (<see cref="SelfState.Shortcuts"/>). One event per slot that changed; the description publishes one with <see cref="Slot"/> -1.
/// </summary>
/// <param name="Change">What happened.</param>
/// <param name="Slot">The slot (0 to 17), or -1 for <see cref="ShortcutChange.Description"/>.</param>
/// <param name="ObjectId">The object now in the slot for <see cref="ShortcutChange.Added"/>; the object that left the slot for the other changes; 0 for the description.</param>
/// <param name="SpellId">The layered spell word of the slot (as <see cref="ShortcutSlot.SpellId"/>), or 0.</param>
public readonly record struct ShortcutsChanged(ShortcutChange Change, int Slot, uint ObjectId, uint SpellId) : IGameEvent;

/// <summary>
/// The player's shortcut bar (<see cref="SelfState.Shortcuts"/>): 18 slots, each empty or an object (retail's toolbar, <c>gmToolbarUI::InitShortcutArray</c>, 11.6096 0x004BE9E0), filled from the PlayerDescription's list at login
/// (option flag 0x1; <c>gmToolbarUI::UpdateFromPlayerDesc</c>, 0x004C04A0) and changed by the client's own AddShortcut and RemoveShortcut (<see cref="IActionApi.AddShortcut"/>) and by an item leaving the inventory (#219).
/// ACE keeps the list and answers neither send (<c>Player_Character.cs</c> 245-262), so the model changes at the send, and the next login's description shows whether the server took it.
/// </summary>
/// <remarks>
/// <para><b>A missing object.</b> A row whose object the player does not hold stays in the bar and reads <see cref="IsMissing"/> (the panel ghosts it). Held means <see cref="PlayerInventory.Contains"/>, so a row naming an object in a
/// side pack reads missing until that pack's contents are known (login: the description lists the main pack, the side packs' contents follow), and the answer is live: nothing publishes when it changes, a panel reads it again on
/// <see cref="InventoryChanged"/>. Whether retail drops such a row at login is unread (<c>TODO(m4-shortcut-missing-at-login)</c>: <c>UpdateFromPlayerDesc</c> adds every row with an object and no check).</para>
/// <para>Retail's toolbar adds only rows with an object (<c>UpdateFromPlayerDesc</c> skips a row whose object is 0), so a spell-only row is kept here and never drawn by the object panel (<c>TODO(m4-shortcut-spell)</c>).</para>
/// <para>Mutated only by the reducer. The slots are an immutable snapshot a reader may keep.</para>
/// </remarks>
public sealed class ShortcutBar
{
    /// <summary>The number of slots (<c>gmToolbarUI::InitShortcutArray</c>, 11.6096 0x004BE9E0; <c>UpdateFromPlayerDesc</c> loops to 18).</summary>
    public const int SlotCount = 18;

    /// <summary>The slot argument of <see cref="IActionApi.AddShortcut"/> that means "the first empty slot" (retail's <c>CreateShortcutToItem</c> with index -1).</summary>
    public const int AutoSlot = -1;

    private readonly ShortcutSlot[] _slots = new ShortcutSlot[SlotCount];
    private PlayerInventory? _inventory;

    /// <summary>An empty bar. Only the session creates one.</summary>
    internal ShortcutBar()
    {
        Slots = Snapshot();
    }

    /// <summary>True once a PlayerDescription arrived.</summary>
    public bool IsKnown { get; internal set; }

    /// <summary>The 18 slots in order (a snapshot: a reader may keep it).</summary>
    public ImmutableArray<ShortcutSlot> Slots { get; private set; }

    /// <summary>How many slots are filled.</summary>
    public int Count
    {
        get
        {
            var n = 0;
            foreach (var slot in _slots)
            {
                n += slot.IsEmpty ? 0 : 1;
            }

            return n;
        }
    }

    /// <summary>One slot; empty for a number outside 0 to 17.</summary>
    /// <param name="slot">The slot, 0 to 17.</param>
    public ShortcutSlot this[int slot] => InRange(slot) ? _slots[slot] : default;

    /// <summary>True for a slot number the bar has.</summary>
    /// <param name="slot">The slot.</param>
    public static bool InRange(int slot) => slot is >= 0 and < SlotCount;

    /// <summary>The slot an object is on, or -1.</summary>
    /// <param name="objectId">The object's guid.</param>
    public int SlotOf(uint objectId)
    {
        if (objectId == 0)
        {
            return -1;
        }

        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i].ObjectId == objectId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when the slot holds an object the player does not hold (see the remarks). False for an empty slot, a spell, and while the inventory is not known.</summary>
    /// <param name="slot">The slot, 0 to 17.</param>
    public bool IsMissing(int slot) =>
        InRange(slot) && _slots[slot].IsItem && _inventory is { IsKnown: true } inventory && !inventory.Contains(_slots[slot].ObjectId);

    /// <summary>The first empty slot, or -1 when the bar is full.</summary>
    public int FirstEmpty()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i].IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The first empty slot to the right of <paramref name="slot"/>, wrapping to the start of the bar and ending at <paramref name="slot"/> itself, or -1 when there is none
    /// (<c>gmToolbarUI::GetFirstEmptyShortcutToTheRightOf</c>, 11.6096 0x004BE1F0: from the next slot to the last, then from the first through <paramref name="slot"/>).
    /// </summary>
    /// <param name="slot">The slot to start after.</param>
    public int FirstEmptyToTheRightOf(int slot)
    {
        for (var i = Math.Max(0, slot + 1); i < SlotCount; i++)
        {
            if (_slots[i].IsEmpty)
            {
                return i;
            }
        }

        for (var i = 0; i < SlotCount && i <= slot; i++)
        {
            if (_slots[i].IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Connects the bar to the inventory it asks about missing objects.</summary>
    internal void Bind(PlayerInventory inventory) => _inventory = inventory;

    /// <summary>
    /// Replaces the bar with the description's list. A row whose index is not a slot is ignored (counted in the result); a row with no object and no spell is ignored; a slot named twice keeps the last row.
    /// </summary>
    /// <returns>How many rows named a slot the bar does not have.</returns>
    internal int ApplyDescription(PlayerDescriptionMsg message)
    {
        Array.Clear(_slots);
        var ignored = 0;
        if (!message.Shortcuts.IsDefault)
        {
            foreach (var row in message.Shortcuts)
            {
                if (row.Index >= SlotCount)
                {
                    ignored++;
                }
                else if (row.ObjectId != 0 || row.SpellId != 0)
                {
                    _slots[row.Index] = new ShortcutSlot(row.ObjectId, row.ObjectId != 0 ? 0 : row.SpellId);
                }
            }
        }

        IsKnown = true;
        Slots = Snapshot();
        return ignored;
    }

    /// <summary>Fills a slot (replacing what it held).</summary>
    internal void Set(int slot, ShortcutSlot value)
    {
        if (InRange(slot))
        {
            _slots[slot] = value;
            Slots = Snapshot();
        }
    }

    /// <summary>Empties a slot.</summary>
    internal void ClearSlot(int slot) => Set(slot, default);

    /// <summary>Empties the bar (leaving the world).</summary>
    internal void Clear()
    {
        IsKnown = false;
        Array.Clear(_slots);
        Slots = Snapshot();
    }

    private ImmutableArray<ShortcutSlot> Snapshot() => [.. _slots];
}
