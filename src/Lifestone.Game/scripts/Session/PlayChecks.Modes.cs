using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

// M4 3.1 g: the combat-mode guard (Session/Contracts/CombatModeGuard.cs) refuses a magic mode with no caster in hand and a melee mode with one. ACE does the same (Player_Combat.cs 812-882) and refuses to wield a mace while a wand
// is in hand (Player_Inventory.cs CheckWeaponCollision), which the stand-in never enforced. A row that needs a mode therefore puts the right thing in hand first, in the stand-in's own world through the Action API, and puts it away when
// it is done; nothing is faked in the read model and no creature or object is spawned that the row does not need.
public sealed partial class PlayChecks
{
    /// <summary>
    /// Puts a caster in hand for the magic mode: the kit's wand (<see cref="GearKit.Wand"/>, whose weenie the stand-in knows), created and wielded through the Action API. Returns the guid it wielded, to hand to <see cref="StowAsync"/>,
    /// or 0 when something is in hand already (nothing to put away) or the wand could not be wielded (named in the log; the row's own mode check then fails with the guard's text).
    /// </summary>
    private async Task<uint> HoldCasterAsync(IGameSessionView session)
    {
        if (session.State.Self.Inventory.EquippedAt((uint)EquipMask.Held) is not null)
        {
            return 0;
        }

        var spawn = session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        var spawned = await WaitFor(() => spawn.IsCompleted, 10.0);
        var guid = spawned ? spawn.Result?.Guid ?? 0 : 0;
        if (guid == 0)
        {
            Log.Warn(LogCategory, "TODO(m4-playcheck-hold-caster): the stand-in did not create the wand a row needs for the magic mode", ("outcome", spawn.Outcome?.Kind.ToString() ?? "pending"));
            return 0;
        }

        var wield = session.Actions.Wield(guid, EquipMask.Held, ActionSource.Host);
        var wielded = await WaitFor(() => wield.IsCompleted, 10.0) && wield.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        if (!wielded)
        {
            Log.Warn(LogCategory, "TODO(m4-playcheck-hold-caster): the wand a row needs for the magic mode was not wielded", ("outcome", wield.Outcome?.Kind.ToString() ?? "pending"), ("detail", wield.Outcome?.Detail ?? string.Empty));
            return 0;
        }

        return guid;
    }

    /// <summary>Puts <paramref name="guid"/> (what <see cref="HoldCasterAsync"/> wielded; 0 is nothing) back in the pack, so the rows after the one that needed it find the player as they left it.</summary>
    private async Task StowAsync(IGameSessionView session, uint guid)
    {
        if (guid == 0)
        {
            return;
        }

        var put = session.Actions.Unwield(guid, ActionSource.Host);
        if (!await WaitFor(() => put.IsCompleted, 10.0) || put.Outcome?.Kind != ActionOutcomeKind.Confirmed)
        {
            Log.Warn(LogCategory, "a row's wand was not put away", ("outcome", put.Outcome?.Kind.ToString() ?? "pending"));
        }
    }

    /// <summary>
    /// Puts away whatever is in the held slot (a caster an earlier row, the cast row or the death scenario's gear, left in hand), so a melee row's setup is consistent: ACE refuses a melee mode and the wield of a melee weapon while a
    /// caster is in hand, and so does the guard. Returns false when the item could not be put away (named in the log; the row's own mode check then fails with the guard's text).
    /// </summary>
    private async Task<bool> StowHeldAsync(IGameSessionView session)
    {
        if (session.State.Self.Inventory.EquippedAt((uint)EquipMask.Held) is not { } held)
        {
            return true;
        }

        var put = session.Actions.Unwield(held, ActionSource.Host);
        var done = await WaitFor(() => put.IsCompleted, 10.0) && put.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        if (!done)
        {
            Log.Warn(LogCategory, "TODO(m4-playcheck-stow-held): the caster in hand was not put away before a melee row", ("item", "0x" + held.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)), ("outcome", put.Outcome?.Kind.ToString() ?? "pending"));
        }

        return done;
    }
}
