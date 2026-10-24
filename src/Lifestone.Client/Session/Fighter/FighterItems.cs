using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// How the fighter's scenarios (the hunt, the fighter build) find a held item by weenie class when the pack may hold several of a class (M4 3.1 fix2). The hunt's loot adds duplicates of the kit's classes (a second Shortbow, a
/// second Mace, more arrows), and <see cref="GearInventory.Collect"/> lists the main pack and the side packs <b>before</b> the equipped items: a lookup that returns the first match finds the pack copy, reads "not wielded" for it
/// for ever, and the swap's wait never ends (live round 4, the gate's first hunter test). The rule here, in one place: an equipped instance of the class wins; otherwise a pack instance is chosen (the preferred guid when it is one,
/// else the first listed).
/// </summary>
internal static class FighterItems
{
    /// <summary>
    /// Finds a held object of weenie class <paramref name="wcid"/>: an equipped one wins (<paramref name="equipped"/> true, whatever its place in the listing); with none equipped, a pack instance (<paramref name="prefer"/> when
    /// that guid is held and not equipped, else the first listed), <paramref name="equipped"/> false. A wield is sent for the guid returned only when <paramref name="equipped"/> is false.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A list for the held guids (cleared first).</param>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="guid">The instance found, or 0.</param>
    /// <param name="equipped">True when <paramref name="guid"/> is wielded or worn.</param>
    /// <param name="prefer">A guid to choose among pack instances (0: none).</param>
    /// <returns>True when an instance is held.</returns>
    internal static bool Find(GameState state, List<uint> scratch, uint wcid, out uint guid, out bool equipped, uint prefer = 0)
    {
        GearInventory.Collect(state, scratch);
        var inventory = state.Self.Inventory;
        uint first = 0;
        uint preferred = 0;
        for (var i = 0; i < scratch.Count; i++)
        {
            var held = scratch[i];
            if (!state.Objects.TryGet(held, out var o) || o.Weenie.WeenieClassId != wcid)
            {
                continue;
            }

            if (inventory.Find(held).Kind == InventoryPlaceKind.Equipped)
            {
                guid = held;
                equipped = true;
                return true;
            }

            first = first == 0 ? held : first;
            preferred = held == prefer ? held : preferred;
        }

        guid = preferred != 0 ? preferred : first;
        equipped = false;
        return guid != 0;
    }

    /// <summary>The launchers a fighter's kit can give or an older build left (the Shortbow, the Longbow): the wielded-launcher fallback looks for each.</summary>
    internal static readonly uint[] LauncherWcids = [FighterKit.ShortbowWcid, FighterKit.LongbowWcid, FighterKit.Archer.LauncherWcid];

    /// <summary>The ammunition a fighter's kit can give or an older build left (the Arrow, the Greater Frost Arrow).</summary>
    internal static readonly uint[] AmmoWcids = [FighterKit.ArrowWcid, FighterKit.GreaterFrostArrowWcid];

    /// <summary>
    /// The wielded launcher, by what the read model shows: <see cref="CombatModeGuard.Read"/>'s launcher (an item in the missile slot 0x400000, ACE's rule, or a wielded ItemType MissileWeapon item with CombatUse 2), else an
    /// equipped item of the Shortbow's class. Null when none is wielded. ACE refuses a melee weapon while it is wielded (<c>CheckWeaponCollision</c>, <c>Player_Inventory.cs:1878-2056</c> at ACE 95ef426d), and so does the combat-mode guard.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A list for the held guids (cleared first).</param>
    /// <returns>The wielded launcher, or null.</returns>
    internal static WorldObject? WieldedLauncher(GameState state, List<uint> scratch)
    {
        if (CombatModeGuard.Read(state).Launcher is { } launcher)
        {
            return launcher;
        }

        foreach (var wcid in LauncherWcids)
        {
            if (Find(state, scratch, wcid, out var bow, out var equipped) && equipped && state.Objects.TryGet(bow, out var wielded))
            {
                return wielded;
            }
        }

        return null;
    }

    /// <summary>
    /// The guid of a wielded ammunition stack: an item equipped at the missile-ammunition location 0x800000 or an equipped stack of the Arrow's class; <paramref name="prefer"/> first when it is one of them. 0 when none.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="prefer">A stack to answer first (the swap's stack), or 0.</param>
    /// <returns>The wielded ammunition's guid, or 0.</returns>
    internal static uint WieldedAmmunition(GameState state, uint prefer = 0)
    {
        uint found = 0;
        var equipped = state.Self.Inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            var entry = equipped[i];
            var ammunition = (entry.Location & (uint)EquipMask.MissileAmmo) != 0 || (state.Objects.TryGet(entry.Guid, out var o) && Array.IndexOf(AmmoWcids, o.Weenie.WeenieClassId) >= 0);
            if (!ammunition)
            {
                continue;
            }

            if (entry.Guid == prefer)
            {
                return entry.Guid;
            }

            found = found == 0 ? entry.Guid : found;
        }

        return found;
    }
}
